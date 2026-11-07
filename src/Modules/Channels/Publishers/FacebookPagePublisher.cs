using System.Text.Json.Nodes;
using Hub.SharedKernel;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Hub.Modules.Channels.Publishers;

public sealed class FacebookOptions
{
    public const string Section = "Facebook";

    /// <summary>Version de l'API Graph de Meta.</summary>
    public string GraphVersion { get; set; } = "v27.0";
    public string GraphBaseUrl { get; set; } = "https://graph.facebook.com";
}

/// <summary>
/// Publication sur une page Facebook par l'API Graph, avec le jeton de la page :
/// texte + lien (/feed), ou photo + légende (/photos) quand une image est fournie.
/// </summary>
internal sealed class FacebookPagePublisher(HttpClient http, IOptions<FacebookOptions> options, ILogger<FacebookPagePublisher> logger)
    : ISocialPublisher, ISocialInsights
{
    /// <summary>Personnes atteintes par une publication (Meta a remplacé la « portée » par les vues uniques en juin 2026).</summary>
    public const string ReachMetric = "post_total_media_view_unique";

    public ChannelType Channel => ChannelType.Facebook;

    private string Url(string path) => $"{options.Value.GraphBaseUrl.TrimEnd('/')}/{options.Value.GraphVersion}/{path}";

    public async Task<Result<string>> TestAsync(ChannelCredentials c, CancellationToken ct = default)
    {
        // Avec un jeton de page, « me » désigne la page elle-même. Avec un jeton d'utilisateur, « me » est la personne :
        // Facebook refuserait alors toute publication (erreur « publish_actions »). On le détecte dès la connexion.
        var url = Url($"me?fields=id,name&access_token={Uri.EscapeDataString(c.AccessToken)}");
        using var response = await http.GetAsync(url, ct);
        var json = JsonNode.Parse(await response.Content.ReadAsStringAsync(ct));
        if (!response.IsSuccessStatusCode)
            return Result<string>.Failure(ErrorOf(json) ?? $"Facebook a répondu {(int)response.StatusCode}.");

        var id = json?["id"]?.GetValue<string>();
        var name = json?["name"]?.GetValue<string>();
        if (!string.Equals(id, c.AccountId.Trim(), StringComparison.Ordinal))
            return Result<string>.Failure(
                $"Ce jeton appartient à « {name ?? id} », pas à la page n° {c.AccountId}. " +
                "Il faut le jeton DE LA PAGE : dans l'Explorateur de l'API Graph, choisissez la page dans « Utilisateur ou Page », " +
                "ou copiez l'access_token renvoyé par la requête « ID_DE_LA_PAGE?fields=name,access_token ».");
        return Result<string>.Success(name ?? c.AccountId);
    }

    public async Task<PublishResult> PublishAsync(PublishRequest r, ChannelCredentials c, CancellationToken ct = default)
    {
        var form = new Dictionary<string, string> { ["access_token"] = c.AccessToken };
        string path;
        if (!string.IsNullOrWhiteSpace(r.ImageUrl))
        {
            // Une photo avec légende : le lien est ajouté au texte (Facebook n'affiche pas d'aperçu de lien sur une photo).
            path = $"{c.AccountId}/photos";
            form["url"] = r.ImageUrl;
            form["caption"] = string.IsNullOrWhiteSpace(r.Link) ? r.Message : $"{r.Message}\n\n{r.Link}";
        }
        else
        {
            path = $"{c.AccountId}/feed";
            form["message"] = r.Message;
            if (!string.IsNullOrWhiteSpace(r.Link)) form["link"] = r.Link;
        }

        try
        {
            using var response = await http.PostAsync(Url(path), new FormUrlEncodedContent(form), ct);
            var json = JsonNode.Parse(await response.Content.ReadAsStringAsync(ct));
            if (!response.IsSuccessStatusCode)
                return PublishResult.Fail(ErrorOf(json) ?? $"Facebook a répondu {(int)response.StatusCode}.");

            // /photos renvoie « post_id » (la publication) en plus de « id » (la photo).
            var id = json?["post_id"]?.GetValue<string>() ?? json?["id"]?.GetValue<string>();
            return id is null
                ? PublishResult.Fail("Réponse de Facebook sans identifiant de publication.")
                : PublishResult.Ok(id, $"https://www.facebook.com/{id}");
        }
        catch (HttpRequestException ex)
        {
            logger.LogError(ex, "Facebook injoignable");
            return PublishResult.Fail("Facebook est injoignable pour le moment.");
        }
    }

    public async Task<Result<PostInsight>> GetPostAsync(string externalId, ChannelCredentials c, CancellationToken ct = default)
    {
        var token = Uri.EscapeDataString(c.AccessToken);
        try
        {
            using var counts = await http.GetAsync(Url(
                $"{externalId}?fields=reactions.summary(total_count).limit(0),comments.summary(total_count).limit(0),shares&access_token={token}"), ct);
            var countsJson = JsonNode.Parse(await counts.Content.ReadAsStringAsync(ct));
            if (!counts.IsSuccessStatusCode) return Result<PostInsight>.Failure(InsightsError(countsJson) ?? $"Facebook a répondu {(int)counts.StatusCode}.");

            // Les vues demandent la permission read_insights : sans elle, on garde au moins les interactions.
            using var insights = await http.GetAsync(Url($"{externalId}/insights?metric={ReachMetric}&access_token={token}"), ct);
            var insightsJson = insights.IsSuccessStatusCode ? JsonNode.Parse(await insights.Content.ReadAsStringAsync(ct)) : null;
            return Result<PostInsight>.Success(new PostInsight(MetricValue(insightsJson, ReachMetric), Interactions(countsJson)));
        }
        catch (HttpRequestException ex)
        {
            logger.LogWarning(ex, "Chiffres Facebook indisponibles");
            return Result<PostInsight>.Failure("Facebook est injoignable pour le moment.");
        }
    }

    public async Task<Result<int>> GetFollowersAsync(ChannelCredentials c, CancellationToken ct = default)
    {
        try
        {
            using var response = await http.GetAsync(Url($"{c.AccountId}?fields=followers_count,fan_count&access_token={Uri.EscapeDataString(c.AccessToken)}"), ct);
            var json = JsonNode.Parse(await response.Content.ReadAsStringAsync(ct));
            if (!response.IsSuccessStatusCode) return Result<int>.Failure(InsightsError(json) ?? $"Facebook a répondu {(int)response.StatusCode}.");
            return Result<int>.Success(Int(json?["followers_count"]) ?? Int(json?["fan_count"]) ?? 0);
        }
        catch (HttpRequestException ex)
        {
            logger.LogWarning(ex, "Abonnés Facebook indisponibles");
            return Result<int>.Failure("Facebook est injoignable pour le moment.");
        }
    }

    /// <summary>Réactions + commentaires + partages d'une publication.</summary>
    internal static int Interactions(JsonNode? json) =>
        (Int(json?["reactions"]?["summary"]?["total_count"]) ?? 0)
        + (Int(json?["comments"]?["summary"]?["total_count"]) ?? 0)
        + (Int(json?["shares"]?["count"]) ?? 0);

    /// <summary>Valeur d'une mesure dans une réponse « insights » de Meta (data[].name / values[0].value).</summary>
    internal static int MetricValue(JsonNode? json, string metric)
    {
        if (json?["data"] is not JsonArray data) return 0;
        var node = data.FirstOrDefault(d => d?["name"]?.GetValue<string>() == metric);
        return Int(node?["values"]?[0]?["value"]) ?? Int(node?["total_value"]?["value"]) ?? 0;
    }

    internal static int? Int(JsonNode? node)
    {
        if (node is not JsonValue v) return null;
        if (v.TryGetValue<int>(out var i)) return i;
        if (v.TryGetValue<long>(out var l)) return (int)Math.Min(l, int.MaxValue);
        if (v.TryGetValue<double>(out var d)) return (int)d;
        return int.TryParse(v.ToString(), out var p) ? p : null;
    }

    private static string? InsightsError(JsonNode? json) =>
        json?["error"]?["code"]?.ToString() is "10" or "200"
            ? "Permission read_insights manquante : régénérez le jeton de la page avec read_insights, puis reconnectez Facebook."
            : ErrorOf(json);

    internal static string? ErrorOf(JsonNode? json)
    {
        var error = json?["error"];
        if (error is null) return null;
        var message = error["message"]?.GetValue<string>();
        var code = error["code"]?.ToString();
        return code is "190" ? "Le jeton de la page Facebook est expiré ou invalide : reconnectez la page." : $"Facebook : {message} (code {code}).";
    }
}

/// <summary>Réseau simulé, pour le développement et les tests : rien ne part, la publication est notée dans le journal.</summary>
internal sealed class SimulatedPublisher(ChannelType channel, ILogger logger) : ISocialPublisher, ISocialInsights
{
    /// <summary>Chiffres simulés, stables pour une même publication (tirés de son identifiant).</summary>
    public Task<Result<PostInsight>> GetPostAsync(string externalId, ChannelCredentials c, CancellationToken ct = default)
    {
        var seed = (int)(StableHash(externalId) % 1000);
        return Task.FromResult(Result<PostInsight>.Success(new PostInsight(150 + seed, 5 + seed / 40)));
    }

    public Task<Result<int>> GetFollowersAsync(ChannelCredentials c, CancellationToken ct = default) =>
        Task.FromResult(Result<int>.Success(channel == ChannelType.Instagram ? 120 : 340));

    private static uint StableHash(string s)
    {
        uint h = 2166136261;
        foreach (var ch in s) h = (h ^ ch) * 16777619;
        return h;
    }

    public ChannelType Channel => channel;

    public Task<Result<string>> TestAsync(ChannelCredentials c, CancellationToken ct = default) =>
        Task.FromResult(Result<string>.Success($"{c.AccountName ?? "Page"} (simulée)"));

    public Task<PublishResult> PublishAsync(PublishRequest r, ChannelCredentials c, CancellationToken ct = default)
    {
        var id = $"simule_{Guid.NewGuid():N}"[..20];
        logger.LogInformation("Publication simulée sur {Channel} : {Message} | {Link} | {Image}", channel, r.Message, r.Link, r.ImageUrl);
        return Task.FromResult(PublishResult.Ok(id, null));
    }
}
