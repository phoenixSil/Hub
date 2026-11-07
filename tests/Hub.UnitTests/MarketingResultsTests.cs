using Hub.Modules.Analytics.Application;
using Hub.Modules.Channels;
using Hub.Modules.Channels.Application;
using Hub.Modules.Leads.Application;
using Hub.Modules.Leads.Contracts;
using Hub.Modules.Leads.Domain;
using Hub.SharedKernel;
using Hub.SharedKernel.Time;
using Hub.Web.Infrastructure;
using Microsoft.Extensions.Configuration;

namespace Hub.UnitTests;

public class MarketingResultsTests
{
    private sealed class FakePosts(params PublishedPost[] posts) : IPostService
    {
        public Task<IReadOnlyList<PublishedPost>> ListPublishedAsync(DateTimeOffset since, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<PublishedPost>>(posts);
        public Task<IReadOnlyList<PostView>> ListAsync(CancellationToken ct = default) => throw new NotSupportedException();
        public Task<PostView?> GetAsync(Guid id, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<Result<Guid>> SaveDraftAsync(PostDraft draft, string author, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<Result> ScheduleAsync(Guid id, DateTimeOffset when, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<Result> PublishNowAsync(Guid id, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<Result> CancelAsync(Guid id, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<Result> DeleteAsync(Guid id, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<string?> PreviewLinkAsync(string? linkPath, ChannelType channel, string campaign, CancellationToken ct = default) => throw new NotSupportedException();
    }

    private sealed class FakeStats(params CampaignSourceViews[] views) : IDashboardQueries
    {
        public Task<IReadOnlyList<CampaignSourceViews>> GetCampaignViewsAsync(IReadOnlyCollection<string> campaigns, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<CampaignSourceViews>>(views.Where(v => campaigns.Contains(v.Campaign)).ToList());
        public Task<DashboardStats> GetAsync(int days, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<IReadOnlyDictionary<string, CampaignStats>> GetCampaignStatsAsync(IReadOnlyCollection<string> campaigns, CancellationToken ct = default) => throw new NotSupportedException();
    }

    private sealed class FakeLeads(params LeadOrigin[] leads) : ILeadAttribution
    {
        public Task<IReadOnlyList<LeadOrigin>> ListSinceAsync(DateTimeOffset since, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<LeadOrigin>>(leads);
        public Task<IReadOnlyList<LeadOrigin>> ListForCampaignsAsync(IReadOnlyCollection<string> campaigns, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<LeadOrigin>>(leads.Where(l => l.Campaign is not null && campaigns.Contains(l.Campaign)).ToList());
    }

    private sealed class FakeQuota(int used) : IImageQuota
    {
        public Task<int> UsedThisMonthAsync(CancellationToken ct = default) => Task.FromResult(used);
        public Task RecordAsync(CancellationToken ct = default) => Task.CompletedTask;
    }

    private static readonly DateTimeOffset Now = DateTimeOffset.UtcNow;

    private static LeadOrigin Lead(string campaign, string source, LeadKind kind = LeadKind.Quote, LeadStatus status = LeadStatus.New) =>
        new(Guid.NewGuid(), Now, source, "social", campaign, kind, status);

    private static async Task<MarketingReport> Report()
    {
        var a = new PublishedPost(Guid.NewGuid(), "Grand ménage", Now.AddDays(-3), "camp-a", "Familles occupées", true, "Photo", "Promotion",
            [ChannelType.Facebook, ChannelType.Instagram], "/img/a.png");
        var b = new PublishedPost(Guid.NewGuid(), "Aide aux aînés", Now.AddDays(-2), "camp-b", "Aînés et leurs proches", false, "Illustration", "Aînés",
            [ChannelType.Facebook], null);
        var results = new MarketingResults(
            new FakePosts(a, b),
            new FakeStats(new("camp-a", "facebook", 10, 8), new("camp-a", "instagram", 4, 4), new("camp-b", "facebook", 2, 2), new("bio", "instagram", 3, 3)),
            new FakeLeads(Lead("camp-a", "facebook", status: LeadStatus.Won), Lead("camp-a", "instagram", LeadKind.Appointment), Lead("bio", "instagram"), Lead("autre", "google")),
            new FakeQuota(12),
            new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Gemini:CostPerImage"] = "0.07" }).Build(),
            TimeProvider.System);
        return await results.GetAsync(30);
    }

    [Fact]
    public async Task Les_totaux_comptent_les_publications_et_le_lien_de_la_bio()
    {
        var r = await Report();

        Assert.Equal(2, r.PostCount);
        Assert.Equal(19, r.Visits);
        Assert.Equal(3, r.Leads);
        Assert.Equal(1, r.Appointments);
        Assert.Equal(1, r.Won);
        Assert.Equal(0.84m, r.PhotoCostThisMonth);
        Assert.Equal("Grand ménage", r.Best!.Post.Title);
    }

    [Fact]
    public async Task Chaque_reseau_compte_ses_propres_visites_et_demandes()
    {
        var r = await Report();

        var facebook = r.ByChannel.Single(g => g.Label == "Facebook");
        Assert.Equal(2, facebook.Posts);
        Assert.Equal(12, facebook.Visits);
        Assert.Equal(1, facebook.Leads);
        Assert.Equal(1, facebook.Won);
        var instagram = r.ByChannel.Single(g => g.Label == "Instagram");
        Assert.Equal(1, instagram.Posts);
        Assert.Equal(4, instagram.Visits);
        Assert.Equal(1, instagram.Leads);
    }

    [Fact]
    public async Task Les_comparaisons_classent_ce_qui_rapporte_le_plus_en_premier()
    {
        var r = await Report();

        Assert.Equal("Familles occupées", r.ByAudience[0].Label);
        Assert.Equal("Promotion", r.ByTheme[0].Label);
        Assert.Equal("Photo", r.ByVisual[0].Label);
        Assert.Equal("Pilote automatique", r.ByOrigin[0].Label);
    }

    [Fact]
    public async Task Le_resume_du_lundi_donne_les_chiffres_et_la_meilleure_publication()
    {
        var (subject, html) = WeeklyReportEmail.Build(await Report(), "Scinti<Net>", "https://www.scintinet.com", "#0097CE");

        Assert.Contains("3 demande(s)", subject);
        Assert.Contains("Grand ménage", html);
        Assert.Contains("Scinti&lt;Net&gt;", html);
        Assert.Contains("https://www.scintinet.com/admin/resultats", html);
    }

    [Theory]
    [InlineData(2026, 11, 9, 8, 30, null, true)]     // lundi 8 h 30
    [InlineData(2026, 11, 9, 7, 30, null, false)]    // lundi trop tôt
    [InlineData(2026, 11, 10, 9, 0, null, false)]    // mardi
    [InlineData(2026, 11, 9, 10, 0, "2026-11-09", false)] // déjà envoyé ce lundi
    [InlineData(2026, 11, 9, 10, 0, "2026-11-02", true)]  // envoyé la semaine dernière
    public void Le_resume_part_le_lundi_a_partir_de_8_h(int y, int m, int d, int h, int min, string? last, bool due)
    {
        var now = BusinessTime.FromLocal(new DateTime(y, m, d, h, min, 0));
        Assert.Equal(due, WeeklyReportWorker.IsDue(now, last is null ? null : DateOnly.Parse(last), out _));
    }
}
