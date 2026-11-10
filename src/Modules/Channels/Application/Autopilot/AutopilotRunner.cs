using System.Net;
using System.Text.Json;
using Hub.Modules.Channels.Application.Planning;
using Hub.Modules.Channels.Domain;
using Hub.SharedKernel;
using Hub.SharedKernel.Email;
using Hub.SharedKernel.Time;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Hub.Modules.Channels.Application.Autopilot;

/// <summary>Calcul des créneaux de la semaine à venir.</summary>
public static class AutopilotSchedule
{
    public const int HorizonDays = 7;

    /// <summary>
    /// Jours (à minuit, heure locale) où placer les publications manquantes : sur les 7 prochains jours,
    /// en évitant les jours déjà occupés et en répartissant les publications sur la semaine.
    /// </summary>
    public static IReadOnlyList<DateTimeOffset> FreeDays(DateTimeOffset now, int postsPerWeek, int alreadyPlanned, IReadOnlyCollection<DateOnly> busyDays)
    {
        var need = Math.Clamp(postsPerWeek, 1, 7) - alreadyPlanned;
        if (need <= 0) return [];
        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(now, BusinessTime.Zone).DateTime);
        var free = Enumerable.Range(1, HorizonDays).Select(today.AddDays).Where(d => !busyDays.Contains(d)).ToList();
        if (free.Count == 0) return [];
        need = Math.Min(need, free.Count);
        return Enumerable.Range(0, need)
            .Select(k => free[(int)Math.Floor((k + 0.5) * free.Count / need)])
            .Distinct()
            .Select(d => BusinessTime.FromLocal(d.ToDateTime(TimeOnly.MinValue)))
            .ToList();
    }

    public static DateOnly LocalDay(DateTimeOffset at) => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(at, BusinessTime.Zone).DateTime);
}

/// <summary>
/// Une passe du pilote automatique pour l'entreprise courante : choisir les sujets, rédiger, illustrer,
/// puis créer les publications (à valider, ou programmées si l'entreprise l'autorise).
/// </summary>
internal sealed class AutopilotRunner(
    IDbContextFactory<ChannelsDbContext> factory,
    IAutopilotContextSource source,
    IAutopilotPlanner planner,
    IPostWriter writer,
    IPostVisualDesigner designer,
    IVisualRenderer renderer,
    IVisualStore store,
    IEmailSender email,
    TimeProvider clock,
    ILogger<AutopilotRunner> logger)
{
    public const string Author = "Pilote automatique";

    /// <summary>
    /// Redressement : les publications du pilote à venir et pas encore publiées sont remplacées par de nouvelles,
    /// préparées avec les formats publicitaires variés. Les publications écrites à la main ne sont jamais touchées.
    /// </summary>
    /// <param name="onlyOldFormat">Vrai : seulement celles préparées avant les formats (redressement automatique).</param>
    /// <returns>Nombre de publications remplacées par de nouvelles.</returns>
    public async Task<Result<int>> RedoUpcomingAsync(bool onlyOldFormat, CancellationToken ct = default)
    {
        var now = clock.GetUtcNow();
        await using (var db = await factory.CreateDbContextAsync(ct))
        {
            var old = await RedoCandidates(db.Posts, now, onlyOldFormat).ToListAsync(ct);
            if (old.Count == 0) return Result<int>.Success(0);
            foreach (var post in old) post.Status = PostStatus.Cancelled;
            await db.SaveChangesAsync(ct);
            logger.LogInformation("Redressement : {Count} publication(s) du pilote à refaire", old.Count);
        }
        return await RunAsync(force: true, ct);
    }

    /// <summary>
    /// Publications du pilote pas encore publiées, de demain à la fin de la semaine préparée :
    /// exactement les jours que le pilote sait remplir à nouveau (celles d'aujourd'hui sont gardées).
    /// </summary>
    internal static IQueryable<Post> RedoCandidates(IQueryable<Post> posts, DateTimeOffset now, bool onlyOldFormat)
    {
        var today = AutopilotSchedule.LocalDay(now);
        var from = BusinessTime.FromLocal(today.AddDays(1).ToDateTime(TimeOnly.MinValue));
        var to = BusinessTime.FromLocal(today.AddDays(AutopilotSchedule.HorizonDays + 1).ToDateTime(TimeOnly.MinValue));
        var query = posts.Where(p => p.FromAutopilot
                                     && (p.Status == PostStatus.AwaitingApproval || p.Status == PostStatus.Scheduled)
                                     && p.PublishedAt == null
                                     && p.ScheduledAt > from && p.ScheduledAt < to);
        return onlyOldFormat ? query.Where(p => p.Format == null) : query;
    }

    public async Task<Result<int>> RunAsync(bool force, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var settings = await db.AutopilotSettings.FirstOrDefaultAsync(ct);
        if (settings is null || (!settings.Enabled && !force)) return Result<int>.Success(0);

        var now = clock.GetUtcNow();
        var horizonEnd = now.AddDays(AutopilotSchedule.HorizonDays + 1);

        // Les propositions jamais validées et dépassées depuis un jour sont abandonnées.
        foreach (var stale in await db.Posts.Where(p => p.Status == PostStatus.AwaitingApproval && p.ScheduledAt < now.AddDays(-1)).ToListAsync(ct))
            stale.Status = PostStatus.Cancelled;

        var upcoming = await db.Posts.AsNoTracking()
            .Where(p => p.Status != PostStatus.Cancelled && (p.ScheduledAt ?? p.PublishedAt) >= now.AddHours(-12) && (p.ScheduledAt ?? p.PublishedAt) < horizonEnd)
            .Select(p => new { p.FromAutopilot, At = p.ScheduledAt ?? p.PublishedAt, p.Status })
            .ToListAsync(ct);
        var busy = upcoming.Where(p => p.At is not null).Select(p => AutopilotSchedule.LocalDay(p.At!.Value)).ToHashSet();
        var planned = upcoming.Count(p => p.FromAutopilot && p.At > now);
        var slots = AutopilotSchedule.FreeDays(now, settings.PostsPerWeek, planned, busy);

        var created = 0;
        var problems = new List<string>();
        if (slots.Count > 0)
        {
            var context = await source.GetAsync(ct);
            var history = await HistoryAsync(db, ct);
            var audiences = AutopilotService.ReadAudiences(settings.AudiencesJson);
            var brief = await BriefAsync(db, slots, ct);

            // Variété : formats et scènes des dernières publications (même pas encore parues), à ne pas répéter.
            var recent = await db.Posts.AsNoTracking()
                .Where(p => p.Status != PostStatus.Cancelled && p.FromAutopilot)
                .OrderByDescending(p => p.ScheduledAt ?? p.PublishedAt)
                .Take(20)
                .Select(p => new { p.Format, p.SceneNote })
                .ToListAsync(ct);
            var memory = await CreativeMemoryAsync(db, settings, ct);
            var recentImages = memory.RecentImageUrls.ToList();
            var recentStyles = memory.RecentStyles.ToList();
            var pillars = Enumerable.Range(0, slots.Count).Select(i => brief?.SlotPillars.ElementAtOrDefault(i)).ToList();
            var formats = CreativeFormats.Assign(pillars, recent.Select(p => p.Format).ToList(), Random.Shared);
            var recentScenes = recent.Where(p => p.SceneNote != null).Select(p => p.SceneNote!).Take(10).ToList();

            var plan = await planner.PlanAsync(new AutopilotPlanRequest(context, audiences, slots, history, settings.Guidelines, now, brief, formats), ct);
            if (plan.IsFailure)
            {
                problems.Add(plan.Error!);
            }
            else
            {
                var channels = AutopilotService.ReadChannels(settings.Channels);
                foreach (var item in plan.Value!)
                {
                    var post = await PrepareAsync(item, context, settings, channels, recentScenes,
                        memory with { RecentImageUrls = recentImages.Take(10).ToList(), RecentStyles = recentStyles.Take(10).ToList() }, problems, ct);
                    if (post is null) continue;
                    if (post.Value.Post.SceneNote is { } scene) recentScenes.Insert(0, scene);
                    if (post.Value.Post.ImageUrl is { } url) recentImages.Insert(0, url);
                    if (post.Value.Post.ArtStyle is { } style) recentStyles.Insert(0, style);
                    db.Posts.Add(post.Value.Post);
                    db.PostTargets.AddRange(post.Value.Targets);
                    created++;
                }
            }
        }

        settings.LastRunAt = now;
        settings.LastRunSummary = (created == 0 && problems.Count == 0
                ? "La semaine est déjà couverte : rien à ajouter."
                : $"{created} publication(s) préparée(s)." + (problems.Count > 0 ? " Problèmes : " + string.Join(" ; ", problems.Distinct()) : ""))
            .Truncate(1000);
        await db.SaveChangesAsync(ct);
        logger.LogInformation("Pilote automatique : {Summary}", settings.LastRunSummary);

        if (created > 0 && !settings.PublishWithoutApproval) await NotifyAsync(created, ct);
        return problems.Count > 0 && created == 0 ? Result<int>.Failure(settings.LastRunSummary) : Result<int>.Success(created);
    }

    private async Task<(Post Post, List<PostTarget> Targets)?> PrepareAsync(
        PlannedPost item, AutopilotContext context, AutopilotSettings settings, IReadOnlyList<ChannelType> channels,
        IReadOnlyList<string> recentScenes, CreativeMemory memory, List<string> problems, CancellationToken ct)
    {
        var audience = AutopilotService.ReadAudiences(settings.AudiencesJson).FirstOrDefault(a => a.Name == item.Audience);
        // Le public n'est transmis qu'à l'IA : les modèles de texte sans IA reprendraient la consigne mot pour mot.
        var details = writer.UsesAi
            ? $"{item.Angle}\nPublic visé : {item.Audience}{(audience is null ? "" : " — " + audience.Description)}. Adresse-toi à ce public."
              + (item.Pillar is null ? "" : $"\nPilier de contenu : {item.Pillar}.")
            : item.Angle;
        var format = CreativeFormats.Find(item.Format);
        var hook = CreativeFormats.Hooks[Random.Shared.Next(CreativeFormats.Hooks.Length)];
        var text = await writer.SuggestAsync(new PostIdea(context.CompanyName, context.City, item.Topic, details, item.Tone, ChannelType.Facebook,
            Technique: format?.Copy, Hook: hook), ct);
        if (text.IsFailure) { problems.Add(text.Error!); return null; }
        var suggestion = text.Value![0];

        var imageUrl = item.ImageUrl;
        var visualKind = "Importée";
        var visual = await designer.DesignAsync(new VisualIdea(context.CompanyName, context.Phone, context.PrimaryColor, context.AccentColor,
            item.Topic, suggestion.Text, item.Facts, item.Style, Audience: item.Audience, Site: context.SiteUrl,
            Format: item.Format, AvoidScenes: recentScenes, Memory: memory,
            LogoUrl: context.LogoUrl, LogoOnDarkUrl: context.LogoOnDarkUrl), ct);
        if (visual.IsSuccess && (visual.Value!.Png ?? (visual.Value.Svg is { } svg ? renderer.RenderPng(svg) : null)) is { } png)
        {
            var saved = await store.SavePngAsync(png, ct);
            if (saved.IsSuccess)
            {
                imageUrl = saved.Value;
                visualKind = visual.Value.Svg is null ? "Photo" : "Illustration";
            }
            else problems.Add(saved.Error!);
        }
        else if (visual.IsFailure)
        {
            problems.Add(visual.Error!);
        }

        var post = new Post
        {
            Title = item.Topic.Truncate(200),
            Message = suggestion.Text,
            LinkPath = item.LinkPath,
            ImageUrl = imageUrl,
            Audience = item.Audience.Truncate(100),
            VisualKind = imageUrl is null ? null : visualKind,
            Theme = item.Style,
            Pillar = item.Pillar?.Truncate(40),
            Format = (visual.IsSuccess ? visual.Value!.Format : null) ?? item.Format,
            SceneNote = visual.IsSuccess ? visual.Value!.Scene?.Truncate(400) : null,
            ArtStyle = visual.IsSuccess ? visual.Value!.ArtStyle : null,
            FromAutopilot = true,
            CreatedBy = Author,
            AiModel = suggestion.Model,
            Campaign = TrackedLink.NewCampaign(item.Topic, item.At),
            ScheduledAt = item.At,
            Status = settings.PublishWithoutApproval ? PostStatus.Scheduled : PostStatus.AwaitingApproval,
        };
        return (post, channels.Select(c => new PostTarget { PostId = post.Id, Channel = c }).ToList());
    }

    /// <summary>Les 10 dernières images et leurs styles (publications non refusées), et les styles désactivés.</summary>
    internal static async Task<CreativeMemory> CreativeMemoryAsync(ChannelsDbContext db, AutopilotSettings? settings, CancellationToken ct)
    {
        var recent = await db.Posts.AsNoTracking()
            .Where(p => p.Status != PostStatus.Cancelled && p.ImageUrl != null)
            .OrderByDescending(p => p.ScheduledAt ?? p.PublishedAt ?? p.CreatedAt)
            .Take(10)
            .Select(p => new { p.ImageUrl, p.ArtStyle })
            .ToListAsync(ct);
        return new CreativeMemory(
            recent.Select(p => p.ImageUrl!).ToList(),
            recent.Where(p => p.ArtStyle != null).Select(p => p.ArtStyle!).ToList(),
            CreativeStyles.Parse(settings?.DisabledStyles).ToHashSet());
    }

    /// <summary>
    /// Plan marketing actif à cette date : l'étape en cours et le pilier de chaque créneau,
    /// choisi pour se rapprocher de la répartition visée sur l'étape.
    /// </summary>
    private async Task<PlanBrief?> BriefAsync(ChannelsDbContext db, IReadOnlyList<DateTimeOffset> slots, CancellationToken ct)
    {
        var plan = await db.MarketingPlans.AsNoTracking().FirstOrDefaultAsync(p => p.Status == PlanStatus.Active, ct);
        if (plan is null || slots.Count == 0) return null;
        var day = AutopilotSchedule.LocalDay(slots[0]);
        var phase = PlanLogic.PhaseAt(PlanLogic.Phases(plan.PhasesJson), day);
        if (phase is null) return null;
        var pillars = PlanLogic.Pillars(plan.PillarsJson);
        var from = BusinessTime.FromLocal(phase.From.ToDateTime(TimeOnly.MinValue));
        var to = BusinessTime.FromLocal(phase.To.AddDays(1).ToDateTime(TimeOnly.MinValue));
        var used = await db.Posts.AsNoTracking()
            .Where(p => p.Pillar != null && p.Status != PostStatus.Cancelled && p.Status != PostStatus.Failed
                        && (p.ScheduledAt ?? p.PublishedAt) >= from && (p.ScheduledAt ?? p.PublishedAt) < to)
            .GroupBy(p => p.Pillar!)
            .Select(g => new { g.Key, Count = g.Count() })
            .ToDictionaryAsync(g => g.Key, g => g.Count, ct);
        return new PlanBrief(plan.Objective, phase, pillars, PlanLogic.AssignPillars(pillars, used, slots.Count));
    }

    private async Task<IReadOnlyList<AutopilotHistory>> HistoryAsync(ChannelsDbContext db, CancellationToken ct)
    {
        var recent = await db.Posts.AsNoTracking()
            .Where(p => p.Status == PostStatus.Published || p.Status == PostStatus.PartiallyPublished)
            .OrderByDescending(p => p.PublishedAt).Take(30)
            .Select(p => new { p.Id, p.Title, p.Audience, p.PublishedAt, p.Campaign, p.Theme, p.Pillar, p.Format })
            .ToListAsync(ct);
        if (recent.Count == 0) return [];
        var results = await source.GetResultsAsync(recent.Select(p => p.Campaign).ToList(), ct);
        var ids = recent.Select(p => p.Id).ToList();
        var stats = await db.PostStats.AsNoTracking().Where(s => ids.Contains(s.PostId))
            .GroupBy(s => s.PostId)
            .Select(g => new { PostId = g.Key, Reach = g.Sum(s => s.Reach), Interactions = g.Sum(s => s.Interactions) })
            .ToDictionaryAsync(s => s.PostId, ct);
        return recent.Select(p =>
        {
            var r = results.GetValueOrDefault(p.Campaign);
            var s = stats.GetValueOrDefault(p.Id);
            return new AutopilotHistory(p.Title, p.Audience, p.PublishedAt, r.Visits, r.Leads, r.Won, p.Theme,
                s?.Reach ?? 0, s?.Interactions ?? 0, p.Pillar, p.Format);
        }).ToList();
    }

    private async Task NotifyAsync(int count, CancellationToken ct)
    {
        var context = await source.GetAsync(ct);
        if (string.IsNullOrWhiteSpace(context.AdminEmail)) return;
        var link = $"{(context.SiteUrl ?? "").TrimEnd('/')}/admin/publications";
        var html = $"""
            <p>Bonjour,</p>
            <p>Le pilote automatique a préparé <strong>{count} publication(s)</strong> pour {WebUtility.HtmlEncode(context.CompanyName)} cette semaine.</p>
            <p>Elles attendent votre accord : relisez-les, modifiez-les au besoin, puis approuvez ou refusez-les.</p>
            <p><a href="{WebUtility.HtmlEncode(link)}">Voir les publications à valider</a></p>
            <p style="color:#666;font-size:13px">Sans votre accord, aucune ne sera publiée.</p>
            """;
        var sent = await email.SendAsync(new EmailMessage(context.AdminEmail, $"{count} publication(s) à valider", html), ct);
        if (sent.IsFailure) logger.LogWarning("Pilote automatique : courriel non envoyé ({Error})", sent.Error);
    }
}

internal static class StringExtensions
{
    public static string Truncate(this string s, int max) => s.Length <= max ? s : s[..max];
}
