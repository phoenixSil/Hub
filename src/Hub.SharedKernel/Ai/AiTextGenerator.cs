namespace Hub.SharedKernel.Ai;

/// <summary>Demande de texte structuré : consignes, demande, schéma JSON de la réponse.</summary>
public sealed record AiJsonRequest(string System, string User, string JsonSchema, int MaxTokens = 1500,
    IReadOnlyList<AiImageInput>? Images = null);

/// <summary>Image jointe à une demande (l'IA la regarde avant de lire le texte).</summary>
public sealed record AiImageInput(byte[] Bytes, string MediaType = "image/jpeg");

public sealed record AiJsonResponse(string Json, string Model, int InputTokens, int OutputTokens);

/// <summary>
/// Génération de texte par IA, indépendante du fournisseur (Claude aujourd'hui).
/// Les modules métier ne connaissent que cette interface.
/// </summary>
public interface IAiTextGenerator
{
    /// <summary>Faux tant qu'aucune clé n'est configurée : l'appelant prévoit alors un repli sans IA.</summary>
    bool IsAvailable { get; }
    string ModelName { get; }
    Task<Result<AiJsonResponse>> GenerateJsonAsync(AiJsonRequest request, CancellationToken ct = default);
}
