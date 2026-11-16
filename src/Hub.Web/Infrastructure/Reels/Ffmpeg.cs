using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Hub.SharedKernel;
using Microsoft.Extensions.Options;

namespace Hub.Web.Infrastructure.Reels;

/// <summary>Réglages vidéo (section « Video ») : outil de montage et IA vidéo.</summary>
public sealed class VideoOptions
{
    public const string Section = "Video";

    /// <summary>Chemin de ffmpeg ; vide = celui livré avec le site (tools/ffmpeg), sinon celui du système.</summary>
    public string? FfmpegPath { get; set; }

    /// <summary>Modèle d'IA vidéo de Google (API Gemini, predictLongRunning).</summary>
    public string Model { get; set; } = "veo-3.1-lite-generate-001";

    /// <summary>720p (moins cher ; agrandi en 1080×1920 au montage) ou 1080p.</summary>
    public string Resolution { get; set; } = "720p";

    public int ClipSeconds { get; set; } = 8;

    /// <summary>Prix estimé par seconde (dollars américains), pour le plafond mensuel. Prudent par défaut.</summary>
    public decimal PricePerSecond { get; set; } = 0.10m;

    /// <summary>Attente maximale d'une vidéo IA (Google annonce jusqu'à 6 minutes aux heures de pointe).</summary>
    public int TimeoutMinutes { get; set; } = 8;
}

/// <summary>
/// Lance ffmpeg (arguments passés un par un, jamais par un interpréteur de commandes).
/// Le binaire livré avec le site est copié dans le dossier temporaire et rendu exécutable au premier usage.
/// </summary>
public sealed partial class Ffmpeg(IOptions<VideoOptions> options, ILogger<Ffmpeg> logger)
{
    private readonly Lazy<string?> path = new(() => Resolve(options.Value.FfmpegPath, logger));

    public bool IsAvailable => path.Value is not null;

    private static string? Resolve(string? configured, ILogger logger)
    {
        var candidates = new List<string>();
        if (!string.IsNullOrWhiteSpace(configured)) candidates.Add(configured);
        var bundled = Path.Combine(AppContext.BaseDirectory, "tools", "ffmpeg");
        if (File.Exists(bundled))
        {
            try
            {
                // Un déploiement par archive perd le droit d'exécution : on travaille sur une copie exécutable.
                var copy = Path.Combine(Path.GetTempPath(), "hub-ffmpeg");
                if (!File.Exists(copy) || new FileInfo(copy).Length != new FileInfo(bundled).Length) File.Copy(bundled, copy, overwrite: true);
                if (!OperatingSystem.IsWindows())
                    File.SetUnixFileMode(copy, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute);
                candidates.Add(copy);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { logger.LogWarning(ex, "ffmpeg livré inutilisable"); }
        }
        candidates.Add("ffmpeg");
        foreach (var candidate in candidates)
        {
            try
            {
                using var p = Process.Start(new ProcessStartInfo(candidate, "-version") { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false });
                if (p is null) continue;
                p.StandardOutput.ReadToEnd();
                if (p.WaitForExit(15000) && p.ExitCode == 0)
                {
                    logger.LogInformation("Montage vidéo : ffmpeg trouvé ({Path})", candidate);
                    return candidate;
                }
            }
            catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException) { }
        }
        logger.LogWarning("Montage vidéo indisponible : ffmpeg introuvable");
        return null;
    }

    public async Task<Result> RunAsync(IReadOnlyList<string> args, TimeSpan timeout, CancellationToken ct)
    {
        if (path.Value is null) return Result.Failure("L'outil de montage vidéo (ffmpeg) n'est pas installé sur le serveur.");
        var info = new ProcessStartInfo(path.Value) { RedirectStandardError = true, RedirectStandardOutput = true, UseShellExecute = false };
        foreach (var a in new[] { "-hide_banner", "-loglevel", "error", "-y" }.Concat(args)) info.ArgumentList.Add(a);
        using var process = Process.Start(info)!;
        var errors = new StringBuilder();
        process.ErrorDataReceived += (_, e) => { if (e.Data is not null && errors.Length < 4000) errors.AppendLine(e.Data); };
        process.OutputDataReceived += (_, _) => { };
        process.BeginErrorReadLine();
        process.BeginOutputReadLine();
        using var limit = CancellationTokenSource.CreateLinkedTokenSource(ct);
        limit.CancelAfter(timeout);
        try { await process.WaitForExitAsync(limit.Token); }
        catch (OperationCanceledException)
        {
            try { process.Kill(entireProcessTree: true); } catch (InvalidOperationException) { }
            if (ct.IsCancellationRequested) throw;
            return Result.Failure("Le montage vidéo a pris trop de temps.");
        }
        if (process.ExitCode == 0) return Result.Success();
        logger.LogWarning("ffmpeg a échoué ({Code}) : {Errors}", process.ExitCode, errors.ToString());
        return Result.Failure("Le montage de la vidéo a échoué (format de vidéo non pris en charge ?).");
    }

    [GeneratedRegex(@"Duration:\s*(\d+):(\d+):(\d+(?:\.\d+)?)")]
    private static partial Regex DurationLine();

    /// <summary>Durée d'une vidéo, en secondes (lue dans l'en-tête par ffmpeg), ou null.</summary>
    public async Task<double?> DurationAsync(string file, CancellationToken ct)
    {
        if (path.Value is null) return null;
        var info = new ProcessStartInfo(path.Value) { RedirectStandardError = true, UseShellExecute = false };
        foreach (var a in new[] { "-hide_banner", "-i", file }) info.ArgumentList.Add(a);
        using var process = Process.Start(info)!;
        var text = await process.StandardError.ReadToEndAsync(ct);
        await process.WaitForExitAsync(ct);
        var m = DurationLine().Match(text);
        if (!m.Success) return null;
        return int.Parse(m.Groups[1].Value) * 3600 + int.Parse(m.Groups[2].Value) * 60
               + double.Parse(m.Groups[3].Value, CultureInfo.InvariantCulture);
    }
}
