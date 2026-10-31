using Hub.Modules.Leads.Contracts;
using Hub.Modules.Quotes.Application;
using Hub.Modules.Quotes.Infrastructure;
using Hub.SharedKernel.Data;
using Hub.SharedKernel.Events;
using Hub.SharedKernel.Modules;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Hub.Modules.Quotes;

/// <summary>
/// Assistant de devis : propose un prix et un message pour chaque demande de devis,
/// à partir de la grille interne. Un humain valide toujours avant l'envoi.
/// L'hôte doit fournir <see cref="IQuoteContextSource"/>.
/// </summary>
public static class QuotesModule
{
    public static IServiceCollection AddQuotesModule(this IServiceCollection services, IConfiguration configuration, string connectionString)
    {
        services.AddModuleDbContext<QuotesDbContext>(connectionString, QuotesDbContext.SchemaName);
        services.Configure<QuoteOptions>(configuration.GetSection(QuoteOptions.Section));
        services.Configure<AnthropicOptions>(configuration.GetSection(AnthropicOptions.Section));

        // Claude si une clé est fournie, sinon le modèle simulé.
        if (!string.IsNullOrWhiteSpace(configuration[$"{AnthropicOptions.Section}:ApiKey"]))
            services.AddHttpClient<IQuoteModel, ClaudeQuoteModel>(c => c.Timeout = TimeSpan.FromSeconds(90));
        else
            services.AddSingleton<IQuoteModel, SimulatedQuoteModel>();

        services.AddScoped<IQuoteService, QuoteService>();
        services.AddSingleton<QuoteQueue>();
        services.AddScoped<IIntegrationEventHandler<LeadSubmitted>, QuoteRequestedHandler>();
        services.AddHostedService<QuoteWorker>();
        services.AddSingleton<IModuleInitializer, QuotesInitializer>();
        return services;
    }
}

internal sealed class QuotesInitializer : IModuleInitializer
{
    public int Order => 60;

    public async Task InitializeAsync(IServiceProvider services, CancellationToken ct = default)
    {
        var factory = services.GetRequiredService<IDbContextFactory<QuotesDbContext>>();
        await using var db = await factory.CreateDbContextAsync(ct);
        await db.Database.MigrateAsync(ct);
    }
}
