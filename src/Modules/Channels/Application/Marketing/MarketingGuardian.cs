using System.Net;
using Hub.Modules.Channels.Application.Autopilot;
using Hub.Modules.Channels.Application.Planning;
using Hub.Modules.Channels.Domain;
using Hub.SharedKernel;
using Hub.SharedKernel.Email;
using Hub.SharedKernel.Tenancy;
using Hub.SharedKernel.Time;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Hub.Modules.Channels.Application.Marketing;

/// <summary>Quand et quoi relancer : une panne passagère se relance, un problème de réglage non.</summary>
public static class RetryPolicy
{
    /// <summary>Tentatives au plus par réseau (la première comprise).</summary>
    public const int MaxAttempts = 3;

    /// <summary>Erreurs qu'une relance ne règle pas : connexion, permission, contenu refusé.</summary>
    private static readonly string[] Permanent =
    [
        "pas connecté", "permission", "autorisation", "oauth", "jeton", "token", "session has expired",
        "(#10)", "(#100)", "(#190)", "(#200)", "code 190", "invalid parameter", "paramètre invalide",
        "refus", "policy", "spam", "not authorized", "n'est pas autorisé",
    ];

    public static bool IsPermanent(string? error) =>
        error is not null && Permanent.Any(p => error.Contains(p, StringComparison.OrdinalIgnoreCase));

    /// <summary>Délai avant la tentative suivante : 30 min après la 1re, 2 h après la 2e.</summary>
    public static TimeSpan Delay(int attempts) => attempts <= 1 ? TimeSpan.FromMinutes(30) : TimeSpan.FromHours(2);

    /// <summary>Vrai quand ce réseau en échec doit être relancé maintenant.</summary>
    public static bool ShouldRetry(int attempts, DateTimeOffset lastAttempt, string? error, DateTimeOffset now, TimeSpan? delay = null) =>
        attempts < MaxAttempts && !IsPermanent(error) && now - lastAttempt >= (delay ?? Delay(attempts));
}

/// <summary>Résultat d'une vérification du gardien.</summary>
public sealed record GuardianReport(DateTimeOffset At, int Fixed, int NeedsAction, IReadOnlyList<string> Lines);

/// <summary>Dernière vérification et journal, pour le calendrier.</summary>
public sealed record GuardianStatus(DateTimeOffset? LastCheck, int Fixed, int NeedsAction, IReadOnlyList<MaintenanceEntry> Journal);

/// <summary>
/// Le gardien : toutes les 30 minutes, il vérifie ce qui a échoué et relance ce qui peut l'être.
/// Il ne publie jamais une publication non approuvée, ne relance jamais une publication retirée,
/// et ne republie jamais sur un réseau où elle est déjà partie.
/// </summary>
internal sealed class MarketingGuardian(
    IDbContextFactory<ChannelsDbContext> factory,
    PostPublishing publishing,
    AutopilotTrigger trigger,
    AutopilotProgress progress,
    PlanInsightsCollector insights,
    Reviews.IReviewService reviews,
    IChannelConnections connections,
    Decisions.DecisionJournal decisions,
    Ads.IAdService ads,
    Reels.IReelService reels,
    IAutopilotContextSource source,
    IEmailSender email,
    ITenantContext tenant,
    Microsoft.Extensions.Options.IOptions<ChannelsOptions> options,
    TimeProvider clock,
    ILogger<MarketingGuardian> logger)
{
    public const string Check = "Verification";

    /// <summary>Repère discret (hors journal) : Google a déjà été ajouté à cette publication ou à la campagne, une seule fois.</summary>
    public const string GoogleMarker = "GoogleAjout";
    private static readonly TimeSpan StuckAfter = TimeSpan.FromMinutes(15);

    public async Task<GuardianReport> RunAsync(CancellationToken ct = default)
    {
        var now = clock.GetUtcNow();
        var entries = new List<MaintenanceEntry>();
        void Log(string kind, string message, bool success, Guid? postId = null) =>
            entries.Add(new MaintenanceEntry { At = now, Kind = kind, Message = message.Truncate(1000), Success = success, PostId = postId });

        await using var db = await factory.CreateDbContextAsync(ct);
        var since = now.AddDays(-1);
        var alreadyFlagged = (await db.MaintenanceEntries.AsNoTracking()
                .Where(e => !e.Success && e.PostId != null && e.At >= now.AddDays(-2)).Select(e => e.PostId!.Value).ToListAsync(ct))
            .ToHashSet();

        // 1. Envois bloqués « en cours » (redémarrage pendant l'envoi) : remis dans la file.
        foreach (var post in await db.Posts.Where(p => p.Status == PostStatus.Publishing && p.UpdatedAt < now - StuckAfter).ToListAsync(ct))
        {
            post.Status = PostStatus.Scheduled;
            Log("Blocage", $"« {post.Title} » était bloquée pendant l'envoi : remise dans la file.", true, post.Id);
        }
        await db.SaveChangesAsync(ct);

        // 2. Publications en échec (tout ou partie) : relance seulement des réseaux en échec, si la panne est passagère.
        var failed = await db.Posts.AsNoTracking()
            .Where(p => (p.Status == PostStatus.Failed || p.Status == PostStatus.PartiallyPublished) && (p.ScheduledAt ?? p.UpdatedAt) >= since)
            .ToListAsync(ct);
        foreach (var post in failed)
        {
            var targets = await db.PostTargets.AsNoTracking().Where(t => t.PostId == post.Id && t.Status == PostTargetStatus.Failed).ToListAsync(ct);
            if (targets.Count == 0) continue;
            var delay = options.Value.RetryDelayMinutes is { } m ? TimeSpan.FromMinutes(m) : (TimeSpan?)null;
            var retry = targets.Where(t => RetryPolicy.ShouldRetry(t.Attempts, t.UpdatedAt, t.Error, now, delay)).ToList();
            var stuck = targets.Where(t => RetryPolicy.IsPermanent(t.Error) || t.Attempts >= RetryPolicy.MaxAttempts).ToList();

            if (retry.Count > 0)
            {
                var result = await publishing.PublishAsync(post.Id, ct);
                var networks = string.Join(" et ", retry.Select(t => t.Channel));
                Log("Publication", result.IsSuccess
                        ? $"« {post.Title} » : relancée sur {networks}, partie."
                        : $"« {post.Title} » : relance sur {networks} sans succès ({result.Error}). Nouvel essai plus tard.",
                    result.IsSuccess, post.Id);
            }
            if (stuck.Count > 0 && !alreadyFlagged.Contains(post.Id))
            {
                var t = stuck[0];
                Log("Publication", RetryPolicy.IsPermanent(t.Error)
                        ? $"« {post.Title} » n'est pas partie sur {t.Channel} : {t.Error} (une relance n'y changerait rien)."
                        : $"« {post.Title} » n'est pas partie sur {t.Channel} après {t.Attempts} tentatives : {t.Error}",
                    false, post.Id);
            }
        }

        // 3. Préparation ratée : relancée, jusqu'à 3 fois par jour.
        var settings = await db.AutopilotSettings.AsNoTracking().FirstOrDefaultAsync(ct);
        var plan = await db.MarketingPlans.AsNoTracking().FirstOrDefaultAsync(p => p.Status == PlanStatus.Active, ct);
        var dayStart = BusinessTime.FromLocal(BusinessTime.Today(clock).ToDateTime(TimeOnly.MinValue));
        var running = progress.Get(tenant.TenantId) is { Finished: false };
        var preparedToday = await db.MaintenanceEntries.CountAsync(e => e.Kind == "Preparation" && e.At >= dayStart, ct);
        var lastFailed = settings?.LastRunSummary is { } run && run.StartsWith("0 publication", StringComparison.Ordinal) && run.Contains("Problèmes", StringComparison.Ordinal);
        if (settings is { Enabled: true } && plan is not null && !running && lastFailed)
        {
            if (preparedToday < 3)
            {
                progress.Queue(tenant.TenantId, now);
                trigger.Request(tenant.TenantId);
                Log("Preparation", "La dernière préparation avait échoué : relancée.", true);
            }
            else if (!await db.MaintenanceEntries.AnyAsync(e => e.Kind == "Preparation" && !e.Success && e.At >= dayStart, ct))
            {
                Log("Preparation", "La préparation échoue encore après 3 relances aujourd'hui : " + settings.LastRunSummary, false);
            }
        }

        // 4. Semaine pas couverte : moins de publications prévues dans les 3 prochains jours que le rythme de la campagne.
        if (settings is { Enabled: true } && plan is not null && !running && !lastFailed)
        {
            var in3Days = await db.Posts.CountAsync(p => p.FromAutopilot && p.ScheduledAt > now && p.ScheduledAt <= now.AddDays(3)
                && (p.Status == PostStatus.AwaitingApproval || p.Status == PostStatus.Scheduled), ct);
            var expected = settings.PostsPerWeek * 3 / 7;
            var recently = await db.MaintenanceEntries.AnyAsync(e => e.Kind == "Semaine" && e.At >= now.AddHours(-6), ct);
            if (in3Days < expected && !recently && (settings.LastRunAt is null || settings.LastRunAt < now.AddHours(-2)))
            {
                progress.Queue(tenant.TenantId, now);
                trigger.Request(tenant.TenantId);
                Log("Semaine", $"Seulement {in3Days} publication(s) prévue(s) dans les 3 prochains jours (au moins {expected} attendues) : semaine complétée.", true);
            }
        }

        // 5. Chiffres des réseaux pas relevés depuis plus d'un jour.
        if (plan is not null && (plan.InsightsAt is null || plan.InsightsAt < now.AddHours(-26))
            && !await db.MaintenanceEntries.AnyAsync(e => e.Kind == "Chiffres" && e.At >= now.AddHours(-6), ct))
        {
            var note = await insights.CollectAsync(ct);
            Log("Chiffres", note is null ? "Chiffres des réseaux relevés (le relevé quotidien n'avait pas abouti)." : "Relevé des chiffres incomplet : " + note, note is null);
        }

        // 6. Avis Google : relevés à chaque passage (les nouveaux sont signalés par courriel).
        if (await reviews.IsConnectedAsync(ct))
        {
            var synced = await reviews.SyncAsync(ct);
            if (synced.IsFailure)
            {
                if (!await db.MaintenanceEntries.AnyAsync(e => e.Kind == "Avis" && !e.Success && e.At >= now.AddHours(-6), ct))
                    Log("Avis", "Avis Google non relevés : " + synced.Error, RetryPolicy.IsPermanent(synced.Error) ? false : true);
            }
            else if (synced.Value!.Count > 0)
            {
                Log("Avis", $"{synced.Value.Count} nouvel(s) avis Google relevé(s).", true);
            }
        }

        // 7. Google Business connecté : les publications à venir partent aussi sur la fiche Google, et la campagne aussi.
        //    Une seule fois par publication : si vous retirez Google d'une publication, le gardien ne le remet pas.
        var markers = new List<MaintenanceEntry>();
        if ((await connections.GetStatusAsync(ChannelType.GoogleBusiness, ct)).IsConnected)
        {
            var done = (await db.MaintenanceEntries.AsNoTracking().Where(e => e.Kind == GoogleMarker)
                .Select(e => e.PostId).ToListAsync(ct)).ToHashSet();
            var upcoming = await db.Posts
                .Where(p => (p.Status == PostStatus.AwaitingApproval || p.Status == PostStatus.Scheduled) && p.ScheduledAt > now
                    && !db.PostTargets.Any(t => t.PostId == p.Id && t.Channel == ChannelType.GoogleBusiness))
                .Select(p => p.Id).ToListAsync(ct);
            var added = upcoming.Where(id => !done.Contains(id)).ToList();
            foreach (var id in added)
            {
                db.PostTargets.Add(new PostTarget { PostId = id, Channel = ChannelType.GoogleBusiness });
                markers.Add(new MaintenanceEntry { At = now, Kind = GoogleMarker, Success = true, PostId = id, Message = "Google ajouté." });
            }
            if (added.Count > 0) Log("Google", $"{added.Count} publication(s) à venir partiront aussi sur votre fiche Google.", true);

            var autopilot = await db.AutopilotSettings.FirstOrDefaultAsync(ct);
            if (autopilot is not null && !done.Contains(null))
            {
                var channels = AutopilotService.ReadChannels(autopilot.Channels);
                if (!channels.Contains(ChannelType.GoogleBusiness))
                {
                    autopilot.Channels = string.Join(",", channels.Append(ChannelType.GoogleBusiness));
                    Log("Google", "Les prochaines publications de la campagne partiront aussi sur Google.", true);
                }
                markers.Add(new MaintenanceEntry { At = now, Kind = GoogleMarker, Success = true, Message = "Google ajouté à la campagne." });
            }
        }

        // 9. Reels demandés par le pilote dont la vidéo n'est pas encore prête.
        var rendered = await reels.RenderPendingAsync(ct);
        if (rendered > 0) Log("Reels", $"{rendered} vidéo(s) de Reel préparée(s).", true);

        // 10. Publicités payantes : état chez Meta (examen, refus, fin) et dépense, avant le journal (le coût compte dans la récompense).
        await ads.SyncAsync(ct);

        // 8. Journal des décisions : publications à journaliser, résultats à clôturer (J+7) et à finaliser (J+30).
        var sync = await decisions.SyncAsync(ct);
        if (sync.Closed + sync.Finalized > 0)
            Log("Décisions", $"{sync.Closed} décision(s) clôturée(s), {sync.Finalized} finalisée(s)"
                + (sync.AverageReward is { } avg ? $" : récompense moyenne {avg.ToString("0.#", System.Globalization.CultureInfo.GetCultureInfo("fr-CA"))}." : "."), true);
        if (sync.Recorded > 0)
            Log("Décisions", $"{sync.Recorded} publication(s) ajoutée(s) au journal des décisions.", true);

        var needsAction = entries.Count(e => !e.Success);
        var fixedCount = entries.Count(e => e.Success);
        entries.Add(new MaintenanceEntry
        {
            At = now, Kind = Check, Success = needsAction == 0,
            Message = $"Vérification : {fixedCount} relance(s), {needsAction} problème(s) à régler.",
        });
        // Le journal ne garde que 30 jours.
        await db.MaintenanceEntries.Where(e => e.At < now.AddDays(-30) && e.Kind != GoogleMarker).ExecuteDeleteAsync(ct);
        db.MaintenanceEntries.AddRange(entries);
        db.MaintenanceEntries.AddRange(markers);
        await db.SaveChangesAsync(ct);

        if (needsAction > 0) await AlertAsync(db, entries.Where(e => !e.Success && e.Kind != Check).ToList(), dayStart, ct);
        var lines = entries.Where(e => e.Kind != Check).Select(e => e.Message).ToList();
        if (lines.Count > 0) logger.LogInformation("Gardien : {Lines}", string.Join(" | ", lines));
        return new GuardianReport(now, fixedCount, needsAction, lines);
    }

    /// <summary>Un courriel seulement quand il faut agir, au plus un par jour.</summary>
    private async Task AlertAsync(ChannelsDbContext db, IReadOnlyList<MaintenanceEntry> problems, DateTimeOffset dayStart, CancellationToken ct)
    {
        if (problems.Count == 0 || await db.MaintenanceEntries.AnyAsync(e => e.Kind == "Alerte" && e.At >= dayStart, ct)) return;
        var context = await source.GetAsync(ct);
        if (string.IsNullOrWhiteSpace(context.AdminEmail)) return;
        var link = $"{(context.SiteUrl ?? "").TrimEnd('/')}/admin/marketing";
        var items = string.Join("", problems.Select(p => $"<li>{WebUtility.HtmlEncode(p.Message)}</li>"));
        var html = $"""
            <p>Bonjour,</p>
            <p>La vérification automatique de {WebUtility.HtmlEncode(context.CompanyName)} a trouvé un problème qu'une relance ne peut pas régler :</p>
            <ul>{items}</ul>
            <p><a href="{WebUtility.HtmlEncode(link)}">Ouvrir le calendrier</a> : le voyant indique quoi faire.</p>
            """;
        var sent = await email.SendAsync(new EmailMessage(context.AdminEmail, "Marketing : un problème à régler", html), ct);
        db.MaintenanceEntries.Add(new MaintenanceEntry { At = clock.GetUtcNow(), Kind = "Alerte", Success = sent.IsSuccess,
            Message = sent.IsSuccess ? "Courriel d'alerte envoyé." : "Courriel d'alerte non envoyé : " + sent.Error });
        await db.SaveChangesAsync(ct);
    }
}

/// <summary>Toutes les 30 minutes, le gardien passe pour chaque entreprise qui publie sur les réseaux.</summary>
internal sealed class MarketingGuardianWorker(IServiceScopeFactory scopes, ILogger<MarketingGuardianWorker> logger) : BackgroundService
{
    public static readonly TimeSpan Interval = TimeSpan.FromMinutes(30);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try { await Task.Delay(TimeSpan.FromMinutes(2), stoppingToken); }
        catch (OperationCanceledException) { return; }

        using var timer = new PeriodicTimer(Interval);
        do
        {
            try { await RunOnceAsync(stoppingToken); }
            catch (Exception ex) when (ex is not OperationCanceledException) { logger.LogError(ex, "Gardien du marketing"); }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    internal async Task RunOnceAsync(CancellationToken ct)
    {
        List<Guid> tenants;
        await using (var scope = scopes.CreateAsyncScope())
        {
            var factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<ChannelsDbContext>>();
            await using var db = await factory.CreateDbContextAsync(ct);
            tenants = await db.AutopilotSettings.IgnoreQueryFilters().Select(s => s.TenantId)
                .Union(db.Posts.IgnoreQueryFilters().Where(p => p.Status == PostStatus.Failed || p.Status == PostStatus.PartiallyPublished || p.Status == PostStatus.Publishing).Select(p => p.TenantId))
                .Distinct().ToListAsync(ct);
        }
        foreach (var tenantId in tenants)
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                scope.ServiceProvider.GetRequiredService<ITenantContext>().Set(tenantId);
                await scope.ServiceProvider.GetRequiredService<MarketingGuardian>().RunAsync(ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Gardien du marketing pour l'entreprise {TenantId}", tenantId);
            }
        }
    }
}
