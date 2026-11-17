using System.Text.Json.Nodes;
using Hub.SharedKernel;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Hub.Modules.Channels.Publishers;

/// <summary>
/// Publication sur un compte Instagram professionnel relié à la page Facebook (API Graph de Meta).
/// Instagram n'accepte que des publications avec image : on crée d'abord le média (image + légende),
/// on attend qu'Instagram l'ait traité, puis on le publie.
/// Les liens ne sont pas cliquables dans une légende Instagram : l'adresse du site est écrite en clair.
/// </summary>
internal sealed class InstagramPublisher(HttpClient http, IOptions<FacebookOptions> options, ILogger<InstagramPublisher> logger)
    : ISocialPublisher, ILinkedChannel, ISocialInsights
{
    public const int MaxCaption = 2200;

    /// <summary>Attente maximale du traitement de l'image par Instagram.</summary>
    internal TimeSpan ProcessingTimeout { get; init; } = TimeSpan.FromSeconds(60);

    /// <summary>Une vidéo (Reel) demande plus de traitement chez Instagram qu'une image.</summary>
    internal TimeSpan VideoProcessingTimeout { get; init; } = TimeSpan.FromMinutes(5);
    internal TimeSpan PollInterval { get; init; } = TimeSpan.FromSeconds(3);

    public ChannelType Channel => ChannelType.Instagram;
    public ChannelType Source => ChannelType.Facebook;

    private string Url(string path) => $"{options.Value.GraphBaseUrl.TrimEnd('/')}/{options.Value.GraphVersion}/{path}";

    public async Task<Result<ChannelCredentials>> DeriveAsync(ChannelCredentials page, CancellationToken ct = default)
    {
        var json = await GetAsync($"{page.AccountId}?fields=instagram_business_account{{id,username}}", page.AccessToken, ct);
        if (json.IsFailure) return Result<ChannelCredentials>.Failure(json.Error!);
        var account = json.Value!["instagram_business_account"];
        var id = account?["id"]?.GetValue<string>();
        if (string.IsNullOrEmpty(id))
            return Result<ChannelCredentials>.Failure(
                "Aucun compte Instagram professionnel n'est relié à la page Facebook. Dans l'application Instagram : " +
                "Paramètres → Type de compte → passer à un compte professionnel, puis relier la page ScintiNet " +
                "(ou depuis la page Facebook : Paramètres → Comptes liés → Instagram).");
        var name = account?["username"]?.GetValue<string>();
        return Result<ChannelCredentials>.Success(new ChannelCredentials(id, page.AccessToken, name is null ? null : "@" + name));
    }

    public async Task<Result<string>> TestAsync(ChannelCredentials c, CancellationToken ct = default)
    {
        var json = await GetAsync($"{c.AccountId}?fields=id,username", c.AccessToken, ct);
        if (json.IsFailure) return Result<string>.Failure(json.Error!);
        var name = json.Value!["username"]?.GetValue<string>();
        return Result<string>.Success(name is null ? c.AccountId : "@" + name);
    }

    public async Task<PublishResult> PublishAsync(PublishRequest r, ChannelCredentials c, CancellationToken ct = default)
    {
        var reel = !string.IsNullOrWhiteSpace(r.VideoUrl);
        if (!reel && string.IsNullOrWhiteSpace(r.ImageUrl))
            return PublishResult.Fail("Instagram exige une image : ajoutez un visuel à la publication.");

        try
        {
            // 1. Le média : un Reel (vidéo verticale, aussi visible dans le fil) ou une image, avec sa légende.
            var form = new Dictionary<string, string>
            {
                ["caption"] = Caption(r.Message, r.Link),
                ["access_token"] = c.AccessToken,
            };
            if (reel)
            {
                form["media_type"] = "REELS";
                form["video_url"] = r.VideoUrl!;
                form["share_to_feed"] = "true";
            }
            else form["image_url"] = r.ImageUrl!;
            var created = await PostAsync($"{c.AccountId}/media", form, ct);
            if (created.IsFailure) return PublishResult.Fail(created.Error!);
            var container = created.Value!["id"]?.GetValue<string>();
            if (container is null) return PublishResult.Fail("Instagram n'a pas créé le média.");

            // 2. Instagram télécharge et traite l'image (ou la vidéo, plus longue) : on attend qu'elle soit prête.
            var ready = await WaitUntilReadyAsync(container, c.AccessToken, ct, reel ? VideoProcessingTimeout : ProcessingTimeout);
            if (ready.IsFailure) return PublishResult.Fail(ready.Error!);

            // 3. La publication.
            var published = await PostAsync($"{c.AccountId}/media_publish", new Dictionary<string, string>
            {
                ["creation_id"] = container,
                ["access_token"] = c.AccessToken,
            }, ct);
            if (published.IsFailure) return PublishResult.Fail(published.Error!);
            var mediaId = published.Value!["id"]?.GetValue<string>();
            if (mediaId is null) return PublishResult.Fail("Réponse d'Instagram sans identifiant de publication.");

            var permalink = await GetAsync($"{mediaId}?fields=permalink", c.AccessToken, ct);
            return PublishResult.Ok(mediaId, permalink.IsSuccess ? permalink.Value!["permalink"]?.GetValue<string>() : null);
        }
        catch (HttpRequestException ex)
        {
            logger.LogError(ex, "Instagram injoignable");
            return PublishResult.Fail("Instagram est injoignable pour le moment.");
        }
    }

    /// <summary>Légende Instagram : le texte, puis l'adresse du site en clair (les liens n'y sont pas cliquables).</summary>
    internal static string Caption(string message, string? link)
    {
        var text = message.Trim();
        if (Uri.TryCreate(link, UriKind.Absolute, out var uri))
        {
            var site = uri.Host.Replace("www.", "", StringComparison.OrdinalIgnoreCase);
            if (!text.Contains(site, StringComparison.OrdinalIgnoreCase)) text += $"\n\n👉 Infos et devis gratuit : {site}";
        }
        return text.Length <= MaxCaption ? text : text[..(MaxCaption - 1)] + "…";
    }

    private async Task<Result> WaitUntilReadyAsync(string container, string token, CancellationToken ct, TimeSpan timeout)
    {
        var deadline = DateTimeOffset.UtcNow + timeout;
        while (true)
        {
            var status = await GetAsync($"{container}?fields=status_code,status", token, ct);
            if (status.IsFailure) return Result.Failure(status.Error!);
            switch (status.Value!["status_code"]?.GetValue<string>())
            {
                case "FINISHED":
                case "PUBLISHED":
                case null:
                    return Result.Success();
                case "ERROR":
                case "EXPIRED":
                    return Result.Failure($"Instagram a refusé le média ({status.Value["status"]?.GetValue<string>() ?? "format non accepté"}). " +
                                          "Image : JPEG de 320 à 1440 px de large. Vidéo : MP4 vertical de 3 à 90 secondes.");
            }
            if (DateTimeOffset.UtcNow >= deadline) return Result.Failure("Instagram traite encore l'image : réessayez dans une minute.");
            await Task.Delay(PollInterval, ct);
        }
    }

    public async Task<Result<PostInsight>> GetPostAsync(string externalId, ChannelCredentials c, CancellationToken ct = default)
    {
        try
        {
            // « reach » = comptes uniques atteints ; « total_interactions » = j'aime, commentaires, partages, enregistrements.
            var json = await GetAsync($"{externalId}/insights?metric=reach,total_interactions", c.AccessToken, ct);
            // Compte récent : Instagram ne donne pas de chiffres tant qu'une publication a trop peu de vues. Ce n'est pas une erreur.
            if (json.IsFailure && json.Error == NotEnoughViewers) return Result<PostInsight>.Success(new PostInsight(0, 0));
            if (json.IsFailure) return Result<PostInsight>.Failure(json.Error!);
            return Result<PostInsight>.Success(new PostInsight(
                FacebookPagePublisher.MetricValue(json.Value, "reach"),
                FacebookPagePublisher.MetricValue(json.Value, "total_interactions")));
        }
        catch (HttpRequestException ex)
        {
            logger.LogWarning(ex, "Chiffres Instagram indisponibles");
            return Result<PostInsight>.Failure("Instagram est injoignable pour le moment.");
        }
    }

    public async Task<Result<int>> GetFollowersAsync(ChannelCredentials c, CancellationToken ct = default)
    {
        try
        {
            var json = await GetAsync($"{c.AccountId}?fields=followers_count", c.AccessToken, ct);
            return json.IsFailure
                ? Result<int>.Failure(json.Error!)
                : Result<int>.Success(FacebookPagePublisher.Int(json.Value!["followers_count"]) ?? 0);
        }
        catch (HttpRequestException ex)
        {
            logger.LogWarning(ex, "Abonnés Instagram indisponibles");
            return Result<int>.Failure("Instagram est injoignable pour le moment.");
        }
    }

    private async Task<Result<JsonNode>> GetAsync(string path, string token, CancellationToken ct)
    {
        var separator = path.Contains('?') ? '&' : '?';
        using var response = await http.GetAsync(Url($"{path}{separator}access_token={Uri.EscapeDataString(token)}"), ct);
        return await ReadAsync(response, ct);
    }

    private async Task<Result<JsonNode>> PostAsync(string path, Dictionary<string, string> form, CancellationToken ct)
    {
        using var response = await http.PostAsync(Url(path), new FormUrlEncodedContent(form), ct);
        return await ReadAsync(response, ct);
    }

    private static async Task<Result<JsonNode>> ReadAsync(HttpResponseMessage response, CancellationToken ct)
    {
        var json = JsonNode.Parse(await response.Content.ReadAsStringAsync(ct));
        if (response.IsSuccessStatusCode && json is not null && json["error"] is null) return Result<JsonNode>.Success(json);
        return Result<JsonNode>.Failure(ErrorOf(json) ?? $"Instagram a répondu {(int)response.StatusCode}.");
    }

    /// <summary>Instagram ne calcule pas les chiffres d'une publication qui a encore trop peu de vues (compte récent).</summary>
    internal const string NotEnoughViewers = "Instagram : pas encore assez de vues pour donner des chiffres.";

    internal static string? ErrorOf(JsonNode? json)
    {
        var error = json?["error"];
        if (error is null) return null;
        var code = error["code"]?.ToString();
        var message = error["message"]?.GetValue<string>() ?? "";
        // Le code 10 sert aussi à « Not enough viewers for the media to show insights » : ce n'est pas une permission.
        if (message.Contains("not enough viewers", StringComparison.OrdinalIgnoreCase)) return NotEnoughViewers;
        return code switch
        {
            "190" => "Le jeton de la page Facebook est expiré ou invalide : reconnectez la page, puis Instagram.",
            "10" or "200" => "Autorisations Instagram manquantes : régénérez le jeton de la page avec instagram_basic, " +
                             "instagram_content_publish et instagram_manage_insights (Canaux → Instagram → la marche à suivre), puis reconnectez." +
                             (message.Length > 0 ? $" (Instagram : {message})" : ""),
            "9004" or "36003" => "Instagram n'a pas pu récupérer l'image : elle doit être publique, en JPEG, au format carré ou proche.",
            _ => $"Instagram : {message} (code {code}).",
        };
    }
}
