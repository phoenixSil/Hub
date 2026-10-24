using Hub.SharedKernel.Domain;

namespace Hub.Modules.Analytics.Domain;

public enum AnalyticsEventType
{
    PageView = 0,
    PhoneClick = 1,
    EmailClick = 2,
    WhatsAppClick = 3,
    FormStarted = 4,
    LeadSubmitted = 5,
}

/// <summary>
/// Un événement de visite. Aucune donnée personnelle : l'identifiant de visite
/// est un nombre aléatoire, posé seulement si le visiteur a accepté la mesure.
/// </summary>
public sealed class AnalyticsEvent : TenantEntity
{
    public DateTimeOffset OccurredAt { get; set; }
    public AnalyticsEventType Type { get; set; }
    public string Path { get; set; } = "/";
    public string? Source { get; set; }
    public string? Medium { get; set; }
    public string? Campaign { get; set; }
    public string? ReferrerHost { get; set; }
    public string? Device { get; set; }
    public string? VisitorId { get; set; }
    public Guid? LeadId { get; set; }
    public string? Detail { get; set; }
}
