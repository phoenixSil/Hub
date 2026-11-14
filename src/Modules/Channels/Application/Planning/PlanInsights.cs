using System.Collections.Concurrent;
using Hub.Modules.Channels.Application.Autopilot;
using Hub.Modules.Channels.Domain;
using Hub.Modules.Channels.Publishers;
using Hub.SharedKernel.Tenancy;
using Hub.SharedKernel.Time;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Hub.Modules.Channels.Application.Planning;

/// <summary>Une publication parue, avec ses chiffres cumulés sur tous les réseaux.</summary>
public sealed record MeasuredPost(Guid Id, DateTimeOffset PublishedAt, string? Pillar, int Reach, int Interactions);

/// <summary>Calcul des chiffres du plan à partir des publications mesurées et des abonnés.</summary>
public static class PlanMeasures
{
    /// <summary>Personnes atteintes par les publications des 7 derniers jours, abonnés, taux d'interaction sur 28 jours.</summary>
    public static PlanNumbers Current(IReadOnlyList<MeasuredPost> posts, int followers, DateTimeOffset now)
    {
        var week = posts.Where(p => p.PublishedAt > now.AddDays(-7) && p.PublishedAt <= now).ToList();
        var month = posts.Where(p => p.PublishedAt > now.AddDays(-28) && p.PublishedAt <= now).ToList();
        return new PlanNumbers(week.Sum(p => p.Reach), followers, PlanLogic.EngagementRate(month.Sum(p => p.Interactions), month.Sum(p => p.Reach)));
    }

    public static IReadOnlyList<PlanWeek> Weeks(IReadOnlyList<MeasuredPost> posts, DateOnly from, DateOnly to, DateOnly today)
    {
        var weeks = new List<PlanWeek>();
        var last = today < to ? today : to;
        for (var start = from; start <= last; start = start.AddDays(7))
        {
            var end = start.AddDays(7);
            var inWeek = posts.Where(p => { var d = AutopilotSchedule.LocalDay(p.PublishedAt); return d >= start && d < end; }).ToList();
            weeks.Add(new PlanWeek(start, inWeek.Count, inWeek.Sum(p => p.Reach), inWeek.Sum(p => p.Interactions)));
        }
        return weeks;
    }

    /// <summary>Résultats par pilier : nombre de publications parues et personnes atteintes en moyenne.</summary>
    public static IReadOnlyDictionary<string, (int Posts, double AvgReach)> ByPillar(IEnumerable<MeasuredPost> posts) =>
        posts.Where(p => p.Pillar is not null)
            .GroupBy(p => p.Pillar!)
            .ToDictionary(g => g.Key, g => (g.Count(), g.Average(p => (double)p.Reach)));

    internal static async Task<IReadOnlyList<MeasuredPost>> PublishedAsync(ChannelsDbContext db, DateTimeOffset since, CancellationToken ct)
    {
        var posts = await db.Posts.AsNoTracking()
            .Where(p => (p.Status == PostStatus.Published || p.Status == PostStatus.PartiallyPublished) && p.PublishedAt >= since)
            .Select(p => new { p.Id, p.PublishedAt, p.Pillar })
            .ToListAsync(ct);
        var ids = posts.Select(p => p.Id).ToList();
        var stats = await db.PostStats.AsNoTracking().Where(s => ids.Contains(s.PostId))
            .GroupBy(s => s.PostId)
            .Select(g => new { PostId = g.Key, Reach = g.Sum(s => s.Reach), Interactions = g.Sum(s => s.Interactions) })
            .ToDictionaryAsync(s => s.PostId, ct);
        return posts.Select(p =>
        {
            var s = stats.GetValueOrDefault(p.Id);
            return new MeasuredPost(p.Id, p.PublishedAt!.Value, p.Pillar, s?.Reach ?? 0, s?.Interactions ?? 0);
        }).ToList();
    }

    internal static async Task<int> FollowersAsync(ChannelsDbContext db, CancellationToken ct)
    {
        var rows = await db.AudienceSnapshots.AsNoTracking()
            .OrderByDescending(s => s.Day).Take(200)
            .Select(s => new { s.Channel, s.Day, s.Followers })
            .ToListAsync(ct);
        // Le dernier relevé de chaque réseau.
        return rows.GroupBy(r => r.Channel).Sum(g => g.First().Followers);
    }

    internal static async Task<MarketingPlanView> BuildViewAsync(
        ChannelsDbContext db, MarketingPlan plan, bool autoPublish, DateOnly today, DateTimeOffset now, CancellationToken ct)
    {
        var phases = PlanLogic.Phases(plan.PhasesJson);
        var pillars = PlanLogic.Pillars(plan.PillarsJson);
        var since = BusinessTime.FromLocal(plan.StartsOn.AddDays(-28).ToDateTime(TimeOnly.MinValue));
        var posts = await PublishedAsync(db, since, ct);
        var current = Current(posts, await FollowersAsync(db, ct), now);
        var baseline = PlanLogic.Numbers(plan.BaselineJson);
        var targets = PlanLogic.Numbers(plan.TargetsJson);
        var expected = baseline is not null && targets is not null && plan.BaselineOn is { } from
            ? PlanLogic.Expected(baseline, targets, from, plan.EndsOn, today)
            : null;

        // Répartition réelle : publications du plan parues ou prévues (pas les refusées).
        var startAt = BusinessTime.FromLocal(plan.StartsOn.ToDateTime(TimeOnly.MinValue));
        var endAt = BusinessTime.FromLocal(plan.EndsOn.AddDays(1).ToDateTime(TimeOnly.MinValue));
        var planned = await db.Posts.AsNoTracking()
            .Where(p => p.Pillar != null && p.Status != PostStatus.Cancelled && p.Status != PostStatus.Failed
                        && (p.PublishedAt ?? p.ScheduledAt) >= startAt && (p.PublishedAt ?? p.ScheduledAt) < endAt)
            .GroupBy(p => p.Pillar!)
            .Select(g => new { Pillar = g.Key, Count = g.Count() })
            .ToDictionaryAsync(g => g.Pillar, g => g.Count, ct);
        var total = Math.Max(1, planned.Values.Sum());
        var reachByPillar = ByPillar(posts.Where(p => p.PublishedAt >= startAt));
        var mix = pillars.Select(p => new PillarActual(p.Name, p.Share, planned.GetValueOrDefault(p.Name),
                Math.Round(planned.GetValueOrDefault(p.Name) * 100.0 / total, 0),
                reachByPillar.TryGetValue(p.Name, out var r) ? Math.Round(r.AvgReach, 0) : 0))
            .ToList();

        var totalWeeks = Math.Max(1, (plan.EndsOn.DayNumber - plan.StartsOn.DayNumber + 1 + 6) / 7);
        var week = Math.Clamp((today.DayNumber - plan.StartsOn.DayNumber) / 7 + 1, 0, totalWeeks);
        return new MarketingPlanView(
            plan.Id, MarketingPlanService.NameFor(plan.Name, plan.StartsOn, plan.EndsOn), plan.Objective, plan.Status, plan.StartsOn, plan.EndsOn, plan.PostsPerWeek,
            phases, pillars, PlanLogic.PhaseAt(phases, today), today < plan.StartsOn ? 0 : week, totalWeeks,
            current, baseline, plan.BaselineOn, targets, expected,
            Weeks(posts.Where(p => p.PublishedAt >= startAt).ToList(), plan.StartsOn, plan.EndsOn, today),
            mix,
            PlanLogic.Numbers(plan.ProposedTargetsJson),
            plan.ProposedPillarsJson is null ? null : PlanLogic.Pillars(plan.ProposedPillarsJson),
            plan.ProposalNote,
            plan.Status == PlanStatus.Active && !autoPublish && plan.ApprovalReviewOn is { } review && today >= review,
            plan.InsightsAt, plan.InsightsNote);
    }
}

/// <summary>
/// Relève chaque jour les chiffres des réseaux (personnes atteintes et interactions par publication, abonnés),
/// puis fait avancer le plan : point de départ et cibles après la première semaine, ajustement en fin d'étape.
/// </summary>
internal sealed class PlanInsightsCollector(
    IDbContextFactory<ChannelsDbContext> factory,
    IChannelConnections connections,
    IPublisherResolver resolver,
    TimeProvider clock,
    ILogger<PlanInsightsCollector> logger)
{
    private static readonly ChannelType[] Measured = [ChannelType.Facebook, ChannelType.Instagram];
    public const int MeasureDays = 35;

    /// <summary>Renvoie le problème rencontré (permission manquante, réseau injoignable), ou null.</summary>
    public async Task<string?> CollectAsync(CancellationToken ct = default)
    {
        var now = clock.GetUtcNow();
        var today = BusinessTime.Today(clock);
        var problems = new List<string>();
        await using var db = await factory.CreateDbContextAsync(ct);

        foreach (var channel in Measured)
        {
            var insights = resolver.InsightsFor(channel);
            var credentials = await connections.GetCredentialsAsync(channel, ct);
            if (insights is null || credentials is null) continue;

            var followers = await insights.GetFollowersAsync(credentials, ct);
            if (followers.IsSuccess)
            {
                var snapshot = await db.AudienceSnapshots.FirstOrDefaultAsync(s => s.Channel == channel && s.Day == today, ct);
                if (snapshot is null) db.AudienceSnapshots.Add(snapshot = new AudienceSnapshot { Channel = channel, Day = today });
                snapshot.Followers = followers.Value;
            }
            else problems.Add(followers.Error!);

            var since = now.AddDays(-MeasureDays);
            var targets = await db.PostTargets.AsNoTracking()
                .Where(t => t.Channel == channel && t.Status == PostTargetStatus.Published && t.ExternalId != null && t.PublishedAt >= since)
                .Select(t => new { t.PostId, t.ExternalId })
                .ToListAsync(ct);
            var postIds = targets.Select(t => t.PostId).ToList();
            var existing = await db.PostStats.Where(s => s.Channel == channel && postIds.Contains(s.PostId))
                .ToDictionaryAsync(s => s.PostId, ct);
            foreach (var target in targets)
            {
                var stats = await insights.GetPostAsync(target.ExternalId!, credentials, ct);
                if (stats.IsFailure)
                {
                    problems.Add(stats.Error!);
                    break; // même problème pour toutes : inutile d'insister
                }
                if (!existing.TryGetValue(target.PostId, out var row))
                    db.PostStats.Add(row = new PostStats { PostId = target.PostId, Channel = channel });
                // Une publication ne « perd » pas de personnes atteintes : on garde le plus haut relevé.
                row.Reach = Math.Max(row.Reach, stats.Value!.Reach);
                row.Interactions = Math.Max(row.Interactions, stats.Value.Interactions);
                row.MeasuredAt = now;
            }
        }
        await db.SaveChangesAsync(ct);

        var note = problems.Count == 0 ? null : string.Join(" ", problems.Distinct()).Truncate(1000);
        var plan = await MarketingPlanService.CurrentAsync(db, tracking: true, ct);
        if (plan is not null && plan.Status == PlanStatus.Active)
        {
            var posts = await PlanMeasures.PublishedAsync(db, BusinessTime.FromLocal(plan.StartsOn.AddDays(-28).ToDateTime(TimeOnly.MinValue)), ct);
            PlanReviewer.Review(plan, posts, await PlanMeasures.FollowersAsync(db, ct), today, now);
            plan.InsightsAt = now;
            plan.InsightsNote = note;
            await db.SaveChangesAsync(ct);
        }
        if (note is not null) logger.LogWarning("Chiffres des réseaux incomplets : {Note}", note);
        return note;
    }
}

/// <summary>Ce que le plan fait de lui-même au fil des jours (toujours sous forme de proposition à approuver).</summary>
public static class PlanReviewer
{
    public const int BaselineDays = 7;

    public static void Review(MarketingPlan plan, IReadOnlyList<MeasuredPost> posts, int followers, DateOnly today, DateTimeOffset now)
    {
        if (plan.Status != PlanStatus.Active) return;
        if (today > plan.EndsOn)
        {
            plan.Status = PlanStatus.Completed;
            return;
        }

        // 1. Après la première semaine : le point de départ, puis des cibles proposées.
        if (plan.BaselineJson is null && today >= plan.StartsOn.AddDays(BaselineDays))
        {
            var baseline = PlanMeasures.Current(posts, followers, now);
            if (baseline.WeeklyReach > 0 || baseline.Followers > 0)
            {
                plan.BaselineJson = PlanLogic.Write(baseline);
                plan.BaselineOn = today;
                plan.ProposedTargetsJson = PlanLogic.Write(PlanLogic.ProposeTargets(baseline));
                plan.ProposalNote = "Point de départ mesuré sur la première semaine. Cibles proposées : " + PlanLogic.TargetsRule;
            }
        }

        // 2. À la fin de chaque étape : ajuster la répartition des piliers selon ce qui atteint le plus de monde.
        var phases = PlanLogic.Phases(plan.PhasesJson);
        var ended = phases.Where(p => p.To < today && p.To >= plan.StartsOn).OrderBy(p => p.To).LastOrDefault();
        if (ended is not null && ended.Name != plan.LastReviewedPhase)
        {
            plan.LastReviewedPhase = ended.Name;
            var inPhase = posts.Where(p => { var d = AutopilotSchedule.LocalDay(p.PublishedAt); return d >= ended.From && d <= ended.To; });
            var proposal = PlanLogic.ProposePillars(PlanLogic.Pillars(plan.PillarsJson), PlanMeasures.ByPillar(inPhase));
            if (proposal is { } p)
            {
                plan.ProposedPillarsJson = PlanLogic.Write(p.Pillars);
                plan.ProposalNote = $"Fin de l'étape « {ended.Name} ». {p.Note}";
            }
        }
    }
}

/// <summary>Une fois par jour et par entreprise (plan actif ou réseau connecté) : relevé des chiffres et revue du plan.</summary>
internal sealed class PlanInsightsWorker(IServiceScopeFactory scopes, TimeProvider clock, ILogger<PlanInsightsWorker> logger) : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromHours(1);
    private static readonly TimeSpan RunEvery = TimeSpan.FromHours(20);
    private readonly ConcurrentDictionary<Guid, DateTimeOffset> lastRun = new();

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try { await Task.Delay(TimeSpan.FromMinutes(2), stoppingToken); }
        catch (OperationCanceledException) { return; }

        while (!stoppingToken.IsCancellationRequested)
        {
            try { await RunDueAsync(stoppingToken); }
            catch (Exception ex) when (ex is not OperationCanceledException) { logger.LogError(ex, "Relevé des chiffres des réseaux"); }
            try { await Task.Delay(Interval, stoppingToken); }
            catch (OperationCanceledException) { return; }
        }
    }

    private async Task RunDueAsync(CancellationToken ct)
    {
        List<Guid> tenants;
        await using (var scope = scopes.CreateAsyncScope())
        {
            var factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<ChannelsDbContext>>();
            await using var db = await factory.CreateDbContextAsync(ct);
            var withPlan = await db.MarketingPlans.IgnoreQueryFilters().Where(p => p.Status == PlanStatus.Active).Select(p => p.TenantId).ToListAsync(ct);
            var connected = await db.Channels.IgnoreQueryFilters().Where(c => c.SettingsJson != null).Select(c => c.TenantId).ToListAsync(ct);
            tenants = withPlan.Union(connected).Distinct().ToList();
        }

        var now = clock.GetUtcNow();
        foreach (var tenantId in tenants)
        {
            if (lastRun.TryGetValue(tenantId, out var at) && now - at < RunEvery) continue;
            lastRun[tenantId] = now;
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                scope.ServiceProvider.GetRequiredService<ITenantContext>().Set(tenantId);
                await scope.ServiceProvider.GetRequiredService<PlanInsightsCollector>().CollectAsync(ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Relevé des chiffres de l'entreprise {TenantId}", tenantId);
            }
        }
    }
}
