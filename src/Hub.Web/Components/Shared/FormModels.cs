using Hub.Modules.Leads.Application;
using Hub.Web.Infrastructure;
using Microsoft.AspNetCore.Http;

namespace Hub.Web.Components.Shared;

public sealed class QuoteForm
{
    public string FullName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string? Phone { get; set; }
    public string? Address { get; set; }
    public string? AreaSlug { get; set; }
    public string? PropertyType { get; set; }
    public string? PropertySize { get; set; }
    public string? Frequency { get; set; }
    public string? Message { get; set; }
    public bool ConsentGiven { get; set; }

    /// <summary>Champ piège invisible : rempli seulement par les robots.</summary>
    public string? Website { get; set; }
}

public sealed class AppointmentForm
{
    public string FullName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string? Phone { get; set; }
    public string? Service { get; set; }
    public string? Address { get; set; }
    public string? Date { get; set; }
    public string? Slot { get; set; }
    public string? Message { get; set; }
    public bool ConsentGiven { get; set; }
    public string? Website { get; set; }
}

public sealed class ContactForm
{
    public string FullName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string? Phone { get; set; }
    public string? Subject { get; set; }
    public string? Message { get; set; }
    public bool ConsentGiven { get; set; }
    public string? Website { get; set; }
}

public static class LeadRequest
{
    /// <summary>Ajoute à la demande la provenance de la visite et l'empreinte (hachée) de l'adresse IP.</summary>
    public static LeadSubmission WithVisit(this LeadSubmission s, HttpContext ctx)
    {
        var source = VisitCookies.ReadSource(ctx);
        s.Source = source?.Source;
        s.Medium = source?.Medium;
        s.Campaign = source?.Campaign;
        s.Referrer = source?.Referrer;
        s.LandingPage = source?.LandingPage;
        s.VisitorId = VisitCookies.VisitorIdOf(ctx);
        s.IpHash = VisitCookies.IpHash(ctx);
        return s;
    }

    public static bool IsSpam(string? honeypot) => !string.IsNullOrWhiteSpace(honeypot);
}
