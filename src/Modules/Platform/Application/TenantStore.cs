using Hub.Modules.Platform.Domain;
using Hub.Modules.Platform.Infrastructure;
using Hub.SharedKernel.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace Hub.Modules.Platform.Application;

public interface ITenantStore
{
    Task<Tenant?> FindByHostAsync(string host, CancellationToken ct = default);
    Task<Tenant?> GetAsync(Guid id, CancellationToken ct = default);
    Task<Tenant?> GetBySlugAsync(string slug, CancellationToken ct = default);
    Task SaveAsync(Tenant tenant, CancellationToken ct = default);
}

internal sealed class TenantStore(IDbContextFactory<PlatformDbContext> factory, IMemoryCache cache) : ITenantStore
{
    private const string AllKey = "platform:tenants";

    private async Task<List<Tenant>> AllAsync(CancellationToken ct)
    {
        if (cache.TryGetValue(AllKey, out List<Tenant>? tenants) && tenants is not null)
            return tenants;

        await using var db = await factory.CreateDbContextAsync(ct);
        tenants = await db.Tenants.AsNoTracking().Where(t => t.IsActive).ToListAsync(ct);
        cache.Set(AllKey, tenants, TimeSpan.FromMinutes(10));
        return tenants;
    }

    public async Task<Tenant?> FindByHostAsync(string host, CancellationToken ct = default)
    {
        host = host.ToLowerInvariant();
        return (await AllAsync(ct)).FirstOrDefault(t => t.DomainList().Contains(host));
    }

    public async Task<Tenant?> GetAsync(Guid id, CancellationToken ct = default) =>
        (await AllAsync(ct)).FirstOrDefault(t => t.Id == id);

    public async Task<Tenant?> GetBySlugAsync(string slug, CancellationToken ct = default) =>
        (await AllAsync(ct)).FirstOrDefault(t => t.Slug == slug);

    public async Task SaveAsync(Tenant tenant, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        tenant.UpdatedAt = DateTimeOffset.UtcNow;
        if (await db.Tenants.AnyAsync(t => t.Id == tenant.Id, ct)) db.Update(tenant);
        else db.Add(tenant);
        await db.SaveChangesAsync(ct);
        cache.Remove(AllKey);
    }
}

/// <summary>Expose aux autres modules l'essentiel de l'entreprise courante.</summary>
internal sealed class TenantInfoProvider(ITenantContext tenantContext, ITenantStore store) : ITenantInfoProvider
{
    public async Task<TenantInfo> GetCurrentAsync(CancellationToken ct = default)
    {
        var tenant = await store.GetAsync(tenantContext.TenantId, ct)
            ?? throw new InvalidOperationException("Entreprise introuvable.");
        return new TenantInfo(
            tenant.Id, tenant.Name, tenant.Tagline, tenant.PublicEmail, tenant.Phone,
            string.IsNullOrWhiteSpace(tenant.AdminNotificationEmail) ? tenant.PublicEmail : tenant.AdminNotificationEmail,
            tenant.PrimaryColor, tenant.SiteUrl);
    }
}

/// <summary>Accès à l'entreprise courante complète pour le site et le back-office.</summary>
public interface ICurrentTenant
{
    Task<Tenant> GetAsync(CancellationToken ct = default);
}

internal sealed class CurrentTenant(ITenantContext tenantContext, ITenantStore store) : ICurrentTenant
{
    public async Task<Tenant> GetAsync(CancellationToken ct = default) =>
        await store.GetAsync(tenantContext.TenantId, ct)
        ?? throw new InvalidOperationException("Entreprise introuvable.");
}
