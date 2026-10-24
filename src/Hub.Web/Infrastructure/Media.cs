using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Hub.SharedKernel.Tenancy;
using Hub.SharedKernel.Text;
using SkiaSharp;

namespace Hub.Web.Infrastructure;

public sealed class MediaOptions
{
    public const string Section = "Media";

    /// <summary>Vide = fichiers dans wwwroot/uploads (développement).</summary>
    public string? BlobConnectionString { get; set; }
    public string Container { get; set; } = "media";

    /// <summary>Adresse publique du conteneur (ou d'un CDN devant lui).</summary>
    public string? PublicBaseUrl { get; set; }
    public int MaxWidth { get; set; } = 1600;
    public int Quality { get; set; } = 80;
}

public interface IMediaStorage
{
    /// <summary>Convertit l'image en WebP, la redimensionne et renvoie son adresse publique.</summary>
    Task<string> SaveImageAsync(Stream input, string fileName, CancellationToken ct = default);
}

internal static class ImageProcessor
{
    public static byte[] ToWebp(Stream input, int maxWidth, int quality)
    {
        using var source = SKBitmap.Decode(input)
            ?? throw new InvalidOperationException("Ce fichier n'est pas une image lisible (JPG, PNG ou WebP).");

        var bitmap = source;
        SKBitmap? resized = null;
        if (source.Width > maxWidth)
        {
            var height = (int)Math.Round(source.Height * (maxWidth / (double)source.Width));
            resized = source.Resize(new SKImageInfo(maxWidth, height), new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear));
            bitmap = resized ?? source;
        }

        try
        {
            using var image = SKImage.FromBitmap(bitmap);
            using var data = image.Encode(SKEncodedImageFormat.Webp, quality);
            return data.ToArray();
        }
        finally
        {
            resized?.Dispose();
        }
    }

    public static string FileNameFor(string original)
    {
        var slug = Slug.From(Path.GetFileNameWithoutExtension(original));
        if (slug.Length > 60) slug = slug[..60].Trim('-');
        var id = Guid.NewGuid().ToString("N")[..8];
        return string.IsNullOrEmpty(slug) ? $"{id}.webp" : $"{slug}-{id}.webp";
    }
}

internal sealed class LocalMediaStorage(IWebHostEnvironment env, ITenantContext tenant, Microsoft.Extensions.Options.IOptions<MediaOptions> options) : IMediaStorage
{
    public async Task<string> SaveImageAsync(Stream input, string fileName, CancellationToken ct = default)
    {
        var bytes = ImageProcessor.ToWebp(input, options.Value.MaxWidth, options.Value.Quality);
        var folder = Path.Combine(env.WebRootPath, "uploads", tenant.TenantId.ToString("N"));
        Directory.CreateDirectory(folder);
        var name = ImageProcessor.FileNameFor(fileName);
        await File.WriteAllBytesAsync(Path.Combine(folder, name), bytes, ct);
        return $"/uploads/{tenant.TenantId:N}/{name}";
    }
}

internal sealed class BlobMediaStorage(ITenantContext tenant, Microsoft.Extensions.Options.IOptions<MediaOptions> options) : IMediaStorage
{
    public async Task<string> SaveImageAsync(Stream input, string fileName, CancellationToken ct = default)
    {
        var opts = options.Value;
        var bytes = ImageProcessor.ToWebp(input, opts.MaxWidth, opts.Quality);
        var container = new BlobContainerClient(opts.BlobConnectionString, opts.Container);
        await container.CreateIfNotExistsAsync(PublicAccessType.Blob, cancellationToken: ct);

        var blobName = $"{tenant.TenantId:N}/{ImageProcessor.FileNameFor(fileName)}";
        var blob = container.GetBlobClient(blobName);
        await blob.UploadAsync(new BinaryData(bytes), new BlobUploadOptions
        {
            HttpHeaders = new BlobHttpHeaders { ContentType = "image/webp", CacheControl = "public, max-age=31536000, immutable" },
        }, ct);

        return string.IsNullOrWhiteSpace(opts.PublicBaseUrl)
            ? blob.Uri.ToString()
            : $"{opts.PublicBaseUrl.TrimEnd('/')}/{blobName}";
    }
}
