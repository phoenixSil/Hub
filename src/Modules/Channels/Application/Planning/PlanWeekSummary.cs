using Hub.Modules.Channels.Domain;
using Hub.SharedKernel.Time;

namespace Hub.Modules.Channels.Application.Planning;

/// <summary>
/// Le résumé de la semaine en cours du plan, pour le tableau de bord :
/// combien de publications prévues, combien déjà parties, combien à venir, et combien attendent votre accord.
/// </summary>
public sealed record PlanWeekSummary(
    string PlanName,
    int Week,
    int TotalWeeks,
    DateOnly From,
    DateOnly To,
    int Planned,
    int Published,
    int Upcoming,
    int ToApprove,
    int Missed)
{
    /// <summary>Résumé de la semaine du plan qui contient <paramref name="today"/> ; null si aucun plan n'est en cours.</summary>
    public static PlanWeekSummary? For(MarketingPlanView? plan, IEnumerable<PostView> posts, DateOnly today, DateTimeOffset now)
    {
        if (plan is not { Status: PlanStatus.Active } || today < plan.StartsOn || today > plan.EndsOn) return null;

        var week = Math.Max(1, plan.Week);
        var from = plan.StartsOn.AddDays(7 * (week - 1));
        var to = from.AddDays(6) > plan.EndsOn ? plan.EndsOn : from.AddDays(6);

        static DateOnly Local(DateTimeOffset at) => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(at, BusinessTime.Zone).DateTime);

        var inWeek = posts
            .Select(p => p.Post)
            .Where(p => (p.PublishedAt ?? p.ScheduledAt) is { } at && Local(at) >= from && Local(at) <= to)
            .ToList();

        var published = inWeek.Count(p => p.Status is PostStatus.Published or PostStatus.PartiallyPublished);
        var upcoming = inWeek.Count(p => (p.Status is PostStatus.Scheduled or PostStatus.Publishing or PostStatus.AwaitingApproval)
                                         && (p.ScheduledAt is null || p.ScheduledAt >= now.AddDays(-1)));
        var missed = inWeek.Count(p => p.Status is PostStatus.Failed
                                       || (p.Status is PostStatus.Cancelled && p.FromAutopilot));
        // À valider : toutes les publications du pilote qui attendent encore (pas seulement celles de la semaine).
        var toApprove = posts.Count(p => p.Post.Status == PostStatus.AwaitingApproval);

        return new PlanWeekSummary(plan.Name, week, plan.TotalWeeks, from, to,
            Math.Max(plan.PostsPerWeek, published + upcoming), published, upcoming, toApprove, missed);
    }
}
