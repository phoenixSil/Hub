using Hub.Modules.Leads.Contracts;
using Hub.SharedKernel.Domain;

namespace Hub.Modules.Leads.Domain;

public enum LeadStatus
{
    New = 0,
    Contacted = 1,
    QuoteSent = 2,
    Scheduled = 3,
    Won = 4,
    Lost = 5,
    Spam = 6,
}

public enum TimeSlot
{
    Morning = 0,
    Afternoon = 1,
    LateDay = 2,
}

/// <summary>Une demande reçue par le site : devis, rendez-vous, contact ou question.</summary>
public sealed class Lead : TenantEntity
{
    public LeadKind Kind { get; set; }
    public LeadStatus Status { get; set; } = LeadStatus.New;

    public string FullName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string? Phone { get; set; }
    public string? Address { get; set; }
    public string? AreaSlug { get; set; }

    /// <summary>Services demandés (titres, séparés par « ; »).</summary>
    public string? Services { get; set; }
    public string? PropertyType { get; set; }
    public string? PropertySize { get; set; }
    public string? Frequency { get; set; }
    public string? Subject { get; set; }
    public string? Message { get; set; }

    // Rendez-vous
    public DateOnly? PreferredDate { get; set; }
    public TimeSlot? PreferredSlot { get; set; }
    public DateTimeOffset? ConfirmedFor { get; set; }

    // Provenance de la visite
    public string? Source { get; set; }
    public string? Medium { get; set; }
    public string? Campaign { get; set; }
    public string? Referrer { get; set; }
    public string? LandingPage { get; set; }
    public string? VisitorId { get; set; }

    public bool ConsentGiven { get; set; }
    public string? IpHash { get; set; }

    /// <summary>Date du rappel envoyé quand la demande est restée « Nouvelle » plus de 24 h.</summary>
    public DateTimeOffset? ReminderSentAt { get; set; }
}

/// <summary>Historique de suivi d'une demande.</summary>
public sealed class LeadActivity : TenantEntity
{
    public Guid LeadId { get; set; }
    public string Author { get; set; } = string.Empty;
    public string Action { get; set; } = string.Empty;
    public string? Note { get; set; }
}

/// <summary>Jours où l'on n'accepte pas de rendez-vous (congés, fériés).</summary>
public sealed class ClosedDay : TenantEntity
{
    public DateOnly Date { get; set; }
    public string? Reason { get; set; }
}

/// <summary>Disponibilités générales pour les demandes de rendez-vous.</summary>
public sealed class SchedulingSettings : TenantEntity
{
    /// <summary>Jours ouverts, 0 = dimanche … 6 = samedi, séparés par des virgules.</summary>
    public string OpenDays { get; set; } = "1,2,3,4,5,6";
    public bool MorningEnabled { get; set; } = true;
    public bool AfternoonEnabled { get; set; } = true;
    public bool LateDayEnabled { get; set; } = true;

    /// <summary>Délai minimal entre la demande et le rendez-vous, en jours.</summary>
    public int MinimumNoticeDays { get; set; } = 1;
    public int MaximumDaysAhead { get; set; } = 60;

    public IReadOnlySet<DayOfWeek> OpenDaySet() =>
        OpenDays.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(d => int.TryParse(d, out var n) ? (DayOfWeek?)(DayOfWeek)n : null)
                .Where(d => d is not null)
                .Select(d => d!.Value)
                .ToHashSet();

    public IEnumerable<TimeSlot> EnabledSlots()
    {
        if (MorningEnabled) yield return TimeSlot.Morning;
        if (AfternoonEnabled) yield return TimeSlot.Afternoon;
        if (LateDayEnabled) yield return TimeSlot.LateDay;
    }
}

public static class LeadLabels
{
    public static string Of(LeadKind kind) => kind switch
    {
        LeadKind.Quote => "Devis",
        LeadKind.Appointment => "Rendez-vous",
        LeadKind.Contact => "Contact",
        LeadKind.Question => "Question",
        _ => kind.ToString(),
    };

    public static string Of(LeadStatus status) => status switch
    {
        LeadStatus.New => "Nouvelle",
        LeadStatus.Contacted => "Contactée",
        LeadStatus.QuoteSent => "Devis envoyé",
        LeadStatus.Scheduled => "Planifiée",
        LeadStatus.Won => "Gagnée",
        LeadStatus.Lost => "Perdue",
        LeadStatus.Spam => "Pourriel",
        _ => status.ToString(),
    };

    public static string Of(TimeSlot slot) => slot switch
    {
        TimeSlot.Morning => "Matin (8 h – 12 h)",
        TimeSlot.Afternoon => "Après-midi (12 h – 16 h)",
        TimeSlot.LateDay => "Fin de journée (16 h – 18 h)",
        _ => slot.ToString(),
    };
}
