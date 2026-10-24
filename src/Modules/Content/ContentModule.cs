using Hub.Modules.Content.Application;
using Hub.Modules.Content.Domain;
using Hub.Modules.Content.Infrastructure;
using Hub.SharedKernel.Data;
using Hub.SharedKernel.Modules;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Hub.Modules.Content;

public static class ContentModule
{
    public static IServiceCollection AddContentModule(this IServiceCollection services, string connectionString)
    {
        services.AddModuleDbContext<ContentDbContext>(connectionString, ContentDbContext.SchemaName);
        services.AddRepository<ContentDbContext, ServiceArea>();
        services.AddRepository<ContentDbContext, Page>();
        services.AddRepository<ContentDbContext, Promotion>();
        services.AddRepository<ContentDbContext, FaqItem>();
        services.AddRepository<ContentDbContext, Review>();
        services.AddRepository<ContentDbContext, ContentBlock>();
        services.AddRepository<ContentDbContext, HighlightItem>();
        services.AddScoped<IContentQueries, ContentQueries>();
        services.AddSingleton<IModuleInitializer, ContentInitializer>();
        return services;
    }
}

internal sealed class ContentInitializer : IModuleInitializer
{
    public int Order => 20;

    public async Task InitializeAsync(IServiceProvider services, CancellationToken ct = default)
    {
        var factory = services.GetRequiredService<IDbContextFactory<ContentDbContext>>();
        await using var db = await factory.CreateDbContextAsync(ct);
        await db.Database.MigrateAsync(ct);
    }
}
