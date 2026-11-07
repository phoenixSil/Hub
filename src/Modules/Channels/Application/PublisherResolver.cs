using Hub.Modules.Channels.Publishers;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Hub.Modules.Channels.Application;

public sealed class ChannelsOptions
{
    public const string Section = "Channels";

    /// <summary>Remplacer les vrais réseaux par des réseaux simulés (développement, tests). Jamais en production.</summary>
    public bool Simulate { get; set; }
}

/// <summary>Donne le connecteur d'un réseau : le vrai, ou le simulé si Channels:Simulate est activé.</summary>
public interface IPublisherResolver
{
    ISocialPublisher For(ChannelType channel);
    IReadOnlyCollection<ChannelType> Supported { get; }
    bool IsSimulated { get; }

    /// <summary>Le connecteur réel du réseau s'il se connecte à partir d'un autre (ex. Instagram depuis Facebook).</summary>
    ILinkedChannel? LinkedFor(ChannelType channel);

    /// <summary>Lecteur des chiffres du réseau (le simulé si Channels:Simulate), ou null si le réseau n'en fournit pas.</summary>
    ISocialInsights? InsightsFor(ChannelType channel);
}

internal sealed class PublisherResolver(
    IEnumerable<ISocialPublisher> publishers,
    IOptions<ChannelsOptions> options,
    ILoggerFactory loggers) : IPublisherResolver
{
    private readonly Dictionary<ChannelType, ISocialPublisher> _byChannel = publishers.ToDictionary(p => p.Channel);

    public IReadOnlyCollection<ChannelType> Supported => _byChannel.Keys;
    public bool IsSimulated => options.Value.Simulate;

    public ILinkedChannel? LinkedFor(ChannelType channel) => _byChannel.GetValueOrDefault(channel) as ILinkedChannel;

    public ISocialInsights? InsightsFor(ChannelType channel) =>
        _byChannel.ContainsKey(channel) ? For(channel) as ISocialInsights : null;

    public ISocialPublisher For(ChannelType channel)
    {
        if (!_byChannel.ContainsKey(channel))
            throw new InvalidOperationException($"Aucun connecteur pour {channel}.");
        return options.Value.Simulate
            ? new SimulatedPublisher(channel, loggers.CreateLogger("Hub.Channels.Simulated"))
            : _byChannel[channel];
    }
}
