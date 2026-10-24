using Hub.SharedKernel.Caching;
using Hub.SharedKernel.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Hub.SharedKernel.Data;

/// <summary>
/// Accès simple aux données d'un module pour le back-office : chaque appel
/// ouvre un contexte court (compatible avec les circuits Blazor Server).
/// </summary>
public interface IRepository<T> where T : TenantEntity
{
    Task<List<T>> ListAsync(Func<IQueryable<T>, IQueryable<T>>? query = null, CancellationToken ct = default);
    Task<T?> GetAsync(Guid id, CancellationToken ct = default);
    Task<int> CountAsync(Func<IQueryable<T>, IQueryable<T>>? query = null, CancellationToken ct = default);
    Task SaveAsync(T entity, CancellationToken ct = default);
    Task DeleteAsync(Guid id, CancellationToken ct = default);
}

public sealed class EfRepository<TContext, T>(IDbContextFactory<TContext> factory, ContentCache cache) : IRepository<T>
    where TContext : DbContext
    where T : TenantEntity
{
    public async Task<List<T>> ListAsync(Func<IQueryable<T>, IQueryable<T>>? query = null, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        IQueryable<T> q = db.Set<T>().AsNoTracking();
        if (query is not null) q = query(q);
        return await q.ToListAsync(ct);
    }

    public async Task<T?> GetAsync(Guid id, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        return await db.Set<T>().AsNoTracking().FirstOrDefaultAsync(e => e.Id == id, ct);
    }

    public async Task<int> CountAsync(Func<IQueryable<T>, IQueryable<T>>? query = null, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        IQueryable<T> q = db.Set<T>();
        if (query is not null) q = query(q);
        return await q.CountAsync(ct);
    }

    public async Task SaveAsync(T entity, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var exists = await db.Set<T>().AnyAsync(e => e.Id == entity.Id, ct);
        if (exists) db.Update(entity);
        else db.Add(entity);
        await db.SaveChangesAsync(ct);
        cache.Invalidate();
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var entity = await db.Set<T>().FirstOrDefaultAsync(e => e.Id == id, ct);
        if (entity is null) return;
        db.Remove(entity);
        await db.SaveChangesAsync(ct);
        cache.Invalidate();
    }
}

public static class DataServiceCollectionExtensions
{
    /// <summary>Inscrit le contexte du module (fabrique à portée « scoped » pour recevoir l'entreprise courante).</summary>
    public static IServiceCollection AddModuleDbContext<TContext>(
        this IServiceCollection services, string connectionString, string schema)
        where TContext : DbContext
    {
        services.AddDbContextFactory<TContext>(options =>
            options.UseSqlServer(connectionString, sql =>
            {
                sql.MigrationsHistoryTable("__EFMigrationsHistory", schema);
                sql.EnableRetryOnFailure(5);
            }),
            ServiceLifetime.Scoped);
        return services;
    }

    public static IServiceCollection AddRepository<TContext, T>(this IServiceCollection services)
        where TContext : DbContext
        where T : TenantEntity
    {
        services.AddScoped<IRepository<T>, EfRepository<TContext, T>>();
        return services;
    }
}
