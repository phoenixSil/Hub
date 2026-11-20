using Hub.SharedKernel.Domain;

namespace Hub.Modules.Channels.Domain;

public enum PostStatus
{
    Draft = 0,
    Scheduled = 1,
    Publishing = 2,
    Published = 3,
    /// <summary>Publiée sur une partie des réseaux seulement.</summary>
    PartiallyPublished = 4,
    Failed = 5,
    Cancelled = 6,
    /// <summary>Proposée par le pilote automatique : attend l'accord de l'administrateur.</summary>
    AwaitingApproval = 7,
    /// <summary>Proposée par le pilote, jamais validée : son heure est passée, elle n'est pas partie (reste visible).</summary>
    Expired = 8,
}

public enum PostTargetStatus
{
    Pending = 0,
    Published = 1,
    Failed = 2,
}

/// <summary>
/// Une publication pour les réseaux sociaux : un texte, une image et un lien mesuré
/// vers le site. Elle part sur un ou plusieurs réseaux (<see cref="PostTarget"/>).
/// </summary>
public sealed class Post : TenantEntity
{
    /// <summary>Titre interne (jamais publié).</summary>
    public string Title { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;

    /// <summary>Page du site vers laquelle pointe le lien (ex. « /services/nettoyage-residentiel »).</summary>
    public string? LinkPath { get; set; }
    public string? ImageUrl { get; set; }

    /// <summary>Identifiant de campagne (utm_campaign) : relie visites et demandes à cette publication.</summary>
    public string Campaign { get; set; } = string.Empty;

    public PostStatus Status { get; set; } = PostStatus.Draft;
    public DateTimeOffset? ScheduledAt { get; set; }
    public DateTimeOffset? PublishedAt { get; set; }
    public string? CreatedBy { get; set; }

    /// <summary>Modèle d'IA ayant proposé le texte, s'il y a lieu.</summary>
    public string? AiModel { get; set; }

    /// <summary>Public visé (ex. « Familles occupées »), choisi par le pilote automatique ou l'administrateur.</summary>
    public string? Audience { get; set; }

    /// <summary>Préparée par le pilote automatique.</summary>
    public bool FromAutopilot { get; set; }

    /// <summary>Campagne (plan marketing) pour laquelle le pilote l'a préparée.</summary>
    public Guid? PlanId { get; set; }

    /// <summary>Type de visuel : « Photo », « Illustration », « Importée » (vide : sans image). Sert aux résultats.</summary>
    public string? VisualKind { get; set; }

    /// <summary>Type de sujet (Promotion, Service, Conseil, Saison, Aînés). Sert aux résultats.</summary>
    public string? Theme { get; set; }

    /// <summary>Pilier de contenu du plan marketing (Conseiller, Montrer, Rassurer, Proposer…).</summary>
    public string? Pillar { get; set; }

    /// <summary>Format publicitaire (avant-apres, humour, question…), pour varier et comparer les résultats.</summary>
    public string? Format { get; set; }

    /// <summary>Résumé de la scène de la photo générée, pour ne pas refaire la même image.</summary>
    public string? SceneNote { get; set; }

    /// <summary>Style visuel de l'image (voir la bible de styles), pour ne pas le répéter.</summary>
    public string? ArtStyle { get; set; }

    /// <summary>
    /// Reel (vidéo verticale) : type demandé (<see cref="ReelKinds"/>). La vidéo est préparée à part ;
    /// tant que <see cref="VideoUrl"/> est vide, la publication partirait avec son image.
    /// </summary>
    public string? ReelKind { get; set; }

    /// <summary>Adresse publique de la vidéo du Reel (MP4 vertical 1080×1920).</summary>
    public string? VideoUrl { get; set; }

    /// <summary>Dernière erreur de préparation de la vidéo (affichée dans l'éditeur), ou null.</summary>
    public string? ReelError { get; set; }
}

/// <summary>Les façons de faire un Reel.</summary>
public static class ReelKinds
{
    /// <summary>Scène réaliste générée par l'IA vidéo (payant, plafonné chaque mois).</summary>
    public const string Ai = "IA";

    /// <summary>Scène « film » générée par l'IA vidéo : lumière dorée, ralenti, mouvements lents (payant, plafonné).</summary>
    public const string Cinematic = "IACinema";

    /// <summary>Vraies vidéos d'archives libres de droits (Pexels), assemblées (gratuit). Jamais présentées comme notre travail.</summary>
    public const string Stock = "Archives";

    /// <summary>Vidéos avant/après filmées et téléversées par l'entreprise.</summary>
    public const string BeforeAfter = "AvantApres";

    /// <summary>Montage animé des images de la publication (gratuit).</summary>
    public const string Montage = "Montage";

    /// <summary>Types générés par l'IA vidéo (comptent dans le plafond mensuel).</summary>
    public static bool IsAi(string? kind) => kind is Ai or Cinematic;

    public static string Label(string? kind) => kind switch
    {
        Ai => "Reel IA",
        Cinematic => "Reel IA cinématique",
        Stock => "Reel vidéos d'archives",
        BeforeAfter => "Reel avant/après",
        Montage => "Reel images animées",
        _ => "Image",
    };
}

/// <summary>Publication d'un <see cref="Post"/> sur un réseau donné.</summary>
public sealed class PostTarget : TenantEntity
{
    public Guid PostId { get; set; }
    public ChannelType Channel { get; set; }
    public PostTargetStatus Status { get; set; } = PostTargetStatus.Pending;
    public string? ExternalId { get; set; }
    public string? ExternalUrl { get; set; }
    public string? Error { get; set; }
    public int Attempts { get; set; }
    public DateTimeOffset? PublishedAt { get; set; }
}
