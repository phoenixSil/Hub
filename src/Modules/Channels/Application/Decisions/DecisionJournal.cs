using System.Globalization;
using System.Text;
using Hub.Modules.Channels.Domain;
using Hub.SharedKernel.Time;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Hub.Modules.Channels.Application.Decisions;

public sealed record DecisionView(
    Guid Id, Guid PostId, string PostTitle, DateTimeOffset DecidedAt, string Policy, string? Explored, double? Propensity,
    string Slot, double Intensity, string? Format, string? Audience, string? Theme, string Channels, bool EditedByHuman,
    int Reach, int Interactions, int Visits, int Leads, int Won, double? Reward, DateTimeOffset? ClosedAt, string? Note);

public sealed record DecisionSummary(int Total, int Closed, int Pending, int Explorations, double? AverageReward, double ExplorationRate);

/// <summary>Bilan d'un passage du gardien sur le journal.</summary>
public sealed record DecisionSync(int Recorded, int Closed, int Finalized, double? AverageReward);

/// <summary>Journal des décisions marketing (étape 1 du noyau décisionnel) : lecture et export.</summary>
public interface IDecisionJournal
{
    Task<DecisionSummary> SummaryAsync(CancellationToken ct = default);
    Task<IReadOnlyList<DecisionView>> ListAsync(int take = 20, CancellationToken ct = default);

    /// <summary>Tout le journal en CSV (séparateur « ; »), pour l'analyse (Python, Excel).</summary>
    Task<string> ExportCsvAsync(CancellationToken ct = default);
}

internal sealed class DecisionJournal(
    IDbContextFactory<ChannelsDbContext> factory,
    IEnumerable<IDecisionAdapter> adapters,
    IDecisionOutcomeSource outcomes,
    IOptions<DecisionOptions> options,
    TimeProvider clock) : IDecisionJournal
{
    private DecisionOptions O => options.Value;

    /// <summary>Écrit la décision d'une publication (à appeler avant l'enregistrement de la publication).</summary>
    public async Task RecordAsync(ChannelsDbContext db, Post post, IEnumerable<ChannelType> channels, string policy,
        string? explored, double? propensity, CancellationToken ct)
    {
        var at = post.ScheduledAt ?? post.PublishedAt ?? clock.GetUtcNow();
        var settings = await db.AutopilotSettings.AsNoTracking().FirstOrDefaultAsync(ct);
        var plan = post.PlanId is { } pid ? await db.MarketingPlans.AsNoTracking().FirstOrDefaultAsync(p => p.Id == pid, ct) : null;
        var perWeek = plan?.PostsPerWeek ?? settings?.PostsPerWeek ?? 0;
        var followers = (await db.AudienceSnapshots.AsNoTracking().ToListAsync(ct))
            .GroupBy(s => s.Channel).ToDictionary(g => g.Key.ToString(), g => g.OrderByDescending(s => s.Day).First().Followers);
        var weekAgo = at.AddDays(-7);
        var recent = await db.Posts.AsNoTracking()
            .Where(p => p.Id != post.Id && p.Status != PostStatus.Cancelled && p.Status != PostStatus.Expired
                && (p.ScheduledAt ?? p.PublishedAt) >= weekAgo && (p.ScheduledAt ?? p.PublishedAt) < at)
            .Select(p => new { p.Audience, p.Theme }).ToListAsync(ct);

        db.Decisions.Add(new Decision
        {
            Domain = PublicationDecisions.Name,
            PostId = post.Id,
            PlanId = post.PlanId,
            DecidedAt = clock.GetUtcNow(),
            Policy = policy,
            Explored = explored,
            Propensity = propensity,
            ExplorationRate = policy is DecisionPolicy.Exploration or DecisionPolicy.Ai ? O.ExplorationRate : 0,
            Slot = PublicationDecisions.Slot(at),
            Intensity = PublicationDecisions.Intensity(post.Theme),
            Format = post.Format,
            Audience = post.Audience,
            Theme = post.Theme,
            Pillar = post.Pillar,
            VisualKind = post.VisualKind,
            Channels = string.Join(",", channels.Distinct().OrderBy(c => c)),
            ContextJson = PublicationDecisions.Context(at, perWeek, perWeek >= 14, followers,
                recent.Count(r => r.Audience != null && r.Audience == post.Audience),
                recent.Count(r => PublicationDecisions.Intensity(r.Theme) >= 1)),
        });
    }

    /// <summary>
    /// Passage du gardien : journalise les publications qui n'y sont pas encore (à la main ou antérieures),
    /// clôture celles arrivées à la fin de la fenêtre d'attribution et met à jour celles arrivées à 30 jours.
    /// </summary>
    public async Task<DecisionSync> SyncAsync(CancellationToken ct = default)
    {
        var now = clock.GetUtcNow();
        await using var db = await factory.CreateDbContextAsync(ct);

        // 1. Rattrapage : publications programmées ou parties sans décision.
        var missing = await db.Posts.AsNoTracking()
            .Where(p => p.Status != PostStatus.Draft && p.Status != PostStatus.Cancelled && p.Status != PostStatus.Expired
                && (p.ScheduledAt ?? p.PublishedAt) != null && !db.Decisions.Any(d => d.PostId == p.Id))
            .OrderBy(p => p.ScheduledAt ?? p.PublishedAt).Take(500).ToListAsync(ct);
        var channelsOf = (await db.PostTargets.AsNoTracking().Where(t => missing.Select(p => p.Id).Contains(t.PostId)).ToListAsync(ct))
            .ToLookup(t => t.PostId, t => t.Channel);
        foreach (var post in missing)
            await RecordAsync(db, post, channelsOf[post.Id], post.FromAutopilot ? DecisionPolicy.Historical : DecisionPolicy.Human, null, null, ct);
        await db.SaveChangesAsync(ct);

        // 2. Clôture (J+7) et mise à jour finale (J+30).
        var closeBefore = now.AddDays(-O.CloseAfterDays);
        var finalBefore = now.AddDays(-O.FinalizeAfterDays);
        var open = await db.Decisions.Where(d => d.FinalizedAt == null).ToListAsync(ct);
        var posts = await db.Posts.AsNoTracking().Where(p => open.Select(d => d.PostId).Contains(p.Id)).ToDictionaryAsync(p => p.Id, ct);
        var due = new List<(Decision Decision, Post? Post, bool Final)>();
        foreach (var d in open)
        {
            var post = posts.GetValueOrDefault(d.PostId);
            var gone = post is null || post.Status is PostStatus.Cancelled or PostStatus.Expired;
            if (gone) due.Add((d, post, true));
            else if (d.ClosedAt is null && post!.PublishedAt is { } pub && pub <= closeBefore) due.Add((d, post, false));
            else if (d.ClosedAt is not null && post!.PublishedAt is { } p30 && p30 <= finalBefore) due.Add((d, post, true));
        }

        var adapter = adapters.FirstOrDefault(a => a.Domain == PublicationDecisions.Name) ?? new PublicationDecisions();
        var campaigns = due.Where(x => x.Post?.Campaign is { Length: > 0 }).Select(x => x.Post!.Campaign).Distinct().ToList();
        var site = campaigns.Count == 0 ? new Dictionary<string, CampaignOutcome>() : await outcomes.GetAsync(campaigns, ct);
        var ids = due.Select(x => x.Decision.PostId).ToList();
        var stats = (await db.PostStats.AsNoTracking().Where(s => ids.Contains(s.PostId)).ToListAsync(ct))
            .GroupBy(s => s.PostId).ToDictionary(g => g.Key, g => g.GroupBy(s => s.Channel).Select(c => c.OrderByDescending(s => s.MeasuredAt).First()).ToList());

        int closed = 0, finalized = 0;
        var rewards = new List<double>();
        foreach (var (d, post, final) in due)
        {
            if (post is null || post.Status is PostStatus.Cancelled or PostStatus.Expired)
            {
                d.Note = post is null ? "Publication supprimée" : "Publication retirée";
                d.Reward = null;
                d.ClosedAt ??= now;
                d.FinalizedAt = now;
                continue;
            }
            // L'administrateur a changé l'heure, la cible ou le thème : le choix réel n'est plus celui journalisé.
            if (PublicationDecisions.Slot(post.PublishedAt ?? post.ScheduledAt ?? d.DecidedAt) != d.Slot
                || post.Audience != d.Audience || post.Theme != d.Theme)
                d.EditedByHuman = true;
            // Reel : le type de vidéo compte comme visuel (Kairos compare images et Reels).
            if (!string.IsNullOrWhiteSpace(post.VideoUrl)) d.VisualKind = ReelKinds.Label(post.ReelKind);
            var s = stats.GetValueOrDefault(post.Id) ?? [];
            d.Reach = s.Sum(x => x.Reach);
            d.Interactions = s.Sum(x => x.Interactions);
            var o = site.GetValueOrDefault(post.Campaign);
            d.Visits = o?.Visits ?? 0;
            d.Leads = o?.Leads ?? 0;
            d.Appointments = o?.Appointments ?? 0;
            d.Won = o?.Won ?? 0;
            d.Cost = post.VisualKind == "Photo" ? O.CostPerPhoto : 0;
            d.Reward = adapter.Reward(d, O);
            rewards.Add(d.Reward.Value);
            if (final) { d.FinalizedAt = now; finalized++; }
            else { d.ClosedAt = now; closed++; }
        }
        await db.SaveChangesAsync(ct);
        return new DecisionSync(missing.Count, closed, finalized, rewards.Count == 0 ? null : Math.Round(rewards.Average(), 1));
    }

    public async Task<DecisionSummary> SummaryAsync(CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var rows = await db.Decisions.AsNoTracking().Select(d => new { d.ClosedAt, d.Policy, d.Reward }).ToListAsync(ct);
        var rewarded = rows.Where(r => r.Reward is not null).Select(r => r.Reward!.Value).ToList();
        return new DecisionSummary(rows.Count, rows.Count(r => r.ClosedAt != null), rows.Count(r => r.ClosedAt == null),
            rows.Count(r => r.Policy == DecisionPolicy.Exploration), rewarded.Count == 0 ? null : Math.Round(rewarded.Average(), 1), O.ExplorationRate);
    }

    public async Task<IReadOnlyList<DecisionView>> ListAsync(int take = 20, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        return await Query(db).Take(take).ToListAsync(ct);
    }

    private static IQueryable<DecisionView> Query(ChannelsDbContext db) =>
        from d in db.Decisions.AsNoTracking()
        join p in db.Posts.AsNoTracking() on d.PostId equals p.Id into pj
        from p in pj.DefaultIfEmpty()
        orderby d.DecidedAt descending
        select new DecisionView(d.Id, d.PostId, p == null ? "(supprimée)" : p.Title, d.DecidedAt, d.Policy, d.Explored, d.Propensity,
            d.Slot, d.Intensity, d.Format, d.Audience, d.Theme, d.Channels, d.EditedByHuman,
            d.Reach, d.Interactions, d.Visits, d.Leads, d.Won, d.Reward, d.ClosedAt, d.Note);

    public async Task<string> ExportCsvAsync(CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var rows = await db.Decisions.AsNoTracking().OrderBy(d => d.DecidedAt).ToListAsync(ct);
        var titles = await db.Posts.AsNoTracking().Where(p => rows.Select(r => r.PostId).Contains(p.Id)).ToDictionaryAsync(p => p.Id, p => p.Title, ct);
        var inv = CultureInfo.InvariantCulture;
        var sb = new StringBuilder();
        string[] header =
        [
            "decision_id", "domaine", "publication_id", "titre", "campagne_id", "decidee_le", "politique", "dimension_exploree", "probabilite",
            "taux_exploration", "creneau", "intensite", "format", "cible", "theme", "pilier", "visuel", "reseaux", "modifiee_par_humain",
            "contexte_json", "portee", "interactions", "visites", "demandes", "rendez_vous", "gagnes", "cout", "recompense",
            "cloturee_le", "finalisee_le", "remarque",
        ];
        sb.AppendLine(string.Join(';', header));
        string D(DateTimeOffset? v) => v is null ? "" : TimeZoneInfo.ConvertTime(v.Value, BusinessTime.Zone).ToString("yyyy-MM-dd HH:mm", inv);
        foreach (var d in rows)
        {
            string?[] cells =
            [
                d.Id.ToString(), d.Domain, d.PostId.ToString(), titles.GetValueOrDefault(d.PostId), d.PlanId?.ToString(), D(d.DecidedAt), d.Policy,
                d.Explored, d.Propensity?.ToString("0.####", inv), d.ExplorationRate.ToString("0.##", inv), d.Slot, d.Intensity.ToString("0.#", inv),
                d.Format, d.Audience, d.Theme, d.Pillar, d.VisualKind, d.Channels, d.EditedByHuman ? "1" : "0",
                d.ContextJson, d.Reach.ToString(inv), d.Interactions.ToString(inv), d.Visits.ToString(inv), d.Leads.ToString(inv),
                d.Appointments.ToString(inv), d.Won.ToString(inv), d.Cost.ToString("0.##", inv), d.Reward?.ToString("0.##", inv),
                D(d.ClosedAt), D(d.FinalizedAt), d.Note,
            ];
            sb.AppendLine(string.Join(';', cells.Select(Csv)));
        }
        return sb.ToString();
    }

    public static string Csv(string? value)
    {
        if (string.IsNullOrEmpty(value)) return "";
        // Une cellule qui commence par = + - @ serait une formule dans Excel.
        if ("=+-@".Contains(value[0]) && !double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out _)) value = "'" + value;
        return value.IndexOfAny([';', '"', '\n', '\r']) >= 0 ? "\"" + value.Replace("\"", "\"\"") + "\"" : value;
    }
}
