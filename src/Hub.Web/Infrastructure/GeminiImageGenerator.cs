using System.Text;
using System.Text.Json.Nodes;
using Hub.SharedKernel;
using Hub.SharedKernel.Ai;
using Microsoft.Extensions.Options;

namespace Hub.Web.Infrastructure;

public sealed class GeminiOptions
{
    public const string Section = "Gemini";
    public string ApiKey { get; set; } = string.Empty;
    public string Endpoint { get; set; } = "https://generativelanguage.googleapis.com/v1beta";

    /// <summary>Modèle d'image (ex. gemini-3.1-flash-image, gemini-3-pro-image pour le plus réaliste).</summary>
    public string ImageModel { get; set; } = "gemini-3.1-flash-image";

    /// <summary>1K, 2K ou 4K : 2K donne une image nette une fois réduite à 1080 px.</summary>
    public string ImageSize { get; set; } = "2K";

    public int MonthlyImageLimit { get; set; } = 150;
}

/// <summary>Photos réalistes par l'API Gemini de Google (generateContent, réponse en image).</summary>
public sealed class GeminiImageGenerator(HttpClient http, IOptions<GeminiOptions> options, ILogger<GeminiImageGenerator> logger)
    : IAiImageGenerator
{
    public bool IsAvailable => true;
    public string ModelName => options.Value.ImageModel;
    public int MonthlyLimit => options.Value.MonthlyImageLimit;

    public async Task<Result<AiImage>> GenerateAsync(AiImageRequest r, CancellationToken ct = default)
    {
        var o = options.Value;
        var body = new JsonObject
        {
            ["contents"] = new JsonArray(new JsonObject
            {
                ["role"] = "user",
                ["parts"] = new JsonArray(new JsonObject { ["text"] = r.Prompt }),
            }),
            ["generationConfig"] = new JsonObject
            {
                ["responseModalities"] = new JsonArray("IMAGE"),
                ["imageConfig"] = new JsonObject { ["aspectRatio"] = r.AspectRatio, ["imageSize"] = o.ImageSize },
            },
        };

        using var request = new HttpRequestMessage(HttpMethod.Post, $"{o.Endpoint.TrimEnd('/')}/models/{o.ImageModel}:generateContent")
        {
            Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json"),
        };
        request.Headers.Add("x-goog-api-key", o.ApiKey);

        try
        {
            using var response = await http.SendAsync(request, ct);
            var json = await response.Content.ReadAsStringAsync(ct);
            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning("API Gemini (image) : {Status} {Body}", (int)response.StatusCode, json.Length > 400 ? json[..400] : json);
                return Result<AiImage>.Failure((int)response.StatusCode switch
                {
                    400 => "Google a refusé la demande d'image (consigne non acceptée). Réessayez.",
                    401 or 403 => "Clé Google refusée : vérifiez la clé et la facturation du projet.",
                    404 => $"Modèle d'image « {o.ImageModel} » introuvable chez Google (réglage Gemini:ImageModel).",
                    429 => "Trop de demandes d'images chez Google pour le moment. Réessayez dans une minute.",
                    _ => $"Le service d'images n'a pas pu répondre (code {(int)response.StatusCode}).",
                });
            }
            return Parse(json, o.ImageModel);
        }
        catch (HttpRequestException ex)
        {
            logger.LogError(ex, "API Gemini injoignable");
            return Result<AiImage>.Failure("Le service d'images est injoignable pour le moment.");
        }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested)
        {
            return Result<AiImage>.Failure("Le service d'images a mis trop de temps à répondre. Réessayez.");
        }
    }

    /// <summary>Lit la première image de la réponse (champ inlineData, base64).</summary>
    public static Result<AiImage> Parse(string json, string model)
    {
        try
        {
            var root = JsonNode.Parse(json);
            var parts = root?["candidates"]?[0]?["content"]?["parts"]?.AsArray() ?? [];
            foreach (var part in parts)
            {
                var inline = part?["inlineData"] ?? part?["inline_data"];
                var data = inline?["data"]?.GetValue<string>();
                if (string.IsNullOrEmpty(data)) continue;
                var mime = (inline?["mimeType"] ?? inline?["mime_type"])?.GetValue<string>() ?? "image/png";
                return Result<AiImage>.Success(new AiImage(Convert.FromBase64String(data), mime, model));
            }
            var reason = root?["promptFeedback"]?["blockReason"]?.GetValue<string>()
                ?? root?["candidates"]?[0]?["finishReason"]?.GetValue<string>();
            return Result<AiImage>.Failure(reason is null
                ? "Aucune image reçue. Réessayez."
                : $"Image refusée par Google ({reason}). Essayez une autre proposition.");
        }
        catch (Exception ex) when (ex is System.Text.Json.JsonException or FormatException or InvalidOperationException)
        {
            return Result<AiImage>.Failure("Réponse du service d'images illisible.");
        }
    }
}

internal sealed class NoAiImageGenerator : IAiImageGenerator
{
    public bool IsAvailable => false;
    public string ModelName => "aucun";
    public int MonthlyLimit => 0;
    public Task<Result<AiImage>> GenerateAsync(AiImageRequest request, CancellationToken ct = default) =>
        Task.FromResult(Result<AiImage>.Failure("Aucun service d'images configuré."));
}

/// <summary>
/// Pour les tests (Gemini:Simulate=true) : une fausse « photo » d'intérieur dessinée localement,
/// pour vérifier la mise en page et tout le parcours sans appeler Google.
/// </summary>
internal sealed class SimulatedImageGenerator : IAiImageGenerator
{
    public bool IsAvailable => true;
    public string ModelName => "simulé";
    public int MonthlyLimit => 1000;

    public Task<Result<AiImage>> GenerateAsync(AiImageRequest request, CancellationToken ct = default)
    {
        const int size = 1536;
        using var surface = SkiaSharp.SKSurface.Create(new SkiaSharp.SKImageInfo(size, size));
        var c = surface.Canvas;
        using var paint = new SkiaSharp.SKPaint { IsAntialias = true };
        paint.Shader = SkiaSharp.SKShader.CreateLinearGradient(new(0, 0), new(0, size),
            [new SkiaSharp.SKColor(0xF4, 0xEF, 0xE6), new SkiaSharp.SKColor(0xD9, 0xCF, 0xC0)], SkiaSharp.SKShaderTileMode.Clamp);
        c.DrawRect(0, 0, size, size, paint);
        paint.Shader = null;
        paint.Color = new SkiaSharp.SKColor(0xBF, 0xDD, 0xF0); c.DrawRect(120, 160, 420, 620, paint);        // fenêtre
        paint.Color = SkiaSharp.SKColors.White; c.DrawRect(320, 160, 14, 620, paint);
        paint.Color = new SkiaSharp.SKColor(0x8C, 0x9A, 0x8E); c.DrawRoundRect(700, 820, 720, 260, 40, 40, paint); // canapé
        paint.Color = new SkiaSharp.SKColor(0xE0, 0x8A, 0x4F); c.DrawRoundRect(760, 760, 180, 150, 30, 30, paint);
        paint.Color = new SkiaSharp.SKColor(0x4E, 0x8F, 0x5A); c.DrawCircle(560, 900, 110, paint);          // plante
        paint.Color = new SkiaSharp.SKColor(0xB8, 0x8B, 0x5E); c.DrawRect(0, 1180, size, size - 1180, paint); // plancher
        using var image = surface.Snapshot();
        using var data = image.Encode(SkiaSharp.SKEncodedImageFormat.Png, 90);
        return Task.FromResult(Result<AiImage>.Success(new AiImage(data.ToArray(), "image/png", ModelName)));
    }
}
