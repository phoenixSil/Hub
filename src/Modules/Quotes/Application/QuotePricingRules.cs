using Hub.Modules.Quotes.Domain;

namespace Hub.Modules.Quotes.Application;

/// <summary>
/// Comment l'assistant traite un type de tarification de la grille : libellé donné à
/// Claude et chiffrage du modèle simulé. Ajouter un type = ajouter une règle ici.
/// </summary>
internal abstract class QuotePricingRule
{
    /// <summary>Nom du type, tel que le catalogue le transmet (ex. « Hourly »).</summary>
    public abstract string Kind { get; }

    /// <summary>Description du type dans les consignes données à Claude.</summary>
    public abstract string PromptLabel { get; }

    /// <summary>Ligne chiffrée par le modèle simulé, ou null si le service doit être évalué sur place.</summary>
    public abstract QuoteLine? SimulatedLine(PriceGridItem item, decimal estimatedHours);
}

internal static class QuotePricingRules
{
    private static readonly IReadOnlyDictionary<string, QuotePricingRule> Rules =
        new QuotePricingRule[] { new OnAssessmentRule(), new HourlyRule(), new PerShiftRule(), new FlatRule(), new MonthlyRule() }
            .ToDictionary(r => r.Kind, StringComparer.OrdinalIgnoreCase);

    /// <summary>Règle du type ; un type inconnu est traité « sur évaluation ».</summary>
    public static QuotePricingRule For(string kind) =>
        Rules.TryGetValue(kind, out var rule) ? rule : Rules["OnAssessment"];
}

internal sealed class OnAssessmentRule : QuotePricingRule
{
    public override string Kind => "OnAssessment";
    public override string PromptLabel => "sur évaluation";
    public override QuoteLine? SimulatedLine(PriceGridItem item, decimal estimatedHours) => null;
}

internal sealed class HourlyRule : QuotePricingRule
{
    public override string Kind => "Hourly";
    public override string PromptLabel => "à l'heure";
    public override QuoteLine? SimulatedLine(PriceGridItem item, decimal estimatedHours) =>
        item.MinAmount is { } rate
            ? new QuoteLine($"{item.Service} : main-d'œuvre", Math.Max(estimatedHours, item.MinimumHours ?? 0), "heure", rate, item.Service)
            : null;
}

internal sealed class PerShiftRule : QuotePricingRule
{
    public override string Kind => "PerShift";
    public override string PromptLabel => "par quart (le montant est un taux horaire, facturé par quart complet)";
    public override QuoteLine? SimulatedLine(PriceGridItem item, decimal estimatedHours)
    {
        if (item.MinAmount is not { } rate) return null;
        var hours = item.ShiftHours ?? 8;
        return new QuoteLine($"{item.Service} : quart de {hours:0.#} h", 1, "quart", rate * hours, item.Service);
    }
}

internal sealed class FlatRule : QuotePricingRule
{
    public override string Kind => "Flat";
    public override string PromptLabel => "forfait";
    public override QuoteLine? SimulatedLine(PriceGridItem item, decimal estimatedHours)
    {
        if (item.MinAmount is not { } min) return null;
        var price = item.MaxAmount is { } max ? Math.Round((min + max) / 2, 0) : min;
        return new QuoteLine(item.Service, 1, "forfait", price, item.Service);
    }
}

internal sealed class MonthlyRule : QuotePricingRule
{
    public override string Kind => "Monthly";
    public override string PromptLabel => "abonnement mensuel";
    public override QuoteLine? SimulatedLine(PriceGridItem item, decimal estimatedHours) =>
        item.MinAmount is { } price ? new QuoteLine($"{item.Service} : abonnement", 1, "mois", price, item.Service) : null;
}
