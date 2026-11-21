using Hub.Modules.Channels.Domain;
using Hub.SharedKernel;

namespace Hub.Modules.Channels.Application.Ads;

/// <summary>Accès au compte publicitaire : identifiant (sans « act_ ») et jeton.</summary>
public sealed record AdAccess(string AccountId, string Token);

public sealed record AdAccountInfo(string Name, string Currency, bool IsActive);

/// <summary>Public visé : un rayon autour d'un point, une tranche d'âge, les réseaux.</summary>
public sealed record AdAudience(double Latitude, double Longitude, int RadiusKm, int AgeMin, int AgeMax, bool Facebook, bool Instagram);

public enum AdCreativeKind
{
    /// <summary>Publication Facebook existante (texte ou photo), boostée telle quelle.</summary>
    FacebookPost,

    /// <summary>Reel ou vidéo Facebook existante (identifiant de vidéo), reprise avec sa couverture.</summary>
    FacebookVideo,

    /// <summary>Publication Instagram existante, boostée telle quelle.</summary>
    InstagramMedia,

    /// <summary>Nouvelle annonce : image, texte, titre et bouton vers le site.</summary>
    LinkImage,

    /// <summary>Nouvelle annonce : vidéo (Reel), texte, titre et bouton vers le site.</summary>
    LinkVideo,
}

/// <summary>Le visuel de l'annonce. Les champs utiles dépendent du type.</summary>
public sealed record AdCreative(
    AdCreativeKind Kind,
    string PageId,
    string? InstagramUserId = null,
    string? SourceId = null,
    string? Message = null,
    string? Headline = null,
    string? Link = null,
    string? ImageUrl = null,
    string? VideoUrl = null);

public sealed record AdLaunch(string Name, AdObjective Objective, decimal Budget, DateTimeOffset StartAt, DateTimeOffset EndAt,
    AdAudience Audience, AdCreative Creative);

public sealed record LaunchedAd(string CampaignId, string AdSetId, string AdId);

/// <summary>Personnes que Meta estime pouvoir atteindre avec ce public.</summary>
public sealed record ReachEstimate(long Lower, long Upper);

/// <summary>État et chiffres d'une publicité chez Meta.</summary>
public sealed record AdSnapshot(string EffectiveStatus, string? ReviewMessage, decimal Spend, int Reach, int Impressions, int Clicks, int Engagements);

/// <summary>
/// Régie publicitaire (fournie par le module : Meta, ou simulée pour les tests). Ajouter Google Ads = une autre implémentation.
/// Rien n'est dépensé sans <see cref="LaunchAsync"/>, appelé uniquement après le clic « Lancer » de l'administrateur.
/// </summary>
public interface IAdsPlatform
{
    bool IsSimulated { get; }
    Task<Result<AdAccountInfo>> TestAsync(AdAccess access, CancellationToken ct = default);

    /// <summary>Taille estimée du public (null si Meta ne la donne pas).</summary>
    Task<ReachEstimate?> EstimateAsync(AdAccess access, AdAudience audience, AdObjective objective, CancellationToken ct = default);

    /// <summary>Crée campagne, ensemble et annonce (en pause), puis active la campagne. En cas d'échec, rien n'est laissé actif.</summary>
    Task<Result<LaunchedAd>> LaunchAsync(AdAccess access, AdLaunch launch, CancellationToken ct = default);

    Task<Result> SetActiveAsync(AdAccess access, string campaignId, bool active, CancellationToken ct = default);
    Task<Result<AdSnapshot>> SnapshotAsync(AdAccess access, string campaignId, string adId, CancellationToken ct = default);
}

/// <summary>Ce que l'administrateur remplit pour un boost ou une campagne.</summary>
public sealed class AdDraft
{
    public AdKind Kind { get; set; }
    public Guid? PostId { get; set; }
    public ChannelType? Source { get; set; }
    public AdObjective Objective { get; set; }
    public decimal Budget { get; set; } = 20m;
    public int Days { get; set; } = 7;

    /// <summary>Début (null : dès l'approbation de Meta).</summary>
    public DateTimeOffset? StartAt { get; set; }
    public bool Facebook { get; set; } = true;
    public bool Instagram { get; set; } = true;
    public int RadiusKm { get; set; } = 35;
    public int AgeMin { get; set; } = 25;
    public int AgeMax { get; set; } = 65;

    // Campagne
    public string? Message { get; set; }
    public string? Headline { get; set; }

    /// <summary>Page du site visée (par défaut « /devis »).</summary>
    public string? LinkPath { get; set; } = "/devis";

    /// <summary>Utiliser la vidéo (Reel) de la publication plutôt que son image.</summary>
    public bool UseVideo { get; set; }
}

public sealed record AdSettingsView(bool Connected, string? AccountId, string? AccountName, string Currency, decimal? MonthlyCap,
    int RadiusKm, int AgeMin, int AgeMax, bool Simulated, bool FacebookConnected, bool InstagramConnected);

/// <summary>Dépenses engagées ce mois-ci et plafond facultatif.</summary>
public sealed record AdMonth(decimal Committed, decimal? Cap)
{
    public decimal? Remaining => Cap is { } c ? Math.Max(0, c - Committed) : null;
}

/// <summary>Une publication qu'on peut booster, et ses publications d'origine.</summary>
public sealed record BoostTarget(Guid PostId, string Title, string? ImageUrl, bool HasVideo, IReadOnlyList<ChannelType> Sources, int Reach, int Interactions);

public sealed record AdView(AdCampaign Ad, string? PostTitle, int Leads)
{
    public decimal? CostPerLead => Leads > 0 ? Math.Round(Ad.Spend / Leads, 2) : null;
}

public sealed record AdsSummary(int Count, decimal Spend, int Reach, int Clicks, int Leads)
{
    public decimal? CostPerLead => Leads > 0 ? Math.Round(Spend / Leads, 2) : null;
}

public interface IAdService
{
    Task<AdSettingsView> SettingsAsync(CancellationToken ct = default);
    Task<Result<string>> ConnectAsync(string accountId, string token, CancellationToken ct = default);
    Task DisconnectAsync(CancellationToken ct = default);
    Task<Result> SaveDefaultsAsync(decimal? monthlyCap, int radiusKm, int ageMin, int ageMax, CancellationToken ct = default);

    Task<AdMonth> MonthAsync(CancellationToken ct = default);
    Task<IReadOnlyList<AdView>> ListAsync(CancellationToken ct = default);

    /// <summary>Publications déjà parties sur Facebook ou Instagram (les plus récentes), pour un boost.</summary>
    Task<IReadOnlyList<BoostTarget>> BoostTargetsAsync(CancellationToken ct = default);

    /// <summary>La publication qui marche le mieux sans publicité ces 14 derniers jours (suggestion de Kairos, jamais lancée seule).</summary>
    Task<BoostTarget?> SuggestBoostAsync(CancellationToken ct = default);

    /// <summary>Vérifie le formulaire (budget, dates, public, plafond) : erreurs par champ.</summary>
    Task<IReadOnlyDictionary<string, string>> ValidateAsync(AdDraft draft, CancellationToken ct = default);

    Task<ReachEstimate?> EstimateAsync(AdDraft draft, CancellationToken ct = default);

    /// <summary>Lance la publicité chez Meta : appelé seulement par le bouton « Lancer » de l'administrateur.</summary>
    Task<Result<Guid>> LaunchAsync(AdDraft draft, string author, CancellationToken ct = default);

    Task<Result> PauseAsync(Guid id, CancellationToken ct = default);
    Task<Result> ResumeAsync(Guid id, CancellationToken ct = default);
    Task<Result> StopAsync(Guid id, CancellationToken ct = default);

    /// <summary>Relève état et chiffres des publicités en cours (gardien) ; renvoie le nombre mis à jour.</summary>
    Task<int> SyncAsync(CancellationToken ct = default);

    Task<AdsSummary> SummaryAsync(DateTimeOffset since, CancellationToken ct = default);
}
