using System.Collections.Concurrent;
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

    /// <summary>Service d'envoi des vidéos (Reels) de Meta.</summary>
    public string UploadBaseUrl { get; set; } = "https://rupload.facebook.com";
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
        if (!string.IsNullOrWhiteSpace(r.VideoUrl)) return await PublishReelAsync(r, c, ct);
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

    /// <summary>
    /// Reel de page en trois temps (API Reels de Meta) : réserver la vidéo, la faire télécharger par Meta depuis son adresse
    /// publique, puis la publier avec sa description (texte + lien).
    /// </summary>
    private async Task<PublishResult> PublishReelAsync(PublishRequest r, ChannelCredentials c, CancellationToken ct)
    {
        try
        {
            var start = await FormAsync(Url($"{c.AccountId}/video_reels"), new()
            {
                ["upload_phase"] = "start",
                ["access_token"] = c.AccessToken,
            }, ct);
            if (start.IsFailure) return PublishResult.Fail(start.Error!);
            var videoId = start.Value!["video_id"]?.GetValue<string>();
            if (videoId is null) return PublishResult.Fail("Facebook n'a pas réservé la vidéo du Reel.");

            using (var upload = new HttpRequestMessage(HttpMethod.Post,
                       $"{options.Value.UploadBaseUrl.TrimEnd('/')}/video-upload/{options.Value.GraphVersion}/{videoId}"))
            {
                upload.Headers.TryAddWithoutValidation("Authorization", $"OAuth {c.AccessToken}");
                upload.Headers.TryAddWithoutValidation("file_url", r.VideoUrl);
                using var sent = await http.SendAsync(upload, ct);
                if (!sent.IsSuccessStatusCode)
                    return PublishResult.Fail(ErrorOf(JsonNode.Parse(await sent.Content.ReadAsStringAsync(ct))) ?? $"Facebook n'a pas reçu la vidéo ({(int)sent.StatusCode}).");
            }

            var finish = await FormAsync(Url($"{c.AccountId}/video_reels"), new()
            {
                ["upload_phase"] = "finish",
                ["video_id"] = videoId,
                ["video_state"] = "PUBLISHED",
                ["description"] = string.IsNullOrWhiteSpace(r.Link) ? r.Message : $"{r.Message}\n\n{r.Link}",
                ["access_token"] = c.AccessToken,
            }, ct);
            if (finish.IsFailure) return PublishResult.Fail(finish.Error!);
            return PublishResult.Ok(videoId, $"https://www.facebook.com/reel/{videoId}");
        }
        catch (HttpRequestException ex)
        {
            logger.LogError(ex, "Facebook injoignable (Reel)");
            return PublishResult.Fail("Facebook est injoignable pour le moment.");
        }
    }

    private async Task<Result<JsonNode>> FormAsync(string url, Dictionary<string, string> form, CancellationToken ct)
    {
        using var response = await http.PostAsync(url, new FormUrlEncodedContent(form), ct);
        var json = JsonNode.Parse(await response.Content.ReadAsStringAsync(ct));
        return response.IsSuccessStatusCode && json is not null
            ? Result<JsonNode>.Success(json)
            : Result<JsonNode>.Failure(ErrorOf(json) ?? $"Facebook a répondu {(int)response.StatusCode}.");
    }

    public async Task<Result<PostInsight>> GetPostAsync(string externalId, ChannelCredentials c, CancellationToken ct = default)
    {
        var token = Uri.EscapeDataString(c.AccessToken);
        try
        {
            // Deux lectures indépendantes : les interactions (réactions, commentaires, partages) et les vues (read_insights).
            // L'une peut être refusée sans l'autre : on garde ce qui est lisible et on ne signale un problème que si rien ne l'est.
            using var counts = await http.GetAsync(Url(
                $"{externalId}?fields=reactions.summary(total_count).limit(0),comments.summary(total_count).limit(0),shares&access_token={token}"), ct);
            var countsJson = JsonNode.Parse(await counts.Content.ReadAsStringAsync(ct));
            if (!counts.IsSuccessStatusCode)
            {
                logger.LogWarning("Interactions Facebook illisibles pour {Post} : {Error}", externalId, countsJson?["error"]?.ToJsonString());
                // Sans la permission de lire les commentaires, on retente avec les seuls partages et réactions.
                using var light = await http.GetAsync(Url($"{externalId}?fields=reactions.summary(total_count).limit(0),shares&access_token={token}"), ct);
                var lightJson = JsonNode.Parse(await light.Content.ReadAsStringAsync(ct));
                countsJson = light.IsSuccessStatusCode ? lightJson : countsJson;
            }

            using var insights = await http.GetAsync(Url($"{externalId}/insights?metric={ReachMetric}&access_token={token}"), ct);
            var insightsJson = JsonNode.Parse(await insights.Content.ReadAsStringAsync(ct));
            if (!insights.IsSuccessStatusCode)
            {
                logger.LogWarning("Vues Facebook illisibles pour {Post} : {Error}", externalId, insightsJson?["error"]?.ToJsonString());
                if (countsJson?["error"] is not null)
                    return Result<PostInsight>.Failure(InsightsError(insightsJson) ?? $"Facebook a répondu {(int)insights.StatusCode}.");
                insightsJson = null;
            }
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

    /// <summary>Refus de Meta, avec son propre message pour savoir exactement quelle autorisation manque.</summary>
    internal static string? InsightsError(JsonNode? json) =>
        json?["error"]?["code"]?.ToString() is "10" or "200"
            ? $"Facebook refuse la lecture des chiffres (autorisation manquante) : « {json["error"]?["message"]?.GetValue<string>()} »."
              + " Vérifiez read_insights et pages_read_engagement sur le jeton de la page, puis reconnectez Facebook."
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
internal sealed class SimulatedPublisher(ChannelType channel, ILogger logger) : ISocialPublisher, ISocialInsights, IReviewSource, ILocationInsights
{
    // Avis simulés (fiche Google) : deux avis, dont un sans réponse ; les réponses envoyées sont gardées en mémoire.
    private static readonly ConcurrentDictionary<string, (string Text, DateTimeOffset At)> Replies = new();

    public Task<Result<IReadOnlyList<ExternalReview>>> ListReviewsAsync(ChannelCredentials c, CancellationToken ct = default)
    {
        IReadOnlyList<ExternalReview> list =
        [
            Review("simule-avis-1", "Marie Tremblay", 5, "Équipe ponctuelle et minutieuse, la maison n'a jamais été aussi propre. Merci !", new DateTimeOffset(2026, 11, 10, 15, 0, 0, TimeSpan.Zero)),
            Review("simule-avis-2", "Jean-François L.", 4, "Très bon travail, petit retard à l'arrivée mais résultat impeccable.", new DateTimeOffset(2026, 11, 12, 18, 30, 0, TimeSpan.Zero)),
        ];
        return Task.FromResult(Result<IReadOnlyList<ExternalReview>>.Success(list));
    }

    private static ExternalReview Review(string id, string author, int rating, string comment, DateTimeOffset at) =>
        Replies.TryGetValue(id, out var r) ? new ExternalReview(id, author, rating, comment, at, r.Text, r.At) : new ExternalReview(id, author, rating, comment, at, null, null);

    public Task<Result> ReplyAsync(ChannelCredentials c, string reviewId, string reply, CancellationToken ct = default)
    {
        Replies[reviewId] = (reply, DateTimeOffset.UtcNow);
        logger.LogInformation("Réponse simulée à l'avis {Review} : {Reply}", reviewId, reply);
        return Task.FromResult(Result.Success());
    }

    public Task<Result<LocationMetrics>> GetLocationAsync(ChannelCredentials c, DateOnly from, DateOnly to, CancellationToken ct = default)
    {
        var days = to.DayNumber - from.DayNumber + 1;
        return Task.FromResult(Result<LocationMetrics>.Success(new LocationMetrics(42 * days, days / 2, days / 3, days)));
    }

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

    /// <summary>Marqueur pour tester les relances : un texte qui le contient échoue une fois par réseau, puis passe.</summary>
    public const string TransientFailureMarker = "[échec passager]";
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, bool> FailedOnce = new();

    public Task<PublishResult> PublishAsync(PublishRequest r, ChannelCredentials c, CancellationToken ct = default)
    {
        if (r.Message.Contains(TransientFailureMarker, StringComparison.Ordinal) && FailedOnce.TryAdd($"{channel}|{r.Message}", true))
            return Task.FromResult(PublishResult.Fail("Service temporairement indisponible (simulé)."));
        var id = $"simule_{Guid.NewGuid():N}"[..20];
        logger.LogInformation("Publication simulée sur {Channel} : {Message} | {Link} | {Image} | {Video}", channel, r.Message, r.Link, r.ImageUrl, r.VideoUrl ?? "(pas de vidéo)");
        return Task.FromResult(PublishResult.Ok(id, null));
    }
}
