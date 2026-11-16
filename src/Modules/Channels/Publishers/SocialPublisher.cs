using Hub.SharedKernel;

namespace Hub.Modules.Channels.Publishers;

/// <summary>
/// Ce qui part sur le réseau : texte, lien mesuré (absolu), image publique (absolue) et, pour un Reel,
/// la vidéo verticale publique (absolue). Un réseau qui ne prend pas la vidéo publie l'image.
/// </summary>
public sealed record PublishRequest(string Message, string? Link, string? ImageUrl, string? VideoUrl = null);

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

/// <summary>Une cible de publication proposée par le réseau (ex. une fiche Google parmi celles du compte).</summary>
public sealed record ChannelTarget(string Id, string Name, string? Detail);

/// <summary>
/// Réseau qui se connecte par le bouton « Se connecter avec… » (OAuth) : l'administrateur choisit son compte
/// chez le réseau, aucun jeton à copier. Le jeton de renouvellement est conservé chiffré.
/// </summary>
public interface IOAuthChannel
{
    /// <summary>Faux tant que l'application n'est pas configurée (identifiant et secret absents).</summary>
    bool IsConfigured { get; }

    string AuthorizeUrl(string redirectUri, string state);

    /// <summary>Échange le code reçu au retour, puis choisit la meilleure cible (ex. la fiche dont le nom ressemble à l'entreprise).</summary>
    Task<Result<ChannelCredentials>> CompleteAsync(string code, string redirectUri, string? preferredName, CancellationToken ct = default);

    /// <summary>Toutes les cibles possibles pour ces identifiants (pour en changer).</summary>
    Task<Result<IReadOnlyList<ChannelTarget>>> ListTargetsAsync(ChannelCredentials credentials, CancellationToken ct = default);
}

/// <summary>Un avis client lu sur le réseau.</summary>
public sealed record ExternalReview(string Id, string Author, int Rating, string? Comment, DateTimeOffset CreatedAt, string? Reply, DateTimeOffset? RepliedAt);

/// <summary>Réseau qui porte des avis clients (Google) : les lire et y répondre.</summary>
public interface IReviewSource
{
    ChannelType Channel { get; }
    Task<Result<IReadOnlyList<ExternalReview>>> ListReviewsAsync(ChannelCredentials credentials, CancellationToken ct = default);
    Task<Result> ReplyAsync(ChannelCredentials credentials, string reviewId, string reply, CancellationToken ct = default);
}

/// <summary>Chiffres d'une fiche d'établissement sur une période.</summary>
public sealed record LocationMetrics(int Views, int Calls, int Directions, int WebsiteClicks);

/// <summary>Réseau qui fournit les chiffres d'une fiche (vues, appels, itinéraires, clics vers le site).</summary>
public interface ILocationInsights
{
    ChannelType Channel { get; }
    Task<Result<LocationMetrics>> GetLocationAsync(ChannelCredentials credentials, DateOnly from, DateOnly to, CancellationToken ct = default);
}
