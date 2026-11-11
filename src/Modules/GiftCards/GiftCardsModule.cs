using Hub.Modules.GiftCards.Application;
using Hub.Modules.GiftCards.Infrastructure;
using Hub.SharedKernel.Data;
using Hub.SharedKernel.Modules;
using Hub.SharedKernel.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Hub.Modules.GiftCards;

/// <summary>
/// Cartes-cadeaux : demande sur le site, paiement confirmé par l'administrateur, envoi de la carte
/// (tout de suite ou à la date choisie), solde et utilisations. L'hôte fournit <see cref="IGiftCardContextSource"/>.
/// </summary>
public static class GiftCardsModule
{
    public static IServiceCollection AddGiftCardsModule(this IServiceCollection services, string connectionString)
    {
        services.AddModuleDbContext<GiftCardsDbContext>(connectionString, GiftCardsDbContext.SchemaName);
        services.AddScoped<IGiftCardService, GiftCardService>();
        services.AddHostedService<GiftCardWorker>();
        services.AddSingleton<IModuleInitializer, GiftCardsInitializer>();
        return services;
    }
}

internal sealed class GiftCardsInitializer : IModuleInitializer
{
    public int Order => 70;

    public async Task InitializeAsync(IServiceProvider services, CancellationToken ct = default)
    {
        var factory = services.GetRequiredService<IDbContextFactory<GiftCardsDbContext>>();
        await using var db = await factory.CreateDbContextAsync(ct);
        await db.Database.MigrateAsync(ct);
    }
}

/// <summary>Toutes les heures : envoie les cartes payées dont la date d'envoi est arrivée (ex. le matin du 24 décembre).</summary>
internal sealed class GiftCardWorker(IServiceScopeFactory scopes, TimeProvider clock, ILogger<GiftCardWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try { await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken); }
        catch (OperationCanceledException) { return; }
        while (!stoppingToken.IsCancellationRequested)
        {
            try { await RunAsync(stoppingToken); }
            catch (Exception ex) when (ex is not OperationCanceledException) { logger.LogError(ex, "Envoi des cartes-cadeaux"); }
            try { await Task.Delay(TimeSpan.FromHours(1), stoppingToken); }
            catch (OperationCanceledException) { return; }
        }
    }

    private async Task RunAsync(CancellationToken ct)
    {
        // Envoi à partir de 8 h (heure de Québec) : une carte du 24 décembre n'arrive pas en pleine nuit.
        if (TimeZoneInfo.ConvertTime(clock.GetUtcNow(), Hub.SharedKernel.Time.BusinessTime.Zone).Hour < 8) return;
        var today = Hub.SharedKernel.Time.BusinessTime.Today(clock);
        List<Guid> tenants;
        await using (var scope = scopes.CreateAsyncScope())
        {
            var factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<GiftCardsDbContext>>();
            await using var db = await factory.CreateDbContextAsync(ct);
            tenants = await db.GiftCards.IgnoreQueryFilters()
                .Where(c => c.Status == Domain.GiftCardStatus.Active && c.SentAt == null && (c.DeliverOn == null || c.DeliverOn <= today))
                .Select(c => c.TenantId).Distinct().ToListAsync(ct);
        }
        foreach (var tenantId in tenants)
        {
            await using var scope = scopes.CreateAsyncScope();
            scope.ServiceProvider.GetRequiredService<ITenantContext>().Set(tenantId);
            var sent = await scope.ServiceProvider.GetRequiredService<IGiftCardService>().SendDueAsync(ct);
            if (sent > 0) logger.LogInformation("{Count} carte(s)-cadeau envoyée(s)", sent);
        }
    }
}
