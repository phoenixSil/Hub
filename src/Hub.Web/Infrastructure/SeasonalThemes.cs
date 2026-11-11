using Hub.Modules.Platform.Domain;

namespace Hub.Web.Infrastructure;

/// <summary>
/// Un habillage saisonnier du site. L'identité ScintiNet reste dominante (bleu, vert, logo) :
/// la saison n'est qu'un accessoire (le « S » du logo se déguise) et une touche de couleur décorative.
/// </summary>
/// <param name="Key">« halloween » ou « noel » (classe CSS et paramètre d'aperçu).</param>
public sealed record SeasonalTheme(
    string Key,
    string Name,
    string Bar,
    string Eyebrow,
    string Title,
    string Highlight,
    string Lead,
    string Cta,
    string CtaUrl,
    (int Month, int Day) From,
    (int Month, int Day) To,
    // Couleurs de lecture : chaque paire fond / texte respecte le contraste AA (4,5:1), vérifié par les tests.
    string BarBackground,
    string BarText,
    string SlideBackground,
    string SlideText);

public static class SeasonalThemes
{
    public const string Auto = "auto", None = "aucun";

    public static readonly SeasonalTheme Halloween = new(
        "halloween", "Halloween",
        Bar: "Après l'Halloween, on fait disparaître les dégâts, sans sortilège",
        Eyebrow: "Halloween",
        Title: "Le ménage d'après fête,", Highlight: "ensorcelé",
        Lead: "Bonbons écrasés, décorations, maquillage sur les tissus : on fait tout briller.",
        Cta: "Demander un devis", CtaUrl: "/devis",
        From: (10, 15), To: (11, 2),
        BarBackground: "#e2e735", BarText: "#0b3954",
        SlideBackground: "#0b3954", SlideText: "#ffffff");

    public static readonly SeasonalTheme Christmas = new(
        "noel", "Noël",
        Bar: "Grand ménage avant les Fêtes : réservez tôt, les places partent vite",
        Eyebrow: "Temps des Fêtes",
        Title: "Un Noël qui brille,", Highlight: "sans la corvée",
        Lead: "Avant le réveillon et après : recevez l'esprit tranquille. Offrez aussi du temps à vos parents.",
        Cta: "Réserver mon ménage des Fêtes", CtaUrl: "/devis",
        From: (11, 20), To: (1, 6),
        BarBackground: "#007aa8", BarText: "#ffffff",
        SlideBackground: "#007aa8", SlideText: "#ffffff");

    public static IReadOnlyList<SeasonalTheme> All { get; } = [Halloween, Christmas];

    public static SeasonalTheme? Find(string? key) => All.FirstOrDefault(t => t.Key == key);

    /// <summary>
    /// Le thème à afficher : l'aperçu demandé (« ?theme=noel »), sinon le réglage de l'entreprise
    /// (« auto » = selon les dates, « aucun », ou un thème forcé).
    /// </summary>
    public static SeasonalTheme? Resolve(string? setting, DateOnly today, string? preview = null)
    {
        if (preview is not null) return preview == None ? null : Find(preview) ?? ResolveSetting(setting, today);
        return ResolveSetting(setting, today);
    }

    private static SeasonalTheme? ResolveSetting(string? setting, DateOnly today) => setting switch
    {
        None => null,
        null or "" or Auto => All.FirstOrDefault(t => IsInSeason(t, today)),
        _ => Find(setting),
    };

    /// <summary>Vrai si la date tombe dans la période du thème (la période peut chevaucher le Nouvel An).</summary>
    public static bool IsInSeason(SeasonalTheme t, DateOnly day)
    {
        var d = day.Month * 100 + day.Day;
        var from = t.From.Month * 100 + t.From.Day;
        var to = t.To.Month * 100 + t.To.Day;
        return from <= to ? d >= from && d <= to : d >= from || d <= to;
    }

    public static string Period(SeasonalTheme t)
    {
        var fr = System.Globalization.CultureInfo.GetCultureInfo("fr-CA");
        string D((int M, int D) x) => new DateOnly(2026, x.M, x.D).ToString("d MMMM", fr);
        return $"du {D(t.From)} au {D(t.To)}";
    }
}

/// <summary>Le thème saisonnier de la page en cours (réglage de l'entreprise, date du jour, aperçu demandé).</summary>
public static class SeasonalThemeContext
{
    public static SeasonalTheme? For(Tenant tenant, TimeProvider clock, Microsoft.AspNetCore.Components.NavigationManager nav)
    {
        var query = Microsoft.AspNetCore.WebUtilities.QueryHelpers.ParseQuery(new Uri(nav.Uri).Query);
        var preview = query.TryGetValue("theme", out var v) ? v.ToString().ToLowerInvariant() : null;
        return SeasonalThemes.Resolve(tenant.SeasonalTheme, Hub.SharedKernel.Time.BusinessTime.Today(clock), preview);
    }
}
