using Hub.SharedKernel;

namespace Hub.Modules.Channels.Publishers;

/// <summary>Ce qui part sur le réseau : texte, lien mesuré (absolu), image publique (absolue).</summary>
public sealed record PublishRequest(string Message, string? Link, string? ImageUrl);

public sealed record PublishResult(bool Success, string? ExternalId, string? ExternalUrl, string? Error)
{
    public static PublishResult Ok(string id, string? url) => new(true, id, url, null);
    public static PublishResult Fail(string error) => new(false, null, null, error);
}

/// <summary>Identifiants de connexion d'un réseau (déchiffrés, jamais affichés).</summary>
public sealed record ChannelCredentials(string AccountId, string AccessToken, string? AccountName = null);

/// <summary>
/// Connecteur d'un réseau social. Ajouter un réseau = une classe qui implémente
/// cette interface, enregistrée dans <see cref="ChannelsModule"/> : rien d'autre ne change.
/// </summary>
public interface ISocialPublisher
{
    ChannelType Channel { get; }

    /// <summary>Vérifie la connexion ; renvoie le nom du compte (ex. le nom de la page).</summary>
    Task<Result<string>> TestAsync(ChannelCredentials credentials, CancellationToken ct = default);

    Task<PublishResult> PublishAsync(PublishRequest request, ChannelCredentials credentials, CancellationToken ct = default);
}

/// <summary>
/// Réseau dont la connexion se déduit d'un autre déjà connecté (ex. Instagram depuis la page Facebook) :
/// l'administrateur n'a aucun jeton à copier.
/// </summary>
public interface ILinkedChannel
{
    ChannelType Source { get; }

    /// <summary>Trouve le compte lié au compte source et renvoie ses identifiants.</summary>
    Task<Result<ChannelCredentials>> DeriveAsync(ChannelCredentials source, CancellationToken ct = default);
}

/// <summary>Chiffres d'une publication : personnes atteintes et interactions.</summary>
public sealed record PostInsight(int Reach, int Interactions);

/// <summary>
/// Lecture des chiffres d'un réseau (personnes atteintes, interactions, abonnés).
/// Implémentée par le connecteur du réseau quand celui-ci les fournit.
/// </summary>
public interface ISocialInsights
{
    ChannelType Channel { get; }
    Task<Result<PostInsight>> GetPostAsync(string externalId, ChannelCredentials credentials, CancellationToken ct = default);
    Task<Result<int>> GetFollowersAsync(ChannelCredentials credentials, CancellationToken ct = default);
}
