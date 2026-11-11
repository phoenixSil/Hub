using Hub.SharedKernel;

namespace Hub.Modules.Channels.Application.Autopilot;

/// <summary>Une page du site dont on peut parler (service ou offre), avec les seuls chiffres autorisés.</summary>
public sealed record AutopilotPage(
    string Title,
    string Url,
    string Summary,
    string? ImageUrl,
    bool IsPromotion,
    // Chiffres que l'on a le droit d'afficher (ex. « -20 % ») : uniquement ceux d'une offre en cours.
    string? Facts);

/// <summary>Résultat d'une publication passée : ce qui a marché sert à choisir les prochaines.</summary>
public sealed record AutopilotHistory(
    string Title, string? Audience, DateTimeOffset? PublishedAt, int Visits, int Leads, int Won = 0, string? Theme = null,
    int Reach = 0, int Interactions = 0, string? Pillar = null, string? Format = null);

/// <summary>Ce que le pilote sait de l'entreprise. Assemblé par l'hôte à partir des autres modules.</summary>
public sealed record AutopilotContext(
    string CompanyName,
    string City,
    string Phone,
    string PrimaryColor,
    string AccentColor,
    string? SiteUrl,
    string AdminEmail,
    IReadOnlyList<AutopilotPage> Pages,
    string? LogoUrl = null,
    string? LogoOnDarkUrl = null,
    // Thème saisonnier du site en cours (« Halloween », « Noël »), ou null.
    string? Season = null);

/// <summary>Fournie par l'hôte : le module ne connaît ni le catalogue, ni le contenu, ni les statistiques.</summary>
public interface IAutopilotContextSource
{
    Task<AutopilotContext> GetAsync(CancellationToken ct = default);

    /// <summary>Visites, demandes et clients gagnés apportés par chaque campagne (utm_campaign).</summary>
    Task<IReadOnlyDictionary<string, (int Visits, int Leads, int Won)>> GetResultsAsync(IReadOnlyCollection<string> campaigns, CancellationToken ct = default);
}

/// <summary>Convertit un visuel SVG en image PNG (côté serveur, avec les polices de la marque).</summary>
public interface IVisualRenderer
{
    byte[]? RenderPng(string svg, int size = 1080);
}

/// <summary>Enregistre l'image d'une publication et renvoie son adresse publique.</summary>
public interface IVisualStore
{
    Task<Result<string>> SavePngAsync(byte[] png, CancellationToken ct = default);

    /// <summary>Relit une image publiée (adresse du site ou du stockage) ; null si elle est introuvable.</summary>
    Task<byte[]?> LoadAsync(string url, CancellationToken ct = default);
}
