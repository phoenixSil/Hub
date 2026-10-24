using Hub.Modules.Catalog.Application;
using Hub.Modules.Catalog.Domain;
using Hub.Modules.Catalog.Infrastructure;
using Hub.SharedKernel.Data;
using Hub.SharedKernel.Modules;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Hub.Modules.Catalog;

public static class CatalogModule
{
    public static IServiceCollection AddCatalogModule(this IServiceCollection services, string connectionString)
    {
        services.AddModuleDbContext<CatalogDbContext>(connectionString, CatalogDbContext.SchemaName);
        services.AddRepository<CatalogDbContext, ServiceCategory>();
        services.AddRepository<CatalogDbContext, Service>();
        services.AddRepository<CatalogDbContext, PricingModel>();
        services.AddScoped<ICatalogQueries, CatalogQueries>();
        services.AddSingleton<IModuleInitializer, CatalogInitializer>();
        return services;
    }
}

internal sealed class CatalogInitializer : IModuleInitializer
{
    public int Order => 10;

    public async Task InitializeAsync(IServiceProvider services, CancellationToken ct = default)
    {
        var factory = services.GetRequiredService<IDbContextFactory<CatalogDbContext>>();
        await using var db = await factory.CreateDbContextAsync(ct);
        await db.Database.MigrateAsync(ct);
    }
}
