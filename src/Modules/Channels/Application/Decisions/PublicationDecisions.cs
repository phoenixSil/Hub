using System.Globalization;
using System.Text.Json;
using Hub.Modules.Channels.Domain;
using Hub.SharedKernel.Time;

namespace Hub.Modules.Channels.Application.Decisions;

/// <summary>Réglages du journal des décisions (section « Decisions »).</summary>
public sealed class DecisionOptions
{
    public const string Section = "Decisions";

    /// <summary>Part des publications du pilote dont une dimension (le créneau) est tirée au hasard.</summary>
    public double ExplorationRate { get; set; } = 0.15;

    /// <summary>Fenêtre d'attribution : clôture du résultat après ce nombre de jours.</summary>
    public double CloseAfterDays { get; set; } = 7;

    /// <summary>Mise à jour finale (clients gagnés, qui arrivent plus tard).</summary>
    public double FinalizeAfterDays { get; set; } = 30;

    /// <summary>Poids de la récompense : r = Lead × demandes + Interaction × interactions + Reach × portée − coût.</summary>
    public double LeadWeight { get; set; } = 100;
    public double InteractionWeight { get; set; } = 1;
    public double ReachWeight { get; set; } = 0.01;

    /// <summary>Coût d'une photo générée par l'IA (même réglage que Gemini:CostPerImage par défaut).</summary>
    public decimal CostPerPhoto { get; set; } = 0.07m;
}

/// <summary>Résultat d'une publication venu du site : visites et demandes reliées à sa campagne (utm_campaign).</summary>
public sealed record CampaignOutcome(int Visits, int Leads, int Appointments, int Won);

/// <summary>Fournit visites et demandes par campagne (assemblé hors du module : statistiques et demandes).</summary>
public interface IDecisionOutcomeSource
{
    Task<IReadOnlyDictionary<string, CampaignOutcome>> GetAsync(IReadOnlyCollection<string> campaigns, CancellationToken ct = default);
}

/// <summary>
/// Adaptateur d'un domaine pour le noyau : comment décrire l'action et calculer la récompense.
/// Ajouter un domaine (tarification…) = ajouter un adaptateur, sans toucher au journal.
/// </summary>
public interface IDecisionAdapter
{
    string Domain { get; }
    double Reward(Decision decision, DecisionOptions options);
}

/// <summary>Adaptateur « Publications » : créneau, intensité, contexte, exploration et récompense.</summary>
public sealed class PublicationDecisions : IDecisionAdapter
{
    public const string Name = "Publications";
    public string Domain => Name;

    /// <summary>Heures (locales) des trois créneaux, pour l'exploration.</summary>
    public static readonly (string Name, TimeOnly Time)[] Slots =
    [
        ("Matin", new TimeOnly(9, 0)), ("Midi", new TimeOnly(12, 0)), ("Soir", new TimeOnly(18, 30)),
    ];

    /// <summary>Créneau d'une heure de publication : « Matin · semaine », « Soir · fin de semaine »…</summary>
    public static string Slot(DateTimeOffset at)
    {
        var local = TimeZoneInfo.ConvertTime(at, BusinessTime.Zone);
        var part = local.Hour < 11 ? "Matin" : local.Hour < 15 ? "Midi" : "Soir";
        var weekend = local.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday;
        return $"{part} · {(weekend ? "fin de semaine" : "semaine")}";
    }

    /// <summary>Intensité τ : 1 promotionnel, 0,5 incitatif (service), 0 informatif (conseil, saison, aînés…).</summary>
    public static double Intensity(string? theme) => theme?.Trim().ToLowerInvariant() switch
    {
        "promotion" or "promo" or "offre" => 1,
        "service" => 0.5,
        _ => 0,
    };

    public double Reward(Decision d, DecisionOptions o) => Math.Round(
        o.LeadWeight * d.Leads + o.InteractionWeight * d.Interactions + o.ReachWeight * d.Reach - (double)d.Cost, 2);

    /// <summary>
    /// Exploration : avec la probabilité <paramref name="rate"/>, le créneau de la journée est tiré au hasard parmi les trois
    /// (probabilité 1/3 chacun). Le jour ne change pas, et une heure déjà passée n'est jamais tirée.
    /// </summary>
    public static (DateTimeOffset At, bool Explored, double? Propensity) Explore(DateTimeOffset at, double rate, Random random, DateTimeOffset now)
    {
        if (rate <= 0 || random.NextDouble() >= rate) return (at, false, null);
        var day = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(at, BusinessTime.Zone).DateTime);
        var options = Slots.Select(s => BusinessTime.FromLocal(day.ToDateTime(s.Time))).Where(t => t > now.AddMinutes(30)).ToList();
        if (options.Count < 2) return (at, false, null);
        return (options[random.Next(options.Count)], true, 1.0 / options.Count);
    }

    /// <summary>Contexte (x) et état (s) au moment de la décision.</summary>
    public static string Context(DateTimeOffset at, int postsPerWeek, bool intensive, IReadOnlyDictionary<string, int> followers,
        int sameAudienceLast7Days, int promotionsLast7Days)
    {
        var local = TimeZoneInfo.ConvertTime(at, BusinessTime.Zone);
        var date = DateOnly.FromDateTime(local.DateTime);
        return JsonSerializer.Serialize(new
        {
            mois = local.Month,
            saison = Season(local.Month),
            semaine = ISOWeek.GetWeekOfYear(local.DateTime),
            jour = CultureInfo.GetCultureInfo("fr-CA").DateTimeFormat.GetDayName(local.DayOfWeek),
            finDeSemaine = local.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday,
            fete = Holiday(date),
            rythmeParSemaine = postsPerWeek,
            intensif = intensive,
            abonnes = followers,
            lassitudeMemeCible7j = sameAudienceLast7Days,
            promotions7j = promotionsLast7Days,
        }, Json);
    }

    private static readonly JsonSerializerOptions Json = new() { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    public static string Season(int month) => month switch
    {
        12 or 1 or 2 => "hiver", 3 or 4 or 5 => "printemps", 6 or 7 or 8 => "été", _ => "automne",
    };

    /// <summary>Période particulière au Québec (dans les 10 jours qui précèdent), sinon null.</summary>
    public static string? Holiday(DateOnly d)
    {
        (string Name, DateOnly Date)[] days =
        [
            ("Noël", new DateOnly(d.Year, 12, 25)), ("Jour de l'An", new DateOnly(d.Year, 1, 1)),
            ("Saint-Valentin", new DateOnly(d.Year, 2, 14)), ("Fête nationale", new DateOnly(d.Year, 6, 24)),
            ("Déménagements", new DateOnly(d.Year, 7, 1)), ("Halloween", new DateOnly(d.Year, 10, 31)),
            ("Jour de l'An", new DateOnly(d.Year + 1, 1, 1)),
        ];
        return days.FirstOrDefault(h => h.Date.DayNumber - d.DayNumber is >= 0 and <= 10).Name;
    }
}
