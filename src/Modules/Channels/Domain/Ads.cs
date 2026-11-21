using Hub.SharedKernel.Domain;

namespace Hub.Modules.Channels.Domain;

/// <summary>Boost d'une publication déjà publiée, ou vraie campagne publicitaire.</summary>
public enum AdKind
{
    Boost = 0,
    Campaign = 1,
}

/// <summary>Ce que la publicité cherche à obtenir.</summary>
public enum AdObjective
{
    /// <summary>Réactions, commentaires, partages (boost).</summary>
    Engagement = 0,

    /// <summary>Faire voir à un maximum de personnes du secteur (notoriété).</summary>
    Reach = 1,

    /// <summary>Visites de la page devis (demandes de devis).</summary>
    Quotes = 2,
}

public enum AdStatus
{
    /// <summary>Envoyée à Meta, qui l'examine avant diffusion.</summary>
    InReview = 0,
    Active = 1,
    Paused = 2,
    Completed = 3,

    /// <summary>Arrêtée avant la fin par l'administrateur.</summary>
    Stopped = 4,

    /// <summary>Refusée par Meta (règles publicitaires).</summary>
    Rejected = 5,

    /// <summary>Le lancement a échoué : rien n'est diffusé, rien n'est dépensé.</summary>
    Failed = 6,
}

/// <summary>
/// Publicité payante sur Meta (Facebook et Instagram). Créée seulement par un clic de l'administrateur,
/// avec un budget TOTAL fixe (Meta ne peut pas le dépasser) et des dates de début et de fin.
/// </summary>
public sealed class AdCampaign : TenantEntity
{
    public AdKind Kind { get; set; }
    public AdObjective Objective { get; set; }
    public string Name { get; set; } = string.Empty;

    /// <summary>Publication boostée, ou dont le visuel sert à la campagne.</summary>
    public Guid? PostId { get; set; }

    /// <summary>Réseau de la publication d'origine boostée (Facebook ou Instagram).</summary>
    public ChannelType? SourceChannel { get; set; }

    // ----- Contenu (campagne) -----
    public string? Message { get; set; }
    public string? Headline { get; set; }
    public string? LinkUrl { get; set; }

    /// <summary>Étiquette utm_campaign : relie les demandes de devis à cette publicité.</summary>
    public string? TrackingCampaign { get; set; }

    // ----- Diffusion -----
    public bool OnFacebook { get; set; } = true;
    public bool OnInstagram { get; set; } = true;
    public decimal Budget { get; set; }
    public string Currency { get; set; } = "CAD";
    public DateTimeOffset StartAt { get; set; }
    public DateTimeOffset EndAt { get; set; }
    public double Latitude { get; set; }
    public double Longitude { get; set; }
    public int RadiusKm { get; set; }
    public int AgeMin { get; set; }
    public int AgeMax { get; set; }

    // ----- Meta -----
    public string? MetaCampaignId { get; set; }
    public string? MetaAdSetId { get; set; }
    public string? MetaAdId { get; set; }
    public AdStatus Status { get; set; } = AdStatus.InReview;
    public string? StatusDetail { get; set; }

    // ----- Résultats (relevés chaque jour) -----
    public decimal Spend { get; set; }
    public int Reach { get; set; }
    public int Impressions { get; set; }
    public int Clicks { get; set; }
    public int Engagements { get; set; }
    public DateTimeOffset? SyncedAt { get; set; }

    public string LaunchedBy { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>Ce qui compte dans le plafond du mois : le budget engagé, ou la dépense réelle si elle s'est arrêtée.</summary>
    public decimal Committed => Status switch
    {
        AdStatus.Failed or AdStatus.Rejected => Spend,
        AdStatus.Stopped or AdStatus.Completed => Spend,
        _ => Budget,
    };

    public bool IsLive => Status is AdStatus.InReview or AdStatus.Active or AdStatus.Paused;
}

/// <summary>Réglages des publicités d'une entreprise : compte publicitaire Meta, jeton (chiffré), public par défaut, plafond facultatif.</summary>
public sealed class AdSettings : TenantEntity
{
    /// <summary>Identifiant du compte publicitaire, sans « act_ ».</summary>
    public string? AdAccountId { get; set; }
    public string? AdAccountName { get; set; }
    public string Currency { get; set; } = "CAD";

    /// <summary>Jeton (utilisateur système de préférence) chiffré par ASP.NET Data Protection.</summary>
    public string? ProtectedToken { get; set; }

    /// <summary>Plafond mensuel facultatif : vide = pas de limite globale (seul le budget de chaque publicité compte).</summary>
    public decimal? MonthlyCap { get; set; }

    public double Latitude { get; set; } = 46.8139;
    public double Longitude { get; set; } = -71.2080;
    public int RadiusKm { get; set; } = 35;
    public int AgeMin { get; set; } = 25;
    public int AgeMax { get; set; } = 65;
    public DateTimeOffset? ConnectedAt { get; set; }
}
