using System.Collections.Concurrent;
using Hub.Modules.Channels.Application.Autopilot;
using Hub.Modules.Channels.Domain;
using Hub.SharedKernel;
using Hub.SharedKernel.Tenancy;
using Hub.SharedKernel.Time;
using Microsoft.EntityFrameworkCore;

namespace Hub.Modules.Channels.Application.Marketing;

/// <summary>Une publication telle qu'affichée dans le calendrier.</summary>
public sealed record CalendarPost(
    Guid Id,
    DateTimeOffset At,
    string Title,
    string? ImageUrl,
    IReadOnlyList<ChannelType> Channels,
    PostStatus Status,
    bool FromAutopilot,
    string? Error);

public sealed record CalendarDay(DateOnly Date, IReadOnlyList<CalendarPost> Posts);

/// <summary>La semaine du calendrier (lundi à dimanche).</summary>
public sealed record CalendarWeek(DateOnly Monday, IReadOnlyList<CalendarDay> Days)
{
    public int ToApprove => Days.Sum(d => d.Posts.Count(p => p.Status == PostStatus.AwaitingApproval));
}

/// <summary>Le voyant d'état : ce qui ne va pas, et le bouton qui le règle.</summary>
public enum HealthLevel { Info, Warning, Error }

public enum HealthAction { None, Retry, OpenChannels, CreateCampaign, ResumeAutopilot, OpenPosts }

public sealed record HealthIssue(HealthLevel Level, string Message, string ActionLabel, HealthAction Action);

/// <summary>Une préparation de publications en cours ou terminée (affichée avec une barre de progression).</summary>
public sealed record PreparationState(DateTimeOffset StartedAt, int Target, int Done, bool Finished, string? Summary, bool Failed, Guid? PlanId = null);

/// <summary>Avancement des préparations, par entreprise (en mémoire : il suffit pour la barre de progression).</summary>
public sealed class AutopilotProgress
{
    private readonly ConcurrentDictionary<Guid, PreparationState> runs = new();

    public void Start(Guid tenantId, int target, DateTimeOffset now, Guid? planId = null) =>
        runs[tenantId] = new PreparationState(now, target, 0, false, null, false, planId);

    public void Step(Guid tenantId, int done) => runs.AddOrUpdate(tenantId,
        _ => new PreparationState(DateTimeOffset.UtcNow, done, done, false, null, false),
        (_, r) => r with { Done = done });

    public void Finish(Guid tenantId, string summary, bool failed) => runs.AddOrUpdate(tenantId,
        _ => new PreparationState(DateTimeOffset.UtcNow, 0, 0, true, summary, failed),
        (_, r) => r with { Finished = true, Summary = summary, Failed = failed });

    /// <summary>Demande envoyée, pas encore commencée : la barre s'affiche tout de suite.</summary>
    public void Queue(Guid tenantId, DateTimeOffset now, Guid? planId = null) => runs[tenantId] = new PreparationState(now, 0, 0, false, null, false, planId);

    public PreparationState? Get(Guid tenantId) => runs.TryGetValue(tenantId, out var r) ? r : null;
}

public interface IMarketingCalendar
{
    /// <summary>La semaine qui commence le lundi donné.</summary>
    Task<CalendarWeek> WeekAsync(DateOnly monday, CancellationToken ct = default);

    /// <summary>Approuve d'un coup toutes les publications « à valider » de la semaine. Renvoie le nombre approuvé.</summary>
    Task<Result<int>> ApproveWeekAsync(DateOnly monday, CancellationToken ct = default);

    /// <summary>Retire une publication pas encore parue (elle ne partira pas).</summary>
    Task<Result> RemoveAsync(Guid postId, CancellationToken ct = default);

    /// <summary>Le pilote refait cette publication : autre sujet, texte et image, au même créneau.</summary>
    Task<Result> ReplaceAsync(Guid postId, CancellationToken ct = default);

    /// <summary>Lance la préparation des publications manquantes (barre de progression dans le calendrier).</summary>
    Task<Result> PrepareAsync(CancellationToken ct = default);

    Task<PreparationState?> PreparationAsync(CancellationToken ct = default);

    /// <summary>Le voyant d'état : vide quand tout fonctionne.</summary>
    Task<IReadOnlyList<HealthIssue>> HealthAsync(CancellationToken ct = default);

    /// <summary>Publications préparées pour l'aperçu d'une campagne pas encore démarrée.</summary>
    Task<IReadOnlyList<CalendarPost>> PreviewPostsAsync(Guid planId, CancellationToken ct = default);

    /// <summary>Dernière vérification du gardien et son journal (30 dernières actions).</summary>
    Task<GuardianStatus> GuardianAsync(CancellationToken ct = default);

    /// <summary>Lance tout de suite la vérification du gardien (sans attendre les 30 minutes).</summary>
    Task<GuardianReport> CheckNowAsync(CancellationToken ct = default);

    /// <summary>Réseaux connectés (Facebook, Instagram) : ceux que l'assistant propose.</summary>
    Task<IReadOnlyList<ChannelType>> ConnectedAsync(CancellationToken ct = default);
}

internal sealed class MarketingCalendar(
    IDbContextFactory<ChannelsDbContext> factory,
    AutopilotRunner runner,
    AutopilotTrigger trigger,
    AutopilotProgress progress,
    MarketingGuardian guardian,
    ITenantContext tenant,
    TimeProvider clock) : IMarketingCalendar
{
    public async Task<GuardianStatus> GuardianAsync(CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var last = await db.MaintenanceEntries.AsNoTracking().Where(e => e.Kind == MarketingGuardian.Check).OrderByDescending(e => e.At).FirstOrDefaultAsync(ct);
        var journal = await db.MaintenanceEntries.AsNoTracking().Where(e => e.Kind != MarketingGuardian.Check)
            .OrderByDescending(e => e.At).Take(30).ToListAsync(ct);
        var batch = last is null ? [] : journal.Where(e => e.At == last.At).ToList();
        return new GuardianStatus(last?.At, batch.Count(e => e.Success), batch.Count(e => !e.Success), journal);
    }

    public Task<GuardianReport> CheckNowAsync(CancellationToken ct = default) => guardian.RunAsync(ct);

    public async Task<CalendarWeek> WeekAsync(DateOnly monday, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var (from, to) = CalendarLogic.Bounds(monday);
        var posts = await db.Posts.AsNoTracking()
            .Where(p => p.Status != PostStatus.Cancelled
                        && ((p.ScheduledAt >= from && p.ScheduledAt < to) || (p.ScheduledAt == null && p.PublishedAt >= from && p.PublishedAt < to)))
            .ToListAsync(ct);
        var ids = posts.Select(p => p.Id).ToList();
        var targets = await db.PostTargets.AsNoTracking().Where(t => ids.Contains(t.PostId)).ToListAsync(ct);
        return CalendarLogic.Week(monday, posts, targets);
    }

    public async Task<Result<int>> ApproveWeekAsync(DateOnly monday, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var (from, to) = CalendarLogic.Bounds(monday);
        var now = clock.GetUtcNow();
        var pending = await db.Posts.Where(p => p.Status == PostStatus.AwaitingApproval && p.ScheduledAt >= from && p.ScheduledAt < to).ToListAsync(ct);
        foreach (var post in pending)
        {
            post.Status = PostStatus.Scheduled;
            if (post.ScheduledAt is null || post.ScheduledAt < now) post.ScheduledAt = now; // créneau passé : part tout de suite
        }
        await db.SaveChangesAsync(ct);
        return Result<int>.Success(pending.Count);
    }

    public async Task<Result> RemoveAsync(Guid postId, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var post = await db.Posts.FirstOrDefaultAsync(p => p.Id == postId, ct);
        if (post is null) return Result.Failure("Publication introuvable.");
        if (post.Status is PostStatus.Published or PostStatus.PartiallyPublished or PostStatus.Publishing)
            return Result.Failure("Elle est déjà partie : retirez-la directement sur le réseau.");
        post.Status = PostStatus.Cancelled;
        await db.SaveChangesAsync(ct);
        return Result.Success();
    }

    public Task<Result> ReplaceAsync(Guid postId, CancellationToken ct = default) => runner.ReplaceAsync(postId, ct);

    public async Task<Result> PrepareAsync(CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        if (!await db.AutopilotSettings.AnyAsync(ct)) return Result.Failure("Créez d'abord une campagne.");
        if (progress.Get(tenant.TenantId) is { Finished: false }) return Result.Success(); // déjà en cours
        progress.Queue(tenant.TenantId, clock.GetUtcNow());
        trigger.Request(tenant.TenantId);
        return Result.Success();
    }

    public Task<PreparationState?> PreparationAsync(CancellationToken ct = default) => Task.FromResult(progress.Get(tenant.TenantId));

    public async Task<IReadOnlyList<CalendarPost>> PreviewPostsAsync(Guid planId, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var plan = await db.MarketingPlans.AsNoTracking().FirstOrDefaultAsync(p => p.Id == planId, ct);
        if (plan is null) return [];
        var posts = await db.Posts.AsNoTracking()
            .Where(p => p.PlanId == planId && p.Status != PostStatus.Cancelled)
            .ToListAsync(ct);
        var ids = posts.Select(p => p.Id).ToList();
        var targets = await db.PostTargets.AsNoTracking().Where(t => ids.Contains(t.PostId)).ToListAsync(ct);
        return CalendarLogic.Week(CalendarLogic.Monday(BusinessTime.Today(clock)), posts, targets).Days.SelectMany(d => d.Posts)
            .Concat(CalendarLogic.Week(CalendarLogic.Monday(BusinessTime.Today(clock)).AddDays(7), posts, targets).Days.SelectMany(d => d.Posts))
            .OrderBy(p => p.At).ToList();
    }

    public async Task<IReadOnlyList<ChannelType>> ConnectedAsync(CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        return await db.Channels.AsNoTracking()
            .Where(c => c.SettingsJson != null && (c.Type == ChannelType.Facebook || c.Type == ChannelType.Instagram))
            .Select(c => c.Type).ToListAsync(ct);
    }

    public async Task<IReadOnlyList<HealthIssue>> HealthAsync(CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var now = clock.GetUtcNow();
        var settings = await db.AutopilotSettings.AsNoTracking().FirstOrDefaultAsync(ct);
        var plan = await db.MarketingPlans.AsNoTracking().FirstOrDefaultAsync(p => p.Status == PlanStatus.Active, ct);
        var connected = await db.Channels.AsNoTracking().Where(c => c.SettingsJson != null).Select(c => c.Type).ToListAsync(ct);
        var since = now.AddDays(-7);
        var failed = await db.Posts.CountAsync(p => p.Status == PostStatus.Failed && p.ScheduledAt >= since, ct);
        var expired = await db.Posts.CountAsync(p => p.Status == PostStatus.Expired && p.ScheduledAt >= since, ct);
        return MarketingHealth.Evaluate(new MarketingHealthInput(
            HasPlan: plan is not null,
            AutopilotEnabled: settings?.Enabled ?? false,
            ConnectedChannels: connected,
            LastRunSummary: settings?.LastRunSummary,
            InsightsNote: plan?.InsightsNote,
            FailedThisWeek: failed,
            ExpiredThisWeek: expired));
    }
}

public sealed record MarketingHealthInput(
    bool HasPlan,
    bool AutopilotEnabled,
    IReadOnlyCollection<ChannelType> ConnectedChannels,
    string? LastRunSummary,
    string? InsightsNote,
    int FailedThisWeek,
    int ExpiredThisWeek);

/// <summary>Règles du voyant d'état : chaque problème dit quoi faire, avec un bouton.</summary>
public static class MarketingHealth
{
    public static IReadOnlyList<HealthIssue> Evaluate(MarketingHealthInput i)
    {
        var issues = new List<HealthIssue>();
        if (!i.ConnectedChannels.Contains(ChannelType.Facebook) && !i.ConnectedChannels.Contains(ChannelType.Instagram))
            issues.Add(new(HealthLevel.Error, "Aucun réseau social n'est connecté : rien ne peut partir.", "Connecter Facebook ou Instagram", HealthAction.OpenChannels));
        if (i.LastRunSummary is { } run && run.StartsWith("0 publication", StringComparison.Ordinal) && run.Contains("Problèmes", StringComparison.Ordinal))
            issues.Add(new(HealthLevel.Error, "La dernière préparation a échoué : " + run[(run.IndexOf("Problèmes", StringComparison.Ordinal) + 12)..].Trim(),
                "Réessayer", HealthAction.Retry));
        if (!i.HasPlan)
            issues.Add(new(HealthLevel.Info, "Aucune campagne en cours : aucune publication ne sera préparée.", "Créer une campagne", HealthAction.CreateCampaign));
        else if (!i.AutopilotEnabled)
            issues.Add(new(HealthLevel.Warning, "La campagne est en cours mais la préparation automatique est en pause.", "Reprendre", HealthAction.ResumeAutopilot));
        if (i.FailedThisWeek > 0)
            issues.Add(new(HealthLevel.Error, $"{i.FailedThisWeek} publication(s) n'ont pas pu partir cette semaine.", "Voir", HealthAction.OpenPosts));
        if (i.ExpiredThisWeek > 0)
            issues.Add(new(HealthLevel.Warning, $"{i.ExpiredThisWeek} publication(s) ne sont pas parties faute de validation.", "Voir", HealthAction.OpenPosts));
        if (!string.IsNullOrWhiteSpace(i.InsightsNote))
            issues.Add(new(HealthLevel.Warning, "Chiffres des réseaux incomplets : " + i.InsightsNote, "Vérifier la connexion", HealthAction.OpenChannels));
        return issues;
    }
}

public static class CalendarLogic
{
    /// <summary>Le lundi de la semaine qui contient ce jour.</summary>
    public static DateOnly Monday(DateOnly day) => day.AddDays(-(((int)day.DayOfWeek + 6) % 7));

    public static (DateTimeOffset From, DateTimeOffset To) Bounds(DateOnly monday) =>
        (BusinessTime.FromLocal(monday.ToDateTime(TimeOnly.MinValue)), BusinessTime.FromLocal(monday.AddDays(7).ToDateTime(TimeOnly.MinValue)));

    public static CalendarWeek Week(DateOnly monday, IEnumerable<Post> posts, IEnumerable<PostTarget> targets)
    {
        var byPost = targets.ToLookup(t => t.PostId);
        var items = posts
            .Where(p => (p.ScheduledAt ?? p.PublishedAt) is not null && p.Status != PostStatus.Cancelled)
            .Select(p => new CalendarPost(p.Id, (p.ScheduledAt ?? p.PublishedAt)!.Value, string.IsNullOrWhiteSpace(p.Title) ? Clip(p.Message) : p.Title,
                p.ImageUrl, byPost[p.Id].Select(t => t.Channel).Distinct().ToList(), p.Status, p.FromAutopilot,
                byPost[p.Id].Select(t => t.Error).FirstOrDefault(e => !string.IsNullOrWhiteSpace(e))))
            .ToList();
        var days = Enumerable.Range(0, 7).Select(i => monday.AddDays(i))
            .Select(d => new CalendarDay(d, items.Where(p => AutopilotSchedule.LocalDay(p.At) == d).OrderBy(p => p.At).ToList()))
            .ToList();
        return new CalendarWeek(monday, days);
    }

    private static string Clip(string text) => text.Length <= 60 ? text : text[..60].TrimEnd() + "…";
}
