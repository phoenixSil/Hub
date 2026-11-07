using Hub.Modules.Analytics.Application;
using Hub.Modules.Channels;
using Hub.Modules.Channels.Application;
using Hub.Modules.Leads.Contracts;
using Hub.Modules.Leads.Application;
using Hub.Modules.Leads.Domain;

namespace Hub.Web.Infrastructure;

/// <summary>Résultats d'une publication : visites par réseau, demandes, rendez-vous, clients gagnés.</summary>
public sealed record PostResult(
    PublishedPost Post,
    int Visits,
    int Visitors,
    IReadOnlyDictionary<string, int> VisitsBySource,
    int Leads,
    int Appointments,
    int Won)
{
    public double Conversion => Visits == 0 ? 0 : 100.0 * Leads / Visits;
}

/// <summary>Une ligne de comparaison (un réseau, un public, un type de sujet ou de visuel).</summary>
public sealed record ResultGroup(string Label, int Posts, int Visits, int Leads, int Won)
{
    public double VisitsPerPost => Posts == 0 ? 0 : (double)Visits / Posts;
    public double LeadsPerPost => Posts == 0 ? 0 : (double)Leads / Posts;
    public double Conversion => Visits == 0 ? 0 : 100.0 * Leads / Visits;
}

public sealed record MarketingReport(
    int Days,
    DateTimeOffset Since,
    IReadOnlyList<PostResult> Posts,
    IReadOnlyList<ResultGroup> ByChannel,
    IReadOnlyList<ResultGroup> ByAudience,
    IReadOnlyList<ResultGroup> ByTheme,
    IReadOnlyList<ResultGroup> ByVisual,
    IReadOnlyList<ResultGroup> ByOrigin,
    int BioVisits,
    int BioLeads,
    int PhotosThisMonth,
    decimal PhotoCostThisMonth)
{
    public int PostCount => Posts.Count;
    public int Visits => Posts.Sum(p => p.Visits) + BioVisits;
    public int Leads => Posts.Sum(p => p.Leads) + BioLeads;
    public int Appointments => Posts.Sum(p => p.Appointments);
    public int Won => Posts.Sum(p => p.Won);
    public PostResult? Best => Posts.Where(p => p.Leads > 0 || p.Visits > 0)
        .OrderByDescending(p => p.Leads).ThenByDescending(p => p.Visits).FirstOrDefault();
}

public interface IMarketingResults
{
    Task<MarketingReport> GetAsync(int days, CancellationToken ct = default);
}

/// <summary>
/// Assemble les résultats du marketing à partir des publications (Canaux), des visites mesurées (Statistiques)
/// et des demandes (Demandes) : chaque lien publié porte sa campagne et son réseau (utm_campaign, utm_source).
/// </summary>
public sealed class MarketingResults(
    IPostService posts,
    IDashboardQueries stats,
    ILeadAttribution leads,
    IImageQuota quota,
    IConfiguration configuration,
    TimeProvider clock) : IMarketingResults
{
    /// <summary>Campagne du lien de la bio Instagram (à mettre dans le profil, voir la page Résultats).</summary>
    public const string BioCampaign = "bio";

    public async Task<MarketingReport> GetAsync(int days, CancellationToken ct = default)
    {
        var since = clock.GetUtcNow().AddDays(-days);
        var published = await posts.ListPublishedAsync(since, ct);
        var campaigns = published.Select(p => p.Campaign).Append(BioCampaign).Distinct().ToList();

        var views = await stats.GetCampaignViewsAsync(campaigns, ct);
        var origins = await leads.ListForCampaignsAsync(campaigns, ct);

        var results = published.Select(p =>
        {
            var v = views.Where(x => x.Campaign == p.Campaign).ToList();
            var l = origins.Where(x => x.Campaign == p.Campaign).ToList();
            return new PostResult(p,
                v.Sum(x => x.PageViews),
                v.Sum(x => x.Visitors),
                v.GroupBy(x => x.Source).ToDictionary(g => g.Key, g => g.Sum(x => x.PageViews)),
                l.Count,
                l.Count(x => x.Kind == LeadKind.Appointment),
                l.Count(x => x.Status == LeadStatus.Won));
        }).ToList();

        // Par réseau : une publication sur Facebook et Instagram compte pour chacun, avec les visites de son lien.
        var byChannel = published.SelectMany(p => p.Channels).Distinct().Select(channel =>
        {
            var source = TrackedLink.SourceOf(channel);
            var onChannel = published.Where(p => p.Channels.Contains(channel)).Select(p => p.Campaign).ToHashSet();
            return new ResultGroup(ChannelLabel(channel), onChannel.Count,
                views.Where(x => onChannel.Contains(x.Campaign) && x.Source == source).Sum(x => x.PageViews),
                origins.Count(x => x.Campaign is not null && onChannel.Contains(x.Campaign) && Same(x.Source, source)),
                origins.Count(x => x.Campaign is not null && onChannel.Contains(x.Campaign) && Same(x.Source, source) && x.Status == LeadStatus.Won));
        }).OrderByDescending(g => g.Leads).ThenByDescending(g => g.Visits).ToList();

        var usedPhotos = await quota.UsedThisMonthAsync(ct);
        var unitCost = configuration.GetValue<decimal?>("Gemini:CostPerImage") ?? 0.07m;

        return new MarketingReport(days, since, results, byChannel,
            Group(results, r => r.Post.Audience ?? "Non précisé"),
            Group(results, r => r.Post.Theme ?? "Non précisé"),
            Group(results, r => r.Post.VisualKind ?? "Sans image"),
            Group(results, r => r.Post.FromAutopilot ? "Pilote automatique" : "Rédigées à la main"),
            views.Where(x => x.Campaign == BioCampaign).Sum(x => x.PageViews),
            origins.Count(x => x.Campaign == BioCampaign),
            usedPhotos, usedPhotos * unitCost);
    }

    private static List<ResultGroup> Group(IEnumerable<PostResult> results, Func<PostResult, string> key) =>
        results.GroupBy(key)
            .Select(g => new ResultGroup(g.Key, g.Count(), g.Sum(r => r.Visits), g.Sum(r => r.Leads), g.Sum(r => r.Won)))
            .OrderByDescending(g => g.LeadsPerPost).ThenByDescending(g => g.VisitsPerPost).ToList();

    private static bool Same(string? a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

    public static string ChannelLabel(ChannelType c) => c switch
    {
        ChannelType.GoogleBusiness => "Google Business",
        _ => c.ToString(),
    };
}
