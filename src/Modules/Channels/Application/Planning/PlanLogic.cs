using System.Text.Json;
using Hub.Modules.Channels.Domain;

namespace Hub.Modules.Channels.Application.Planning;

/// <summary>Modèles de plan prêts à l'emploi (modifiables ensuite dans le back-office).</summary>
public static class PlanTemplates
{
    public const int DefaultWeeks = 13;

    /// <summary>Thème de saison (Québec) pour une entreprise de services à domicile, mois par mois.</summary>
    public static string SeasonTheme(int month) => month switch
    {
        1 => "Repartir à neuf après les Fêtes ; crédit d'impôt pour le maintien à domicile des aînés",
        2 => "Hiver : entrées, abrasifs et sel ; garder la maison saine quand on vit fenêtres fermées",
        3 => "Préparer le grand ménage du printemps",
        4 => "Grand ménage du printemps : fenêtres, placards, garages",
        5 => "Déménagements et fin de bail qui approchent",
        6 => "Fin des classes, déménagements, vacances : partir l'esprit tranquille",
        7 => "1er juillet : ménage de fin et de début de bail ; été sans corvées",
        8 => "Rentrée : remettre la maison en ordre et reprendre une routine",
        9 => "Rentrée et routines d'automne ; aînés : rester bien chez soi",
        10 => "Préparer la maison pour l'hiver ; Halloween",
        11 => "Préparer l'hiver et le grand ménage avant les Fêtes",
        12 => "Recevoir pour les Fêtes ; ne pas laisser les aînés seuls",
        _ => "Saison en cours",
    };

    public static IReadOnlyList<PlanPillar> DefaultPillars { get; } =
    [
        new("Conseiller", "Astuces et conseils d'entretien utiles même sans nous engager : ce que les gens enregistrent et partagent.", 35),
        new("Montrer", "Avant/après, coulisses, l'équipe au travail : on voit le résultat.", 25),
        new("Rassurer", "L'équipe, les avis de clients, les assurances et le sérieux : pourquoi nous faire confiance.", 20),
        new("Proposer", "Un service ou une offre précise, avec l'invitation à demander un devis.", 20),
    ];

    /// <summary>Plan « visibilité » de 13 semaines : se faire connaître, accélérer, amplifier.</summary>
    public static (IReadOnlyList<PlanPhase> Phases, IReadOnlyList<PlanPillar> Pillars, DateOnly EndsOn) Visibility(DateOnly start, int weeks = DefaultWeeks)
    {
        weeks = Math.Clamp(weeks, 6, 52);
        // Ex. 13 semaines : 4, 4 et 5 (la dernière étape prend le reste).
        var a = weeks / 3;
        var b = weeks / 3;
        var c = weeks - a - b;
        var p1 = start;
        var p2 = p1.AddDays(7 * a);
        var p3 = p2.AddDays(7 * b);
        var end = p3.AddDays(7 * c - 1);
        IReadOnlyList<PlanPhase> phases =
        [
            new("Se faire connaître", p1, p2.AddDays(-1),
                "Publier avec régularité, présenter l'équipe et les services, lancer Instagram, inviter à s'abonner.", SeasonTheme(Mid(p1, p2).Month)),
            new("Accélérer", p2, p3.AddDays(-1),
                "Refaire davantage ce qui a atteint le plus de monde ; contenus utiles à partager et à enregistrer.", SeasonTheme(Mid(p2, p3).Month)),
            new("Amplifier", p3, end,
                "Mettre en avant les meilleures publications (boost avec accord et plafond), inviter à commenter et à partager.", SeasonTheme(Mid(p3, end).Month)),
        ];
        return (phases, DefaultPillars, end);
    }

    private static DateOnly Mid(DateOnly a, DateOnly b) => a.AddDays((b.DayNumber - a.DayNumber) / 2);
}

/// <summary>Règles de calcul du plan : étape en cours, piliers à placer, cibles, trajectoire, ajustements.</summary>
public static class PlanLogic
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static IReadOnlyList<PlanPhase> Phases(string? json) => Read<List<PlanPhase>>(json) ?? [];
    public static IReadOnlyList<PlanPillar> Pillars(string? json) => Read<List<PlanPillar>>(json) ?? [];
    public static PlanNumbers? Numbers(string? json) => Read<PlanNumbers>(json);
    public static string Write<T>(T value) => JsonSerializer.Serialize(value, Json);

    private static T? Read<T>(string? json) where T : class
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try { return JsonSerializer.Deserialize<T>(json, Json); }
        catch (JsonException) { return null; }
    }

    public static PlanPhase? PhaseAt(IReadOnlyList<PlanPhase> phases, DateOnly day) =>
        phases.FirstOrDefault(p => p.From <= day && day <= p.To);

    /// <summary>
    /// Pilier de chaque nouvelle publication, pour se rapprocher de la répartition visée :
    /// à chaque créneau, le pilier le plus « en retard » sur sa part (méthode des plus forts restes).
    /// </summary>
    public static IReadOnlyList<string> AssignPillars(IReadOnlyList<PlanPillar> pillars, IReadOnlyDictionary<string, int> alreadyPublished, int slots)
    {
        var usable = pillars.Where(p => p.Share > 0).ToList();
        if (usable.Count == 0 || slots <= 0) return [];
        var counts = usable.ToDictionary(p => p.Name, p => alreadyPublished.GetValueOrDefault(p.Name));
        var total = counts.Values.Sum();
        var shareSum = usable.Sum(p => p.Share);
        var result = new List<string>();
        for (var i = 0; i < slots; i++)
        {
            total++;
            var pick = usable
                .OrderByDescending(p => p.Share / (double)shareSum * total - counts[p.Name])
                .ThenByDescending(p => p.Share)
                .First();
            counts[pick.Name]++;
            result.Add(pick.Name);
        }
        return result;
    }

    /// <summary>Cibles proposées pour un plan « visibilité » à partir du point de départ.</summary>
    public static PlanNumbers ProposeTargets(PlanNumbers b) => new(
        WeeklyReach: Math.Max(b.WeeklyReach * 2, b.WeeklyReach + 300),
        Followers: b.Followers + Math.Max(30, (int)Math.Ceiling(b.Followers * 0.25)),
        EngagementRate: Math.Round(Math.Max(b.EngagementRate, 2.0), 1));

    public const string TargetsRule =
        "Personnes atteintes par semaine × 2 (au moins +300), abonnés +25 % (au moins +30), taux d'interaction au moins égal au départ et à 2 %.";

    /// <summary>Où l'on devrait être à cette date, en ligne droite entre le départ et les cibles.</summary>
    public static PlanNumbers Expected(PlanNumbers baseline, PlanNumbers targets, DateOnly from, DateOnly to, DateOnly day)
    {
        var span = Math.Max(1, to.DayNumber - from.DayNumber);
        var f = Math.Clamp((day.DayNumber - from.DayNumber) / (double)span, 0, 1);
        return new PlanNumbers(
            (int)Math.Round(baseline.WeeklyReach + (targets.WeeklyReach - baseline.WeeklyReach) * f),
            (int)Math.Round(baseline.Followers + (targets.Followers - baseline.Followers) * f),
            Math.Round(baseline.EngagementRate + (targets.EngagementRate - baseline.EngagementRate) * f, 1));
    }

    /// <summary>Taux d'interaction en % (interactions / personnes atteintes).</summary>
    public static double EngagementRate(int interactions, int reach) => reach <= 0 ? 0 : Math.Round(interactions * 100.0 / reach, 1);

    /// <summary>
    /// Ajustement de fin d'étape : 5 points de plus pour le pilier qui atteint le plus de monde par publication,
    /// 5 de moins pour celui qui en atteint le moins (jamais sous 10 %). Null si les données sont insuffisantes.
    /// </summary>
    public static (IReadOnlyList<PlanPillar> Pillars, string Note)? ProposePillars(
        IReadOnlyList<PlanPillar> pillars, IReadOnlyDictionary<string, (int Posts, double AvgReach)> results)
    {
        var measured = pillars
            .Where(p => results.TryGetValue(p.Name, out var r) && r.Posts >= 2)
            .Select(p => (Pillar: p, Avg: results[p.Name].AvgReach))
            .ToList();
        if (measured.Count < 2) return null;
        var best = measured.MaxBy(x => x.Avg);
        var worst = measured.MinBy(x => x.Avg);
        if (best.Pillar.Name == worst.Pillar.Name || best.Avg <= worst.Avg * 1.15) return null; // écart trop faible pour conclure
        var shift = Math.Min(5, worst.Pillar.Share - 10);
        if (shift <= 0) return null;
        var updated = pillars.Select(p =>
            p.Name == best.Pillar.Name ? p with { Share = p.Share + shift }
            : p.Name == worst.Pillar.Name ? p with { Share = p.Share - shift }
            : p).ToList();
        var note = $"« {best.Pillar.Name} » atteint en moyenne {best.Avg:0} personnes par publication, " +
                   $"« {worst.Pillar.Name} » {worst.Avg:0}. Proposition : {best.Pillar.Name} {best.Pillar.Share} → {best.Pillar.Share + shift} %, " +
                   $"{worst.Pillar.Name} {worst.Pillar.Share} → {worst.Pillar.Share - shift} %.";
        return (updated, note);
    }

    /// <summary>Vérifie une répartition saisie : des noms, des parts entre 0 et 100, total de 100.</summary>
    public static string? ValidatePillars(IReadOnlyList<PlanPillar> pillars)
    {
        if (pillars.Count is < 2 or > 6) return "Entre 2 et 6 piliers.";
        if (pillars.Any(p => string.IsNullOrWhiteSpace(p.Name))) return "Chaque pilier a besoin d'un nom.";
        if (pillars.Select(p => p.Name.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).Count() != pillars.Count) return "Deux piliers portent le même nom.";
        if (pillars.Any(p => p.Share is < 0 or > 100)) return "Chaque part est entre 0 et 100 %.";
        var sum = pillars.Sum(p => p.Share);
        return sum == 100 ? null : $"Les parts doivent faire 100 % (actuellement {sum} %).";
    }
}
