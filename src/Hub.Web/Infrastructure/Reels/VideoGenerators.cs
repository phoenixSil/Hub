using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Hub.Modules.Channels.Application.Reels;
using Hub.SharedKernel;
using Microsoft.Extensions.Options;

namespace Hub.Web.Infrastructure.Reels;

/// <summary>
/// Vidéo générée par l'IA de Google (API Gemini, opération longue « predictLongRunning ») :
/// on lance la génération, on attend qu'elle soit prête, puis on télécharge le clip vertical.
/// Le modèle se règle dans Video:Model (Google retire régulièrement des versions).
/// </summary>
internal sealed class GeminiVideoGenerator(HttpClient http, IOptions<GeminiOptions> gemini, IOptions<VideoOptions> options,
    ILogger<GeminiVideoGenerator> logger) : IAiVideoGenerator
{
    public bool IsConfigured => !string.IsNullOrWhiteSpace(gemini.Value.ApiKey);
    public string ModelName => options.Value.Model;
    public int ClipSeconds => options.Value.ClipSeconds;
    public decimal PricePerSecond => options.Value.PricePerSecond;

    internal TimeSpan PollInterval { get; init; } = TimeSpan.FromSeconds(10);

    public async Task<Result<string>> GenerateAsync(string prompt, CancellationToken ct = default)
    {
        var o = options.Value;
        var baseUrl = gemini.Value.Endpoint.TrimEnd('/');
        var body = new JsonObject
        {
            ["instances"] = new JsonArray(new JsonObject { ["prompt"] = prompt }),
            ["parameters"] = new JsonObject
            {
                ["aspectRatio"] = "9:16",
                ["resolution"] = o.Resolution,
                ["durationSeconds"] = o.ClipSeconds,
                ["personGeneration"] = "allow_all",
            },
        };
        try
        {
            using var start = new HttpRequestMessage(HttpMethod.Post, $"{baseUrl}/models/{o.Model}:predictLongRunning")
            {
                Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json"),
            };
            start.Headers.Add("x-goog-api-key", gemini.Value.ApiKey);
            using var started = await http.SendAsync(start, ct);
            var json = JsonNode.Parse(await started.Content.ReadAsStringAsync(ct));
            if (!started.IsSuccessStatusCode)
                return Result<string>.Failure(Error(json, (int)started.StatusCode, o.Model));
            var operation = json?["name"]?.GetValue<string>();
            if (operation is null) return Result<string>.Failure("Google n'a pas lancé la génération de la vidéo.");

            var deadline = DateTimeOffset.UtcNow.AddMinutes(o.TimeoutMinutes);
            JsonNode? done = null;
            while (DateTimeOffset.UtcNow < deadline)
            {
                await Task.Delay(PollInterval, ct);
                using var poll = new HttpRequestMessage(HttpMethod.Get, $"{baseUrl}/{operation}");
                poll.Headers.Add("x-goog-api-key", gemini.Value.ApiKey);
                using var polled = await http.SendAsync(poll, ct);
                var state = JsonNode.Parse(await polled.Content.ReadAsStringAsync(ct));
                if (!polled.IsSuccessStatusCode) return Result<string>.Failure(Error(state, (int)polled.StatusCode, o.Model));
                if (state?["done"]?.GetValue<bool>() == true) { done = state; break; }
            }
            if (done is null) return Result<string>.Failure("La vidéo IA n'était pas prête à temps : elle sera retentée.");
            if (done["error"]?["message"]?.GetValue<string>() is { } failed) return Result<string>.Failure($"Google a refusé la vidéo : {failed}");

            var uri = VideoUri(done);
            if (uri is null)
                return Result<string>.Failure("Google n'a renvoyé aucune vidéo (la scène a peut-être été refusée par ses filtres).");

            using var download = new HttpRequestMessage(HttpMethod.Get, uri);
            download.Headers.Add("x-goog-api-key", gemini.Value.ApiKey);
            using var file = await http.SendAsync(download, HttpCompletionOption.ResponseHeadersRead, ct);
            if (!file.IsSuccessStatusCode) return Result<string>.Failure($"Vidéo IA introuvable au téléchargement ({(int)file.StatusCode}).");
            var path = Path.Combine(Path.GetTempPath(), $"hub-veo-{Guid.NewGuid():N}.mp4");
            await using (var output = File.Create(path)) await file.Content.CopyToAsync(output, ct);
            return Result<string>.Success(path);
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or TaskCanceledException && !ct.IsCancellationRequested)
        {
            logger.LogError(ex, "IA vidéo injoignable");
            return Result<string>.Failure("Le service de vidéo IA de Google est injoignable pour le moment.");
        }
    }

    /// <summary>Adresse du clip dans la réponse (« generateVideoResponse.generatedSamples[0].video.uri »).</summary>
    internal static string? VideoUri(JsonNode done) =>
        done["response"]?["generateVideoResponse"]?["generatedSamples"]?[0]?["video"]?["uri"]?.GetValue<string>()
        ?? done["response"]?["generatedVideos"]?[0]?["video"]?["uri"]?.GetValue<string>();

    internal static string Error(JsonNode? json, int status, string model)
    {
        var message = json?["error"]?["message"]?.GetValue<string>();
        return status switch
        {
            404 => $"Le modèle vidéo « {model} » n'existe plus chez Google : changez le réglage Video__Model.",
            401 or 403 => "La clé Gemini n'a pas accès à la génération de vidéos (facturation activée ?).",
            429 => "Quota de vidéos IA de Google atteint pour le moment : réessayez plus tard.",
            _ => $"Google a refusé la vidéo ({status}){(message is null ? "." : $" : {message}")}",
        };
    }
}

/// <summary>Pour les tests : un clip vertical de couleur fabriqué par ffmpeg, sans appel à Google.</summary>
internal sealed class SimulatedVideoGenerator(Ffmpeg ffmpeg, IOptions<VideoOptions> options) : IAiVideoGenerator
{
    public bool IsConfigured => ffmpeg.IsAvailable;
    public string ModelName => "simulé";
    public int ClipSeconds => options.Value.ClipSeconds;
    public decimal PricePerSecond => options.Value.PricePerSecond;

    public async Task<Result<string>> GenerateAsync(string prompt, CancellationToken ct = default)
    {
        var path = Path.Combine(Path.GetTempPath(), $"hub-veo-simule-{Guid.NewGuid():N}.mp4");
        var run = await ffmpeg.RunAsync(["-f", "lavfi", "-i", $"gradients=s=720x1280:d={ClipSeconds}:speed=0.02:c0=0xdceefa:c1=0x0097ce",
                                         "-t", ClipSeconds.ToString(), "-c:v", "libx264", "-preset", "veryfast", "-pix_fmt", "yuv420p", path], TimeSpan.FromMinutes(2), ct);
        return run.IsSuccess ? Result<string>.Success(path) : Result<string>.Failure(run.Error!);
    }
}

/// <summary>Sans clé Gemini : pas de vidéo IA (le pilote fait des Reels montés).</summary>
internal sealed class NoAiVideoGenerator(IOptions<VideoOptions> options) : IAiVideoGenerator
{
    public bool IsConfigured => false;
    public string ModelName => "aucun";
    public int ClipSeconds => options.Value.ClipSeconds;
    public decimal PricePerSecond => options.Value.PricePerSecond;
    public Task<Result<string>> GenerateAsync(string prompt, CancellationToken ct = default) =>
        Task.FromResult(Result<string>.Failure("Aucune clé Gemini : la génération de vidéos par l'IA n'est pas disponible."));
}
