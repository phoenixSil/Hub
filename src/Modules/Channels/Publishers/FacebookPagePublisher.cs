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
    : ISocialPublisher
{
    public ChannelType Channel => ChannelType.Facebook;

    private string Url(string path) => $"{options.Value.GraphBaseUrl.TrimEnd('/')}/{options.Value.GraphVersion}/{path}";

    public async Task<Result<string>> TestAsync(ChannelCredentials c, CancellationToken ct = default)
    {
        var url = Url($"{Uri.EscapeDataString(c.AccountId)}?fields=name&access_token={Uri.EscapeDataString(c.AccessToken)}");
        using var response = await http.GetAsync(url, ct);
        var json = JsonNode.Parse(await response.Content.ReadAsStringAsync(ct));
        if (!response.IsSuccessStatusCode)
            return Result<string>.Failure(ErrorOf(json) ?? $"Facebook a répondu {(int)response.StatusCode}.");
        return Result<string>.Success(json?["name"]?.GetValue<string>() ?? c.AccountId);
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
internal sealed class SimulatedPublisher(ChannelType channel, ILogger logger) : ISocialPublisher
{
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
