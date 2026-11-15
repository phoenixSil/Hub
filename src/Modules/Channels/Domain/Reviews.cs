using Hub.SharedKernel.Domain;

namespace Hub.Modules.Channels.Domain;

/// <summary>Un avis client lu sur la fiche Google, avec la réponse proposée puis publiée.</summary>
public sealed class GoogleReview : TenantEntity
{
    public ChannelType Channel { get; set; } = ChannelType.GoogleBusiness;

    /// <summary>Identifiant de l'avis chez le réseau.</summary>
    public string ExternalId { get; set; } = string.Empty;

    public string Author { get; set; } = string.Empty;

    /// <summary>De 1 à 5 étoiles (0 : inconnu).</summary>
    public int Rating { get; set; }

    public string? Comment { get; set; }

    public DateTimeOffset WrittenAt { get; set; }

    /// <summary>Réponse proposée (par l'IA ou écrite), pas encore publiée.</summary>
    public string? DraftReply { get; set; }

    /// <summary>Réponse publiée sur le réseau.</summary>
    public string? Reply { get; set; }

    public DateTimeOffset? RepliedAt { get; set; }
}
