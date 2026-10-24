using Hub.Modules.Catalog.Domain;
using Hub.Modules.Catalog.Infrastructure;
using Hub.SharedKernel.Caching;
using Hub.SharedKernel.Domain;
using Hub.SharedKernel.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace Hub.Modules.Catalog.Application;

public sealed record ServiceSummary(
    Guid Id, string Title, string Slug, string Summary, string? ImageUrl, string? Icon, string Url, bool ShowInQuoteForm);

public sealed record CategoryWithServices(
    Guid Id, string Name, string Slug, string RouteSegment, string? Intro, IReadOnlyList<ServiceSummary> Services);

public sealed record ServiceDetail(
    Service Service, ServiceCategory Category, IReadOnlyList<ServiceSummary> Related, PricingModel? PublicPricing);

/// <summary>Lecture du catalogue pour le site public, mise en cache.</summary>
public interface ICatalogQueries
{
    Task<IReadOnlyList<CategoryWithServices>> GetCatalogAsync(CancellationToken ct = default);
    Task<ServiceDetail?> GetServiceAsync(string routeSegment, string slug, CancellationToken ct = default);
}

internal sealed class CatalogQueries(
    IDbContextFactory<CatalogDbContext> factory,
    ContentCache cache,
    ITenantContext tenant) : ICatalogQueries
{
    public Task<IReadOnlyList<CategoryWithServices>> GetCatalogAsync(CancellationToken ct = default) =>
        cache.GetOrCreateAsync<IReadOnlyList<CategoryWithServices>>($"{tenant.TenantId}:catalog", async () =>
        {
            await using var db = await factory.CreateDbContextAsync(ct);
            var categories = await db.Categories.AsNoTracking()
                .Where(c => c.Locale == Locales.Default)
                .OrderBy(c => c.SortOrder).ThenBy(c => c.Name)
                .ToListAsync(ct);
            var services = await db.Services.AsNoTracking()
                .Where(s => s.IsActive && s.Locale == Locales.Default)
                .OrderBy(s => s.SortOrder).ThenBy(s => s.Title)
                .ToListAsync(ct);

            return categories
                .Select(c => new CategoryWithServices(
                    c.Id, c.Name, c.Slug, c.RouteSegment, c.Intro,
                    services.Where(s => s.CategoryId == c.Id).Select(s => ToSummary(s, c)).ToList()))
                .ToList();
        });

    public async Task<ServiceDetail?> GetServiceAsync(string routeSegment, string slug, CancellationToken ct = default)
    {
        return await cache.GetOrCreateAsync<ServiceDetail?>($"{tenant.TenantId}:service:{routeSegment}:{slug}", async () =>
        {
            await using var db = await factory.CreateDbContextAsync(ct);
            var service = await db.Services.AsNoTracking()
                .FirstOrDefaultAsync(s => s.Slug == slug && s.IsActive && s.Locale == Locales.Default, ct);
            if (service is null) return null;

            var category = await db.Categories.AsNoTracking().FirstOrDefaultAsync(c => c.Id == service.CategoryId, ct);
            if (category is null || !string.Equals(category.RouteSegment, routeSegment, StringComparison.OrdinalIgnoreCase))
                return null;

            var related = await db.Services.AsNoTracking()
                .Where(s => s.CategoryId == category.Id && s.Id != service.Id && s.IsActive)
                .OrderBy(s => s.SortOrder)
                .ToListAsync(ct);

            var pricing = await db.PricingModels.AsNoTracking()
                .FirstOrDefaultAsync(p => p.ServiceId == service.Id && p.IsPublic, ct);

            return new ServiceDetail(service, category, related.Select(s => ToSummary(s, category)).ToList(), pricing);
        });
    }

    private static ServiceSummary ToSummary(Service s, ServiceCategory c) =>
        new(s.Id, s.Title, s.Slug, s.Summary, s.ImageUrl, s.Icon, $"/{c.RouteSegment}/{s.Slug}", s.ShowInQuoteForm);
}
