using Hub.Modules.Analytics.Application;
using Hub.Modules.Analytics.Infrastructure;
using Hub.Modules.Leads.Contracts;
using Hub.SharedKernel.Data;
using Hub.SharedKernel.Events;
using Hub.SharedKernel.Modules;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Hub.Modules.Analytics;

public static class AnalyticsModule
{
    public static IServiceCollection AddAnalyticsModule(this IServiceCollection services, string connectionString)
    {
        services.AddModuleDbContext<AnalyticsDbContext>(connectionString, AnalyticsDbContext.SchemaName);
        services.AddSingleton<BufferedAnalyticsRecorder>();
        services.AddSingleton<IAnalyticsRecorder>(sp => sp.GetRequiredService<BufferedAnalyticsRecorder>());
        services.AddSingleton<IHostedService>(sp => sp.GetRequiredService<BufferedAnalyticsRecorder>());
        services.AddScoped<IIntegrationEventHandler<LeadSubmitted>, LeadSubmittedHandler>();
        services.AddScoped<IDashboardQueries, DashboardQueries>();
        services.AddSingleton<IModuleInitializer, AnalyticsInitializer>();
        return services;
    }
}

internal sealed class AnalyticsInitializer : IModuleInitializer
{
    public int Order => 40;

    public async Task InitializeAsync(IServiceProvider services, CancellationToken ct = default)
    {
        var factory = services.GetRequiredService<IDbContextFactory<AnalyticsDbContext>>();
        await using var db = await factory.CreateDbContextAsync(ct);
        await db.Database.MigrateAsync(ct);
    }
}
