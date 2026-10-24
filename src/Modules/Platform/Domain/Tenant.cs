using Microsoft.AspNetCore.Identity;

namespace Hub.Modules.Platform.Domain;

/// <summary>Une entreprise pilotée par le système (ScintiNet, puis d'autres).</summary>
public sealed class Tenant
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public string Slug { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;

    /// <summary>Domaines servis, séparés par des virgules (ex. « scintinet.com,www.scintinet.com »).</summary>
    public string Domains { get; set; } = string.Empty;
    public string? SiteUrl { get; set; }

    // Identité visuelle
    public string? Tagline { get; set; }
    public string PrimaryColor { get; set; } = "#0097CE";
    public string SecondaryColor { get; set; } = "#38D430";
    public string AccentColor { get; set; } = "#E2E735";
    public string? LogoUrl { get; set; }
    public string? FaviconUrl { get; set; }
    public string? DefaultOgImageUrl { get; set; }

    // Coordonnées
    public string Phone { get; set; } = string.Empty;
    public string PublicEmail { get; set; } = string.Empty;
    public string? WhatsAppNumber { get; set; }
    public string? StreetAddress { get; set; }
    public bool ShowStreetAddress { get; set; }
    public string City { get; set; } = string.Empty;
    public string Region { get; set; } = string.Empty;
    public string? PostalCode { get; set; }
    public string Country { get; set; } = "CA";
    public string? BusinessNumber { get; set; }
    public string? BusinessHours { get; set; }
    public string? ServiceAreaSummary { get; set; }

    // Réseaux sociaux
    public string? FacebookUrl { get; set; }
    public string? InstagramUrl { get; set; }
    public string? YouTubeUrl { get; set; }
    public string? TikTokUrl { get; set; }
    public string? GoogleBusinessUrl { get; set; }

    // Notifications et conformité
    public string AdminNotificationEmail { get; set; } = string.Empty;
    public string? PrivacyOfficerName { get; set; }
    public string? PrivacyOfficerEmail { get; set; }

    public bool IsActive { get; set; } = true;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;

    public IEnumerable<string> DomainList() =>
        Domains.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
               .Select(d => d.ToLowerInvariant());
}

/// <summary>Administrateur du back-office, rattaché à une entreprise.</summary>
public sealed class HubUser : IdentityUser
{
    public Guid TenantId { get; set; }
    public string? DisplayName { get; set; }
}

public static class HubClaims
{
    public const string TenantId = "hub:tenant";
}
