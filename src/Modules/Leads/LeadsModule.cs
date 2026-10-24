using Hub.Modules.Leads.Application;
using Hub.Modules.Leads.Domain;
using Hub.Modules.Leads.Infrastructure;
using Hub.SharedKernel.Data;
using Hub.SharedKernel.Modules;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Hub.Modules.Leads;

public static class LeadsModule
{
    public static IServiceCollection AddLeadsModule(this IServiceCollection services, string connectionString)
    {
        services.AddModuleDbContext<LeadsDbContext>(connectionString, LeadsDbContext.SchemaName);
        services.AddRepository<LeadsDbContext, ClosedDay>();
        services.AddScoped<ILeadService, LeadService>();
        services.AddSingleton<IModuleInitializer, LeadsInitializer>();
        return services;
    }
}

internal sealed class LeadsInitializer : IModuleInitializer
{
    public int Order => 30;

    public async Task InitializeAsync(IServiceProvider services, CancellationToken ct = default)
    {
        var factory = services.GetRequiredService<IDbContextFactory<LeadsDbContext>>();
        await using var db = await factory.CreateDbContextAsync(ct);
        await db.Database.MigrateAsync(ct);
    }
}
