using Hub.Modules.Channels.Application;
using Hub.Modules.Channels.Application.Autopilot;
using Hub.SharedKernel;
using SkiaSharp;
using Svg.Skia;
using Svg.Skia.TypefaceProviders;

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
            using var document = new SKSvg();
            document.Settings.TypefaceProviders = [BrandFont.Instance];
            var picture = document.FromSvg(clean);
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
internal sealed class BrandFont : ITypefaceProvider
{
    public static readonly BrandFont Instance = new();

    private readonly SKTypeface regular = Load("Fonts.Poppins-Regular.ttf");
    private readonly SKTypeface bold = Load("Fonts.Poppins-Bold.ttf");

    public SKTypeface? FromFamilyName(string fontFamily, SKFontStyleWeight fontWeight, SKFontStyleWidth fontWidth, SKFontStyleSlant fontStyle) =>
        (int)fontWeight >= (int)SKFontStyleWeight.SemiBold ? bold : regular;

    private static SKTypeface Load(string name)
    {
        using var stream = typeof(BrandFont).Assembly.GetManifestResourceStream(name)
            ?? throw new InvalidOperationException($"Police intégrée introuvable : {name}");
        return SKTypeface.FromStream(stream);
    }
}

/// <summary>Enregistre le PNG d'un visuel dans le stockage des médias (local ou Azure Blob).</summary>
internal sealed class MediaVisualStore(IMediaStorage media) : IVisualStore
{
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
