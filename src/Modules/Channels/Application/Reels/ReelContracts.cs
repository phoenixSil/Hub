using Hub.SharedKernel;

namespace Hub.Modules.Channels.Application.Reels;

/// <summary>Réglages des Reels (section « Reels »).</summary>
public sealed class ReelOptions
{
    public const string Section = "Reels";

    /// <summary>Plafond mensuel des vidéos générées par l'IA, en dollars américains. Au-delà : vidéos d'archives ou images animées (gratuit).</summary>
    public decimal MonthlyAiBudget { get; set; } = 15m;

    /// <summary>Reels préparés par le pilote au plus par jour (plafond de coût : il l'emporte sur la part minimale).</summary>
    public int MaxPerDay { get; set; } = 1;

    /// <summary>Reels préparés à chaque passage (la vidéo IA peut prendre plusieurs minutes).</summary>
    public int MaxPerPass { get; set; } = 3;
}

/// <summary>Ce que l'entreprise affiche dans ses vidéos (fin « Devis gratuit », logo, couleurs).</summary>
public sealed record ReelBrand(string CompanyName, string Phone, string? SiteUrl, string? LogoUrl, string PrimaryColor, string AccentColor);

/// <summary>Textes à l'écran : l'accroche du début et l'appel à l'action de la fin.</summary>
public sealed record ReelText(string Hook, string Cta);

/// <summary>Vidéo terminée et enregistrée : adresse publique, image de couverture, durée.</summary>
public sealed record RenderedReel(string VideoUrl, string CoverUrl, double Seconds);

/// <summary>
/// Atelier de montage (fourni par l'hôte : ffmpeg et SkiaSharp). Produit une vidéo verticale 1080×1920
/// avec accroche, logo, musique libre de droits et fin « Devis gratuit », puis l'enregistre.
/// </summary>
public interface IReelStudio
{
    /// <summary>Faux si l'outil de montage n'est pas disponible sur le serveur.</summary>
    bool IsAvailable { get; }

    /// <summary>Montage animé d'images (mouvement de caméra lent sur chacune).</summary>
    Task<Result<RenderedReel>> MontageAsync(IReadOnlyList<string> imageUrls, ReelText text, ReelBrand brand, CancellationToken ct = default);

    /// <summary>Montage de clips vidéo (fichiers locaux), chacun avec son étiquette (« AVANT », « APRÈS » ou vide).</summary>
    Task<Result<RenderedReel>> FromClipsAsync(IReadOnlyList<string> clipPaths, IReadOnlyList<string> labels, ReelText text, ReelBrand brand, CancellationToken ct = default);
}

/// <summary>Vidéo générée par l'IA (fournie par l'hôte : Gemini / Veo).</summary>
public interface IAiVideoGenerator
{
    bool IsConfigured { get; }
    string ModelName { get; }

    /// <summary>Durée d'un clip généré, en secondes.</summary>
    int ClipSeconds { get; }

    /// <summary>Prix estimé par seconde de vidéo, en dollars américains.</summary>
    decimal PricePerSecond { get; }

    /// <summary>Génère un clip vertical et renvoie le chemin d'un fichier MP4 temporaire.</summary>
    Task<Result<string>> GenerateAsync(string prompt, CancellationToken ct = default);
}

/// <summary>Vidéos d'archives libres de droits (fournies par l'hôte : Pexels).</summary>
public interface IStockVideoSource
{
    bool IsConfigured { get; }

    /// <summary>
    /// Cherche des vidéos verticales pour chaque recherche (en anglais) et en télécharge une par recherche, au hasard parmi les meilleures ;
    /// renvoie les chemins de fichiers MP4 temporaires (à supprimer après le montage).
    /// </summary>
    Task<Result<IReadOnlyList<string>>> DownloadAsync(IReadOnlyList<string> queries, CancellationToken ct = default);
}

/// <summary>Sans clé Pexels : pas de vidéos d'archives (le pilote ne tire pas ce type).</summary>
public sealed class NoStockVideoSource : IStockVideoSource
{
    public bool IsConfigured => false;
    public Task<Result<IReadOnlyList<string>>> DownloadAsync(IReadOnlyList<string> queries, CancellationToken ct = default) =>
        Task.FromResult(Result<IReadOnlyList<string>>.Failure("Aucune clé Pexels : les vidéos d'archives ne sont pas disponibles."));
}

/// <summary>Une vidéo avant/après téléversée par l'administrateur.</summary>
public sealed record BeforeAfterRequest(
    string BeforePath, string? AfterPath, string Title, string Message, string? LinkPath, DateTimeOffset? ScheduledAt, string Author,
    IReadOnlyList<Hub.Modules.Channels.ChannelType> Channels);

public sealed record ReelBudget(decimal Used, decimal Cap, int Count)
{
    public decimal Remaining => Math.Max(0, Cap - Used);
}

/// <summary>Reels : préparation des vidéos, budget de l'IA, avant/après.</summary>
public interface IReelService
{
    bool IsAvailable { get; }
    bool AiConfigured { get; }

    /// <summary>Vidéos d'archives (Pexels) disponibles.</summary>
    bool StockConfigured { get; }

    /// <summary>Coût estimé d'un Reel généré par l'IA.</summary>
    decimal AiCostPerReel { get; }

    Task<ReelBudget> BudgetAsync(CancellationToken ct = default);

    /// <summary>Prépare (ou refait) la vidéo d'une publication, du type demandé.</summary>
    Task<Result> RenderAsync(Guid postId, string kind, CancellationToken ct = default);

    /// <summary>Retire la vidéo : la publication repart avec son image.</summary>
    Task<Result> RemoveAsync(Guid postId, CancellationToken ct = default);

    /// <summary>Crée une publication brouillon à partir de vidéos avant/après ; renvoie son identifiant.</summary>
    Task<Result<Guid>> CreateBeforeAfterAsync(BeforeAfterRequest request, CancellationToken ct = default);

    /// <summary>Prépare les vidéos demandées par le pilote qui ne le sont pas encore ; renvoie le nombre fait.</summary>
    Task<int> RenderPendingAsync(CancellationToken ct = default);
}
