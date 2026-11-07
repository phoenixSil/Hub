namespace Hub.SharedKernel.Ai;

/// <summary>Demande d'image : la description (en anglais de préférence), le format et la définition.</summary>
public sealed record AiImageRequest(string Prompt, string AspectRatio = "1:1");

public sealed record AiImage(byte[] Bytes, string MimeType, string Model);

/// <summary>
/// Création d'images (photos réalistes) par IA, indépendante du fournisseur (Google Gemini aujourd'hui).
/// Les modules métier ne connaissent que cette interface.
/// </summary>
public interface IAiImageGenerator
{
    /// <summary>Faux tant qu'aucune clé n'est configurée : l'appelant se replie sur une illustration dessinée.</summary>
    bool IsAvailable { get; }
    string ModelName { get; }

    /// <summary>Nombre maximal d'images par mois et par entreprise (garde-fou de coût).</summary>
    int MonthlyLimit { get; }

    Task<Result<AiImage>> GenerateAsync(AiImageRequest request, CancellationToken ct = default);
}
