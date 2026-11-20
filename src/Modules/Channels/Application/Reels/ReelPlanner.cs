using Hub.Modules.Channels.Domain;
using Hub.SharedKernel.Time;

namespace Hub.Modules.Channels.Application.Reels;

/// <summary>Une publication à préparer : son heure et son format créatif.</summary>
public sealed record ReelSlot(DateTimeOffset At, string? Format);

/// <summary>Média tiré pour une publication : « Image » ou un type de Reel, avec sa probabilité.</summary>
public sealed record MediaChoice(string Kind, double Propensity)
{
    public const string Image = "Image";
    public bool IsReel => Kind != Image;
}

/// <summary>Ce que le pilote peut faire en Reel en ce moment.</summary>
/// <param name="AiReels">Nombre de Reels IA que le budget du mois permet encore.</param>
/// <param name="StockAvailable">Vidéos d'archives (Pexels) configurées.</param>
public sealed record ReelCapacity(int AiReels, bool StockAvailable);

/// <summary>
/// Choisit au hasard quelles publications du pilote deviennent des Reels, et de quel type, en notant la probabilité
/// de chaque choix (pour que Kairos compare images et types de Reels sans biais).
/// Règles : au moins <c>share</c> des publications en Reels, jamais plus d'un Reel par jour (le plafond l'emporte).
/// </summary>
public static class ReelPlanner
{
    /// <summary>Poids des types de Reels tirés par le pilote (l'avant/après vient de vos propres vidéos).</summary>
    public static readonly (string Kind, double Weight)[] Weights =
    [
        (ReelKinds.Ai, 0.20), (ReelKinds.Cinematic, 0.15), (ReelKinds.Stock, 0.35), (ReelKinds.Montage, 0.30),
    ];

    /// <summary>
    /// Formats qui montrent un résultat ou l'équipe : la vidéo doit venir de la publication elle-même (images animées),
    /// jamais d'archives ou de l'IA, qui ne montrent pas notre travail.
    /// </summary>
    public static readonly HashSet<string> ProofFormats = ["avant-apres", "coulisses", "portrait"];

    public static DateOnly DayOf(DateTimeOffset at) => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(at, BusinessTime.Zone).DateTime);

    /// <param name="slots">Publications à préparer, dans l'ordre.</param>
    /// <param name="sharePercent">Part minimale de Reels, en pour cent.</param>
    /// <param name="reelDays">Jours (heure locale) qui ont déjà un Reel prévu.</param>
    /// <returns>Un choix par publication, dans l'ordre.</returns>
    public static IReadOnlyList<MediaChoice> Assign(IReadOnlyList<ReelSlot> slots, int sharePercent, IReadOnlySet<DateOnly> reelDays,
        ReelCapacity capacity, Random random, int maxPerDay = 1)
    {
        var result = Enumerable.Repeat(new MediaChoice(MediaChoice.Image, 1), slots.Count).ToArray();
        if (slots.Count == 0 || sharePercent <= 0 || maxPerDay <= 0) return result;

        // 1. Combien de Reels : la part minimale (arrondie vers le haut), moins ceux déjà prévus ces jours-là.
        var byDay = slots.Select((s, i) => (Day: DayOf(s.At), Index: i)).GroupBy(x => x.Day).ToList();
        var already = byDay.Count(g => reelDays.Contains(g.Key));
        var target = (int)Math.Ceiling(slots.Count * Math.Min(sharePercent, 100) / 100.0) - already;
        var free = byDay.Where(g => !reelDays.Contains(g.Key)).ToList();
        var count = Math.Clamp(target, 0, free.Count);
        if (count == 0) return result;

        // 2. Quels jours : tirés au hasard (probabilité count / jours libres), puis une publication du jour au hasard.
        var chosenDays = free.OrderBy(_ => random.NextDouble()).Take(count).Select(g => g.Key).ToHashSet();
        var aiLeft = capacity.AiReels;
        foreach (var day in free)
        {
            var dayShare = (double)count / free.Count;
            var indexes = day.Select(x => x.Index).ToList();
            var pick = chosenDays.Contains(day.Key) ? indexes[random.Next(indexes.Count)] : -1;
            foreach (var i in indexes)
            {
                var reelChance = dayShare / indexes.Count;
                if (i != pick)
                {
                    result[i] = new MediaChoice(MediaChoice.Image, Math.Round(1 - reelChance, 4));
                    continue;
                }
                // 3. Quel type : tiré selon les poids, parmi ceux permis (budget IA, archives, format).
                var allowed = Allowed(slots[i].Format, aiLeft > 0, capacity.StockAvailable);
                var total = allowed.Sum(k => k.Weight);
                var roll = random.NextDouble() * total;
                var kind = allowed[^1];
                foreach (var k in allowed)
                {
                    if ((roll -= k.Weight) < 0) { kind = k; break; }
                }
                if (ReelKinds.IsAi(kind.Kind)) aiLeft--;
                result[i] = new MediaChoice(kind.Kind, Math.Round(reelChance * kind.Weight / total, 4));
            }
        }
        return result;
    }

    internal static List<(string Kind, double Weight)> Allowed(string? format, bool ai, bool stock)
    {
        if (format is not null && ProofFormats.Contains(format)) return [(ReelKinds.Montage, 1)];
        return Weights.Where(w => w.Kind switch
        {
            ReelKinds.Ai or ReelKinds.Cinematic => ai,
            ReelKinds.Stock => stock,
            _ => true,
        }).ToList();
    }
}
