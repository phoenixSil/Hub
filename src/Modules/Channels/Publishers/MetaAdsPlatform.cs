using System.Collections.Concurrent;
using System.Globalization;
using System.Text.Json.Nodes;
using Hub.Modules.Channels.Application.Ads;
using Hub.Modules.Channels.Domain;
using Hub.SharedKernel;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Hub.Modules.Channels.Publishers;

/// <summary>
/// Régie publicitaire de Meta (Marketing API) : campagne avec budget TOTAL fixe, ensemble de publicités (public, dates),
/// visuel (publication existante ou nouvelle annonce), annonce. Tout est créé en pause, puis la campagne est activée
/// d'un coup : si une étape échoue, la campagne est supprimée et rien n'est diffusé.
/// </summary>
internal sealed class MetaAdsPlatform(HttpClient http, IOptions<FacebookOptions> options, ILogger<MetaAdsPlatform> logger) : IAdsPlatform
{
    public bool IsSimulated => false;

    internal TimeSpan VideoPollInterval { get; init; } = TimeSpan.FromSeconds(10);

    private string Url(string path) => $"{options.Value.GraphBaseUrl.TrimEnd('/')}/{options.Value.GraphVersion}/{path}";

    public async Task<Result<AdAccountInfo>> TestAsync(AdAccess access, CancellationToken ct = default)
    {
        var got = await GetAsync($"act_{access.AccountId}?fields=name,currency,account_status", access, ct);
        if (got.IsFailure) return Result<AdAccountInfo>.Failure(got.Error!);
        var j = got.Value!;
        return Result<AdAccountInfo>.Success(new AdAccountInfo(
            j["name"]?.GetValue<string>() ?? $"act_{access.AccountId}",
            j["currency"]?.GetValue<string>() ?? "CAD",
            j["account_status"]?.GetValue<int>() == 1));
    }

    public async Task<ReachEstimate?> EstimateAsync(AdAccess access, AdAudience audience, AdObjective objective, CancellationToken ct = default)
    {
        var goal = Plan(objective).Optimization;
        var got = await GetAsync($"act_{access.AccountId}/delivery_estimate?optimization_goal={goal}&targeting_spec={Uri.EscapeDataString(Targeting(audience).ToJsonString())}", access, ct);
        if (got.IsFailure) { logger.LogInformation("Estimation Meta indisponible : {Error}", got.Error); return null; }
        var first = First(got.Value!["data"]);
        var lower = first?["estimate_mau_lower_bound"]?.GetValue<long>();
        var upper = first?["estimate_mau_upper_bound"]?.GetValue<long>();
        return lower is null || upper is null ? null : new ReachEstimate(lower.Value, upper.Value);
    }

    public async Task<Result<LaunchedAd>> LaunchAsync(AdAccess access, AdLaunch launch, CancellationToken ct = default)
    {
        var plan = Plan(launch.Objective);
        var campaign = await PostAsync($"act_{access.AccountId}/campaigns", access, new()
        {
            ["name"] = launch.Name,
            ["objective"] = plan.Objective,
            ["status"] = "PAUSED",
            ["special_ad_categories"] = "[]",
            ["lifetime_budget"] = Cents(launch.Budget),
            ["bid_strategy"] = "LOWEST_COST_WITHOUT_CAP",
        }, ct);
        if (campaign.IsFailure) return Result<LaunchedAd>.Failure(campaign.Error!);
        var campaignId = campaign.Value!["id"]!.GetValue<string>();
        try
        {
            var adSetForm = new Dictionary<string, string>
            {
                ["name"] = $"{launch.Name} · public",
                ["campaign_id"] = campaignId,
                ["optimization_goal"] = plan.Optimization,
                ["billing_event"] = "IMPRESSIONS",
                ["start_time"] = launch.StartAt.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture),
                ["end_time"] = launch.EndAt.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture),
                ["targeting"] = Targeting(launch.Audience).ToJsonString(),
                ["status"] = "ACTIVE",
            };
            if (plan.Destination is not null) adSetForm["destination_type"] = plan.Destination;
            var adSet = await PostAsync($"act_{access.AccountId}/adsets", access, adSetForm, ct);
            if (adSet.IsFailure) return await AbortAsync(access, campaignId, adSet.Error!, ct);
            var adSetId = adSet.Value!["id"]!.GetValue<string>();

            var creativeForm = await CreativeAsync(access, launch.Creative, launch.Objective, launch.Audience.Instagram, ct);
            if (creativeForm.IsFailure) return await AbortAsync(access, campaignId, creativeForm.Error!, ct);
            creativeForm.Value!["name"] = $"{launch.Name} · visuel";
            var creative = await PostAsync($"act_{access.AccountId}/adcreatives", access, creativeForm.Value, ct);
            if (creative.IsFailure && creative.Error!.Contains("enroll_status", StringComparison.OrdinalIgnoreCase))
            {
                // Certains comptes exigent un choix explicite sur les « améliorations standard » de Meta : on les refuse
                // (le visuel reste tel que vous l'avez approuvé).
                creativeForm.Value["degrees_of_freedom_spec"] = """{"creative_features_spec":{"standard_enhancements":{"enroll_status":"OPT_OUT"}}}""";
                creative = await PostAsync($"act_{access.AccountId}/adcreatives", access, creativeForm.Value, ct);
            }
            if (creative.IsFailure) return await AbortAsync(access, campaignId, creative.Error!, ct);

            var ad = await PostAsync($"act_{access.AccountId}/ads", access, new()
            {
                ["name"] = launch.Name,
                ["adset_id"] = adSetId,
                ["creative"] = new JsonObject { ["creative_id"] = creative.Value!["id"]!.GetValue<string>() }.ToJsonString(),
                ["status"] = "ACTIVE",
            }, ct);
            if (ad.IsFailure) return await AbortAsync(access, campaignId, ad.Error!, ct);

            var on = await PostAsync(campaignId, access, new() { ["status"] = "ACTIVE" }, ct);
            if (on.IsFailure) return await AbortAsync(access, campaignId, on.Error!, ct);
            return Result<LaunchedAd>.Success(new LaunchedAd(campaignId, adSetId, ad.Value!["id"]!.GetValue<string>()));
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !ct.IsCancellationRequested)
        {
            logger.LogError(ex, "Meta injoignable pendant le lancement");
            return await AbortAsync(access, campaignId, "Meta est injoignable pour le moment.", ct);
        }
    }

    private async Task<Result<LaunchedAd>> AbortAsync(AdAccess access, string campaignId, string error, CancellationToken ct)
    {
        try { await PostAsync(campaignId, access, new() { ["status"] = "DELETED" }, CancellationToken.None); }
        catch (HttpRequestException ex) { logger.LogWarning(ex, "Campagne {Id} à supprimer à la main (elle est en pause)", campaignId); }
        return Result<LaunchedAd>.Failure(error);
    }

    public async Task<Result> SetActiveAsync(AdAccess access, string campaignId, bool active, CancellationToken ct = default)
    {
        var done = await PostAsync(campaignId, access, new() { ["status"] = active ? "ACTIVE" : "PAUSED" }, ct);
        return done.IsSuccess ? Result.Success() : Result.Failure(done.Error!);
    }

    public async Task<Result<AdSnapshot>> SnapshotAsync(AdAccess access, string campaignId, string adId, CancellationToken ct = default)
    {
        var ad = await GetAsync($"{adId}?fields=effective_status,ad_review_feedback", access, ct);
        if (ad.IsFailure) return Result<AdSnapshot>.Failure(ad.Error!);
        var insights = await GetAsync($"{campaignId}/insights?fields=spend,reach,impressions,inline_link_clicks,actions&date_preset=maximum", access, ct);
        if (insights.IsFailure) return Result<AdSnapshot>.Failure(insights.Error!);
        return Result<AdSnapshot>.Success(Snapshot(ad.Value!, insights.Value!));
    }

    internal static AdSnapshot Snapshot(JsonNode ad, JsonNode insights)
    {
        var review = ad["ad_review_feedback"] is JsonObject feedback
            ? string.Join(" ", feedback.SelectMany(section => section.Value is JsonObject reasons
                ? reasons.Select(r => r.Value?.ToString() ?? r.Key) : [section.Value?.ToString() ?? ""])).Trim()
            : null;
        var row = First(insights["data"]);
        int Int(string name) => int.TryParse(row?[name]?.ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : 0;
        var spend = decimal.TryParse(row?["spend"]?.ToString(), NumberStyles.Number, CultureInfo.InvariantCulture, out var s) ? s : 0;
        var engagements = (row?["actions"]?.AsArray() ?? [])
            .Where(a => a?["action_type"]?.GetValue<string>() == "post_engagement")
            .Select(a => int.TryParse(a!["value"]?.ToString(), out var v) ? v : 0).Sum();
        return new AdSnapshot(ad["effective_status"]?.GetValue<string>() ?? "UNKNOWN", string.IsNullOrWhiteSpace(review) ? null : review,
            spend, Int("reach"), Int("impressions"), Int("inline_link_clicks"), engagements);
    }

    /// <summary>Premier élément d'une liste de Meta (null si elle est vide : pas encore de chiffres).</summary>
    private static JsonNode? First(JsonNode? list) => list is JsonArray { Count: > 0 } array ? array[0] : null;

    /// <summary>Objectif Hub → objectif de campagne, optimisation et destination chez Meta.</summary>
    internal static (string Objective, string Optimization, string? Destination) Plan(AdObjective objective) => objective switch
    {
        AdObjective.Engagement => ("OUTCOME_ENGAGEMENT", "POST_ENGAGEMENT", "ON_POST"),
        AdObjective.Reach => ("OUTCOME_AWARENESS", "REACH", null),
        _ => ("OUTCOME_TRAFFIC", "LINK_CLICKS", "WEBSITE"),
    };

    /// <summary>
    /// Public : un rayon autour d'un point (en km), une tranche d'âge, les réseaux choisis.
    /// Le public élargi automatique de Meta (Advantage+) est désactivé : la publicité reste dans votre secteur.
    /// </summary>
    internal static JsonObject Targeting(AdAudience a)
    {
        var platforms = new JsonArray();
        if (a.Facebook) platforms.Add("facebook");
        if (a.Instagram) platforms.Add("instagram");
        return new JsonObject
        {
            ["geo_locations"] = new JsonObject
            {
                ["custom_locations"] = new JsonArray(new JsonObject
                {
                    ["latitude"] = a.Latitude,
                    ["longitude"] = a.Longitude,
                    ["radius"] = a.RadiusKm,
                    ["distance_unit"] = "kilometer",
                }),
            },
            ["age_min"] = a.AgeMin,
            ["age_max"] = a.AgeMax,
            ["publisher_platforms"] = platforms,
            ["targeting_automation"] = new JsonObject { ["advantage_audience"] = 0 },
        };
    }

    internal static string Cents(decimal amount) => ((long)Math.Round(amount * 100m, MidpointRounding.AwayFromZero)).ToString(CultureInfo.InvariantCulture);

    /// <summary>Champs du visuel selon le type (publication existante, ou nouvelle annonce image / vidéo).</summary>
    private async Task<Result<Dictionary<string, string>>> CreativeAsync(AdAccess access, AdCreative c, AdObjective objective, bool instagram, CancellationToken ct)
    {
        var form = new Dictionary<string, string>();
        var igUser = instagram ? c.InstagramUserId : null;
        switch (c.Kind)
        {
            case AdCreativeKind.FacebookPost:
                form["object_story_id"] = c.SourceId!;
                if (igUser is not null) form["instagram_user_id"] = igUser;
                return Result<Dictionary<string, string>>.Success(form);
            case AdCreativeKind.InstagramMedia:
                form["object_id"] = c.PageId;
                form["instagram_user_id"] = c.InstagramUserId!;
                form["source_instagram_media_id"] = c.SourceId!;
                return Result<Dictionary<string, string>>.Success(form);
            case AdCreativeKind.FacebookVideo:
                form["object_story_spec"] = StorySpec(c.PageId, igUser, "video_data", new JsonObject
                {
                    ["video_id"] = c.SourceId,
                    ["image_url"] = c.ImageUrl,
                    ["message"] = c.Message,
                }).ToJsonString();
                return Result<Dictionary<string, string>>.Success(form);
            case AdCreativeKind.LinkImage:
            {
                var hash = await UploadImageAsync(access, c.ImageUrl!, ct);
                if (hash.IsFailure) return Result<Dictionary<string, string>>.Failure(hash.Error!);
                form["object_story_spec"] = StorySpec(c.PageId, igUser, "link_data", new JsonObject
                {
                    ["link"] = c.Link,
                    ["message"] = c.Message,
                    ["name"] = c.Headline,
                    ["image_hash"] = hash.Value,
                    ["call_to_action"] = CallToAction(objective, c.Link!),
                }).ToJsonString();
                return Result<Dictionary<string, string>>.Success(form);
            }
            default:
            {
                var video = await UploadVideoAsync(access, c.VideoUrl!, ct);
                if (video.IsFailure) return Result<Dictionary<string, string>>.Failure(video.Error!);
                form["object_story_spec"] = StorySpec(c.PageId, igUser, "video_data", new JsonObject
                {
                    ["video_id"] = video.Value,
                    ["image_url"] = c.ImageUrl,
                    ["title"] = c.Headline,
                    ["message"] = c.Message,
                    ["call_to_action"] = CallToAction(objective, c.Link!),
                }).ToJsonString();
                return Result<Dictionary<string, string>>.Success(form);
            }
        }
    }

    internal static JsonObject StorySpec(string pageId, string? instagramUserId, string dataKey, JsonObject data)
    {
        var spec = new JsonObject { ["page_id"] = pageId };
        if (instagramUserId is not null) spec["instagram_user_id"] = instagramUserId;
        spec[dataKey] = data;
        return spec;
    }

    /// <summary>Bouton de l'annonce : « Obtenir un devis » pour les demandes, « En savoir plus » sinon.</summary>
    internal static JsonObject CallToAction(AdObjective objective, string link) => new()
    {
        ["type"] = objective == AdObjective.Quotes ? "GET_QUOTE" : "LEARN_MORE",
        ["value"] = new JsonObject { ["link"] = link },
    };

    private async Task<Result<string>> UploadImageAsync(AdAccess access, string imageUrl, CancellationToken ct)
    {
        using var image = await http.GetAsync(imageUrl, ct);
        if (!image.IsSuccessStatusCode) return Result<string>.Failure($"Image de l'annonce introuvable ({(int)image.StatusCode}).");
        var bytes = await image.Content.ReadAsByteArrayAsync(ct);
        var sent = await PostAsync($"act_{access.AccountId}/adimages", access, new() { ["bytes"] = Convert.ToBase64String(bytes) }, ct);
        if (sent.IsFailure) return Result<string>.Failure(sent.Error!);
        var hash = (sent.Value!["images"] as JsonObject)?.Select(i => i.Value?["hash"]?.GetValue<string>()).FirstOrDefault(h => h is not null);
        return hash is null ? Result<string>.Failure("Meta n'a pas accepté l'image de l'annonce.") : Result<string>.Success(hash);
    }

    /// <summary>Envoie la vidéo (Meta la télécharge depuis son adresse publique), puis attend qu'elle soit prête (3 min au plus).</summary>
    private async Task<Result<string>> UploadVideoAsync(AdAccess access, string videoUrl, CancellationToken ct)
    {
        var sent = await PostAsync($"act_{access.AccountId}/advideos", access, new() { ["file_url"] = videoUrl }, ct);
        if (sent.IsFailure) return Result<string>.Failure(sent.Error!);
        var id = sent.Value!["id"]!.GetValue<string>();
        var deadline = DateTimeOffset.UtcNow.AddMinutes(3);
        while (DateTimeOffset.UtcNow < deadline)
        {
            var state = await GetAsync($"{id}?fields=status", access, ct);
            var status = state.IsSuccess ? state.Value!["status"]?["video_status"]?.GetValue<string>() : null;
            if (status == "ready") return Result<string>.Success(id);
            if (status == "error") return Result<string>.Failure("Meta n'a pas pu traiter la vidéo de l'annonce.");
            await Task.Delay(VideoPollInterval, ct);
        }
        return Result<string>.Failure("La vidéo de l'annonce n'était pas prête à temps chez Meta : réessayez dans quelques minutes.");
    }

    private async Task<Result<JsonNode>> GetAsync(string pathAndQuery, AdAccess access, CancellationToken ct)
    {
        var separator = pathAndQuery.Contains('?') ? '&' : '?';
        try
        {
            using var response = await http.GetAsync($"{Url(pathAndQuery)}{separator}access_token={Uri.EscapeDataString(access.Token)}", ct);
            var json = JsonNode.Parse(await response.Content.ReadAsStringAsync(ct));
            return response.IsSuccessStatusCode && json is not null
                ? Result<JsonNode>.Success(json)
                : Result<JsonNode>.Failure(ErrorOf(json) ?? $"Meta a répondu {(int)response.StatusCode}.");
        }
        catch (Exception ex) when (ex is HttpRequestException or System.Text.Json.JsonException or TaskCanceledException && !ct.IsCancellationRequested)
        {
            logger.LogError(ex, "Meta (publicités) injoignable");
            return Result<JsonNode>.Failure("Meta est injoignable pour le moment.");
        }
    }

    private async Task<Result<JsonNode>> PostAsync(string path, AdAccess access, Dictionary<string, string> form, CancellationToken ct)
    {
        form["access_token"] = access.Token;
        try
        {
            using var response = await http.PostAsync(Url(path), new FormUrlEncodedContent(form), ct);
            var json = JsonNode.Parse(await response.Content.ReadAsStringAsync(ct));
            return response.IsSuccessStatusCode && json is not null
                ? Result<JsonNode>.Success(json)
                : Result<JsonNode>.Failure(ErrorOf(json) ?? $"Meta a répondu {(int)response.StatusCode}.");
        }
        catch (Exception ex) when (ex is HttpRequestException or System.Text.Json.JsonException or TaskCanceledException && !ct.IsCancellationRequested)
        {
            logger.LogError(ex, "Meta (publicités) injoignable");
            return Result<JsonNode>.Failure("Meta est injoignable pour le moment.");
        }
    }

    /// <summary>Message d'erreur lisible (Meta donne souvent un détail destiné à l'utilisateur).</summary>
    internal static string? ErrorOf(JsonNode? json)
    {
        var error = json?["error"];
        if (error is null) return null;
        var code = error["code"]?.ToString();
        var detail = error["error_user_msg"]?.GetValue<string>() ?? error["message"]?.GetValue<string>();
        return code switch
        {
            "190" => "Le jeton des publicités est expiré ou invalide : reconnectez le compte publicitaire.",
            "200" or "10" or "294" => $"Permission manquante pour les publicités (ads_management) : {detail}",
            _ => $"Meta : {detail} (code {code}).",
        };
    }
}

/// <summary>Régie simulée (développement et tests) : rien n'est envoyé à Meta, rien n'est dépensé.</summary>
internal sealed class SimulatedAdsPlatform(TimeProvider clock) : IAdsPlatform
{
    private static readonly ConcurrentDictionary<string, (decimal Budget, DateTimeOffset Start, DateTimeOffset End, bool Paused)> Ads = new();

    public bool IsSimulated => true;

    public Task<Result<AdAccountInfo>> TestAsync(AdAccess access, CancellationToken ct = default) =>
        Task.FromResult(Result<AdAccountInfo>.Success(new AdAccountInfo("Compte publicitaire (simulé)", "CAD", true)));

    public Task<ReachEstimate?> EstimateAsync(AdAccess access, AdAudience audience, AdObjective objective, CancellationToken ct = default) =>
        Task.FromResult<ReachEstimate?>(new ReachEstimate(audience.RadiusKm * 9_000L, audience.RadiusKm * 12_000L));

    public Task<Result<LaunchedAd>> LaunchAsync(AdAccess access, AdLaunch launch, CancellationToken ct = default)
    {
        var id = Guid.NewGuid().ToString("N")[..10];
        Ads[$"simule-c-{id}"] = (launch.Budget, launch.StartAt, launch.EndAt, false);
        return Task.FromResult(Result<LaunchedAd>.Success(new LaunchedAd($"simule-c-{id}", $"simule-s-{id}", $"simule-a-{id}")));
    }

    public Task<Result> SetActiveAsync(AdAccess access, string campaignId, bool active, CancellationToken ct = default)
    {
        if (Ads.TryGetValue(campaignId, out var ad)) Ads[campaignId] = ad with { Paused = !active };
        return Task.FromResult(Result.Success());
    }

    /// <summary>Dépense proportionnelle au temps écoulé (jamais plus que le budget), chiffres déduits de la dépense.</summary>
    public Task<Result<AdSnapshot>> SnapshotAsync(AdAccess access, string campaignId, string adId, CancellationToken ct = default)
    {
        if (!Ads.TryGetValue(campaignId, out var ad))
            return Task.FromResult(Result<AdSnapshot>.Success(new AdSnapshot("ACTIVE", null, 0, 0, 0, 0, 0)));
        var total = (ad.End - ad.Start).TotalSeconds;
        var elapsed = Math.Clamp((clock.GetUtcNow() - ad.Start).TotalSeconds / Math.Max(1, total), 0.05, 1);
        var spend = Math.Round(ad.Budget * (decimal)elapsed, 2);
        var reach = (int)(spend * 180);
        return Task.FromResult(Result<AdSnapshot>.Success(new AdSnapshot(ad.Paused ? "CAMPAIGN_PAUSED" : "ACTIVE", null,
            spend, reach, reach * 2, (int)(spend * 4), (int)(spend * 9))));
    }
}
