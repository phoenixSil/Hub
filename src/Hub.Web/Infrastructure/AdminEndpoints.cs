using System.Globalization;
using System.Text;
using Hub.Modules.Content.Application;
using Hub.Modules.Leads.Application;
using Hub.Modules.Leads.Domain;
using Hub.Modules.Platform.Domain;
using Hub.SharedKernel.Tenancy;
using Hub.SharedKernel.Time;

namespace Hub.Web.Infrastructure;

public static class AdminEndpoints
{
    /// <summary>GET /admin/demandes/export.csv : toutes les demandes, ouvrable dans Excel (séparateur « ; », UTF-8).</summary>
    public static void MapAdminExports(this IEndpointRouteBuilder app)
    {
        app.MapGet("/admin/demandes/export.csv", async (HttpContext ctx, ITenantContext tenant, ILeadService leads, IContentQueries content) =>
        {
            // L'entreprise de l'administrateur connecté prime sur celle du nom de domaine.
            if (Guid.TryParse(ctx.User.FindFirst(HubClaims.TenantId)?.Value, out var tenantId))
                tenant.Set(tenantId);

            var areaNames = (await content.GetAsync(ctx.RequestAborted)).Areas
                .GroupBy(a => a.Slug).ToDictionary(g => g.Key, g => g.First().Name);
            areaNames["autre"] = "Autre secteur";
            var rows = await leads.ListAsync(new LeadListFilter(Take: 100_000), ctx.RequestAborted);
            var fr = CultureInfo.GetCultureInfo("fr-CA");
            var sb = new StringBuilder();
            string[] header =
            [
                "Reçue le", "Type", "Statut", "Nom", "Courriel", "Téléphone", "Adresse", "Secteur", "Services",
                "Propriété", "Superficie", "Fréquence", "Sujet", "Message", "Date souhaitée", "Plage",
                "Rendez-vous confirmé", "Source", "Support", "Campagne", "Page d'arrivée",
            ];
            sb.AppendLine(string.Join(';', header.Select(Csv)));

            string Local(DateTimeOffset d) => TimeZoneInfo.ConvertTime(d, BusinessTime.Zone).ToString("yyyy-MM-dd HH:mm", fr);

            foreach (var l in rows)
            {
                string?[] cells =
                [
                    Local(l.CreatedAt), LeadLabels.Of(l.Kind), LeadLabels.Of(l.Status), l.FullName, l.Email, l.Phone,
                    l.Address, l.AreaSlug is { } a ? areaNames.GetValueOrDefault(a, a) : null, l.Services, l.PropertyType, l.PropertySize, l.Frequency, l.Subject, l.Message,
                    l.PreferredDate?.ToString("yyyy-MM-dd"), l.PreferredSlot is { } s ? LeadLabels.Of(s) : null,
                    l.ConfirmedFor is { } c ? Local(c) : null, l.Source, l.Medium, l.Campaign, l.LandingPage,
                ];
                sb.AppendLine(string.Join(';', cells.Select(Csv)));
            }

            var bytes = Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(sb.ToString())).ToArray();
            var name = $"demandes-{BusinessTime.Today(TimeProvider.System):yyyy-MM-dd}.csv";
            return Results.File(bytes, "text/csv; charset=utf-8", name);
        }).RequireAuthorization();
    }

    /// <summary>Échappe une cellule CSV et neutralise les formules (=, +, -, @) pour Excel.</summary>
    private static string Csv(string? value)
    {
        if (string.IsNullOrEmpty(value)) return string.Empty;
        var v = value.Replace("\r\n", " ").Replace('\n', ' ');
        if (v[0] is '=' or '+' or '-' or '@') v = "'" + v;
        return v.Contains(';') || v.Contains('"') ? $"\"{v.Replace("\"", "\"\"")}\"" : v;
    }
}
