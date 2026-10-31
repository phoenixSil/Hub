namespace Hub.Modules.Quotes.Application;

/// <summary>Ce que le client a demandé, tel que l'assistant le voit.</summary>
public sealed record QuoteLeadFacts(
    Guid LeadId,
    string CustomerName,
    string CustomerEmail,
    IReadOnlyList<string> Services,
    string? PropertyType,
    string? PropertySize,
    string? Frequency,
    string? Area,
    string? Message);

/// <summary>Une ligne de la grille de prix interne (publiée ou non).</summary>
public sealed record PriceGridItem(
    string Service,
    string Category,
    string Kind,
    decimal? MinAmount,
    decimal? MaxAmount,
    decimal? MinimumHours,
    decimal? ShiftHours,
    string? InternalNotes)
{
    /// <summary>« services » (entretien) ou « aines » : sert à choisir le plancher.</summary>
    public string CategoryKey { get; init; } = "services";
}

/// <summary>Tout ce qu'il faut pour proposer un prix : la demande, la grille, les planchers.</summary>
public sealed record QuoteContext(
    string CompanyName,
    string CompanyPhone,
    QuoteLeadFacts Lead,
    IReadOnlyList<PriceGridItem> Grid,
    IReadOnlyDictionary<string, decimal> FloorHourlyRates);

/// <summary>
/// Fourni par l'hôte (Hub.Web), qui assemble la demande (module Demandes) et la
/// grille (module Catalogue) : ce module ne dépend ainsi d'aucun autre.
/// </summary>
public interface IQuoteContextSource
{
    Task<QuoteContext?> GetAsync(Guid leadId, CancellationToken ct = default);
}
