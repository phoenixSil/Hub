using Hub.Modules.Quotes.Domain;

namespace Hub.Modules.Quotes.Application;

public sealed record GuardResult(IReadOnlyList<QuoteLine> Lines, decimal Total, IReadOnlyList<string> Warnings);

/// <summary>
/// Contrôle fait par le code, jamais délégué à l'IA : un devis ne descend pas sous
/// le coût de revient (plancher horaire) ni sous le minimum d'heures de la grille.
/// Les lignes trop basses sont relevées, et chaque correction est signalée.
/// </summary>
internal static class PricingGuard
{
    public static GuardResult Apply(ModelQuote quote, QuoteContext context)
    {
        var warnings = new List<string>();
        var lines = new List<QuoteLine>();

        foreach (var line in quote.Lines)
        {
            var current = line with
            {
                Quantity = Math.Max(0, Math.Round(line.Quantity, 2)),
                UnitPrice = Math.Max(0, Math.Round(line.UnitPrice, 2)),
            };
            var item = Match(current, context.Grid);
            var isHourly = current.Unit.StartsWith("heure", StringComparison.OrdinalIgnoreCase);

            if (item is null && isHourly && context.FloorHourlyRates.Count > 0)
            {
                // Ligne horaire hors grille : on applique le plancher le plus élevé, par prudence.
                var strictest = context.FloorHourlyRates.Values.Max();
                if (current.UnitPrice < strictest)
                {
                    warnings.Add($"{current.Description} : {current.UnitPrice:0.00} $/h sous le plancher de {strictest:0.00} $/h, relevé au plancher.");
                    current = current with { UnitPrice = strictest };
                }
            }
            else if (item is not null && isHourly)
            {
                if (item.MinimumHours is { } minHours && current.Quantity > 0 && current.Quantity < minHours)
                {
                    warnings.Add($"{current.Description} : {current.Quantity:0.##} h relevé au minimum de {minHours:0.##} h.");
                    current = current with { Quantity = minHours };
                }

                if (context.FloorHourlyRates.TryGetValue(item.CategoryKey, out var floor) && current.UnitPrice < floor)
                {
                    warnings.Add($"{current.Description} : {current.UnitPrice:0.00} $/h sous le plancher de {floor:0.00} $/h, relevé au plancher.");
                    current = current with { UnitPrice = floor };
                }
                else if (item.Kind == "Hourly" && item.MinAmount is { } gridRate && current.UnitPrice < gridRate)
                {
                    warnings.Add($"{current.Description} : {current.UnitPrice:0.00} $/h, sous le tarif de la grille ({gridRate:0.00} $/h).");
                }
            }

            lines.Add(current);
        }

        var hourlyTotal = lines.Where(l => l.Unit.StartsWith("heure", StringComparison.OrdinalIgnoreCase)).Sum(l => l.Quantity);
        var floorRate = context.FloorHourlyRates.Count > 0 ? context.FloorHourlyRates.Values.Min() : 0;
        var total = lines.Sum(l => l.Total);
        if (quote.EstimatedHours > 0 && floorRate > 0 && hourlyTotal == 0 && total > 0 && total < quote.EstimatedHours * floorRate)
            warnings.Add($"Le total ({total:0.00} $) couvre moins que {quote.EstimatedHours:0.#} h au plancher ({quote.EstimatedHours * floorRate:0.00} $) : à vérifier.");

        if (lines.Count == 0)
            warnings.Add("Aucune ligne de prix : à chiffrer à la main.");

        return new GuardResult(lines, Math.Round(total, 2), warnings);
    }

    private static PriceGridItem? Match(QuoteLine line, IReadOnlyList<PriceGridItem> grid) =>
        grid.FirstOrDefault(g => line.Service is not null && string.Equals(g.Service, line.Service, StringComparison.OrdinalIgnoreCase))
        ?? grid.Where(g => line.Description.Contains(g.Service, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(g => g.Service.Length)
            .FirstOrDefault();
}
