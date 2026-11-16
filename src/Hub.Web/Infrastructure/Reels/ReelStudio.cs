using System.Globalization;
using Hub.Modules.Channels.Application.Autopilot;
using Hub.Modules.Channels.Application.Reels;
using Hub.SharedKernel;
using SkiaSharp;

namespace Hub.Web.Infrastructure.Reels;

/// <summary>
/// Montage des Reels avec ffmpeg : vidéo verticale 1080×1920 à 30 images/s, accroche animée en haut,
/// petit logo, étiquettes « AVANT / APRÈS », fin « Devis gratuit » et musique libre de droits.
/// Les éléments graphiques sont dessinés avec SkiaSharp (police de la marque) puis superposés.
/// </summary>
internal sealed class ReelStudio(Ffmpeg ffmpeg, IVisualStore visuals, IMediaStorage media, IWebHostEnvironment env, ILogger<ReelStudio> logger)
    : IReelStudio
{
    public const int W = 1080, H = 1920, Fps = 30;
    private const double EndSeconds = 2.8, Fade = 0.5;
    private static readonly TimeSpan Timeout = TimeSpan.FromMinutes(6);
    private static readonly string[] Tracks = ["lumiere", "fraicheur", "douceur"];

    public bool IsAvailable => ffmpeg.IsAvailable;

    public async Task<Result<RenderedReel>> MontageAsync(IReadOnlyList<string> imageUrls, ReelText text, ReelBrand brand, CancellationToken ct = default)
    {
        using var work = new WorkDir();
        var images = new List<string>();
        foreach (var url in imageUrls.Take(3))
        {
            var bytes = await visuals.LoadAsync(url, ct);
            if (bytes is null) continue;
            var file = work.File($"image{images.Count}.{Extension(bytes)}");
            await File.WriteAllBytesAsync(file, bytes, ct);
            images.Add(file);
        }
        if (images.Count == 0) return Result<RenderedReel>.Failure("Les images de la publication sont introuvables.");

        var each = images.Count == 1 ? 9.0 : 5.0;
        var args = new List<string>();
        foreach (var image in images) args.AddRange(["-i", image]);
        var filter = new List<string>();
        for (var i = 0; i < images.Count; i++)
        {
            var frames = (int)(each * Fps);
            // Mouvement lent : zoom avant sur les images paires, arrière sur les impaires.
            var zoom = i % 2 == 0 ? "min(zoom+0.0009,1.16)" : "if(eq(on,0),1.16,max(zoom-0.0009,1.0))";
            filter.Add($"[{i}:v]scale={Big(W)}:{Big(H)}:force_original_aspect_ratio=increase,crop={Big(W)}:{Big(H)},"
                       + $"zoompan=z='{zoom}':x='iw/2-(iw/zoom/2)':y='ih/2-(ih/zoom/2)':d={frames}:s={W}x{H}:fps={Fps},settb=1/{Fps},setsar=1,format=yuv420p[v{i}]");
        }
        var (joined, seconds) = Join(filter, Enumerable.Repeat(each, images.Count).ToList(), 0.6);
        var body = work.File("corps.mp4");
        args.AddRange(["-filter_complex", string.Join(";", filter), "-map", joined, "-r", Fps.ToString(), "-c:v", "libx264", "-preset", "veryfast", "-crf", "18", "-an", body]);
        var run = await ffmpeg.RunAsync(args, Timeout, ct);
        if (run.IsFailure) return Result<RenderedReel>.Failure(run.Error!);
        return await FinishAsync(work, body, seconds, [], text, brand, ct);
    }

    public async Task<Result<RenderedReel>> FromClipsAsync(IReadOnlyList<string> clipPaths, IReadOnlyList<string> labels, ReelText text, ReelBrand brand, CancellationToken ct = default)
    {
        using var work = new WorkDir();
        var lengths = new List<double>();
        var args = new List<string>();
        var filter = new List<string>();
        for (var i = 0; i < clipPaths.Count; i++)
        {
            var duration = await ffmpeg.DurationAsync(clipPaths[i], ct);
            if (duration is null or < 1) return Result<RenderedReel>.Failure("Une des vidéos est illisible ou trop courte (1 seconde au moins).");
            // Une seule vidéo : jusqu'à 15 s ; avant/après : jusqu'à 7 s chacune (le Reel reste court et rythmé).
            var take = Math.Round(Math.Min(duration.Value, clipPaths.Count == 1 ? 15 : 7), 2);
            lengths.Add(take);
            args.AddRange(["-i", clipPaths[i]]);
            filter.Add($"[{i}:v]trim=0:{F(take)},setpts=PTS-STARTPTS,scale={W}:{H}:force_original_aspect_ratio=increase,crop={W}:{H},fps={Fps},settb=1/{Fps},setsar=1,format=yuv420p[v{i}]");
        }
        var (joined, seconds) = Join(filter, lengths, 0.5);
        var body = work.File("corps.mp4");
        args.AddRange(["-filter_complex", string.Join(";", filter), "-map", joined, "-r", Fps.ToString(), "-c:v", "libx264", "-preset", "veryfast", "-crf", "18", "-an", body]);
        var run = await ffmpeg.RunAsync(args, Timeout, ct);
        if (run.IsFailure) return Result<RenderedReel>.Failure(run.Error!);

        // Fenêtre de chaque étiquette (« AVANT » sur le premier clip, « APRÈS » sur le second).
        var windows = new List<(string Label, double From, double To)>();
        var start = 0.0;
        for (var i = 0; i < lengths.Count; i++)
        {
            var end = start + lengths[i] - (i < lengths.Count - 1 ? 0.5 : 0);
            if (!string.IsNullOrWhiteSpace(labels.ElementAtOrDefault(i))) windows.Add((labels[i], start + 0.2, end - 0.1));
            start = end;
        }
        return await FinishAsync(work, body, seconds, windows, text, brand, ct);
    }

    /// <summary>Enchaîne les segments par un fondu ; renvoie l'étiquette de sortie et la durée totale.</summary>
    internal static (string Output, double Seconds) Join(List<string> filter, IReadOnlyList<double> lengths, double fade)
    {
        if (lengths.Count == 1) return ("[v0]", lengths[0]);
        var current = "[v0]";
        var offset = lengths[0];
        for (var i = 1; i < lengths.Count; i++)
        {
            offset -= fade;
            var output = $"[x{i}]";
            filter.Add($"{current}[v{i}]xfade=transition=fade:duration={F(fade)}:offset={F(offset)}{output}");
            current = output;
            offset += lengths[i];
        }
        return (current, offset);
    }

    /// <summary>Superpose accroche, logo, étiquettes et fin, ajoute la musique, puis enregistre la vidéo et sa couverture.</summary>
    private async Task<Result<RenderedReel>> FinishAsync(WorkDir work, string body, double bodySeconds,
        IReadOnlyList<(string Label, double From, double To)> labels, ReelText text, ReelBrand brand, CancellationToken ct)
    {
        var total = bodySeconds + EndSeconds - Fade;
        var logo = brand.LogoUrl is null ? null : await visuals.LoadAsync(brand.LogoUrl, ct);
        var hook = work.File("accroche.png");
        var badge = work.File("logo.png");
        var end = work.File("fin.png");
        await File.WriteAllBytesAsync(hook, ReelGraphics.Hook(text.Hook, brand), ct);
        await File.WriteAllBytesAsync(badge, ReelGraphics.Badge(logo, brand), ct);
        await File.WriteAllBytesAsync(end, ReelGraphics.EndCard(text.Cta, brand, logo), ct);
        var labelFiles = new List<string>();
        foreach (var (label, _, _) in labels)
        {
            var file = work.File($"etiquette{labelFiles.Count}.png");
            await File.WriteAllBytesAsync(file, ReelGraphics.Label(label, brand), ct);
            labelFiles.Add(file);
        }
        var music = Path.Combine(env.WebRootPath, "media", "musique", $"{Tracks[(uint)text.Hook.GetHashCode() % Tracks.Length]}.m4a");

        var args = new List<string> { "-i", body, "-loop", "1", "-t", F(EndSeconds), "-i", end, "-loop", "1", "-t", F(total), "-i", hook,
                                      "-loop", "1", "-t", F(total), "-i", badge };
        foreach (var f in labelFiles) args.AddRange(["-loop", "1", "-t", F(total), "-i", f]);
        var musicInput = 4 + labelFiles.Count;
        var hasMusic = File.Exists(music);
        if (hasMusic) args.AddRange(["-stream_loop", "-1", "-i", music]);

        var hookEnd = Math.Max(1.2, Math.Min(5.5, bodySeconds - Fade));
        var filter = new List<string>
        {
            $"[1:v]scale={W}:{H},fps={Fps},settb=1/{Fps},setsar=1,format=yuv420p[fin]",
            $"[0:v]fps={Fps},settb=1/{Fps},setsar=1,format=yuv420p[corps]",
            $"[corps][fin]xfade=transition=fade:duration={F(Fade)}:offset={F(bodySeconds - Fade)}[base]",
            $"[2:v]format=rgba,fade=t=in:st=0.2:d=0.4:alpha=1,fade=t=out:st={F(hookEnd - 0.4)}:d=0.4:alpha=1[acc]",
            "[base][acc]overlay=0:0[o0]",
            $"[3:v]format=rgba,fade=t=out:st={F(bodySeconds - Fade - 0.3)}:d=0.3:alpha=1[logo]",
            "[o0][logo]overlay=0:0[o1]",
        };
        var current = "[o1]";
        for (var i = 0; i < labelFiles.Count; i++)
        {
            var (_, from, to) = labels[i];
            filter.Add($"[{4 + i}:v]format=rgba,fade=t=in:st={F(from)}:d=0.3:alpha=1,fade=t=out:st={F(Math.Max(from + 0.3, to - 0.3))}:d=0.3:alpha=1[e{i}]");
            filter.Add($"{current}[e{i}]overlay=0:0[l{i}]");
            current = $"[l{i}]";
        }
        filter.Add($"{current}format=yuv420p[v]");
        if (hasMusic) filter.Add($"[{musicInput}:a]atrim=0:{F(total)},afade=t=in:d=0.6,afade=t=out:st={F(total - 1.4)}:d=1.4,volume=0.85[a]");

        var output = work.File("reel.mp4");
        args.AddRange(["-filter_complex", string.Join(";", filter), "-map", "[v]"]);
        if (hasMusic) args.AddRange(["-map", "[a]", "-c:a", "aac", "-b:a", "128k"]);
        args.AddRange(["-t", F(total), "-r", Fps.ToString(), "-c:v", "libx264", "-preset", "veryfast", "-crf", "22", "-pix_fmt", "yuv420p",
                       "-movflags", "+faststart", output]);
        var run = await ffmpeg.RunAsync(args, Timeout, ct);
        if (run.IsFailure) return Result<RenderedReel>.Failure(run.Error!);

        var cover = work.File("couverture.jpg");
        var shot = await ffmpeg.RunAsync(["-ss", "1.2", "-i", output, "-frames:v", "1", "-q:v", "3", cover], TimeSpan.FromMinutes(1), ct);
        try
        {
            string videoUrl;
            await using (var stream = File.OpenRead(output))
                videoUrl = await media.SaveFileAsync(stream, "reel.mp4", "video/mp4", ct);
            var coverUrl = videoUrl;
            if (shot.IsSuccess)
                await using (var stream = File.OpenRead(cover))
                    coverUrl = await media.SaveImageAsync(stream, "reel-couverture.jpg", ct);
            return Result<RenderedReel>.Success(new RenderedReel(videoUrl, coverUrl, Math.Round(total, 1)));
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or Azure.RequestFailedException)
        {
            logger.LogError(ex, "Reel non enregistré");
            return Result<RenderedReel>.Failure($"La vidéo n'a pas pu être enregistrée : {ex.Message}");
        }
    }

    private static string F(double v) => v.ToString("0.###", CultureInfo.InvariantCulture);

    /// <summary>Taille agrandie de 20 % : marge pour le mouvement de caméra.</summary>
    private static int Big(int size) => size * 6 / 5;

    /// <summary>Extension d'après les premiers octets (ffmpeg choisit le décodeur d'image par l'extension).</summary>
    internal static string Extension(byte[] b) =>
        b.Length > 12 && b[0] == 0x89 && b[1] == 0x50 ? "png"
        : b.Length > 12 && b[0] == 0xFF && b[1] == 0xD8 ? "jpg"
        : b.Length > 12 && b[8] == (byte)'W' && b[9] == (byte)'E' && b[10] == (byte)'B' && b[11] == (byte)'P' ? "webp"
        : "png";

    /// <summary>Dossier de travail temporaire, effacé à la fin.</summary>
    private sealed class WorkDir : IDisposable
    {
        private readonly string root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "hub-reels", Guid.NewGuid().ToString("N"))).FullName;
        public string File(string name) => Path.Combine(root, name);
        public void Dispose()
        {
            try { Directory.Delete(root, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
    }
}

/// <summary>Éléments graphiques des Reels (PNG transparents 1080×1920), dessinés avec la police de la marque.</summary>
internal static class ReelGraphics
{
    private const int W = ReelStudio.W, H = ReelStudio.H;
    private static readonly SKColor Ink = SKColor.Parse("#0b2a3c");

    private static byte[] Draw(Action<SKCanvas> paint)
    {
        using var surface = SKSurface.Create(new SKImageInfo(W, H, SKColorType.Rgba8888, SKAlphaType.Premul));
        surface.Canvas.Clear(SKColors.Transparent);
        paint(surface.Canvas);
        using var image = surface.Snapshot();
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }

    private static SKColor Color(string hex, SKColor fallback) => SKColor.TryParse(hex, out var c) ? c : fallback;

    /// <summary>Accroche : carte blanche arrondie en haut, texte en gras, filet de la couleur de la marque.</summary>
    public static byte[] Hook(string text, ReelBrand brand) => Draw(canvas =>
    {
        using var font = new SKFont(BrandFont.Instance.Bold, 64);
        var lines = Wrap(text, font, W - 240).Take(3).ToList();
        var height = 70 + lines.Count * 82;
        var rect = new SKRect(80, 220, W - 80, 220 + height);
        using var shadow = new SKPaint { Color = new SKColor(11, 42, 60, 60), IsAntialias = true, ImageFilter = SKImageFilter.CreateBlur(24, 24) };
        canvas.DrawRoundRect(new SKRoundRect(SKRect.Create(rect.Left, rect.Top + 14, rect.Width, rect.Height), 36), shadow);
        using var card = new SKPaint { Color = SKColors.White.WithAlpha(245), IsAntialias = true };
        canvas.DrawRoundRect(new SKRoundRect(rect, 36), card);
        using var accent = new SKPaint { Color = Color(brand.PrimaryColor, SKColor.Parse("#0097ce")), IsAntialias = true };
        canvas.DrawRoundRect(new SKRoundRect(new SKRect(rect.Left + 40, rect.Top - 10, rect.Left + 180, rect.Top + 10), 10), accent);
        using var ink = new SKPaint { Color = Ink, IsAntialias = true };
        for (var i = 0; i < lines.Count; i++)
            canvas.DrawText(lines[i], W / 2f, rect.Top + 100 + i * 82, SKTextAlign.Center, font, ink);
    });

    /// <summary>Petit logo en pastille blanche, en haut à gauche.</summary>
    public static byte[] Badge(byte[]? logo, ReelBrand brand) => Draw(canvas =>
    {
        using var pill = new SKPaint { Color = SKColors.White.WithAlpha(235), IsAntialias = true };
        using var bitmap = logo is null ? null : SKBitmap.Decode(logo);
        if (bitmap is not null)
        {
            var h = 70f;
            var w = bitmap.Width * h / bitmap.Height;
            canvas.DrawRoundRect(new SKRoundRect(new SKRect(56, 70, 56 + w + 44, 70 + h + 28), 26), pill);
            canvas.DrawBitmap(bitmap, new SKRect(78, 84, 78 + w, 84 + h), new SKPaint { IsAntialias = true });
        }
        else
        {
            using var font = new SKFont(BrandFont.Instance.Bold, 40);
            var w = font.MeasureText(brand.CompanyName);
            canvas.DrawRoundRect(new SKRoundRect(new SKRect(56, 70, 56 + w + 56, 140), 26), pill);
            using var ink = new SKPaint { Color = Ink, IsAntialias = true };
            canvas.DrawText(brand.CompanyName, 84, 120, SKTextAlign.Left, font, ink);
        }
    });

    /// <summary>Étiquette « AVANT » / « APRÈS » en bas de l'écran.</summary>
    public static byte[] Label(string label, ReelBrand brand) => Draw(canvas =>
    {
        using var font = new SKFont(BrandFont.Instance.Bold, 92);
        var w = font.MeasureText(label) + 120;
        var rect = new SKRect((W - w) / 2, H - 520, (W + w) / 2, H - 370);
        var after = label.StartsWith("APR", StringComparison.OrdinalIgnoreCase);
        using var back = new SKPaint { Color = after ? Color(brand.PrimaryColor, SKColor.Parse("#0097ce")) : Ink, IsAntialias = true };
        canvas.DrawRoundRect(new SKRoundRect(rect, 75), back);
        using var white = new SKPaint { Color = SKColors.White, IsAntialias = true };
        canvas.DrawText(label, W / 2f, rect.MidY + 32, SKTextAlign.Center, font, white);
    });

    /// <summary>Fin : fond blanc, logo, « Devis gratuit · site », téléphone.</summary>
    public static byte[] EndCard(string cta, ReelBrand brand, byte[]? logo) => Draw(canvas =>
    {
        using (var shader = SKShader.CreateLinearGradient(new SKPoint(0, 0), new SKPoint(0, H),
                   [SKColor.Parse("#dceefa"), SKColors.White], SKShaderTileMode.Clamp))
        using (var sky = new SKPaint { Shader = shader })
            canvas.DrawRect(new SKRect(0, 0, W, H), sky);
        using var bitmap = logo is null ? null : SKBitmap.Decode(logo);
        var y = 640f;
        if (bitmap is not null)
        {
            var w = 640f;
            var h = bitmap.Height * w / bitmap.Width;
            canvas.DrawBitmap(bitmap, new SKRect((W - w) / 2, y - h, (W + w) / 2, y), new SKPaint { IsAntialias = true });
        }
        else
        {
            using var name = new SKFont(BrandFont.Instance.Bold, 110);
            using var ink0 = new SKPaint { Color = Ink, IsAntialias = true };
            canvas.DrawText(brand.CompanyName, W / 2f, y, SKTextAlign.Center, name, ink0);
        }
        var parts = cta.Split(" · ");
        using var big = new SKFont(BrandFont.Instance.Bold, 104);
        using var mid = new SKFont(BrandFont.Instance.Regular, 58);
        using var ink = new SKPaint { Color = Ink, IsAntialias = true };
        using var button = new SKPaint { Color = Ink, IsAntialias = true };
        var bw = big.MeasureText(parts[0]) + 140;
        var rect = new SKRect((W - bw) / 2, 820, (W + bw) / 2, 1000);
        canvas.DrawRoundRect(new SKRoundRect(rect, 90), button);
        using var white = new SKPaint { Color = SKColors.White, IsAntialias = true };
        canvas.DrawText(parts[0], W / 2f, rect.MidY + 36, SKTextAlign.Center, big, white);
        if (parts.Length > 1) canvas.DrawText(parts[1], W / 2f, 1130, SKTextAlign.Center, mid, ink);
        if (!string.IsNullOrWhiteSpace(brand.Phone)) canvas.DrawText(brand.Phone, W / 2f, 1220, SKTextAlign.Center, mid, ink);
    });

    internal static IEnumerable<string> Wrap(string text, SKFont font, float width)
    {
        var line = "";
        foreach (var word in text.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            var candidate = line.Length == 0 ? word : $"{line} {word}";
            if (line.Length > 0 && font.MeasureText(candidate) > width)
            {
                yield return line;
                line = word;
            }
            else line = candidate;
        }
        if (line.Length > 0) yield return line;
    }
}
