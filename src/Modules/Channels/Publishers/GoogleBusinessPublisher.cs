using System.Collections.Concurrent;
using System.Globalization;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Hub.SharedKernel;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Hub.Modules.Channels.Publishers;

/// <summary>Application Google (projet Google Cloud) : identifiant et secret OAuth, dans les réglages (Google__ClientId, Google__ClientSecret).</summary>
public sealed class GoogleOptions
{
    public const string Section = "Google";
    public string? ClientId { get; set; }
    public string? ClientSecret { get; set; }
    public string AuthUrl { get; set; } = "https://accounts.google.com/o/oauth2/v2/auth";
    public string TokenUrl { get; set; } = "https://oauth2.googleapis.com/token";
    public string AccountsUrl { get; set; } = "https://mybusinessaccountmanagement.googleapis.com/v1";
    public string InformationUrl { get; set; } = "https://mybusinessbusinessinformation.googleapis.com/v1";
    public string BusinessUrl { get; set; } = "https://mybusiness.googleapis.com/v4";
    public string PerformanceUrl { get; set; } = "https://businessprofileperformance.googleapis.com/v1";
    public const string Scope = "https://www.googleapis.com/auth/business.manage";
}

/// <summary>
/// Fiche Google (Google Business Profile) : publications sur la fiche, avis clients et chiffres de la fiche.
/// Connexion par OAuth : le jeton de renouvellement est conservé chiffré ; un jeton d'accès (1 h) en est tiré au besoin.
/// L'identifiant du compte est le chemin de la fiche : « accounts/{compte}/locations/{fiche} ».
/// </summary>
internal sealed class GoogleBusinessPublisher(HttpClient http, IOptions<GoogleOptions> options, ILogger<GoogleBusinessPublisher> logger)
    : ISocialPublisher, IOAuthChannel, IReviewSource, ILocationInsights
{
    public const int MaxSummary = 1500;
    private static readonly ConcurrentDictionary<string, (string Token, DateTimeOffset Expires)> AccessTokens = new();
    private GoogleOptions O => options.Value;

    public ChannelType Channel => ChannelType.GoogleBusiness;
    public bool IsConfigured => !string.IsNullOrWhiteSpace(O.ClientId) && !string.IsNullOrWhiteSpace(O.ClientSecret);

    public string AuthorizeUrl(string redirectUri, string state) =>
        $"{O.AuthUrl}?client_id={Uri.EscapeDataString(O.ClientId ?? "")}&redirect_uri={Uri.EscapeDataString(redirectUri)}" +
        $"&response_type=code&scope={Uri.EscapeDataString(GoogleOptions.Scope)}&access_type=offline&prompt=consent" +
        $"&include_granted_scopes=true&state={Uri.EscapeDataString(state)}";

    public async Task<Result<ChannelCredentials>> CompleteAsync(string code, string redirectUri, string? preferredName, CancellationToken ct = default)
    {
        using var response = await http.PostAsync(O.TokenUrl, new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["code"] = code, ["client_id"] = O.ClientId ?? "", ["client_secret"] = O.ClientSecret ?? "",
            ["redirect_uri"] = redirectUri, ["grant_type"] = "authorization_code",
        }), ct);
        var json = JsonNode.Parse(await response.Content.ReadAsStringAsync(ct));
        var refresh = json?["refresh_token"]?.GetValue<string>();
        if (!response.IsSuccessStatusCode || string.IsNullOrEmpty(refresh))
            return Result<ChannelCredentials>.Failure("Google n'a pas accordé l'accès : " + (json?["error_description"]?.GetValue<string>() ?? json?["error"]?.GetValue<string>() ?? $"réponse {(int)response.StatusCode}"));
        if (json?["access_token"]?.GetValue<string>() is { } access)
            AccessTokens[refresh] = (access, DateTimeOffset.UtcNow.AddSeconds((json["expires_in"]?.GetValue<int>() ?? 3600) - 120));

        var temp = new ChannelCredentials("", refresh);
        var targets = await ListTargetsAsync(temp, ct);
        if (targets.IsFailure) return Result<ChannelCredentials>.Failure(targets.Error!);
        if (targets.Value!.Count == 0) return Result<ChannelCredentials>.Failure("Aucune fiche Google trouvée pour ce compte : vérifiez que la fiche est validée et que vous en êtes propriétaire ou gérant.");
        var best = BestTarget(targets.Value!, preferredName);
        return Result<ChannelCredentials>.Success(new ChannelCredentials(best.Id, refresh, best.Name));
    }

    /// <summary>La fiche dont le nom ressemble le plus à celui de l'entreprise (sinon la première).</summary>
    public static ChannelTarget BestTarget(IReadOnlyList<ChannelTarget> targets, string? preferredName)
    {
        if (string.IsNullOrWhiteSpace(preferredName)) return targets[0];
        static string N(string s) => new(s.ToLowerInvariant().Normalize(NormalizationForm.FormD).Where(char.IsLetterOrDigit).ToArray());
        var wanted = N(preferredName);
        return targets.FirstOrDefault(t => N(t.Name).Contains(wanted) || wanted.Contains(N(t.Name))) ?? targets[0];
    }

    public async Task<Result<IReadOnlyList<ChannelTarget>>> ListTargetsAsync(ChannelCredentials c, CancellationToken ct = default)
    {
        var accounts = await GetAsync(c, $"{O.AccountsUrl}/accounts", ct);
        if (accounts.IsFailure) return Result<IReadOnlyList<ChannelTarget>>.Failure(accounts.Error!);
        var list = new List<ChannelTarget>();
        foreach (var account in accounts.Value!["accounts"]?.AsArray() ?? [])
        {
            var accountName = account?["name"]?.GetValue<string>();
            if (accountName is null) continue;
            var locations = await GetAsync(c, $"{O.InformationUrl}/{accountName}/locations?readMask=name,title,storefrontAddress&pageSize=100", ct);
            if (locations.IsFailure) return Result<IReadOnlyList<ChannelTarget>>.Failure(locations.Error!);
            foreach (var l in locations.Value!["locations"]?.AsArray() ?? [])
            {
                var name = l?["name"]?.GetValue<string>();
                if (name is null) continue;
                var address = l?["storefrontAddress"];
                var detail = address is null ? null : string.Join(", ", new[]
                {
                    string.Join(" ", address["addressLines"]?.AsArray().Select(x => x?.GetValue<string>()) ?? []),
                    address["locality"]?.GetValue<string>(), address["regionCode"]?.GetValue<string>(),
                }.Where(x => !string.IsNullOrWhiteSpace(x)));
                list.Add(new ChannelTarget($"{accountName}/{name}", l?["title"]?.GetValue<string>() ?? name, detail));
            }
        }
        return Result<IReadOnlyList<ChannelTarget>>.Success(list);
    }

    public async Task<Result<string>> TestAsync(ChannelCredentials c, CancellationToken ct = default)
    {
        var targets = await ListTargetsAsync(c, ct);
        if (targets.IsFailure) return Result<string>.Failure(targets.Error!);
        var match = targets.Value!.FirstOrDefault(t => t.Id == c.AccountId);
        return match is null
            ? Result<string>.Failure("Cette fiche n'est plus accessible avec ce compte Google.")
            : Result<string>.Success(match.Name);
    }

    public async Task<PublishResult> PublishAsync(PublishRequest r, ChannelCredentials c, CancellationToken ct = default)
    {
        var body = new JsonObject
        {
            ["languageCode"] = "fr",
            ["summary"] = Summary(r.Message),
            ["topicType"] = "STANDARD",
        };
        if (!string.IsNullOrWhiteSpace(r.Link))
            body["callToAction"] = new JsonObject { ["actionType"] = "LEARN_MORE", ["url"] = r.Link };
        if (!string.IsNullOrWhiteSpace(r.ImageUrl))
            body["media"] = new JsonArray(new JsonObject { ["mediaFormat"] = "PHOTO", ["sourceUrl"] = r.ImageUrl });

        var sent = await SendAsync(c, HttpMethod.Post, $"{O.BusinessUrl}/{c.AccountId}/localPosts", body, ct);
        if (sent.IsFailure) return PublishResult.Fail(sent.Error!);
        var name = sent.Value?["name"]?.GetValue<string>() ?? "";
        return PublishResult.Ok(name, sent.Value?["searchUrl"]?.GetValue<string>());
    }

    private static readonly Regex Phone = new(@"(\+?1[\s.-]?)?\(?\d{3}\)?[\s.-]?\d{3}[\s.-]?\d{4}", RegexOptions.Compiled);

    /// <summary>
    /// Texte d'une publication Google : sans numéro de téléphone (Google refuse les publications qui en contiennent ;
    /// le bouton et la fiche donnent déjà le numéro), sans mots-clics, et au plus 1 500 caractères.
    /// </summary>
    public static string Summary(string message)
    {
        var lines = message.Replace("\r", "").Split('\n')
            .Select(l => Phone.Replace(l, "").Trim())
            .Where(l => !(l.StartsWith('#') && l.Split(' ', StringSplitOptions.RemoveEmptyEntries).All(w => w.StartsWith('#'))))
            .Select(l => Regex.Replace(l, @"[📞☎️]\s*[:\-]?\s*$", "").TrimEnd(' ', ':', '-', '·'));
        var text = Regex.Replace(string.Join("\n", lines), @"\n{3,}", "\n\n").Trim();
        return text.Length <= MaxSummary ? text : text[..(MaxSummary - 1)].TrimEnd() + "…";
    }

    public async Task<Result<IReadOnlyList<ExternalReview>>> ListReviewsAsync(ChannelCredentials c, CancellationToken ct = default)
    {
        var got = await GetAsync(c, $"{O.BusinessUrl}/{c.AccountId}/reviews?pageSize=50", ct);
        if (got.IsFailure) return Result<IReadOnlyList<ExternalReview>>.Failure(got.Error!);
        var list = (got.Value!["reviews"]?.AsArray() ?? []).Where(x => x is not null).Select(x => new ExternalReview(
                x!["reviewId"]?.GetValue<string>() ?? "",
                x["reviewer"]?["displayName"]?.GetValue<string>() ?? "Client",
                Stars(x["starRating"]?.GetValue<string>()),
                x["comment"]?.GetValue<string>(),
                Date(x["createTime"]) ?? DateTimeOffset.UtcNow,
                x["reviewReply"]?["comment"]?.GetValue<string>(),
                Date(x["reviewReply"]?["updateTime"])))
            .Where(r => r.Id.Length > 0).ToList();
        return Result<IReadOnlyList<ExternalReview>>.Success(list);
    }

    public static int Stars(string? rating) => rating switch
    {
        "ONE" => 1, "TWO" => 2, "THREE" => 3, "FOUR" => 4, "FIVE" => 5, _ => 0,
    };

    private static DateTimeOffset? Date(JsonNode? node) =>
        node?.GetValue<string>() is { } s && DateTimeOffset.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var d) ? d : null;

    public async Task<Result> ReplyAsync(ChannelCredentials c, string reviewId, string reply, CancellationToken ct = default)
    {
        var sent = await SendAsync(c, HttpMethod.Put, $"{O.BusinessUrl}/{c.AccountId}/reviews/{Uri.EscapeDataString(reviewId)}/reply",
            new JsonObject { ["comment"] = reply.Trim() }, ct);
        return sent.IsSuccess ? Result.Success() : Result.Failure(sent.Error!);
    }

    public async Task<Result<LocationMetrics>> GetLocationAsync(ChannelCredentials c, DateOnly from, DateOnly to, CancellationToken ct = default)
    {
        var location = c.AccountId[(c.AccountId.IndexOf("locations/", StringComparison.Ordinal))..];
        string[] metrics =
        [
            "BUSINESS_IMPRESSIONS_DESKTOP_MAPS", "BUSINESS_IMPRESSIONS_DESKTOP_SEARCH", "BUSINESS_IMPRESSIONS_MOBILE_MAPS",
            "BUSINESS_IMPRESSIONS_MOBILE_SEARCH", "CALL_CLICKS", "BUSINESS_DIRECTION_REQUESTS", "WEBSITE_CLICKS",
        ];
        var query = string.Join("&", metrics.Select(m => "dailyMetrics=" + m)) +
            $"&dailyRange.start_date.year={from.Year}&dailyRange.start_date.month={from.Month}&dailyRange.start_date.day={from.Day}" +
            $"&dailyRange.end_date.year={to.Year}&dailyRange.end_date.month={to.Month}&dailyRange.end_date.day={to.Day}";
        var got = await GetAsync(c, $"{O.PerformanceUrl}/{location}:fetchMultiDailyMetricsTimeSeries?{query}", ct);
        return got.IsFailure ? Result<LocationMetrics>.Failure(got.Error!) : Result<LocationMetrics>.Success(Metrics(got.Value!));
    }

    /// <summary>Additionne les séries quotidiennes de la réponse de Google (vues = recherche + Maps, ordinateur + mobile).</summary>
    public static LocationMetrics Metrics(JsonNode json)
    {
        var totals = new Dictionary<string, int>();
        foreach (var group in json["multiDailyMetricTimeSeries"]?.AsArray() ?? [])
        foreach (var series in group?["dailyMetricTimeSeries"]?.AsArray() ?? [])
        {
            var metric = series?["dailyMetric"]?.GetValue<string>() ?? "";
            var sum = (series?["timeSeries"]?["datedValues"]?.AsArray() ?? [])
                .Sum(v => int.TryParse(v?["value"]?.ToString(), out var n) ? n : 0);
            totals[metric] = totals.GetValueOrDefault(metric) + sum;
        }
        return new LocationMetrics(
            totals.Where(t => t.Key.StartsWith("BUSINESS_IMPRESSIONS", StringComparison.Ordinal)).Sum(t => t.Value),
            totals.GetValueOrDefault("CALL_CLICKS"),
            totals.GetValueOrDefault("BUSINESS_DIRECTION_REQUESTS"),
            totals.GetValueOrDefault("WEBSITE_CLICKS"));
    }

    // ----- appels -----

    private async Task<Result<string>> AccessTokenAsync(ChannelCredentials c, CancellationToken ct)
    {
        if (AccessTokens.TryGetValue(c.AccessToken, out var cached) && cached.Expires > DateTimeOffset.UtcNow) return Result<string>.Success(cached.Token);
        using var response = await http.PostAsync(O.TokenUrl, new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["client_id"] = O.ClientId ?? "", ["client_secret"] = O.ClientSecret ?? "",
            ["refresh_token"] = c.AccessToken, ["grant_type"] = "refresh_token",
        }), ct);
        var json = JsonNode.Parse(await response.Content.ReadAsStringAsync(ct));
        var token = json?["access_token"]?.GetValue<string>();
        if (!response.IsSuccessStatusCode || token is null)
            return Result<string>.Failure("Le jeton Google n'est plus valide : reconnectez Google Business dans Canaux (" + (json?["error"]?.GetValue<string>() ?? $"réponse {(int)response.StatusCode}") + ").");
        AccessTokens[c.AccessToken] = (token, DateTimeOffset.UtcNow.AddSeconds((json?["expires_in"]?.GetValue<int>() ?? 3600) - 120));
        return Result<string>.Success(token);
    }

    private Task<Result<JsonNode>> GetAsync(ChannelCredentials c, string url, CancellationToken ct) => SendAsync(c, HttpMethod.Get, url, null, ct);

    private async Task<Result<JsonNode>> SendAsync(ChannelCredentials c, HttpMethod method, string url, JsonNode? body, CancellationToken ct)
    {
        var token = await AccessTokenAsync(c, ct);
        if (token.IsFailure) return Result<JsonNode>.Failure(token.Error!);
        using var request = new HttpRequestMessage(method, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.Value);
        if (body is not null) request.Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json");
        using var response = await http.SendAsync(request, ct);
        var text = await response.Content.ReadAsStringAsync(ct);
        JsonNode? json = null;
        try { json = string.IsNullOrWhiteSpace(text) ? new JsonObject() : JsonNode.Parse(text); } catch (JsonException) { }
        if (response.IsSuccessStatusCode) return Result<JsonNode>.Success(json ?? new JsonObject());
        logger.LogWarning("Google Business {Method} {Url} : {Status} {Body}", method, url.Split('?')[0], (int)response.StatusCode, text.Length > 500 ? text[..500] : text);
        return Result<JsonNode>.Failure(Error(json, (int)response.StatusCode));
    }

    /// <summary>Message d'erreur lisible (les droits et la connexion sont signalés comme tels : une relance n'y changerait rien).</summary>
    public static string Error(JsonNode? json, int status)
    {
        var message = json?["error"]?["message"]?.GetValue<string>() ?? $"réponse {status}";
        return status switch
        {
            401 => "Google : connexion expirée, reconnectez Google Business (permission).",
            403 => $"Google : permission refusée ({message}).",
            429 => "Google : trop de demandes, nouvel essai plus tard.",
            _ => $"Google : {message} (code {status}).",
        };
    }
}
