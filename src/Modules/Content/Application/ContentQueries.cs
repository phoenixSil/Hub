using Hub.Modules.Content.Domain;
using Hub.Modules.Content.Infrastructure;
using Hub.SharedKernel.Caching;
using Hub.SharedKernel.Domain;
using Hub.SharedKernel.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace Hub.Modules.Content.Application;

/// <summary>Contenu complet du site pour une entreprise, chargé en une fois et mis en cache.</summary>
public sealed record SiteContent(
    IReadOnlyList<ServiceArea> Areas,
    IReadOnlyList<Page> Pages,
    IReadOnlyList<Promotion> Promotions,
    IReadOnlyList<FaqItem> Faq,
    IReadOnlyList<Review> Reviews,
    IReadOnlyDictionary<string, ContentBlock> Blocks,
    IReadOnlyList<HighlightItem> Highlights)
{
    public ContentBlock? Block(string key) => Blocks.TryGetValue(key, out var b) ? b : null;

    public IEnumerable<HighlightItem> Group(string group) =>
        Highlights.Where(h => h.Group == group).OrderBy(h => h.SortOrder);

    public IEnumerable<Promotion> LivePromotions(DateOnly today) =>
        Promotions.Where(p => p.IsLiveOn(today)).OrderBy(p => p.SortOrder);

    public Page? FindPage(string slug) => Pages.FirstOrDefault(p => p.Slug == slug && p.IsPublished);

    public ServiceArea? FindArea(string slug) => Areas.FirstOrDefault(a => a.Slug == slug && a.IsActive);

    public double? AverageRating => Reviews.Count == 0 ? null : Reviews.Average(r => r.Rating);
}

public interface IContentQueries
{
    Task<SiteContent> GetAsync(CancellationToken ct = default);
}

internal sealed class ContentQueries(
    IDbContextFactory<ContentDbContext> factory,
    ContentCache cache,
    ITenantContext tenant) : IContentQueries
{
    public Task<SiteContent> GetAsync(CancellationToken ct = default) =>
        cache.GetOrCreateAsync($"{tenant.TenantId}:content", async () =>
        {
            await using var db = await factory.CreateDbContextAsync(ct);
            const string fr = Locales.Default;

            var areas = await db.Areas.AsNoTracking().Where(a => a.Locale == fr && a.IsActive)
                .OrderBy(a => a.SortOrder).ThenBy(a => a.Name).ToListAsync(ct);
            var pages = await db.Pages.AsNoTracking().Where(p => p.Locale == fr && p.IsPublished).ToListAsync(ct);
            var promotions = await db.Promotions.AsNoTracking().Where(p => p.Locale == fr && p.IsActive)
                .OrderBy(p => p.SortOrder).ToListAsync(ct);
            var faq = await db.Faq.AsNoTracking().Where(f => f.Locale == fr && f.IsActive)
                .OrderBy(f => f.SortOrder).ToListAsync(ct);
            var reviews = await db.Reviews.AsNoTracking().Where(r => r.IsApproved)
                .OrderByDescending(r => r.ReviewedOn).ToListAsync(ct);
            var blocks = await db.Blocks.AsNoTracking().Where(b => b.Locale == fr).ToListAsync(ct);
            var highlights = await db.Highlights.AsNoTracking().Where(h => h.Locale == fr)
                .OrderBy(h => h.SortOrder).ToListAsync(ct);

            return new SiteContent(areas, pages, promotions, faq, reviews,
                blocks.ToDictionary(b => b.Key), highlights);
        });
}
