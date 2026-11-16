using System.Globalization;
using Hub.Modules.Channels.Domain;
using Hub.SharedKernel.Time;

namespace Hub.Modules.Channels.Application.Reels;

/// <summary>
/// Choisit quelles publications préparées par le pilote deviennent des Reels : <c>perWeek</c> par semaine,
/// réparties dans la semaine ; la première de la semaine est générée par l'IA (si aucune ne l'est déjà), les autres montées.
/// </summary>
public static class ReelPlanner
{
    public static string WeekOf(DateTimeOffset at)
    {
        var local = TimeZoneInfo.ConvertTime(at, BusinessTime.Zone).DateTime;
        return $"{ISOWeek.GetYear(local)}-{ISOWeek.GetWeekOfYear(local):00}";
    }

    /// <param name="slots">Heures des publications à préparer, dans l'ordre.</param>
    /// <param name="existing">Reels déjà prévus par semaine (total, dont générés par l'IA).</param>
    /// <returns>Index de la publication → type de Reel.</returns>
    public static IReadOnlyDictionary<int, string> Assign(IReadOnlyList<DateTimeOffset> slots, int perWeek,
        IReadOnlyDictionary<string, (int Total, int Ai)> existing, bool aiAllowed)
    {
        var result = new Dictionary<int, string>();
        if (perWeek <= 0) return result;
        foreach (var week in slots.Select((at, i) => (Week: WeekOf(at), Index: i)).GroupBy(x => x.Week))
        {
            var (total, ai) = existing.GetValueOrDefault(week.Key);
            var indexes = week.Select(x => x.Index).ToList();
            var needed = Math.Min(perWeek - total, indexes.Count);
            for (var k = 0; k < needed; k++)
            {
                // Réparties dans la semaine : le milieu de chaque tranche.
                var index = indexes[(int)((k + 0.5) * indexes.Count / needed)];
                var kind = aiAllowed && ai == 0 ? ReelKinds.Ai : ReelKinds.Montage;
                if (kind == ReelKinds.Ai) ai++;
                result[index] = kind;
            }
        }
        return result;
    }
}
