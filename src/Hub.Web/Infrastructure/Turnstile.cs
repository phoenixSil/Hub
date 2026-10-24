using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;

namespace Hub.Web.Infrastructure;

public sealed class TurnstileOptions
{
    public const string Section = "Turnstile";
    public string? SiteKey { get; set; }
    public string? SecretKey { get; set; }
    public bool Enabled => !string.IsNullOrWhiteSpace(SiteKey) && !string.IsNullOrWhiteSpace(SecretKey);
}

/// <summary>Anti-pourriel invisible Cloudflare Turnstile. Désactivé tant que les clés sont vides.</summary>
public sealed class TurnstileVerifier(HttpClient http, IOptions<TurnstileOptions> options, ILogger<TurnstileVerifier> logger)
{
    public bool Enabled => options.Value.Enabled;
    public string? SiteKey => options.Value.SiteKey;

    public async Task<bool> VerifyAsync(HttpContext ctx)
    {
        if (!Enabled) return true;

        var token = ctx.Request.HasFormContentType ? (await ctx.Request.ReadFormAsync(ctx.RequestAborted))["cf-turnstile-response"].ToString() : null;
        if (string.IsNullOrWhiteSpace(token)) return false;

        try
        {
            using var content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["secret"] = options.Value.SecretKey!,
                ["response"] = token,
                ["remoteip"] = ctx.Connection.RemoteIpAddress?.ToString() ?? string.Empty,
            });
            using var response = await http.PostAsync("https://challenges.cloudflare.com/turnstile/v0/siteverify", content, ctx.RequestAborted);
            var result = await response.Content.ReadFromJsonAsync<SiteVerify>(ctx.RequestAborted);
            return result?.Success == true;
        }
        catch (Exception ex)
        {
            // Turnstile indisponible : on laisse passer plutôt que de perdre un client.
            logger.LogWarning(ex, "Vérification Turnstile impossible.");
            return true;
        }
    }

    private sealed class SiteVerify
    {
        [JsonPropertyName("success")]
        public bool Success { get; set; }
    }
}
