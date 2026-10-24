using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Hub.SharedKernel.Events;

/// <summary>Événement publié par un module et capté par d'autres, en mémoire.</summary>
public interface IIntegrationEvent
{
    Guid TenantId { get; }
    DateTimeOffset OccurredAt { get; }
}

public interface IIntegrationEventHandler<in TEvent> where TEvent : IIntegrationEvent
{
    Task HandleAsync(TEvent @event, CancellationToken ct = default);
}

public interface IEventPublisher
{
    Task PublishAsync<TEvent>(TEvent @event, CancellationToken ct = default) where TEvent : IIntegrationEvent;
}

/// <summary>
/// Publie l'événement à tous les gestionnaires inscrits. Une erreur dans un
/// gestionnaire est journalisée sans bloquer les autres ni l'appelant.
/// </summary>
public sealed class InMemoryEventPublisher(IServiceProvider services, ILogger<InMemoryEventPublisher> logger) : IEventPublisher
{
    public async Task PublishAsync<TEvent>(TEvent @event, CancellationToken ct = default) where TEvent : IIntegrationEvent
    {
        foreach (var handler in services.GetServices<IIntegrationEventHandler<TEvent>>())
        {
            try
            {
                await handler.HandleAsync(@event, ct);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Le gestionnaire {Handler} a échoué pour {Event}",
                    handler.GetType().Name, typeof(TEvent).Name);
            }
        }
    }
}
