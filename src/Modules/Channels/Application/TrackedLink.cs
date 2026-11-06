using Hub.SharedKernel.Text;

namespace Hub.Modules.Channels.Application;

/// <summary>Liens mesurés : chaque réseau ajoute utm_source / utm_medium / utm_campaign au lien vers le site.</summary>
public static class TrackedLink
{
    public static string SourceOf(ChannelType channel) => channel switch
    {
        ChannelType.GoogleBusiness => "google-business",
        _ => channel.ToString().ToLowerInvariant(),
    };

    /// <summary>« https://www.scintinet.com/services/x?utm_source=facebook&amp;utm_medium=social&amp;utm_campaign=… ».</summary>
    public static string? Build(string? siteUrl, string? path, ChannelType channel, string campaign)
    {
        var absolute = Absolute(siteUrl, path);
        if (absolute is null) return null;
        var separator = absolute.Contains('?') ? '&' : '?';
        return $"{absolute}{separator}utm_source={SourceOf(channel)}&utm_medium=social&utm_campaign={Uri.EscapeDataString(campaign)}";
    }

    /// <summary>Rend une adresse absolue (les réseaux exigent des liens et des images en https complet).</summary>
    public static string? Absolute(string? siteUrl, string? pathOrUrl)
    {
        if (string.IsNullOrWhiteSpace(pathOrUrl)) return null;
        if (Uri.TryCreate(pathOrUrl, UriKind.Absolute, out var abs) && abs.Scheme is "http" or "https") return abs.ToString();
        if (string.IsNullOrWhiteSpace(siteUrl)) return null;
        return $"{siteUrl.TrimEnd('/')}/{pathOrUrl.TrimStart('/')}";
    }

    /// <summary>Identifiant de campagne lisible et unique : « 2026-11-menage-d-automne-3f2a ».</summary>
    public static string NewCampaign(string title, DateTimeOffset now)
    {
        var slug = Slug.From(title);
        if (slug.Length > 40) slug = slug[..40].TrimEnd('-');
        return $"{now:yyyy-MM}-{(slug.Length == 0 ? "publication" : slug)}-{Guid.NewGuid().ToString("N")[..4]}";
    }
}
