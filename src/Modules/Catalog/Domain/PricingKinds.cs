using System.Globalization;

namespace Hub.Modules.Catalog.Domain;

/// <summary>
/// Comportement d'un type de tarification. Ajouter un type = ajouter une valeur à
/// <see cref="PricingKind"/> et une classe ici : aucun écran ni service à modifier.
/// </summary>
public abstract class PricingKindDefinition
{
    protected static readonly CultureInfo Fr = CultureInfo.GetCultureInfo("fr-CA");

    public abstract PricingKind Kind { get; }

    /// <summary>Libellé dans le back-office (liste déroulante, tableau).</summary>
    public abstract string AdminLabel { get; }

    /// <summary>Le montant est-il un taux horaire (sert à l'estimateur du crédit d'impôt) ?</summary>
    public virtual bool IsHourlyRate => false;

    /// <summary>Texte du prix affiché sur la page publique du service.</summary>
    public abstract string PublicText(PricingModel p);

    protected static string Amount(decimal? v) => v is { } d ? d.ToString("0.## $", Fr) : "";

    protected static string Range(PricingModel p) =>
        p.MinAmount is not null && p.MaxAmount is not null && p.MaxAmount != p.MinAmount
            ? $"de {Amount(p.MinAmount)} à {Amount(p.MaxAmount)}"
            : $"à partir de {Amount(p.MinAmount ?? p.MaxAmount)}";
}

public static class PricingKinds
{
    private static readonly IReadOnlyDictionary<PricingKind, PricingKindDefinition> Definitions =
        new PricingKindDefinition[] { new OnAssessmentPricing(), new HourlyPricing(), new PerShiftPricing(), new FlatPricing(), new MonthlyPricing() }
            .ToDictionary(d => d.Kind);

    public static IReadOnlyCollection<PricingKindDefinition> All => Definitions.Values.ToList();

    /// <summary>Définition du type ; un type inconnu est traité « sur évaluation ».</summary>
    public static PricingKindDefinition Of(PricingKind kind) =>
        Definitions.TryGetValue(kind, out var d) ? d : Definitions[PricingKind.OnAssessment];
}

internal sealed class OnAssessmentPricing : PricingKindDefinition
{
    public override PricingKind Kind => PricingKind.OnAssessment;
    public override string AdminLabel => "Sur évaluation";
    public override string PublicText(PricingModel p) => "Prix établi après une courte évaluation, sans frais.";
}

internal sealed class HourlyPricing : PricingKindDefinition
{
    public override PricingKind Kind => PricingKind.Hourly;
    public override string AdminLabel => "À l'heure";
    public override bool IsHourlyRate => true;
    public override string PublicText(PricingModel p) =>
        $"{Range(p)} l'heure" + (p.MinimumHours is { } h ? $" (minimum {h.ToString("0.#", Fr)} h)" : "");
}

/// <summary>Taux horaire facturé par quart complet (ex. 40 $/h × 8 h = 320 $).</summary>
internal sealed class PerShiftPricing : PricingKindDefinition
{
    public override PricingKind Kind => PricingKind.PerShift;
    public override string AdminLabel => "Par quart (taux horaire)";
    public override bool IsHourlyRate => true;
    public override string PublicText(PricingModel p)
    {
        if (p.ShiftHours is not { } hours || p.MinAmount is not { } rate)
            return $"{Range(p)} l'heure, par quart";
        return $"{Range(p)} l'heure, par quart de {hours.ToString("0.#", Fr)} h (soit {Amount(rate * hours)} le quart)";
    }
}

internal sealed class FlatPricing : PricingKindDefinition
{
    public override PricingKind Kind => PricingKind.Flat;
    public override string AdminLabel => "Forfait";
    public override string PublicText(PricingModel p) => Range(p);
}

internal sealed class MonthlyPricing : PricingKindDefinition
{
    public override PricingKind Kind => PricingKind.Monthly;
    public override string AdminLabel => "Par mois";
    public override string PublicText(PricingModel p) => $"{Range(p)} par mois";
}
