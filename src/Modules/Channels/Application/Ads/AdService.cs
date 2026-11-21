using Hub.Modules.Channels.Application.Autopilot;
using Hub.Modules.Channels.Domain;
using Hub.SharedKernel;
using Hub.SharedKernel.Tenancy;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Hub.Modules.Channels.Application.Ads;

/// <summary>Règles des publicités, sans accès aux données (testables).</summary>
public static class AdRules
{
    /// <summary>Dépense minimale par jour exigée par Meta, avec une marge (en dollars).</summary>
    public const decimal MinPerDay = 2m;
    public const decimal MaxBudget = 5000m;

    public static IReadOnlyDictionary<string, string> Validate(AdDraft d, AdMonth month)
    {
        var e = new Dictionary<string, string>();
        if (d.Days is < 1 or > 30) e[nameof(d.Days)] = "Entre 1 et 30 jours.";
        if (d.Budget <= 0) e[nameof(d.Budget)] = "Indiquez un budget total.";
        else if (d.Budget > MaxBudget) e[nameof(d.Budget)] = $"Au plus {MaxBudget:0} $ par publicité.";
        else if (d.Days >= 1 && d.Budget / d.Days < MinPerDay)
            e[nameof(d.Budget)] = $"Au moins {MinPerDay:0} $ par jour (minimum de Meta) : {MinPerDay * d.Days:0} $ pour {d.Days} jour{(d.Days > 1 ? "s" : "")}.";
        else if (month.Remaining is { } left && d.Budget > left)
            e[nameof(d.Budget)] = $"Ce budget dépasserait votre plafond mensuel ({month.Committed:0.##} $ engagés sur {month.Cap:0.##} $ ; reste {left:0.##} $).";
        if (!d.Facebook && !d.Instagram) e[nameof(d.Facebook)] = "Choisissez au moins un réseau.";
        if (d.RadiusKm is < 5 or > 80) e[nameof(d.RadiusKm)] = "Entre 5 et 80 km.";
        if (d.AgeMin < 18 || d.AgeMax > 65 || d.AgeMin >= d.AgeMax) e[nameof(d.AgeMin)] = "Âges entre 18 et 65 ans (65 = 65 ans et plus).";
        if (d.PostId is null) e[nameof(d.PostId)] = d.Kind == AdKind.Boost ? "Choisissez la publication à booster." : "Choisissez la publication dont le visuel servira.";
        if (d.Kind == AdKind.Boost)
        {
            if (d.Source is not (ChannelType.Facebook or ChannelType.Instagram)) e[nameof(d.Source)] = "Choisissez la publication Facebook ou Instagram à booster.";
            if (d.Objective == AdObjective.Quotes) e[nameof(d.Objective)] = "Un boost vise les interactions ou la portée.";
        }
        else
        {
            if (string.IsNullOrWhiteSpace(d.Message)) e[nameof(d.Message)] = "Le texte de l'annonce est obligatoire.";
            else if (d.Message.Length > 2000) e[nameof(d.Message)] = "2000 caractères au plus.";
            if (string.IsNullOrWhiteSpace(d.Headline)) e[nameof(d.Headline)] = "Le titre est obligatoire.";
            else if (d.Headline.Length > 60) e[nameof(d.Headline)] = "60 caractères au plus.";
            if (d.Objective == AdObjective.Engagement) e[nameof(d.Objective)] = "Une campagne vise les demandes de devis ou la notoriété.";
            if (string.IsNullOrWhiteSpace(d.LinkPath) || !(d.LinkPath.StartsWith('/') || d.LinkPath.StartsWith("https://")))
                e[nameof(d.LinkPath)] = "Une page du site, par exemple /devis.";
        }
        return e;
    }

    /// <summary>Dates de diffusion : début demandé (jamais dans le passé), fin après le nombre de jours.</summary>
    public static (DateTimeOffset Start, DateTimeOffset End) Window(AdDraft d, DateTimeOffset now)
    {
        var start = d.StartAt is { } s && s > now ? s : now.AddMinutes(5);
        return (start, start.AddDays(d.Days));
    }

    /// <summary>État Hub d'après l'état de l'annonce chez Meta et les dates.</summary>
    public static AdStatus Map(AdStatus current, string effective, DateTimeOffset endAt, DateTimeOffset now)
    {
        if (current is AdStatus.Stopped or AdStatus.Failed) return current;
        if (effective == "DISAPPROVED") return AdStatus.Rejected;
        if (effective is "ARCHIVED" or "DELETED") return AdStatus.Stopped;
        if (now >= endAt) return AdStatus.Completed;
        return effective switch
        {
            "ACTIVE" => AdStatus.Active,
            "PAUSED" or "CAMPAIGN_PAUSED" or "ADSET_PAUSED" => AdStatus.Paused,
            "PENDING_REVIEW" or "IN_PROCESS" or "PREAPPROVED" => AdStatus.InReview,
            _ => current,
        };
    }

    /// <summary>Lien de l'annonce, mesuré : « …/devis?utm_source=meta&amp;utm_medium=paid&amp;utm_campaign=pub-… ».</summary>
    public static string? Link(string? siteUrl, string? path, string tracking)
    {
        var absolute = TrackedLink.Absolute(siteUrl, path);
        if (absolute is null) return null;
        return $"{absolute}{(absolute.Contains('?') ? '&' : '?')}utm_source=meta&utm_medium=paid&utm_campaign={Uri.EscapeDataString(tracking)}";
    }
}

internal sealed class AdService(
    IDbContextFactory<ChannelsDbContext> factory,
    IAdsPlatform platform,
    IChannelConnections connections,
    ITenantInfoProvider tenantInfo,
    IAutopilotContextSource context,
    IDataProtectionProvider protection,
    TimeProvider clock,
    ILogger<AdService> logger) : IAdService
{
    private readonly IDataProtector _protector = protection.CreateProtector("Hub.Channels.Ads.v1");

    public async Task<AdSettingsView> SettingsAsync(CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var s = await db.AdSettings.AsNoTracking().FirstOrDefaultAsync(ct) ?? new AdSettings();
        var fb = await connections.GetStatusAsync(ChannelType.Facebook, ct);
        var ig = await connections.GetStatusAsync(ChannelType.Instagram, ct);
        return new AdSettingsView(s.ProtectedToken is not null && s.AdAccountId is not null, s.AdAccountId, s.AdAccountName, s.Currency,
            s.MonthlyCap, s.RadiusKm, s.AgeMin, s.AgeMax, platform.IsSimulated, fb.IsConnected || platform.IsSimulated, ig.IsConnected || platform.IsSimulated);
    }

    public async Task<Result<string>> ConnectAsync(string accountId, string token, CancellationToken ct = default)
    {
        accountId = accountId.Trim();
        if (accountId.StartsWith("act_", StringComparison.OrdinalIgnoreCase)) accountId = accountId[4..];
        token = token.Trim();
        if (accountId.Length == 0 || !accountId.All(char.IsAsciiDigit)) return Result<string>.Failure("L'identifiant du compte publicitaire est fait de chiffres (act_123456789).");
        if (token.Length == 0) return Result<string>.Failure("Le jeton est obligatoire.");

        var test = await platform.TestAsync(new AdAccess(accountId, token), ct);
        if (test.IsFailure) return Result<string>.Failure(test.Error!);

        await using var db = await factory.CreateDbContextAsync(ct);
        var s = await db.AdSettings.FirstOrDefaultAsync(ct);
        if (s is null) { s = new AdSettings(); db.AdSettings.Add(s); }
        s.AdAccountId = accountId;
        s.AdAccountName = test.Value!.Name;
        s.Currency = test.Value.Currency;
        s.ProtectedToken = _protector.Protect(token);
        s.ConnectedAt = clock.GetUtcNow();
        await db.SaveChangesAsync(ct);
        return Result<string>.Success(test.Value.IsActive
            ? $"{test.Value.Name} ({test.Value.Currency})"
            : $"{test.Value.Name} ({test.Value.Currency}) — attention : Meta indique que ce compte n'est pas actif (moyen de paiement ?).");
    }

    public async Task DisconnectAsync(CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var s = await db.AdSettings.FirstOrDefaultAsync(ct);
        if (s is null) return;
        s.ProtectedToken = null;
        s.ConnectedAt = null;
        await db.SaveChangesAsync(ct);
    }

    public async Task<Result> SaveDefaultsAsync(decimal? monthlyCap, int radiusKm, int ageMin, int ageMax, CancellationToken ct = default)
    {
        if (monthlyCap is < 0) return Result.Failure("Le plafond ne peut pas être négatif.");
        if (radiusKm is < 5 or > 80) return Result.Failure("Rayon entre 5 et 80 km.");
        if (ageMin < 18 || ageMax > 65 || ageMin >= ageMax) return Result.Failure("Âges entre 18 et 65 ans.");
        await using var db = await factory.CreateDbContextAsync(ct);
        var s = await db.AdSettings.FirstOrDefaultAsync(ct);
        if (s is null) { s = new AdSettings(); db.AdSettings.Add(s); }
        s.MonthlyCap = monthlyCap is 0 ? null : monthlyCap;
        s.RadiusKm = radiusKm;
        s.AgeMin = ageMin;
        s.AgeMax = ageMax;
        await db.SaveChangesAsync(ct);
        return Result.Success();
    }

    private DateTimeOffset MonthStart()
    {
        var local = TimeZoneInfo.ConvertTime(clock.GetUtcNow(), Hub.SharedKernel.Time.BusinessTime.Zone);
        return Hub.SharedKernel.Time.BusinessTime.FromLocal(new DateTime(local.Year, local.Month, 1));
    }

    public async Task<AdMonth> MonthAsync(CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        return await MonthAsync(db, ct);
    }

    private async Task<AdMonth> MonthAsync(ChannelsDbContext db, CancellationToken ct)
    {
        var from = MonthStart();
        var ads = await db.AdCampaigns.AsNoTracking().Where(a => a.CreatedAt >= from).ToListAsync(ct);
        var cap = await db.AdSettings.AsNoTracking().Select(s => s.MonthlyCap).FirstOrDefaultAsync(ct);
        return new AdMonth(ads.Sum(a => a.Committed), cap);
    }

    public async Task<IReadOnlyList<AdView>> ListAsync(CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var ads = await db.AdCampaigns.AsNoTracking().OrderByDescending(a => a.CreatedAt).Take(100).ToListAsync(ct);
        var postIds = ads.Where(a => a.PostId is not null).Select(a => a.PostId!.Value).Distinct().ToList();
        var posts = await db.Posts.AsNoTracking().Where(p => postIds.Contains(p.Id)).Select(p => new { p.Id, p.Title }).ToDictionaryAsync(p => p.Id, p => p.Title, ct);
        var tags = ads.Where(a => a.TrackingCampaign is not null).Select(a => a.TrackingCampaign!).ToList();
        IReadOnlyDictionary<string, (int Visits, int Leads, int Won)> results = tags.Count == 0
            ? new Dictionary<string, (int Visits, int Leads, int Won)>()
            : await context.GetResultsAsync(tags, ct);
        return ads.Select(a => new AdView(a, a.PostId is { } p ? posts.GetValueOrDefault(p) : null,
            a.TrackingCampaign is { } t && results.TryGetValue(t, out var r) ? r.Leads : 0)).ToList();
    }

    public async Task<IReadOnlyList<BoostTarget>> BoostTargetsAsync(CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        return await TargetsAsync(db, clock.GetUtcNow().AddDays(-60), 40, ct);
    }

    private static async Task<List<BoostTarget>> TargetsAsync(ChannelsDbContext db, DateTimeOffset since, int take, CancellationToken ct)
    {
        var targets = await db.PostTargets.AsNoTracking()
            .Where(t => t.Status == PostTargetStatus.Published && t.ExternalId != null && t.PublishedAt >= since
                && (t.Channel == ChannelType.Facebook || t.Channel == ChannelType.Instagram))
            .ToListAsync(ct);
        var ids = targets.Select(t => t.PostId).Distinct().ToList();
        var posts = await db.Posts.AsNoTracking().Where(p => ids.Contains(p.Id)).ToListAsync(ct);
        var stats = await db.PostStats.AsNoTracking().Where(s => ids.Contains(s.PostId)).ToListAsync(ct);
        return posts
            .OrderByDescending(p => targets.Where(t => t.PostId == p.Id).Max(t => t.PublishedAt))
            .Take(take)
            .Select(p => new BoostTarget(p.Id, p.Title, p.ImageUrl, !string.IsNullOrWhiteSpace(p.VideoUrl),
                targets.Where(t => t.PostId == p.Id).Select(t => t.Channel).Distinct().OrderBy(c => c).ToList(),
                stats.Where(s => s.PostId == p.Id).Sum(s => s.Reach), stats.Where(s => s.PostId == p.Id).Sum(s => s.Interactions)))
            .ToList();
    }

    public async Task<BoostTarget?> SuggestBoostAsync(CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var boosted = await db.AdCampaigns.AsNoTracking().Where(a => a.PostId != null && a.Status != AdStatus.Failed)
            .Select(a => a.PostId!.Value).Distinct().ToListAsync(ct);
        var candidates = (await TargetsAsync(db, clock.GetUtcNow().AddDays(-14), 60, ct)).Where(t => !boosted.Contains(t.PostId)).ToList();
        return Suggest(candidates);
    }

    /// <summary>Le meilleur taux d'interaction (portée d'au moins 30 personnes pour que le taux veuille dire quelque chose).</summary>
    internal static BoostTarget? Suggest(IEnumerable<BoostTarget> candidates) =>
        candidates.Where(c => c.Reach >= 30 && c.Interactions > 0)
            .OrderByDescending(c => (double)c.Interactions / c.Reach).ThenByDescending(c => c.Reach)
            .FirstOrDefault();

    public async Task<IReadOnlyDictionary<string, string>> ValidateAsync(AdDraft draft, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        return AdRules.Validate(draft, await MonthAsync(db, ct));
    }

    public async Task<ReachEstimate?> EstimateAsync(AdDraft draft, CancellationToken ct = default)
    {
        var (s, access) = await AccessAsync(ct);
        if (access is null) return null;
        return await platform.EstimateAsync(access, Audience(draft, s!), draft.Objective, ct);
    }

    private async Task<(AdSettings? Settings, AdAccess? Access)> AccessAsync(CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var s = await db.AdSettings.AsNoTracking().FirstOrDefaultAsync(ct);
        if (s?.AdAccountId is null || s.ProtectedToken is null) return (s, null);
        try { return (s, new AdAccess(s.AdAccountId, _protector.Unprotect(s.ProtectedToken))); }
        catch (System.Security.Cryptography.CryptographicException ex)
        {
            logger.LogWarning(ex, "Jeton des publicités illisible : à reconnecter");
            return (s, null);
        }
    }

    private static AdAudience Audience(AdDraft d, AdSettings s) =>
        new(s.Latitude, s.Longitude, d.RadiusKm, d.AgeMin, d.AgeMax, d.Facebook, d.Instagram);

    public async Task<Result<Guid>> LaunchAsync(AdDraft draft, string author, CancellationToken ct = default)
    {
        var errors = await ValidateAsync(draft, ct);
        if (errors.Count > 0) return Result<Guid>.Failure(errors.First().Value);
        var (settings, access) = await AccessAsync(ct);
        if (access is null) return Result<Guid>.Failure("Connectez d'abord le compte publicitaire Meta (page Publicités).");

        await using var db = await factory.CreateDbContextAsync(ct);
        var post = await db.Posts.AsNoTracking().FirstOrDefaultAsync(p => p.Id == draft.PostId, ct);
        if (post is null) return Result<Guid>.Failure("Publication introuvable.");
        var facebook = await CredentialsAsync(ChannelType.Facebook, ct);
        if (facebook is null) return Result<Guid>.Failure("La page Facebook doit être connectée (page Canaux) : toutes les publicités Meta passent par elle.");
        var instagram = await CredentialsAsync(ChannelType.Instagram, ct);
        if (draft.Instagram && instagram is null) draft.Instagram = false;
        if (!draft.Facebook && !draft.Instagram) return Result<Guid>.Failure("Instagram n'est pas connecté : choisissez Facebook.");

        var info = await tenantInfo.GetCurrentAsync(ct);
        var now = clock.GetUtcNow();
        var (start, end) = AdRules.Window(draft, now);
        var ad = new AdCampaign
        {
            Kind = draft.Kind, Objective = draft.Objective, PostId = post.Id, Budget = Math.Round(draft.Budget, 2), Currency = settings!.Currency,
            StartAt = start, EndAt = end, OnFacebook = draft.Facebook, OnInstagram = draft.Instagram,
            Latitude = settings.Latitude, Longitude = settings.Longitude, RadiusKm = draft.RadiusKm, AgeMin = draft.AgeMin, AgeMax = draft.AgeMax,
            LaunchedBy = author, CreatedAt = now,
        };

        AdCreative creative;
        if (draft.Kind == AdKind.Boost)
        {
            var source = await db.PostTargets.AsNoTracking().FirstOrDefaultAsync(t => t.PostId == post.Id && t.Channel == draft.Source
                && t.Status == PostTargetStatus.Published && t.ExternalId != null, ct);
            if (source is null) return Result<Guid>.Failure($"Cette publication n'est pas encore partie sur {draft.Source}.");
            ad.SourceChannel = draft.Source;
            ad.Name = Name("Boost", post.Title, now);
            if (draft.Source == ChannelType.Instagram)
            {
                if (instagram is null) return Result<Guid>.Failure("Instagram n'est pas connecté.");
                creative = new AdCreative(AdCreativeKind.InstagramMedia, facebook.AccountId, instagram.AccountId, source.ExternalId);
            }
            else if (source.ExternalId!.Contains('_'))
                creative = new AdCreative(AdCreativeKind.FacebookPost, facebook.AccountId, instagram?.AccountId, source.ExternalId);
            else
                creative = new AdCreative(AdCreativeKind.FacebookVideo, facebook.AccountId, instagram?.AccountId, source.ExternalId,
                    Message: post.Message, ImageUrl: TrackedLink.SocialImage(info.SiteUrl, post.ImageUrl));
        }
        else
        {
            ad.TrackingCampaign = TrackedLink.NewCampaign($"pub {post.Title}", now);
            if (ad.TrackingCampaign.Length > 100) ad.TrackingCampaign = ad.TrackingCampaign[..100];
            ad.Name = Name("Campagne", draft.Headline!, now);
            ad.Message = draft.Message!.Trim();
            ad.Headline = draft.Headline!.Trim();
            ad.LinkUrl = AdRules.Link(info.SiteUrl, draft.LinkPath, ad.TrackingCampaign);
            if (ad.LinkUrl is null) return Result<Guid>.Failure("L'adresse du site n'est pas configurée.");
            var image = TrackedLink.SocialImage(info.SiteUrl, post.ImageUrl);
            var video = draft.UseVideo && !string.IsNullOrWhiteSpace(post.VideoUrl) ? TrackedLink.Absolute(info.SiteUrl, post.VideoUrl) : null;
            if (image is null) return Result<Guid>.Failure("Cette publication n'a pas d'image à utiliser pour l'annonce.");
            creative = new AdCreative(video is null ? AdCreativeKind.LinkImage : AdCreativeKind.LinkVideo, facebook.AccountId, instagram?.AccountId,
                Message: ad.Message, Headline: ad.Headline, Link: ad.LinkUrl, ImageUrl: image, VideoUrl: video);
        }

        var launched = await platform.LaunchAsync(access, new AdLaunch(ad.Name, ad.Objective, ad.Budget, start, end, Audience(draft, settings), creative), ct);
        if (launched.IsSuccess)
        {
            (ad.MetaCampaignId, ad.MetaAdSetId, ad.MetaAdId) = (launched.Value!.CampaignId, launched.Value.AdSetId, launched.Value.AdId);
            ad.Status = AdStatus.InReview;
            ad.StatusDetail = "Envoyée à Meta : examen de l'annonce (quelques minutes à 24 h) avant la diffusion.";
        }
        else
        {
            ad.Status = AdStatus.Failed;
            ad.StatusDetail = launched.Error;
        }
        db.AdCampaigns.Add(ad);
        await db.SaveChangesAsync(ct);
        logger.LogInformation("Publicité {Name} : {Status} (budget {Budget} {Currency}, par {Author})", ad.Name, ad.Status, ad.Budget, ad.Currency, author);
        return launched.IsSuccess ? Result<Guid>.Success(ad.Id) : Result<Guid>.Failure(launched.Error!);
    }

    /// <summary>Identifiants de la page et du compte Instagram (en mode simulé, des identifiants fictifs s'ils ne sont pas connectés).</summary>
    private async Task<Hub.Modules.Channels.Publishers.ChannelCredentials?> CredentialsAsync(ChannelType channel, CancellationToken ct) =>
        await connections.GetCredentialsAsync(channel, ct)
        ?? (platform.IsSimulated ? new Hub.Modules.Channels.Publishers.ChannelCredentials($"simule-{channel}".ToLowerInvariant(), "simulé") : null);

    private static string Name(string kind, string title, DateTimeOffset now)
    {
        var name = $"Hub · {kind} · {title.Trim()} · {now:yyyy-MM-dd}";
        return name.Length > 190 ? name[..190] : name;
    }

    public Task<Result> PauseAsync(Guid id, CancellationToken ct = default) => ChangeAsync(id, AdStatus.Paused, ct);
    public Task<Result> ResumeAsync(Guid id, CancellationToken ct = default) => ChangeAsync(id, AdStatus.Active, ct);
    public Task<Result> StopAsync(Guid id, CancellationToken ct = default) => ChangeAsync(id, AdStatus.Stopped, ct);

    private async Task<Result> ChangeAsync(Guid id, AdStatus target, CancellationToken ct)
    {
        var (_, access) = await AccessAsync(ct);
        if (access is null) return Result.Failure("Le compte publicitaire n'est plus connecté.");
        await using var db = await factory.CreateDbContextAsync(ct);
        var ad = await db.AdCampaigns.FirstOrDefaultAsync(a => a.Id == id, ct);
        if (ad?.MetaCampaignId is null) return Result.Failure("Publicité introuvable.");
        if (!ad.IsLive) return Result.Failure("Cette publicité est déjà terminée.");
        if (target == AdStatus.Active && clock.GetUtcNow() >= ad.EndAt) return Result.Failure("La date de fin est passée.");
        var done = await platform.SetActiveAsync(access, ad.MetaCampaignId, target == AdStatus.Active, ct);
        if (done.IsFailure) return done;
        ad.Status = target;
        ad.StatusDetail = target switch
        {
            AdStatus.Stopped => "Arrêtée : plus rien ne sera dépensé.",
            AdStatus.Paused => "En pause : rien n'est dépensé pendant la pause.",
            _ => null,
        };
        await db.SaveChangesAsync(ct);
        return Result.Success();
    }

    public async Task<int> SyncAsync(CancellationToken ct = default)
    {
        var (_, access) = await AccessAsync(ct);
        if (access is null) return 0;
        await using var db = await factory.CreateDbContextAsync(ct);
        var now = clock.GetUtcNow();
        var recent = now.AddDays(-3);
        // En cours, ou terminées depuis peu (Meta finalise la dépense dans les jours qui suivent).
        var ads = await db.AdCampaigns
            .Where(a => a.MetaCampaignId != null && a.MetaAdId != null && a.Status != AdStatus.Failed
                && (a.Status == AdStatus.InReview || a.Status == AdStatus.Active || a.Status == AdStatus.Paused || a.EndAt >= recent))
            .ToListAsync(ct);
        var updated = 0;
        foreach (var ad in ads)
        {
            var snap = await platform.SnapshotAsync(access, ad.MetaCampaignId!, ad.MetaAdId!, ct);
            if (snap.IsFailure) { logger.LogWarning("Publicité {Id} : {Error}", ad.Id, snap.Error); continue; }
            var s = snap.Value!;
            var status = AdRules.Map(ad.Status, s.EffectiveStatus, ad.EndAt, now);
            if (status != ad.Status)
            {
                ad.StatusDetail = status switch
                {
                    AdStatus.Rejected => $"Refusée par Meta{(s.ReviewMessage is null ? "." : $" : {s.ReviewMessage}")}",
                    AdStatus.Active => null,
                    AdStatus.Completed => "Terminée.",
                    _ => ad.StatusDetail,
                };
                ad.Status = status;
            }
            if (s.EffectiveStatus is "WITH_ISSUES" or "PENDING_BILLING_INFO")
                ad.StatusDetail = s.EffectiveStatus == "PENDING_BILLING_INFO"
                    ? "Meta attend un moyen de paiement valide sur le compte publicitaire."
                    : $"Meta signale un problème{(s.ReviewMessage is null ? "" : $" : {s.ReviewMessage}")}.";
            (ad.Spend, ad.Reach, ad.Impressions, ad.Clicks, ad.Engagements) = (Math.Min(s.Spend, ad.Budget), s.Reach, s.Impressions, s.Clicks, s.Engagements);
            ad.SyncedAt = now;
            updated++;
        }
        await db.SaveChangesAsync(ct);
        return updated;
    }

    public async Task<AdsSummary> SummaryAsync(DateTimeOffset since, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var ads = await db.AdCampaigns.AsNoTracking().Where(a => a.CreatedAt >= since && a.Status != AdStatus.Failed).ToListAsync(ct);
        var tags = ads.Where(a => a.TrackingCampaign is not null).Select(a => a.TrackingCampaign!).ToList();
        var leads = tags.Count == 0 ? 0 : (await context.GetResultsAsync(tags, ct)).Values.Sum(r => r.Leads);
        return new AdsSummary(ads.Count, ads.Sum(a => a.Spend), ads.Sum(a => a.Reach), ads.Sum(a => a.Clicks), leads);
    }
}
