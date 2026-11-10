using Hub.Modules.Channels.Application;
using Hub.Modules.Channels.Application.Autopilot;
using Hub.SharedKernel;
using SkiaSharp;
using Svg.Skia;
using Svg.Skia.TypefaceProviders;
using Shim = ShimSkiaSharp;

namespace Hub.Web.Infrastructure;

/// <summary>
/// Dessine un visuel SVG en PNG côté serveur (SkiaSharp + Svg.Skia). Tout le texte utilise la police
/// de la marque, embarquée dans l'application : le rendu est identique en local, en test et sur Azure.
/// </summary>
public sealed class SkiaVisualRenderer(ILogger<SkiaVisualRenderer> logger) : IVisualRenderer
{
    public byte[]? RenderPng(string svg, int size = 1080)
    {
        var clean = SvgSanitizer.Clean(svg);
        if (clean is null) return null;
        try
        {
            // Svg.Skia cherche les polices du système, absentes sur un serveur Linux minimal :
            // le chargeur ci-dessous impose la police de la marque, pour la mesure comme pour le dessin.
            var settings = new SKSvgSettings { TypefaceProviders = [BrandFont.Instance] };
            var model = new SkiaModel(settings);
            var document = Svg.Model.Services.SvgService.FromSvg(clean);
            if (document is null) return null;
            using var picture = SKSvg.ToPicture(document, model, new BrandAssetLoader(model));
            if (picture is null) return null;

            using var surface = SKSurface.Create(new SKImageInfo(size, size, SKColorType.Rgba8888, SKAlphaType.Premul));
            var canvas = surface.Canvas;
            canvas.Clear(SKColors.White);
            var bounds = picture.CullRect;
            if (bounds.Width > 0 && bounds.Height > 0) canvas.Scale(size / bounds.Width, size / bounds.Height);
            canvas.DrawPicture(picture);
            canvas.Flush();

            using var image = surface.Snapshot();
            using var data = image.Encode(SKEncodedImageFormat.Png, 100);
            return data.ToArray();
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Visuel SVG impossible à dessiner");
            return null;
        }
    }
}

/// <summary>Toute famille demandée (Arial, Helvetica…) est servie par la police de la marque, normale ou grasse.</summary>
public sealed class BrandFont : ITypefaceProvider
{
    public static readonly BrandFont Instance = new();

    private readonly SKTypeface regular = Load("Fonts.Poppins-Regular.ttf");
    private readonly SKTypeface bold = Load("Fonts.Poppins-Bold.ttf");

    public SKTypeface? FromFamilyName(string fontFamily, SKFontStyleWeight fontWeight, SKFontStyleWidth fontWidth, SKFontStyleSlant fontStyle) =>
        (int)fontWeight >= (int)SKFontStyleWeight.SemiBold ? bold : regular;

    public SKTypeface Regular => regular;
    public SKTypeface Bold => bold;

    // Les octets sont copiés dans un SKData gardé en vie : Skia lit les glyphes à la demande,
    // un flux .NET refermé donnerait une police vide (et le texte retomberait sur une police par défaut).
    private static SKTypeface Load(string name)
    {
        using var stream = typeof(BrandFont).Assembly.GetManifestResourceStream(name)
            ?? throw new InvalidOperationException($"Police intégrée introuvable : {name}");
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        var data = SKData.CreateCopy(buffer.ToArray());
        return SKTypeface.FromData(data) ?? throw new InvalidOperationException($"Police illisible : {name}");
    }
}

/// <summary>
/// Chargeur Svg.Skia qui garde, pour chaque texte, la police demandée par le SVG (servie ensuite par <see cref="BrandFont"/>)
/// au lieu de chercher une police système caractère par caractère.
/// </summary>
internal sealed class BrandAssetLoader(SkiaModel model) : Svg.Model.ISvgAssetLoader
{
    private readonly SkiaSvgAssetLoader inner = new(model);

    public Shim.SKImage LoadImage(Stream stream) => inner.LoadImage(stream);

    public List<Svg.Model.TypefaceSpan> FindTypefaces(string? text, Shim.SKPaint paint)
    {
        if (string.IsNullOrEmpty(text)) return [];
        var typeface = paint.Typeface ?? Shim.SKTypeface.FromFamilyName("Poppins",
            Shim.SKFontStyleWeight.Normal, Shim.SKFontStyleWidth.Normal, Shim.SKFontStyleSlant.Upright);
        var bounds = new Shim.SKRect();
        return [new Svg.Model.TypefaceSpan(text, inner.MeasureText(text, paint, ref bounds), typeface)];
    }

    public Shim.SKFontMetrics GetFontMetrics(Shim.SKPaint paint) => inner.GetFontMetrics(paint);
    public float MeasureText(string? text, Shim.SKPaint paint, ref Shim.SKRect bounds) => inner.MeasureText(text, paint, ref bounds);
    public Shim.SKPath? GetTextPath(string? text, Shim.SKPaint paint, float x, float y) => inner.GetTextPath(text, paint, x, y);
}

/// <summary>Enregistre le PNG d'un visuel dans le stockage des médias (local ou Azure Blob).</summary>
internal sealed class MediaVisualStore(IMediaStorage media, IWebHostEnvironment env, IHttpClientFactory http,
    Microsoft.Extensions.Options.IOptions<MediaOptions> options, ILogger<MediaVisualStore> logger) : IVisualStore
{
    private const int MaxBytes = 12 * 1024 * 1024;

    /// <summary>
    /// Relit une image : fichier du site (« /images/… », « /uploads/… ») ou image du stockage Azure.
    /// Aucune autre adresse n'est lue (le serveur ne va pas chercher n'importe quoi sur Internet).
    /// </summary>
    public async Task<byte[]?> LoadAsync(string url, CancellationToken ct = default)
    {
        try
        {
            if (url.StartsWith('/') && !url.StartsWith("//"))
            {
                var root = Path.GetFullPath(env.WebRootPath);
                var path = Path.GetFullPath(Path.Combine(root, Uri.UnescapeDataString(url.Split('?')[0]).TrimStart('/')));
                return path.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.Ordinal) && File.Exists(path)
                    ? await File.ReadAllBytesAsync(path, ct) : null;
            }
            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps || !IsMediaHost(uri)) return null;
            using var response = await http.CreateClient(nameof(MediaVisualStore)).GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, ct);
            if (!response.IsSuccessStatusCode || response.Content.Headers.ContentLength > MaxBytes) return null;
            var bytes = await response.Content.ReadAsByteArrayAsync(ct);
            return bytes.Length > MaxBytes ? null : bytes;
        }
        catch (Exception ex) when (ex is IOException or HttpRequestException or TaskCanceledException or UnauthorizedAccessException)
        {
            logger.LogWarning(ex, "Image illisible : {Url}", url);
            return null;
        }
    }

    private bool IsMediaHost(Uri uri) =>
        uri.Host.EndsWith(".blob.core.windows.net", StringComparison.OrdinalIgnoreCase)
        || (Uri.TryCreate(options.Value.PublicBaseUrl, UriKind.Absolute, out var cdn) && string.Equals(cdn.Host, uri.Host, StringComparison.OrdinalIgnoreCase));

    public async Task<Result<string>> SavePngAsync(byte[] png, CancellationToken ct = default)
    {
        try
        {
            using var stream = new MemoryStream(png);
            return Result<string>.Success(await media.SaveImageAsync(stream, "visuel.png", ct));
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or Azure.RequestFailedException)
        {
            return Result<string>.Failure($"Image non enregistrée : {ex.Message}");
        }
    }
}
