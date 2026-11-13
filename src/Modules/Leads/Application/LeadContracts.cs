using Hub.Modules.Leads.Contracts;
using Hub.Modules.Leads.Domain;
using Hub.Modules.Leads.Infrastructure;
using Hub.SharedKernel;
using Hub.SharedKernel.Email;
using Hub.SharedKernel.Events;
using Hub.SharedKernel.Tenancy;
using Hub.SharedKernel.Time;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;

namespace Hub.Modules.Leads.Application;

public sealed record LeadListFilter(LeadStatus? Status = null, LeadKind? Kind = null, string? Search = null, int Take = 200);

public sealed record LeadDetail(Lead Lead, IReadOnlyList<LeadActivity> Activities);

public sealed record AppointmentAvailability(
    DateOnly FirstDate, DateOnly LastDate, IReadOnlySet<DayOfWeek> OpenDays,
    IReadOnlyList<DateOnly> ClosedDays, IReadOnlyList<TimeSlot> Slots);

/// <summary>Réception des demandes envoyées par le site (formulaires publics).</summary>
public interface ILeadIntake
{
    Task<Result<Guid>> SubmitAsync(LeadSubmission submission, CancellationToken ct = default);
}

/// <summary>Suivi des demandes dans le back-office.</summary>
public interface ILeadInbox
{
    Task<List<Lead>> ListAsync(LeadListFilter filter, CancellationToken ct = default);
    Task<LeadDetail?> GetAsync(Guid id, CancellationToken ct = default);
    Task<Dictionary<LeadStatus, int>> CountByStatusAsync(CancellationToken ct = default);
    Task ChangeStatusAsync(Guid id, LeadStatus status, string author, CancellationToken ct = default);
    Task AddNoteAsync(Guid id, string note, string author, CancellationToken ct = default);
    Task<Result> ConfirmAppointmentAsync(Guid id, DateTimeOffset when, string? note, string author, CancellationToken ct = default);

    /// <summary>
    /// Répond au client par courriel depuis le Hub, avec des pièces jointes (offre en PDF…).
    /// L'envoi est noté dans le suivi ; une demande « Nouvelle » passe à « Contactée ».
    /// </summary>
    Task<Result> SendReplyAsync(Guid id, string subject, string body, IReadOnlyList<Hub.SharedKernel.Email.EmailAttachment> attachments,
        string author, CancellationToken ct = default);
}

public static class LeadEmailLimits
{
    /// <summary>Nombre maximal de pièces jointes dans une réponse au client.</summary>
    public const int MaxAttachments = 5;
}

/// <summary>Disponibilités pour les demandes de rendez-vous.</summary>
public interface ILeadScheduling
{
    Task<AppointmentAvailability> GetAvailabilityAsync(CancellationToken ct = default);
    Task<SchedulingSettings> GetSchedulingAsync(CancellationToken ct = default);
    Task SaveSchedulingAsync(SchedulingSettings settings, CancellationToken ct = default);
    Task<IReadOnlyList<DateOnly>> GetClosedDaysAsync(CancellationToken ct = default);
}

internal static class LeadText
{
    public static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    public static string? Clip(string? value, int max)
    {
        var v = Clean(value);
        return v is null ? null : v.Length <= max ? v : v[..max];
    }
}
