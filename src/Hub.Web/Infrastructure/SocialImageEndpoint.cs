using Microsoft.Extensions.Options;

namespace Hub.Web.Infrastructure;

/// <summary>
/// « /media/social.jpg?src=… » : une image du site (WebP) convertie en JPEG pour les réseaux sociaux.
/// N'accepte que les images du site lui-même (dossier wwwroot) ou du stockage d'images configuré :
/// impossible de s'en servir pour aller chercher une adresse quelconque.
/// </summary>
public static class SocialImageEndpoint
{
    public const string Path = "/media/social.jpg";

    public static IEndpointRouteBuilder MapSocialImages(this IEndpointRouteBuilder app)
    {
        app.MapGet(Path, async (string src, IWebHostEnvironment env, IOptions<MediaOptions> media, IHttpClientFactory http, CancellationToken ct) =>
        {
            await using var input = await OpenAsync(src, env, media.Value, http, ct);
            if (input is null) return Results.NotFound();
            try
            {
                var jpeg = ImageProcessor.ToJpeg(input, 1600, 85);
                return Results.File(jpeg, "image/jpeg", lastModified: null, entityTag: null);
            }
            catch (InvalidOperationException)
            {
                return Results.NotFound();
            }
        });
        return app;
    }

    private static async Task<Stream?> OpenAsync(string src, IWebHostEnvironment env, MediaOptions media, IHttpClientFactory http, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(src) || src.Length > 500) return null;

        // 1) Fichier du site : /images/…, /uploads/…
        if (src.StartsWith('/') && !src.Contains("..") && !src.Contains('\\'))
        {
            var root = System.IO.Path.GetFullPath(env.WebRootPath);
            var full = System.IO.Path.GetFullPath(System.IO.Path.Combine(root, src.TrimStart('/').Split('?')[0]));
            return full.StartsWith(root, StringComparison.Ordinal) && File.Exists(full) ? File.OpenRead(full) : null;
        }

        // 2) Stockage d'images Azure configuré (et seulement lui)
        if (Uri.TryCreate(src, UriKind.Absolute, out var uri) && uri.Scheme == "https" && IsMediaHost(uri, media))
        {
            using var response = await http.CreateClient().GetAsync(uri, ct);
            if (!response.IsSuccessStatusCode) return null;
            var buffer = new MemoryStream();
            await response.Content.CopyToAsync(buffer, ct);
            buffer.Position = 0;
            return buffer;
        }
        return null;
    }

    private static bool IsMediaHost(Uri uri, MediaOptions media)
    {
        if (!string.IsNullOrWhiteSpace(media.PublicBaseUrl) && Uri.TryCreate(media.PublicBaseUrl, UriKind.Absolute, out var pub)
            && string.Equals(pub.Host, uri.Host, StringComparison.OrdinalIgnoreCase))
            return true;

        var account = media.BlobConnectionString?.Split(';')
            .Select(p => p.Split('=', 2))
            .FirstOrDefault(kv => kv.Length == 2 && kv[0].Trim().Equals("AccountName", StringComparison.OrdinalIgnoreCase))?[1].Trim();
        return account is not null && string.Equals(uri.Host, $"{account}.blob.core.windows.net", StringComparison.OrdinalIgnoreCase);
    }

}
