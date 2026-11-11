using System.Text.RegularExpressions;
using Hub.Modules.Catalog.Application;
using Hub.Modules.Content.Application;

namespace Hub.Web.Infrastructure;

/// <summary>
/// Anciennes adresses (ancien site, liens externes, pages signalées « introuvables » par Google)
/// renvoyées vers la bonne page par une redirection permanente (301), pour ne pas perdre le référencement.
/// </summary>
public static class LegacyRedirectResolver
{
    /// <summary>Alias connus d'anciennes adresses → page actuelle. Complétés par Hub:Redirects dans la configuration.</summary>
    public static readonly IReadOnlyDictionary<string, string> DefaultAliases = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["accueil"] = "/", ["home"] = "/", ["index"] = "/", ["blog"] = "/",
        ["services"] = "/", ["aines"] = "/", ["service"] = "/", ["nos-services"] = "/", ["nos-services-2"] = "/",
        ["contact-us"] = "/contact", ["contactez-nous"] = "/contact", ["nous-joindre"] = "/contact", ["joindre"] = "/contact",
        ["about"] = "/a-propos", ["about-us"] = "/a-propos", ["qui-sommes-nous"] = "/a-propos", ["a-propos-de-nous"] = "/a-propos", ["notre-equipe"] = "/a-propos",
        ["soumission"] = "/devis", ["soumission-gratuite"] = "/devis", ["estimation"] = "/devis", ["quote"] = "/devis", ["demande-de-soumission"] = "/devis", ["tarifs"] = "/devis", ["prix"] = "/devis",
        ["reservation"] = "/rendez-vous", ["reserver"] = "/rendez-vous", ["prendre-rendez-vous"] = "/rendez-vous",
        ["questions-frequentes"] = "/faq", ["foire-aux-questions"] = "/faq",
        ["politique-de-confidentialite"] = "/confidentialite", ["privacy-policy"] = "/confidentialite", ["confidentialité"] = "/confidentialite",
        ["cartes-cadeaux"] = "/carte-cadeau", ["carte-cadeaux"] = "/carte-cadeau", ["gift-card"] = "/carte-cadeau", ["cheque-cadeau"] = "/carte-cadeau",
        ["zones-desservies"] = "/secteurs", ["territoire"] = "/secteurs", ["regions"] = "/secteurs",
    };

    private static readonly string[] SkippedPrefixes =
        ["/admin", "/compte", "/_blazor", "/_framework", "/_content", "/api", "/css", "/js", "/images", "/uploads", "/introuvable", "/erreur", "/media"];

    private static readonly Regex LegacyExtension = new(@"\.(html?|php|aspx?)$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>
    /// Renvoie la page vers laquelle rediriger, ou null quand l'adresse est déjà bonne ou inconnue.
    /// </summary>
    /// <param name="path">Chemin demandé (sans la chaîne de requête).</param>
    /// <param name="livePaths">Toutes les pages publiques actuelles ("/", "/devis", "/services/menage", …).</param>
    /// <param name="aliases">Anciennes adresses connues (dernier segment ou chemin complet) → page actuelle.</param>
    public static string? Resolve(string path, IReadOnlyCollection<string> livePaths, IReadOnlyDictionary<string, string> aliases)
    {
        if (string.IsNullOrEmpty(path) || path == "/") return null;
        if (SkippedPrefixes.Any(p => path.StartsWith(p, StringComparison.OrdinalIgnoreCase))) return null;
        if (Path.HasExtension(path) && !LegacyExtension.IsMatch(path)) return null; // fichiers : pas nos affaires

        var live = new HashSet<string>(livePaths, StringComparer.Ordinal);
        if (live.Contains(path)) return null; // déjà la bonne adresse

        var clean = Normalize(path);
        if (clean != path && live.Contains(clean)) return clean;

        if (aliases.TryGetValue(clean.TrimStart('/'), out var full)) return full;
        var segments = clean.Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length == 0) return "/";
        var last = segments[^1];
        if (aliases.TryGetValue(last, out var target)) return target;

        // Même nom de page sous un autre dossier (ex. /service/menage-residentiel → /services/menage-residentiel).
        var bySlug = live.Where(p => p.Length > 1 && p.EndsWith("/" + last, StringComparison.Ordinal)).ToList();
        if (bySlug.Count == 1) return bySlug[0];

        // Ancien nom un peu différent (ex. /menage-residentiel-montreal → /services/menage-residentiel).
        var close = live
            .Where(p => p.Length > 1 && p.Count(c => c == '/') >= 2)
            .Select(p => (Path: p, Slug: p[(p.LastIndexOf('/') + 1)..]))
            .Where(x => x.Slug.Length >= 6 && (last.StartsWith(x.Slug + "-", StringComparison.Ordinal) || x.Slug.StartsWith(last + "-", StringComparison.Ordinal)))
            .Select(x => x.Path)
            .Distinct()
            .ToList();
        return close.Count == 1 ? close[0] : null;
    }

    /// <summary>Minuscules, sans /fr, sans index.php ni .html, sans barre finale.</summary>
    public static string Normalize(string path)
    {
        var p = Uri.UnescapeDataString(path).Trim().ToLowerInvariant();
        p = Regex.Replace(p, "/{2,}", "/");
        p = Regex.Replace(p, @"^/(fr|fr-ca|en|en-ca)(?=/|$)", "");
        p = Regex.Replace(p, @"/index\.(php|html?|aspx?)$", "/");
        p = LegacyExtension.Replace(p, "");
        p = p.TrimEnd('/');
        return p.Length == 0 ? "/" : p;
    }
}

/// <summary>Applique <see cref="LegacyRedirectResolver"/> aux requêtes GET du site public.</summary>
public sealed class LegacyRedirectMiddleware(RequestDelegate next, IConfiguration configuration, ILogger<LegacyRedirectMiddleware> logger)
{
    private readonly IReadOnlyDictionary<string, string> aliases = BuildAliases(configuration);

    public async Task InvokeAsync(HttpContext ctx, ICatalogQueries catalog, IContentQueries content)
    {
        var path = ctx.Request.Path.Value ?? "/";
        if (!(HttpMethods.IsGet(ctx.Request.Method) || HttpMethods.IsHead(ctx.Request.Method)) || path == "/"
            || ctx.Request.Path.StartsWithSegments("/admin") || ctx.Request.Path.StartsWithSegments("/_blazor"))
        {
            await next(ctx);
            return;
        }

        var live = await LivePathsAsync(catalog, content, ctx.RequestAborted);
        var target = LegacyRedirectResolver.Resolve(path, live, aliases);
        if (target is not null && !string.Equals(target, path, StringComparison.Ordinal))
        {
            logger.LogInformation("Ancienne adresse {Path} redirigée vers {Target}", path, target);
            ctx.Response.Redirect(target + ctx.Request.QueryString, permanent: true);
            return;
        }

        await next(ctx);

        if (ctx.Response.StatusCode == StatusCodes.Status404NotFound)
            logger.LogWarning("Page introuvable : {Path} (référent : {Referer})", path, ctx.Request.Headers.Referer.ToString());
    }

    /// <summary>Les pages publiques actuelles, mêmes sources que le plan du site.</summary>
    public static async Task<IReadOnlyCollection<string>> LivePathsAsync(ICatalogQueries catalog, IContentQueries content, CancellationToken ct)
    {
        var paths = new List<string> { "/", "/devis", "/rendez-vous", "/contact", "/faq", "/secteurs", "/merci", "/carte-cadeau" };
        foreach (var category in await catalog.GetCatalogAsync(ct))
            paths.AddRange(category.Services.Select(s => s.Url));
        var site = await content.GetAsync(ct);
        paths.AddRange(site.Areas.Select(a => $"/secteurs/{a.Slug}"));
        paths.AddRange(site.Pages.Select(p => $"/{p.Slug}"));
        return paths;
    }

    private static IReadOnlyDictionary<string, string> BuildAliases(IConfiguration configuration)
    {
        var map = new Dictionary<string, string>(LegacyRedirectResolver.DefaultAliases, StringComparer.OrdinalIgnoreCase);
        // Hub:Redirects:{ancienne-adresse} = /nouvelle-adresse (réglage Azure : Hub__Redirects__ancienne-adresse)
        foreach (var entry in configuration.GetSection("Hub:Redirects").GetChildren())
            if (!string.IsNullOrWhiteSpace(entry.Value) && entry.Value.StartsWith('/'))
                map[LegacyRedirectResolver.Normalize("/" + entry.Key.Trim('/')).TrimStart('/')] = entry.Value;
        return map;
    }
}
