using System.Text.Json.Nodes;
using Hub.Modules.Channels.Application.Reels;
using Hub.SharedKernel;
using Microsoft.Extensions.Options;

namespace Hub.Web.Infrastructure.Reels;

/// <summary>Réglages de Pexels (section « Pexels ») : banque de vidéos gratuite, libre d'usage commercial.</summary>
public sealed class PexelsOptions
{
    public const string Section = "Pexels";

    /// <summary>Clé de l'API (pexels.com/api), dans les paramètres d'Azure : Pexels__ApiKey.</summary>
    public string? ApiKey { get; set; }

    /// <summary>Pour les tests : des clips fabriqués par ffmpeg, sans appel à Pexels.</summary>
    public bool Simulate { get; set; }

    public string Endpoint { get; set; } = "https://api.pexels.com";
}

/// <summary>Vraies vidéos d'archives de Pexels : recherche de clips verticaux, puis téléchargement d'un clip par recherche.</summary>
internal sealed class PexelsVideoSource(HttpClient http, IOptions<PexelsOptions> options, ILogger<PexelsVideoSource> logger) : IStockVideoSource
{
    public bool IsConfigured => !string.IsNullOrWhiteSpace(options.Value.ApiKey);

    public async Task<Result<IReadOnlyList<string>>> DownloadAsync(IReadOnlyList<string> queries, CancellationToken ct = default)
    {
        var paths = new List<string>();
        var used = new HashSet<string>();
        try
        {
            foreach (var query in queries)
            {
                using var search = new HttpRequestMessage(HttpMethod.Get,
                    $"{options.Value.Endpoint.TrimEnd('/')}/videos/search?query={Uri.EscapeDataString(query)}&orientation=portrait&size=medium&per_page=15");
                search.Headers.TryAddWithoutValidation("Authorization", options.Value.ApiKey);
                using var found = await http.SendAsync(search, ct);
                if (!found.IsSuccessStatusCode)
                {
                    if ((int)found.StatusCode is 401 or 403) return Fail(paths, "La clé Pexels est refusée : vérifiez Pexels__ApiKey dans Azure.");
                    if ((int)found.StatusCode == 429) return Fail(paths, "Quota de Pexels atteint pour le moment : réessayez plus tard.");
                    logger.LogWarning("Pexels {Status} pour « {Query} »", (int)found.StatusCode, query);
                    continue;
                }
                var link = PickFile(JsonNode.Parse(await found.Content.ReadAsStringAsync(ct)), used, Random.Shared);
                if (link is null) continue;
                using var file = await http.GetAsync(link, HttpCompletionOption.ResponseHeadersRead, ct);
                if (!file.IsSuccessStatusCode) continue;
                var path = Path.Combine(Path.GetTempPath(), $"hub-pexels-{Guid.NewGuid():N}.mp4");
                await using (var output = File.Create(path)) await file.Content.CopyToAsync(output, ct);
                paths.Add(path);
            }
        }
        catch (Exception ex) when (ex is HttpRequestException or System.Text.Json.JsonException or TaskCanceledException && !ct.IsCancellationRequested)
        {
            logger.LogError(ex, "Pexels injoignable");
            return Fail(paths, "Pexels est injoignable pour le moment.");
        }
        return paths.Count == 0
            ? Result<IReadOnlyList<string>>.Failure("Aucune vidéo d'archives trouvée pour ce sujet.")
            : Result<IReadOnlyList<string>>.Success(paths);
    }

    private static Result<IReadOnlyList<string>> Fail(List<string> paths, string error)
    {
        foreach (var p in paths) try { File.Delete(p); } catch (IOException) { }
        return Result<IReadOnlyList<string>>.Failure(error);
    }

    /// <summary>
    /// Parmi les résultats : vidéos de 5 secondes au moins, jamais deux fois la même ; une au hasard parmi les 5 premières ;
    /// son fichier MP4 vertical le plus proche de 1080×1920 (au moins 720 px de large).
    /// </summary>
    internal static string? PickFile(JsonNode? json, HashSet<string> used, Random random)
    {
        var candidates = (json?["videos"]?.AsArray() ?? [])
            .Where(v => v is not null && (v["duration"]?.GetValue<int>() ?? 0) >= 5 && !used.Contains(v["id"]?.ToString() ?? ""))
            .Select(v => (Id: v!["id"]!.ToString(), File: (v["video_files"]?.AsArray() ?? [])
                .Where(f => f is not null && f["file_type"]?.GetValue<string>() == "video/mp4"
                    && (f["width"]?.GetValue<int>() ?? 0) >= 720
                    && (f["height"]?.GetValue<int>() ?? 0) > (f["width"]?.GetValue<int>() ?? 0))
                .OrderBy(f => Math.Abs((f!["height"]?.GetValue<int>() ?? 0) - 1920))
                .Select(f => f!["link"]?.GetValue<string>())
                .FirstOrDefault(l => l is not null)))
            .Where(c => c.File is not null)
            .Take(5).ToList();
        if (candidates.Count == 0) return null;
        var pick = candidates[random.Next(candidates.Count)];
        used.Add(pick.Id);
        return pick.File;
    }
}

/// <summary>Pour les tests : un clip vertical par recherche, fabriqué par ffmpeg (sans appel à Pexels).</summary>
internal sealed class SimulatedStockVideoSource(Ffmpeg ffmpeg) : IStockVideoSource
{
    public bool IsConfigured => ffmpeg.IsAvailable;

    public async Task<Result<IReadOnlyList<string>>> DownloadAsync(IReadOnlyList<string> queries, CancellationToken ct = default)
    {
        var colors = new[] { "0xf4efe6", "0xcfe3ee", "0xe9e2d4" };
        var paths = new List<string>();
        for (var i = 0; i < queries.Count; i++)
        {
            var path = Path.Combine(Path.GetTempPath(), $"hub-pexels-simule-{Guid.NewGuid():N}.mp4");
            var run = await ffmpeg.RunAsync(["-f", "lavfi", "-i", $"gradients=s=720x1280:d=6:speed=0.03:c0={colors[i % 3]}:c1=0x0097ce",
                                             "-t", "6", "-c:v", "libx264", "-preset", "veryfast", "-pix_fmt", "yuv420p", path], TimeSpan.FromMinutes(2), ct);
            if (run.IsFailure) return Result<IReadOnlyList<string>>.Failure(run.Error!);
            paths.Add(path);
        }
        return Result<IReadOnlyList<string>>.Success(paths);
    }
}
