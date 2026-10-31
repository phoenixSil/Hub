using Hub.Modules.Catalog.Application;
using Hub.Modules.Catalog.Domain;
using Hub.Modules.Content.Application;
using Hub.Modules.Leads.Application;
using Hub.Modules.Platform.Application;
using Hub.Modules.Quotes.Application;
using Hub.SharedKernel.Data;
using Microsoft.Extensions.Options;

namespace Hub.Web.Infrastructure;

/// <summary>
/// Assemble, pour l'assistant de devis, la demande (module Demandes), la grille
/// de prix interne (module Catalogue, prix publiés ou non) et les planchers.
/// </summary>
internal sealed class QuoteContextSource(
    ILeadService leads,
    ICatalogQueries catalog,
    IRepository<PricingModel> pricing,
    IContentQueries content,
    ICurrentTenant tenant,
    IOptions<QuoteOptions> options) : IQuoteContextSource
{
    public async Task<QuoteContext?> GetAsync(Guid leadId, CancellationToken ct = default)
    {
        var detail = await leads.GetAsync(leadId, ct);
        if (detail is null) return null;
        var lead = detail.Lead;

        var company = await tenant.GetAsync(ct);
        var categories = await catalog.GetCatalogAsync(ct);
        var models = await pricing.ListAsync(ct: ct);

        var grid = new List<PriceGridItem>();
        foreach (var category in categories)
        foreach (var service in category.Services)
        {
            var p = models.FirstOrDefault(m => m.ServiceId == service.Id);
            grid.Add(new PriceGridItem(
                service.Title, category.Name, (p?.Kind ?? PricingKind.OnAssessment).ToString(),
                p?.MinAmount, p?.MaxAmount, p?.MinimumHours, p?.ShiftHours, p?.Notes)
            {
                CategoryKey = category.RouteSegment == "aines" ? "aines" : "services",
            });
        }

        string? area = null;
        if (lead.AreaSlug is { } slug)
            area = slug == "autre" ? "Autre secteur" : (await content.GetAsync(ct)).Areas.FirstOrDefault(a => a.Slug == slug)?.Name ?? slug;

        var services = (lead.Services ?? string.Empty)
            .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToList();

        return new QuoteContext(
            company.Name,
            company.Phone,
            new QuoteLeadFacts(lead.Id, lead.FullName, lead.Email, services,
                lead.PropertyType, lead.PropertySize, lead.Frequency, area, lead.Message ?? lead.Subject),
            grid,
            options.Value.FloorHourlyRates);
    }
}
