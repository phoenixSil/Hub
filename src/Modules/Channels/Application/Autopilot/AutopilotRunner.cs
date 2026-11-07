using System.Net;
using System.Text.Json;
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
            var plan = await planner.PlanAsync(new AutopilotPlanRequest(context, audiences, slots, history, settings.Guidelines, now), ct);
            if (plan.IsFailure)
            {
                problems.Add(plan.Error!);
            }
            else
            {
                var channels = AutopilotService.ReadChannels(settings.Channels);
                foreach (var item in plan.Value!)
                {
                    var post = await PrepareAsync(item, context, settings, channels, problems, ct);
                    if (post is null) continue;
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
        PlannedPost item, AutopilotContext context, AutopilotSettings settings, IReadOnlyList<ChannelType> channels, List<string> problems, CancellationToken ct)
    {
        var audience = AutopilotService.ReadAudiences(settings.AudiencesJson).FirstOrDefault(a => a.Name == item.Audience);
        var details = $"{item.Angle}\nPublic visé : {item.Audience}{(audience is null ? "" : " — " + audience.Description)}.";
        var text = await writer.SuggestAsync(new PostIdea(context.CompanyName, context.City, item.Topic, details, item.Tone, ChannelType.Facebook), ct);
        if (text.IsFailure) { problems.Add(text.Error!); return null; }
        var suggestion = text.Value![0];

        var imageUrl = item.ImageUrl;
        var visual = await designer.DesignAsync(new VisualIdea(context.CompanyName, context.Phone, context.PrimaryColor, context.AccentColor,
            item.Topic, suggestion.Text, item.Facts, item.Style), ct);
        if (visual.IsSuccess && renderer.RenderPng(visual.Value!.Svg) is { } png)
        {
            var saved = await store.SavePngAsync(png, ct);
            if (saved.IsSuccess) imageUrl = saved.Value;
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
            FromAutopilot = true,
            CreatedBy = Author,
            AiModel = suggestion.Model,
            Campaign = TrackedLink.NewCampaign(item.Topic, item.At),
            ScheduledAt = item.At,
            Status = settings.PublishWithoutApproval ? PostStatus.Scheduled : PostStatus.AwaitingApproval,
        };
        return (post, channels.Select(c => new PostTarget { PostId = post.Id, Channel = c }).ToList());
    }

    private async Task<IReadOnlyList<AutopilotHistory>> HistoryAsync(ChannelsDbContext db, CancellationToken ct)
    {
        var recent = await db.Posts.AsNoTracking()
            .Where(p => p.Status == PostStatus.Published || p.Status == PostStatus.PartiallyPublished)
            .OrderByDescending(p => p.PublishedAt).Take(15)
            .Select(p => new { p.Title, p.Audience, p.PublishedAt, p.Campaign })
            .ToListAsync(ct);
        if (recent.Count == 0) return [];
        var results = await source.GetResultsAsync(recent.Select(p => p.Campaign).ToList(), ct);
        return recent.Select(p =>
        {
            var r = results.GetValueOrDefault(p.Campaign);
            return new AutopilotHistory(p.Title, p.Audience, p.PublishedAt, r.Visits, r.Leads);
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
