using Hub.Modules.Analytics.Application;
using Hub.Modules.Catalog.Application;
using Hub.Modules.Channels.Application.Autopilot;
using Hub.Modules.Content.Application;
using Hub.Modules.Platform.Application;
using Hub.SharedKernel.Time;

namespace Hub.Web.Infrastructure;

/// <summary>
/// Assemble, pour le pilote automatique, l'entreprise (Plateforme), ses services (Catalogue),
/// ses offres en cours (Contenu) et les résultats des campagnes (Statistiques).
/// </summary>
internal sealed class AutopilotContextSource(
    ICurrentTenant tenant,
    ICatalogQueries catalog,
    IContentQueries content,
    IDashboardQueries stats,
    Hub.Modules.Leads.Application.ILeadAttribution leads,
    TimeProvider clock) : IAutopilotContextSource
{
    public async Task<AutopilotContext> GetAsync(CancellationToken ct = default)
    {
        var company = await tenant.GetAsync(ct);
        var today = BusinessTime.Today(clock);
        var pages = new List<AutopilotPage>();

        foreach (var p in (await content.GetAsync(ct)).Promotions.Where(p => p.IsActive
                     && (p.StartsOn is null || p.StartsOn <= today) && (p.EndsOn is null || p.EndsOn >= today)))
        {
            var facts = string.Join(" ", new[] { p.Highlight, p.EndsOn is { } end ? "jusqu'au " + end.ToString("d MMMM", System.Globalization.CultureInfo.GetCultureInfo("fr-CA")) : null }
                .Where(x => !string.IsNullOrWhiteSpace(x)));
            pages.Add(new AutopilotPage(p.Title, p.PrimaryUrl, p.Text, p.ImageUrl, true, string.IsNullOrWhiteSpace(facts) ? null : facts));
        }

        foreach (var s in (await catalog.GetCatalogAsync(ct)).SelectMany(c => c.Services))
            pages.Add(new AutopilotPage(s.Title, s.Url, s.Summary, s.ImageUrl, false, null));

        return new AutopilotContext(
            company.Name,
            string.IsNullOrWhiteSpace(company.City) ? "Québec" : company.City,
            company.Phone,
            company.PrimaryColor,
            company.AccentColor,
            company.SiteUrl,
            company.AdminNotificationEmail,
            pages);
    }

    public async Task<IReadOnlyDictionary<string, (int Visits, int Leads, int Won)>> GetResultsAsync(IReadOnlyCollection<string> campaigns, CancellationToken ct = default)
    {
        var views = await stats.GetCampaignStatsAsync(campaigns.ToList(), ct);
        var origins = await leads.ListForCampaignsAsync(campaigns, ct);
        return campaigns.Distinct().ToDictionary(c => c, c => (
            views.GetValueOrDefault(c)?.PageViews ?? 0,
            origins.Count(l => l.Campaign == c),
            origins.Count(l => l.Campaign == c && l.Status == Hub.Modules.Leads.Domain.LeadStatus.Won)));
    }
}
