using Hub.Modules.Channels.Application;
using SkiaSharp;

namespace Hub.Web.Infrastructure;

/// <summary>
/// Met en page une photo de publicité : recadrage carré, puis texte de la marque par-dessus
/// (étiquette, accroche avec mots en couleur, phrase, nom, téléphone, site), dans la police de la marque.
/// Le texte est mesuré et coupé pour ne jamais déborder.
/// </summary>
public sealed class SkiaPhotoComposer(ILogger<SkiaPhotoComposer> logger) : IPhotoComposer
{
    public const string Bottom = "Bas";
    public const string Band = "Bandeau";
    public const string Card = "Carte";

    private static readonly SKColor Navy = new(0x10, 0x23, 0x3F);
    private const float Margin = 60;

    public IReadOnlyList<string> Layouts { get; } = [Bottom, Band, Card];

    public byte[]? Compose(byte[] photo, PhotoOverlay o, int size = 1080)
    {
        try
        {
            using var source = SKBitmap.Decode(photo);
            if (source is null) return null;
            using var image = SKImage.FromBitmap(source);
            using var surface = SKSurface.Create(new SKImageInfo(size, size, SKColorType.Rgba8888, SKAlphaType.Premul));
            var canvas = surface.Canvas;
            canvas.Clear(SKColors.White);
            DrawCover(canvas, image, size);

            var primary = Color(o.PrimaryColor, new SKColor(0x00, 0x97, 0xCE));
            var accent = Color(o.AccentColor, new SKColor(0xE2, 0xE7, 0x35));
            switch (o.Layout)
            {
                case Band: DrawBand(canvas, o, size, primary, accent); break;
                case Card: DrawCard(canvas, o, size, primary, accent); break;
                default: DrawBottom(canvas, o, size, primary, accent); break;
            }

            canvas.Flush();
            using var snapshot = surface.Snapshot();
            using var data = snapshot.Encode(SKEncodedImageFormat.Png, 100);
            return data.ToArray();
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Mise en page de la photo impossible");
            return null;
        }
    }

    // --- Mises en page -------------------------------------------------------------------------

    /// <summary>Texte blanc sur un dégradé sombre en bas de la photo.</summary>
    private static void DrawBottom(SKCanvas canvas, PhotoOverlay o, int size, SKColor primary, SKColor accent)
    {
        var width = size - 2 * Margin;
        var block = Measure(o, width, titleMax: 80);
        var top = size - 48 - block.Height;

        using (var shade = new SKPaint())
        {
            var start = Math.Max(0, top - 260);
            shade.Shader = SKShader.CreateLinearGradient(new SKPoint(0, start), new SKPoint(0, size),
                [Navy.WithAlpha(0), Navy.WithAlpha(150), Navy.WithAlpha(235)], [0f, 0.4f, 1f], SKShaderTileMode.Clamp);
            canvas.DrawRect(0, start, size, size - start, shade);
        }
        DrawBlock(canvas, o, block, Margin, top, SKColors.White, accent, SKColors.White.WithAlpha(225), primary, SKColors.White);
        using var line = new SKPaint { Color = accent };
        canvas.DrawRect(0, size - 10, size, 10, line);
    }

    /// <summary>Bandeau de la couleur principale sous la photo, liseré de la couleur d'accent.</summary>
    private static void DrawBand(SKCanvas canvas, PhotoOverlay o, int size, SKColor primary, SKColor accent)
    {
        var width = size - 2 * Margin;
        var block = Measure(o with { Badge = null }, width, titleMax: 72);
        var panelTop = Math.Min(size * 0.66f, size - block.Height - 96);
        using (var panel = new SKPaint { Color = primary })
            canvas.DrawRect(0, panelTop, size, size - panelTop, panel);
        using (var bar = new SKPaint { Color = accent })
            canvas.DrawRect(0, panelTop, size, 10, bar);

        if (!string.IsNullOrWhiteSpace(o.Badge))
            DrawBadge(canvas, o.Badge!, Margin, panelTop - 30, accent, Navy);
        var highlight = Contrast(accent, primary) >= 2.2 ? accent : Navy;
        DrawBlock(canvas, o with { Badge = null }, block, Margin, panelTop + 48, SKColors.White, highlight, SKColors.White.WithAlpha(235), primary, SKColors.White);
    }

    /// <summary>Carte blanche arrondie, avec ombre, posée sur le bas de la photo.</summary>
    private static void DrawCard(SKCanvas canvas, PhotoOverlay o, int size, SKColor primary, SKColor accent)
    {
        const float outer = 44, inner = 44;
        var width = size - 2 * outer - 2 * inner;
        var block = Measure(o, width, titleMax: 68);
        var cardHeight = block.Height + 2 * inner;
        var rect = SKRect.Create(outer, size - outer - cardHeight, size - 2 * outer, cardHeight);

        using (var shadow = new SKPaint { Color = SKColors.Black.WithAlpha(90), MaskFilter = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, 18), IsAntialias = true })
            canvas.DrawRoundRect(new SKRoundRect(SKRect.Create(rect.Left, rect.Top + 10, rect.Width, rect.Height), 28), shadow);
        using (var card = new SKPaint { Color = SKColors.White, IsAntialias = true })
            canvas.DrawRoundRect(new SKRoundRect(rect, 28), card);

        DrawBlock(canvas, o, block, rect.Left + inner, rect.Top + inner, Navy, primary, new SKColor(0x44, 0x54, 0x6A), accent, primary);
    }

    // --- Bloc de texte -------------------------------------------------------------------------

    private sealed record Block(float TitleSize, List<List<(string Word, bool Strong)>> TitleLines, List<string> SubtitleLines, float Height, float Width);

    private const float BadgeHeight = 52, SubtitleSize = 34, FooterSize = 30;

    private static Block Measure(PhotoOverlay o, float width, float titleMax)
    {
        List<List<(string, bool)>> lines = [];
        var titleSize = titleMax;
        for (; titleSize >= 46; titleSize -= 4)
        {
            using var font = Font(true, titleSize);
            lines = WrapTitle(o.Title, o.Highlight, font, width);
            if (lines.Count <= 3) break;
        }
        if (lines.Count > 3) lines = lines.Take(3).ToList();

        List<string> subtitle = [];
        if (!string.IsNullOrWhiteSpace(o.Subtitle))
        {
            using var font = Font(false, SubtitleSize);
            subtitle = Wrap(o.Subtitle!, font, width).Take(2).ToList();
        }

        var height = (string.IsNullOrWhiteSpace(o.Badge) ? 0 : BadgeHeight + 22)
                     + lines.Count * titleSize * 1.12f
                     + (subtitle.Count > 0 ? 14 + subtitle.Count * SubtitleSize * 1.3f : 0)
                     + 26 + FooterSize * 1.2f;
        return new Block(titleSize, lines, subtitle, height, width);
    }

    private static void DrawBlock(SKCanvas canvas, PhotoOverlay o, Block b, float x, float top,
        SKColor text, SKColor strong, SKColor soft, SKColor badgeBack, SKColor footer)
    {
        var y = top;
        if (!string.IsNullOrWhiteSpace(o.Badge))
        {
            DrawBadge(canvas, o.Badge!, x, y, badgeBack, Contrast(SKColors.White, badgeBack) >= 3 ? SKColors.White : Navy);
            y += BadgeHeight + 22;
        }

        using (var font = Font(true, b.TitleSize))
        using (var paint = new SKPaint { IsAntialias = true })
        {
            var space = font.MeasureText(" ");
            foreach (var line in b.TitleLines)
            {
                y += b.TitleSize * 1.12f;
                var cx = x;
                foreach (var (word, isStrong) in line)
                {
                    paint.Color = isStrong ? strong : text;
                    canvas.DrawText(word, cx, y - b.TitleSize * 0.22f, SKTextAlign.Left, font, paint);
                    cx += font.MeasureText(word) + space;
                }
            }
        }

        if (b.SubtitleLines.Count > 0)
        {
            y += 14;
            using var font = Font(false, SubtitleSize);
            using var paint = new SKPaint { IsAntialias = true, Color = soft };
            foreach (var line in b.SubtitleLines)
            {
                y += SubtitleSize * 1.3f;
                canvas.DrawText(line, x, y - SubtitleSize * 0.3f, SKTextAlign.Left, font, paint);
            }
        }

        y += 26 + FooterSize * 1.2f;
        using (var bold = Font(true, FooterSize))
        using (var regular = Font(false, FooterSize))
        using (var paint = new SKPaint { IsAntialias = true, Color = footer })
        {
            var name = o.CompanyName;
            canvas.DrawText(name, x, y - FooterSize * 0.25f, SKTextAlign.Left, bold, paint);
            var after = x + bold.MeasureText(name);
            if (!string.IsNullOrWhiteSpace(o.Phone))
            {
                var phone = "  ·  " + o.Phone;
                canvas.DrawText(phone, after, y - FooterSize * 0.25f, SKTextAlign.Left, regular, paint);
                after += regular.MeasureText(phone);
            }
            if (!string.IsNullOrWhiteSpace(o.Site) && after + regular.MeasureText(o.Site!) + 40 < x + b.Width)
            {
                paint.Color = footer.WithAlpha(200);
                canvas.DrawText(o.Site!, x + b.Width, y - FooterSize * 0.25f, SKTextAlign.Right, regular, paint);
            }
        }
    }

    private static void DrawBadge(SKCanvas canvas, string label, float x, float y, SKColor back, SKColor fore)
    {
        using var font = Font(true, 24);
        var text = label.ToUpperInvariant();
        var width = font.MeasureText(text) + 44;
        using (var paint = new SKPaint { Color = back, IsAntialias = true })
            canvas.DrawRoundRect(new SKRoundRect(SKRect.Create(x, y, width, BadgeHeight), BadgeHeight / 2), paint);
        using (var paint = new SKPaint { Color = fore, IsAntialias = true })
            canvas.DrawText(text, x + 22, y + BadgeHeight / 2 + 9, SKTextAlign.Left, font, paint);
    }

    // --- Outils --------------------------------------------------------------------------------

    private static SKFont Font(bool bold, float size) =>
        new(bold ? BrandFont.Instance.Bold : BrandFont.Instance.Regular, size) { Edging = SKFontEdging.SubpixelAntialias, Subpixel = true };

    /// <summary>Découpe l'accroche en lignes ; les mots de <paramref name="highlight"/> sont marqués pour la couleur.</summary>
    public static List<List<(string Word, bool Strong)>> WrapTitle(string title, string? highlight, SKFont font, float width)
    {
        var words = title.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var strong = new bool[words.Length];
        if (!string.IsNullOrWhiteSpace(highlight))
        {
            var start = title.IndexOf(highlight, StringComparison.OrdinalIgnoreCase);
            if (start >= 0)
            {
                var end = start + highlight.Length;
                var pos = 0;
                for (var i = 0; i < words.Length; i++)
                {
                    var at = title.IndexOf(words[i], pos, StringComparison.Ordinal);
                    strong[i] = at < end && at + words[i].Length > start;
                    pos = at + words[i].Length;
                }
            }
        }

        var lines = new List<List<(string, bool)>>();
        var current = new List<(string, bool)>();
        var space = font.MeasureText(" ");
        float used = 0;
        for (var i = 0; i < words.Length; i++)
        {
            var w = font.MeasureText(words[i]);
            if (current.Count > 0 && used + space + w > width)
            {
                lines.Add(current);
                current = [];
                used = 0;
            }
            used += (current.Count > 0 ? space : 0) + w;
            current.Add((words[i], strong[i]));
        }
        if (current.Count > 0) lines.Add(current);
        return lines;
    }

    private static IEnumerable<string> Wrap(string text, SKFont font, float width)
    {
        var line = "";
        foreach (var word in text.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            var candidate = line.Length == 0 ? word : line + " " + word;
            if (line.Length > 0 && font.MeasureText(candidate) > width)
            {
                yield return line;
                line = word;
            }
            else line = candidate;
        }
        if (line.Length > 0) yield return line;
    }

    private static void DrawCover(SKCanvas canvas, SKImage image, int size)
    {
        var scale = Math.Max(size / (float)image.Width, size / (float)image.Height);
        var w = size / scale;
        var h = size / scale;
        var src = SKRect.Create((image.Width - w) / 2, (image.Height - h) / 2, w, h);
        using var paint = new SKPaint { IsAntialias = true };
        canvas.DrawImage(image, src, SKRect.Create(0, 0, size, size), new SKSamplingOptions(SKCubicResampler.Mitchell), paint);
    }

    private static SKColor Color(string? hex, SKColor fallback) => SKColor.TryParse(hex, out var c) ? c : fallback;

    /// <summary>Rapport de contraste WCAG entre deux couleurs.</summary>
    internal static double Contrast(SKColor a, SKColor b)
    {
        static double L(SKColor c)
        {
            static double Ch(byte v) { var x = v / 255.0; return x <= 0.03928 ? x / 12.92 : Math.Pow((x + 0.055) / 1.055, 2.4); }
            return 0.2126 * Ch(c.Red) + 0.7152 * Ch(c.Green) + 0.0722 * Ch(c.Blue);
        }
        var (l1, l2) = (L(a), L(b));
        return (Math.Max(l1, l2) + 0.05) / (Math.Min(l1, l2) + 0.05);
    }
}
