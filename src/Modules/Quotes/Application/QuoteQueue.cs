using System.Threading.Channels;
using Hub.Modules.Leads.Contracts;
using Hub.SharedKernel.Events;
using Hub.SharedKernel.Tenancy;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Hub.Modules.Quotes.Application;

/// <summary>File des demandes de devis à chiffrer : le formulaire répond tout de suite, l'assistant travaille ensuite.</summary>
internal sealed class QuoteQueue
{
    private readonly Channel<(Guid TenantId, Guid LeadId)> _channel =
        Channel.CreateBounded<(Guid, Guid)>(new BoundedChannelOptions(200) { FullMode = BoundedChannelFullMode.DropOldest });

    public void Enqueue(Guid tenantId, Guid leadId) => _channel.Writer.TryWrite((tenantId, leadId));
    public IAsyncEnumerable<(Guid TenantId, Guid LeadId)> ReadAllAsync(CancellationToken ct) => _channel.Reader.ReadAllAsync(ct);
}

/// <summary>Une demande de devis arrive : on la met dans la file de l'assistant.</summary>
internal sealed class QuoteRequestedHandler(QuoteQueue queue, IOptions<QuoteOptions> options) : IIntegrationEventHandler<LeadSubmitted>
{
    public Task HandleAsync(LeadSubmitted e, CancellationToken ct = default)
    {
        if (e.Kind == LeadKind.Quote && options.Value.AutoGenerate)
            queue.Enqueue(e.TenantId, e.LeadId);
        return Task.CompletedTask;
    }
}

internal sealed class QuoteWorker(QuoteQueue queue, IServiceScopeFactory scopes, ILogger<QuoteWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var (tenantId, leadId) in queue.ReadAllAsync(stoppingToken))
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                scope.ServiceProvider.GetRequiredService<ITenantContext>().Set(tenantId);
                await scope.ServiceProvider.GetRequiredService<IQuoteService>().GenerateAsync(leadId, stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Proposition automatique impossible pour la demande {LeadId}", leadId);
            }
        }
    }
}
