using Hub.SharedKernel.Domain;

namespace Hub.Modules.Channels.Domain;

/// <summary>Un public visé par les publications : à qui l'on parle, et à quelle heure il est le plus disponible.</summary>
public sealed record AutopilotAudience(string Name, string Description, int Hour);

/// <summary>
/// Réglages du pilote automatique d'une entreprise : il prépare chaque semaine les publications
/// (sujet, public, texte, visuel, heure), puis les publie après accord — ou seul, si on le lui permet.
/// </summary>
public sealed class AutopilotSettings : TenantEntity
{
    public bool Enabled { get; set; }

    /// <summary>Faux : chaque publication attend l'accord de l'administrateur. Vrai : elle part seule à son heure.</summary>
    public bool PublishWithoutApproval { get; set; }

    public int PostsPerWeek { get; set; } = 3;

    /// <summary>Part minimale de Reels parmi les publications du pilote, en pour cent (au plus un Reel par jour).</summary>
    public int ReelSharePercent { get; set; } = 45;

    /// <summary>Publics visés, en JSON (liste de <see cref="AutopilotAudience"/>).</summary>
    public string AudiencesJson { get; set; } = "[]";

    /// <summary>Réseaux visés, séparés par des virgules (ex. « Facebook »).</summary>
    public string Channels { get; set; } = nameof(ChannelType.Facebook);

    /// <summary>Consignes libres pour l'IA (ex. « ne jamais parler de prix », « mettre en avant les aînés »).</summary>
    public string? Guidelines { get; set; }

    /// <summary>Styles visuels écartés par l'administrateur (clés séparées par des virgules).</summary>
    public string? DisabledStyles { get; set; }

    public DateTimeOffset? LastRunAt { get; set; }

    /// <summary>Jour du dernier rappel « des publications partent demain et attendent votre accord » (un par jour au plus).</summary>
    public DateOnly? LastReminderOn { get; set; }
    public string? LastRunSummary { get; set; }
}

/// <summary>Nombre de photos créées par mois (plafond de coût du service d'images).</summary>
public sealed class ImageUsage : TenantEntity
{
    /// <summary>Mois, au format « 2026-11 ».</summary>
    public string Month { get; set; } = string.Empty;
    public int Count { get; set; }
}

/// <summary>Vidéos générées par l'IA dans le mois : secondes et coût estimé (plafond mensuel).</summary>
public sealed class VideoUsage : TenantEntity
{
    /// <summary>Mois, au format « 2026-11 ».</summary>
    public string Month { get; set; } = string.Empty;
    public int Count { get; set; }
    public double Seconds { get; set; }
    public decimal Cost { get; set; }
}
