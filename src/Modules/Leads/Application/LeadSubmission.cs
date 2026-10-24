using System.Net.Mail;
using Hub.Modules.Leads.Contracts;
using Hub.Modules.Leads.Domain;

namespace Hub.Modules.Leads.Application;

/// <summary>Ce que le visiteur a saisi, plus la provenance de sa visite.</summary>
public sealed class LeadSubmission
{
    public LeadKind Kind { get; set; }
    public string FullName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string? Phone { get; set; }
    public string? Address { get; set; }
    public string? AreaSlug { get; set; }
    public List<string> Services { get; set; } = [];
    public string? PropertyType { get; set; }
    public string? PropertySize { get; set; }
    public string? Frequency { get; set; }
    public string? Subject { get; set; }
    public string? Message { get; set; }
    public DateOnly? PreferredDate { get; set; }
    public TimeSlot? PreferredSlot { get; set; }
    public bool ConsentGiven { get; set; }

    // Renseigné par le site, pas par le visiteur
    public string? Source { get; set; }
    public string? Medium { get; set; }
    public string? Campaign { get; set; }
    public string? Referrer { get; set; }
    public string? LandingPage { get; set; }
    public string? VisitorId { get; set; }
    public string? IpHash { get; set; }
}

internal static class LeadValidator
{
    public static Dictionary<string, string> Validate(
        LeadSubmission s, SchedulingSettings scheduling, IReadOnlyCollection<DateOnly> closedDays, DateOnly today)
    {
        var errors = new Dictionary<string, string>();

        if (string.IsNullOrWhiteSpace(s.FullName))
            errors[nameof(s.FullName)] = "Indiquez votre nom.";
        else if (s.FullName.Length > 160)
            errors[nameof(s.FullName)] = "Le nom est trop long.";

        if (string.IsNullOrWhiteSpace(s.Email) || !IsEmail(s.Email))
            errors[nameof(s.Email)] = "Indiquez une adresse courriel valide.";

        var phoneRequired = s.Kind is LeadKind.Quote or LeadKind.Appointment;
        var digits = (s.Phone ?? string.Empty).Count(char.IsDigit);
        if (phoneRequired && digits < 10)
            errors[nameof(s.Phone)] = "Indiquez un numéro de téléphone à 10 chiffres.";
        else if (!phoneRequired && !string.IsNullOrWhiteSpace(s.Phone) && digits < 10)
            errors[nameof(s.Phone)] = "Ce numéro semble incomplet.";

        if (s.Kind == LeadKind.Quote && s.Services.Count == 0)
            errors[nameof(s.Services)] = "Choisissez au moins un service.";

        if (s.Kind is LeadKind.Contact or LeadKind.Question &&
            (string.IsNullOrWhiteSpace(s.Message) || s.Message.Trim().Length < 10))
            errors[nameof(s.Message)] = "Écrivez votre message (10 caractères minimum).";

        if (s.Message?.Length > 4000)
            errors[nameof(s.Message)] = "Le message est trop long (4 000 caractères maximum).";

        if (s.Kind == LeadKind.Appointment)
        {
            if (s.PreferredDate is not { } date)
            {
                errors[nameof(s.PreferredDate)] = "Choisissez une date.";
            }
            else
            {
                var first = today.AddDays(scheduling.MinimumNoticeDays);
                var last = today.AddDays(scheduling.MaximumDaysAhead);
                if (date < first || date > last)
                    errors[nameof(s.PreferredDate)] = $"Choisissez une date entre le {first:d MMMM} et le {last:d MMMM}.";
                else if (!scheduling.OpenDaySet().Contains(date.DayOfWeek) || closedDays.Contains(date))
                    errors[nameof(s.PreferredDate)] = "Nous ne sommes pas disponibles ce jour-là. Choisissez une autre date.";
            }

            if (s.PreferredSlot is not { } slot || !scheduling.EnabledSlots().Contains(slot))
                errors[nameof(s.PreferredSlot)] = "Choisissez une plage horaire.";
        }

        if (!s.ConsentGiven)
            errors[nameof(s.ConsentGiven)] = "Votre accord est nécessaire pour que nous puissions vous répondre.";

        return errors;
    }

    private static bool IsEmail(string value)
    {
        if (value.Length > 256 || !value.Contains('@')) return false;
        return MailAddress.TryCreate(value.Trim(), out var address) && address.Address == value.Trim();
    }
}
