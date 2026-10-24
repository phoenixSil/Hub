using System.Threading.Channels;
using Hub.Modules.Analytics.Domain;
using Hub.Modules.Analytics.Infrastructure;
using Hub.Modules.Leads.Contracts;
using Hub.SharedKernel.Events;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Hub.Modules.Analytics.Application;

/// <summary>Enregistre un événement de visite. Les écritures sont regroupées en arrière-plan.</summary>
public interface IAnalyticsRecorder
{
    void Record(AnalyticsEvent evt);
}

/// <summary>
/// File en mémoire vidée toutes les 2 minutes (ou dès 200 événements) :
/// une écriture groupée plutôt qu'une écriture par page vue, ce qui laisse
/// la base serverless se mettre en pause entre deux lots.
/// </summary>
internal sealed class BufferedAnalyticsRecorder : BackgroundService, IAnalyticsRecorder
{
    private static readonly TimeSpan FlushEvery = TimeSpan.FromMinutes(2);
    private const int BatchSize = 200;

    private readonly Channel<AnalyticsEvent> _queue =
        Channel.CreateBounded<AnalyticsEvent>(new BoundedChannelOptions(10_000) { FullMode = BoundedChannelFullMode.DropOldest });
    private readonly IServiceScopeFactory _scopes;
    private readonly ILogger<BufferedAnalyticsRecorder> _logger;

    public BufferedAnalyticsRecorder(IServiceScopeFactory scopes, ILogger<BufferedAnalyticsRecorder> logger)
    {
        _scopes = scopes;
        _logger = logger;
    }

    public void Record(AnalyticsEvent evt)
    {
        if (evt.TenantId == Guid.Empty) return;
        _queue.Writer.TryWrite(evt);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var batch = new List<AnalyticsEvent>(BatchSize);
        using var timer = new PeriodicTimer(FlushEvery);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await timer.WaitForNextTickAsync(stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }

            await DrainAsync(batch, stoppingToken);
        }

        // Dernier vidage à l'arrêt de l'application.
        await DrainAsync(batch, CancellationToken.None);
    }

    private async Task DrainAsync(List<AnalyticsEvent> batch, CancellationToken ct)
    {
        while (_queue.Reader.TryRead(out var evt))
        {
            batch.Add(evt);
            if (batch.Count >= BatchSize)
                await FlushAsync(batch, ct);
        }
        if (batch.Count > 0)
            await FlushAsync(batch, ct);
    }

    private async Task FlushAsync(List<AnalyticsEvent> batch, CancellationToken ct)
    {
        try
        {
            await using var scope = _scopes.CreateAsyncScope();
            var factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<AnalyticsDbContext>>();
            await using var db = await factory.CreateDbContextAsync(ct);
            db.Events.AddRange(batch);
            await db.SaveChangesAsync(ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Impossible d'enregistrer {Count} événements de mesure.", batch.Count);
        }
        finally
        {
            batch.Clear();
        }
    }
}

/// <summary>Compte chaque demande reçue comme une conversion, avec sa provenance.</summary>
internal sealed class LeadSubmittedHandler(IAnalyticsRecorder recorder) : IIntegrationEventHandler<LeadSubmitted>
{
    public Task HandleAsync(LeadSubmitted e, CancellationToken ct = default)
    {
        recorder.Record(new AnalyticsEvent
        {
            TenantId = e.TenantId,
            OccurredAt = e.OccurredAt,
            Type = AnalyticsEventType.LeadSubmitted,
            Path = e.LandingPage ?? "/",
            Source = e.Source,
            Medium = e.Medium,
            Campaign = e.Campaign,
            VisitorId = e.VisitorId,
            LeadId = e.LeadId,
            Detail = e.Kind.ToString(),
        });
        return Task.CompletedTask;
    }
}
