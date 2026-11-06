using System.Text;
using System.Text.Json.Nodes;
using Hub.Modules.Quotes.Application;
using Hub.SharedKernel;
using Hub.SharedKernel.Ai;
using Microsoft.Extensions.Options;

namespace Hub.Web.Infrastructure;

/// <summary>Génération de texte structuré par l'API Claude (sorties JSON contraintes par un schéma).</summary>
internal sealed class ClaudeTextGenerator(HttpClient http, IOptions<AnthropicOptions> options, ILogger<ClaudeTextGenerator> logger)
    : IAiTextGenerator
{
    public bool IsAvailable => true;
    public string ModelName => options.Value.Model;

    public async Task<Result<AiJsonResponse>> GenerateJsonAsync(AiJsonRequest r, CancellationToken ct = default)
    {
        var body = new JsonObject
        {
            ["model"] = options.Value.Model,
            ["max_tokens"] = r.MaxTokens,
            ["system"] = r.System,
            ["output_config"] = new JsonObject
            {
                ["format"] = new JsonObject { ["type"] = "json_schema", ["schema"] = JsonNode.Parse(r.JsonSchema) },
            },
            ["messages"] = new JsonArray(new JsonObject { ["role"] = "user", ["content"] = r.User }),
        };

        using var request = new HttpRequestMessage(HttpMethod.Post, options.Value.Endpoint)
        {
            Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json"),
        };
        request.Headers.Add("x-api-key", options.Value.ApiKey);
        request.Headers.Add("anthropic-version", "2023-06-01");

        try
        {
            using var response = await http.SendAsync(request, ct);
            var json = await response.Content.ReadAsStringAsync(ct);
            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning("API Claude : {Status} {Body}", (int)response.StatusCode, json.Length > 300 ? json[..300] : json);
                return Result<AiJsonResponse>.Failure($"L'IA n'a pas pu répondre (code {(int)response.StatusCode}).");
            }

            var root = JsonNode.Parse(json)!;
            if (root["stop_reason"]?.GetValue<string>() is "refusal" or "max_tokens")
                return Result<AiJsonResponse>.Failure("Réponse de l'IA incomplète. Réessayez.");

            var text = string.Concat((root["content"]?.AsArray() ?? [])
                .Where(c => c?["type"]?.GetValue<string>() == "text")
                .Select(c => c!["text"]?.GetValue<string>()));
            return Result<AiJsonResponse>.Success(new AiJsonResponse(text, options.Value.Model,
                root["usage"]?["input_tokens"]?.GetValue<int>() ?? 0,
                root["usage"]?["output_tokens"]?.GetValue<int>() ?? 0));
        }
        catch (HttpRequestException ex)
        {
            logger.LogError(ex, "API Claude injoignable");
            return Result<AiJsonResponse>.Failure("L'IA est injoignable pour le moment.");
        }
    }
}

/// <summary>Aucune clé configurée : les modules utilisent leur repli sans IA.</summary>
internal sealed class NoAiTextGenerator : IAiTextGenerator
{
    public bool IsAvailable => false;
    public string ModelName => "aucun";
    public Task<Result<AiJsonResponse>> GenerateJsonAsync(AiJsonRequest request, CancellationToken ct = default) =>
        Task.FromResult(Result<AiJsonResponse>.Failure("Aucune IA n'est configurée."));
}
