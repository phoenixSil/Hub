using Hub.Modules.Analytics.Domain;
using Hub.Modules.Analytics.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Hub.Modules.Analytics.Application;

public sealed record DailyPoint(DateOnly Day, int PageViews, int Leads);

public sealed record SourceRow(string Source, int PageViews, int Leads);

public sealed record PageRow(string Path, int PageViews);

public sealed record DashboardStats(
    DateOnly From,
    DateOnly To,
    int PageViews,
    int Visitors,
    int Leads,
    int PhoneClicks,
    int EmailClicks,
    int WhatsAppClicks,
    IReadOnlyList<DailyPoint> Daily,
    IReadOnlyList<SourceRow> Sources,
    IReadOnlyList<PageRow> TopPages)
{
    /// <summary>Demandes pour 100 pages vues.</summary>
    public double ConversionRate => PageViews == 0 ? 0 : 100.0 * Leads / PageViews;
}

public interface IDashboardQueries
{
    Task<DashboardStats> GetAsync(int days, CancellationToken ct = default);
}

internal sealed class DashboardQueries(IDbContextFactory<AnalyticsDbContext> factory, TimeProvider clock) : IDashboardQueries
{
    public async Task<DashboardStats> GetAsync(int days, CancellationToken ct = default)
    {
        var to = Hub.SharedKernel.Time.BusinessTime.Today(clock);
        var from = to.AddDays(-(days - 1));
        var since = new DateTimeOffset(from.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero).AddHours(-6);

        await using var db = await factory.CreateDbContextAsync(ct);
        var events = await db.Events.AsNoTracking()
            .Where(e => e.OccurredAt >= since)
            .Select(e => new { e.OccurredAt, e.Type, e.Path, e.Source, e.VisitorId })
            .ToListAsync(ct);

        var zone = Hub.SharedKernel.Time.BusinessTime.Zone;
        var local = events
            .Select(e => new { Day = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(e.OccurredAt, zone).DateTime), e.Type, e.Path, Source = e.Source ?? "Direct", e.VisitorId })
            .Where(e => e.Day >= from && e.Day <= to)
            .ToList();

        var views = local.Where(e => e.Type == AnalyticsEventType.PageView).ToList();
        var leads = local.Where(e => e.Type == AnalyticsEventType.LeadSubmitted).ToList();

        var daily = Enumerable.Range(0, days)
            .Select(i => from.AddDays(i))
            .Select(d => new DailyPoint(d, views.Count(v => v.Day == d), leads.Count(l => l.Day == d)))
            .ToList();

        var sources = views.Select(v => v.Source).Concat(leads.Select(l => l.Source)).Distinct()
            .Select(s => new SourceRow(s, views.Count(v => v.Source == s), leads.Count(l => l.Source == s)))
            .OrderByDescending(r => r.Leads).ThenByDescending(r => r.PageViews)
            .Take(10).ToList();

        var pages = views.GroupBy(v => v.Path)
            .Select(g => new PageRow(g.Key, g.Count()))
            .OrderByDescending(p => p.PageViews)
            .Take(10).ToList();

        return new DashboardStats(
            from, to,
            views.Count,
            views.Where(v => v.VisitorId != null).Select(v => v.VisitorId).Distinct().Count(),
            leads.Count,
            local.Count(e => e.Type == AnalyticsEventType.PhoneClick),
            local.Count(e => e.Type == AnalyticsEventType.EmailClick),
            local.Count(e => e.Type == AnalyticsEventType.WhatsAppClick),
            daily, sources, pages);
    }
}
