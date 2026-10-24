using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Hub.Modules.Analytics.Application;
using Hub.Modules.Analytics.Domain;
using Hub.SharedKernel.Tenancy;

namespace Hub.Web.Infrastructure;

/// <summary>
/// Provenance de la visite et consentement.
/// - hub_src : cookie de session sans identifiant (source, support, campagne, page d'arrivée).
/// - hub_consent : choix du visiteur (« yes » / « no »).
/// - hub_vid : identifiant aléatoire de visite, posé seulement après consentement (Loi 25).
/// </summary>
public sealed record VisitSource(string? Source, string? Medium, string? Campaign, string? Referrer, string? LandingPage);

public static class VisitCookies
{
    public const string Source = "hub_src";
    public const string Consent = "hub_consent";
    public const string VisitorId = "hub_vid";

    public static bool HasConsent(HttpContext ctx) => ctx.Request.Cookies[Consent] == "yes";

    public static string? VisitorIdOf(HttpContext ctx) => HasConsent(ctx) ? ctx.Request.Cookies[VisitorId] : null;

    public static VisitSource? ReadSource(HttpContext ctx)
    {
        var raw = ctx.Request.Cookies[Source];
        if (string.IsNullOrEmpty(raw)) return null;
        try
        {
            return JsonSerializer.Deserialize<VisitSource>(Encoding.UTF8.GetString(Convert.FromBase64String(raw)));
        }
        catch
        {
            return null;
        }
    }

    public static string? IpHash(HttpContext ctx)
    {
        var ip = ctx.Connection.RemoteIpAddress?.ToString();
        if (string.IsNullOrEmpty(ip)) return null;
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes("hub:" + ip + ":" + DateTime.UtcNow.ToString("yyyyMMdd")));
        return Convert.ToHexString(hash)[..32];
    }

    public static string DeviceOf(HttpContext ctx)
    {
        var ua = ctx.Request.Headers.UserAgent.ToString();
        return ua.Contains("Mobi", StringComparison.OrdinalIgnoreCase) ? "mobile"
            : ua.Contains("Tablet", StringComparison.OrdinalIgnoreCase) || ua.Contains("iPad", StringComparison.OrdinalIgnoreCase) ? "tablet"
            : "desktop";
    }

    public static bool IsBot(HttpContext ctx)
    {
        var ua = ctx.Request.Headers.UserAgent.ToString();
        if (string.IsNullOrWhiteSpace(ua)) return true;
        foreach (var marker in new[] { "bot", "crawl", "spider", "slurp", "facebookexternalhit", "preview", "monitor", "headless" })
        {
            if (ua.Contains(marker, StringComparison.OrdinalIgnoreCase)) return true;
        }
        return false;
    }
}

/// <summary>
/// Retient la provenance à l'arrivée, pose l'identifiant de visite si le visiteur a
/// consenti, et compte les pages vues du site public (hors robots et back-office).
/// </summary>
public sealed class VisitTrackingMiddleware(RequestDelegate next)
{
    private static readonly string[] Excluded = ["/admin", "/compte", "/_blazor", "/_framework", "/api", "/css", "/js", "/images", "/uploads", "/favicon", "/sitemap.xml", "/robots.txt", "/_content"];

    public async Task InvokeAsync(HttpContext ctx, IAnalyticsRecorder recorder, ITenantContext tenant)
    {
        var path = ctx.Request.Path.Value ?? "/";
        var isPage = HttpMethods.IsGet(ctx.Request.Method)
                     && !Excluded.Any(p => path.StartsWith(p, StringComparison.OrdinalIgnoreCase))
                     && !Path.HasExtension(path);

        if (!isPage)
        {
            await next(ctx);
            return;
        }

        var source = VisitCookies.ReadSource(ctx);
        var incoming = SourceFromRequest(ctx);
        if (incoming is not null && (source is null || incoming.Source is not null))
        {
            source = incoming;
            var json = JsonSerializer.Serialize(source);
            ctx.Response.Cookies.Append(VisitCookies.Source, Convert.ToBase64String(Encoding.UTF8.GetBytes(json)),
                new CookieOptions { HttpOnly = true, Secure = ctx.Request.IsHttps, SameSite = SameSiteMode.Lax, IsEssential = true });
        }

        string? visitorId = null;
        if (VisitCookies.HasConsent(ctx))
        {
            visitorId = ctx.Request.Cookies[VisitCookies.VisitorId];
            if (string.IsNullOrEmpty(visitorId))
            {
                visitorId = Convert.ToHexString(RandomNumberGenerator.GetBytes(12)).ToLowerInvariant();
                ctx.Response.Cookies.Append(VisitCookies.VisitorId, visitorId,
                    new CookieOptions { HttpOnly = true, Secure = ctx.Request.IsHttps, SameSite = SameSiteMode.Lax, MaxAge = TimeSpan.FromDays(180) });
            }
        }

        await next(ctx);

        var isHtml = ctx.Response.ContentType?.StartsWith("text/html", StringComparison.OrdinalIgnoreCase) == true;
        if (ctx.Response.StatusCode == 200 && isHtml && !VisitCookies.IsBot(ctx) && tenant.IsResolved)
        {
            recorder.Record(new AnalyticsEvent
            {
                TenantId = tenant.TenantId,
                OccurredAt = DateTimeOffset.UtcNow,
                Type = AnalyticsEventType.PageView,
                Path = path.Length > 300 ? path[..300] : path,
                Source = source?.Source,
                Medium = source?.Medium,
                Campaign = source?.Campaign,
                ReferrerHost = HostOf(source?.Referrer),
                Device = VisitCookies.DeviceOf(ctx),
                VisitorId = visitorId,
            });
        }
    }

    /// <summary>Paramètres UTM, sinon site référent externe. Null pour une navigation interne.</summary>
    private static VisitSource? SourceFromRequest(HttpContext ctx)
    {
        var q = ctx.Request.Query;
        var landing = ctx.Request.Path.Value;
        if (q.ContainsKey("utm_source"))
            return new VisitSource(q["utm_source"].ToString(), q["utm_medium"].ToString(), q["utm_campaign"].ToString(),
                ctx.Request.Headers.Referer.ToString(), landing);
        if (q.ContainsKey("gclid"))
            return new VisitSource("google", "cpc", null, ctx.Request.Headers.Referer.ToString(), landing);
        if (q.ContainsKey("fbclid"))
            return new VisitSource("facebook", "social", null, ctx.Request.Headers.Referer.ToString(), landing);

        var referer = ctx.Request.Headers.Referer.ToString();
        var refHost = HostOf(referer);
        if (refHost is null || string.Equals(refHost, ctx.Request.Host.Host, StringComparison.OrdinalIgnoreCase))
            return null;

        var medium = refHost.Contains("google") || refHost.Contains("bing") || refHost.Contains("duckduckgo") || refHost.Contains("yahoo")
            ? "organic"
            : refHost.Contains("facebook") || refHost.Contains("instagram") || refHost.Contains("tiktok") || refHost.Contains("youtube") || refHost.Contains("t.co")
                ? "social"
                : "referral";
        var name = refHost.StartsWith("www.") ? refHost[4..] : refHost;
        return new VisitSource(name.Split('.').FirstOrDefault() ?? name, medium, null, referer, landing);
    }

    private static string? HostOf(string? url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri) ? uri.Host.ToLowerInvariant() : null;
}

public static class TrackingEndpoints
{
    private sealed record Beacon(string? Type, string? Path, string? Detail);

    /// <summary>POST /api/t : clics sur téléphone, courriel, WhatsApp, début de formulaire.</summary>
    public static void MapTracking(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/t", async (HttpContext ctx, IAnalyticsRecorder recorder, ITenantContext tenant) =>
        {
            if (VisitCookies.IsBot(ctx) || !tenant.IsResolved) return Results.NoContent();

            Beacon? beacon;
            try
            {
                beacon = await JsonSerializer.DeserializeAsync<Beacon>(ctx.Request.Body,
                    new JsonSerializerOptions(JsonSerializerDefaults.Web), ctx.RequestAborted);
            }
            catch (JsonException)
            {
                return Results.BadRequest();
            }

            var type = beacon?.Type switch
            {
                "phone" => AnalyticsEventType.PhoneClick,
                "email" => AnalyticsEventType.EmailClick,
                "whatsapp" => AnalyticsEventType.WhatsAppClick,
                "form" => AnalyticsEventType.FormStarted,
                _ => (AnalyticsEventType?)null,
            };
            if (type is null) return Results.BadRequest();

            var source = VisitCookies.ReadSource(ctx);
            var path = beacon!.Path ?? "/";
            recorder.Record(new AnalyticsEvent
            {
                TenantId = tenant.TenantId,
                OccurredAt = DateTimeOffset.UtcNow,
                Type = type.Value,
                Path = path.Length > 300 ? path[..300] : path,
                Source = source?.Source,
                Medium = source?.Medium,
                Campaign = source?.Campaign,
                Device = VisitCookies.DeviceOf(ctx),
                VisitorId = VisitCookies.VisitorIdOf(ctx),
                Detail = beacon.Detail is { Length: > 200 } d ? d[..200] : beacon.Detail,
            });
            return Results.NoContent();
        }).DisableAntiforgery();
    }
}
