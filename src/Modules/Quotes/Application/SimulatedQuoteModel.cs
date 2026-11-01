using System.Globalization;
using System.Text.RegularExpressions;
using Hub.Modules.Quotes.Domain;

namespace Hub.Modules.Quotes.Application;

/// <summary>
/// Modèle sans IA, utilisé tant qu'aucune clé n'est configurée (et dans les tests) :
/// heures estimées d'après la superficie, prix tirés de la grille.
/// </summary>
internal sealed partial class SimulatedQuoteModel : IQuoteModel
{
    public string Name => "simulé";

    public Task<ModelQuote> ProposeAsync(QuoteContext context, CancellationToken ct = default)
    {
        var lead = context.Lead;
        var hours = EstimateHours(lead.PropertySize);
        var lines = new List<QuoteLine>();
        var assumptions = new List<string>();

        var requested = lead.Services.Count > 0 ? lead.Services : ["Nettoyage résidentiel"];
        foreach (var name in requested)
        {
            var item = context.Grid.FirstOrDefault(g => string.Equals(g.Service, name, StringComparison.OrdinalIgnoreCase));
            if (item is null)
            {
                assumptions.Add($"« {name} » n'est pas dans la grille : à évaluer.");
                continue;
            }

            var line = QuotePricingRules.For(item.Kind).SimulatedLine(item, hours);
            if (line is not null)
            {
                lines.Add(line);
            }
            else
            {
                lines.Add(new QuoteLine($"{item.Service} : visite d'évaluation (gratuite)", 1, "visite", 0, item.Service));
                assumptions.Add($"« {item.Service} » se chiffre après une visite d'évaluation.");
            }
        }

        if (lead.PropertySize is null)
            assumptions.Add($"Superficie inconnue : {hours:0.#} h estimées par défaut.");

        var message =
            $"Bonjour {FirstName(lead.CustomerName)},\n\n" +
            "Merci d'avoir pensé à nous. Voici notre proposition pour votre demande, " +
            "établie à partir des informations que vous nous avez transmises.\n\n" +
            "Le prix final sera confirmé avec vous avant la première visite. " +
            "N'hésitez pas à nous écrire ou à nous appeler pour toute question.\n\n" +
            $"Au plaisir de vous servir,\nL'équipe {context.CompanyName}";

        return Task.FromResult(new ModelQuote(
            hours, lines,
            $"Estimation simulée (sans IA) : {hours:0.#} h d'après la superficie indiquée, tarifs de la grille interne.",
            assumptions,
            lead.PropertySize is null ? ["Quelle est la superficie approximative du logement ?"] : [],
            message, "faible", Name));
    }

    /// <summary>Environ 1 h par 500 pi², entre 3 h et 8 h ; 3 h si la superficie est inconnue.</summary>
    internal static decimal EstimateHours(string? size)
    {
        if (string.IsNullOrWhiteSpace(size)) return 3;
        var digits = Number().Match(size.Replace(" ", "").Replace(" ", "").Replace(" ", ""));
        if (!digits.Success || !decimal.TryParse(digits.Value, NumberStyles.Number, CultureInfo.InvariantCulture, out var sqft) || sqft <= 0)
            return 3;
        if (size.Contains("m2", StringComparison.OrdinalIgnoreCase) || size.Contains("m²", StringComparison.OrdinalIgnoreCase))
            sqft *= 10.764m;
        var hours = Math.Ceiling(sqft / 500m * 2) / 2;
        return Math.Clamp(hours, 3, 8);
    }

    private static string FirstName(string fullName) =>
        fullName.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? fullName;

    [GeneratedRegex(@"\d+")]
    private static partial Regex Number();
}
