using Hub.Modules.Quotes.Domain;

namespace Hub.Modules.Quotes.Application;

/// <summary>Proposition brute renvoyée par un modèle, avant contrôle des prix.</summary>
public sealed record ModelQuote(
    decimal EstimatedHours,
    IReadOnlyList<QuoteLine> Lines,
    string Reasoning,
    IReadOnlyList<string> Assumptions,
    IReadOnlyList<string> Questions,
    string CustomerMessage,
    string Confidence,
    string Model,
    int InputTokens = 0,
    int OutputTokens = 0);

/// <summary>Un modèle capable de proposer un devis : Claude, ou le modèle simulé.</summary>
public interface IQuoteModel
{
    string Name { get; }
    Task<ModelQuote> ProposeAsync(QuoteContext context, CancellationToken ct = default);
}
