using Hub.SharedKernel.Events;

namespace Hub.Modules.Leads.Contracts;

public enum LeadKind
{
    Quote = 0,
    Appointment = 1,
    Contact = 2,
    Question = 3,
}

/// <summary>Publié quand une demande est enregistrée. Capté par la Mesure pour compter une conversion.</summary>
public sealed record LeadSubmitted(
    Guid TenantId,
    Guid LeadId,
    LeadKind Kind,
    string? Source,
    string? Medium,
    string? Campaign,
    string? LandingPage,
    string? VisitorId,
    DateTimeOffset OccurredAt) : IIntegrationEvent;
