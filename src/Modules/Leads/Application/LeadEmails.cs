using System.Globalization;
using System.Net;
using System.Text;
using Hub.Modules.Leads.Contracts;
using Hub.Modules.Leads.Domain;
using Hub.SharedKernel.Tenancy;

namespace Hub.Modules.Leads.Application;

/// <summary>Gabarits des courriels envoyés pour une demande.</summary>
internal static class LeadEmails
{
    private static readonly CultureInfo Fr = CultureInfo.GetCultureInfo("fr-CA");

    private static string E(string? value) => WebUtility.HtmlEncode(value ?? string.Empty);

    public static (string Subject, string Html) AdminNotification(Lead lead, TenantInfo tenant, string? areaName = null)
    {
        var subject = $"[{tenant.Name}] {LeadLabels.Of(lead.Kind)} — {lead.FullName}";
        var rows = new StringBuilder();
        void Row(string label, string? value)
        {
            if (!string.IsNullOrWhiteSpace(value))
                rows.Append($"<tr><td style=\"padding:6px 12px 6px 0;color:#64748b;vertical-align:top;white-space:nowrap\">{E(label)}</td><td style=\"padding:6px 0;color:#0f172a\">{E(value)}</td></tr>");
        }

        Row("Nom", lead.FullName);
        Row("Courriel", lead.Email);
        Row("Téléphone", lead.Phone);
        Row("Adresse", lead.Address);
        Row("Secteur", areaName ?? lead.AreaSlug);
        Row("Services", lead.Services);
        Row("Propriété", lead.PropertyType);
        Row("Superficie", lead.PropertySize);
        Row("Fréquence", lead.Frequency);
        Row("Sujet", lead.Subject);
        if (lead.PreferredDate is { } d)
            Row("Date souhaitée", d.ToString("dddd d MMMM yyyy", Fr) +
                (lead.PreferredSlot is { } s ? " · " + LeadLabels.Of(s) : string.Empty));
        Row("Provenance", string.Join(" / ", new[] { lead.Source, lead.Medium, lead.Campaign }.Where(x => !string.IsNullOrWhiteSpace(x))));

        var message = string.IsNullOrWhiteSpace(lead.Message)
            ? string.Empty
            : $"<div style=\"margin-top:16px;padding:16px;background:#f8fafc;border-radius:6px;white-space:pre-wrap;color:#0f172a\">{E(lead.Message)}</div>";

        var link = string.IsNullOrWhiteSpace(tenant.SiteUrl)
            ? string.Empty
            : $"<p style=\"margin-top:24px\"><a href=\"{E(tenant.SiteUrl!.TrimEnd('/'))}/admin/demandes/{lead.Id}\" style=\"background:{E(tenant.PrimaryColor)};color:#fff;padding:10px 18px;border-radius:6px;text-decoration:none\">Ouvrir la demande</a></p>";

        var html = Layout(tenant, $"Nouvelle demande : {LeadLabels.Of(lead.Kind).ToLowerInvariant()}",
            $"<table style=\"border-collapse:collapse;font-size:14px\">{rows}</table>{message}{link}");
        return (subject, html);
    }

    public static (string Subject, string Html) Acknowledgement(Lead lead, TenantInfo tenant)
    {
        var what = lead.Kind switch
        {
            LeadKind.Quote => "votre demande de devis",
            LeadKind.Appointment => "votre demande de rendez-vous",
            LeadKind.Question => "votre question",
            _ => "votre message",
        };

        var appointment = lead.Kind == LeadKind.Appointment && lead.PreferredDate is { } d
            ? $"<p>Date souhaitée : <strong>{E(d.ToString("dddd d MMMM yyyy", Fr))}</strong>{(lead.PreferredSlot is { } s ? ", " + E(LeadLabels.Of(s)) : string.Empty)}.<br>Ce rendez-vous n'est pas encore confirmé : nous vous écrirons pour le confirmer ou proposer une autre plage.</p>"
            : string.Empty;

        var body = $"""
            <p>Bonjour {E(lead.FullName)},</p>
            <p>Nous avons bien reçu {what}. Un membre de l'équipe vous contactera dans un délai d'un jour ouvrable.</p>
            {appointment}
            <p>Pour toute urgence, appelez-nous au <a href="tel:{E(Digits(tenant.Phone))}">{E(tenant.Phone)}</a>.</p>
            <p>Merci de votre confiance,<br>L'équipe {E(tenant.Name)}</p>
            """;

        return ($"{tenant.Name} — nous avons bien reçu {what}", Layout(tenant, "Demande bien reçue", body));
    }

    public static (string Subject, string Html) AppointmentConfirmed(Lead lead, TenantInfo tenant, DateTimeOffset when, string? note)
    {
        var body = $"""
            <p>Bonjour {E(lead.FullName)},</p>
            <p>Votre rendez-vous est confirmé pour le <strong>{E(when.ToString("dddd d MMMM yyyy 'à' H 'h' mm", Fr))}</strong>.</p>
            {(string.IsNullOrWhiteSpace(note) ? string.Empty : $"<p>{E(note)}</p>")}
            <p>Pour le déplacer ou l'annuler, répondez à ce courriel ou appelez-nous au <a href="tel:{E(Digits(tenant.Phone))}">{E(tenant.Phone)}</a>, au moins 24 heures à l'avance.</p>
            <p>À bientôt,<br>L'équipe {E(tenant.Name)}</p>
            """;
        return ($"{tenant.Name} — rendez-vous confirmé", Layout(tenant, "Rendez-vous confirmé", body));
    }

    public static (string Subject, string Html) Reminder(IReadOnlyList<Lead> leads, TenantInfo tenant)
    {
        var baseUrl = tenant.SiteUrl?.TrimEnd('/');
        var rows = new StringBuilder();
        foreach (var lead in leads)
        {
            var hours = (int)(DateTimeOffset.UtcNow - lead.CreatedAt).TotalHours;
            var name = string.IsNullOrEmpty(baseUrl)
                ? E(lead.FullName)
                : $"<a href=\"{E(baseUrl)}/admin/demandes/{lead.Id}\">{E(lead.FullName)}</a>";
            rows.Append($"<tr><td style=\"padding:6px 12px 6px 0\">{name}</td><td style=\"padding:6px 12px 6px 0;color:#64748b\">{E(LeadLabels.Of(lead.Kind))}</td><td style=\"padding:6px 0;color:#64748b\">reçue il y a {hours} h</td></tr>");
        }

        var body = $"""
            <p>{(leads.Count == 1 ? "Une demande attend" : $"{leads.Count} demandes attendent")} toujours une réponse depuis plus de 24 heures.</p>
            <table style="border-collapse:collapse;font-size:14px">{rows}</table>
            <p style="margin-top:16px;color:#64748b;font-size:13px">Passez la demande au statut « Contactée » (ou un autre) pour ne plus recevoir ce rappel.</p>
            """;
        var subject = $"[{tenant.Name}] Rappel : {leads.Count} demande{(leads.Count > 1 ? "s" : "")} sans réponse";
        return (subject, Layout(tenant, "Demandes en attente", body));
    }

    private static string Digits(string phone) => "+" + new string(phone.Where(char.IsDigit).ToArray());

    private static string Layout(TenantInfo tenant, string title, string content) => $"""
        <!DOCTYPE html>
        <html lang="fr"><head><meta charset="utf-8"></head>
        <body style="margin:0;padding:24px;background:#f1f5f9;font-family:Arial,Helvetica,sans-serif;font-size:15px;line-height:1.5;color:#1e293b">
          <div style="max-width:600px;margin:0 auto;background:#fff;border-radius:8px;overflow:hidden">
            <div style="background:{E(tenant.PrimaryColor)};padding:20px 24px">
              <div style="color:#fff;font-size:20px;font-weight:bold">{E(tenant.Name)}</div>
              <div style="color:#fff;opacity:.85;font-size:13px">{E(tenant.Tagline)}</div>
            </div>
            <div style="padding:24px">
              <h1 style="font-size:18px;margin:0 0 16px">{E(title)}</h1>
              {content}
            </div>
            <div style="padding:16px 24px;border-top:1px solid #e2e8f0;color:#94a3b8;font-size:12px">
              {E(tenant.Name)} · {E(tenant.Phone)} · {E(tenant.PublicEmail)}
            </div>
          </div>
        </body></html>
        """;
}
