using Hub.Modules.Channels.Application;
using Hub.Modules.Channels.Application.Autopilot;
using Hub.Modules.Channels.Application.Planning;
using Hub.Modules.Channels.Domain;
using Hub.Modules.Channels.Publishers;
using Hub.SharedKernel.Data;
using Hub.SharedKernel.Domain;
using Hub.SharedKernel.Modules;
using Hub.SharedKernel.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Hub.Modules.Channels;

public enum ChannelType
{
    Website = 0,
    Facebook = 1,
    Instagram = 2,
    YouTube = 3,
    TikTok = 4,
    X = 5,
    GoogleBusiness = 6,
    LinkedIn = 7,
}

public enum ChannelStatus
{
    Planned = 0,
    Active = 1,
    Paused = 2,
}

/// <summary>
/// Un canal de présence en ligne. En phase 1, seul le site est actif ;
/// les réseaux sociaux se brancheront ici, un module de publication à la fois.
/// </summary>
public sealed class Channel : TenantEntity
{
    public ChannelType Type { get; set; }
    public ChannelStatus Status { get; set; } = ChannelStatus.Planned;
    public string? Url { get; set; }
    public string? Notes { get; set; }

    /// <summary>Paramètres propres au canal (jetons, identifiants de page), en JSON. Jamais affiché.</summary>
    public string? SettingsJson { get; set; }
    public int Phase { get; set; } = 1;
}

public sealed class ChannelsDbContext(DbContextOptions<ChannelsDbContext> options, ITenantContext tenant)
    : ModuleDbContext(options, tenant)
{
    public const string SchemaName = "channels";
    public override string Schema => SchemaName;

    public DbSet<Channel> Channels => Set<Channel>();
    public DbSet<Post> Posts => Set<Post>();
    public DbSet<PostTarget> PostTargets => Set<PostTarget>();
    public DbSet<AutopilotSettings> AutopilotSettings => Set<AutopilotSettings>();
    public DbSet<ImageUsage> ImageUsages => Set<ImageUsage>();
    public DbSet<MarketingPlan> MarketingPlans => Set<MarketingPlan>();
    public DbSet<PostStats> PostStats => Set<PostStats>();
    public DbSet<AudienceSnapshot> AudienceSnapshots => Set<AudienceSnapshot>();
    public DbSet<MaintenanceEntry> MaintenanceEntries => Set<MaintenanceEntry>();
    public DbSet<Decision> Decisions => Set<Decision>();
    public DbSet<VideoUsage> VideoUsages => Set<VideoUsage>();
    public DbSet<GoogleReview> Reviews => Set<GoogleReview>();
    public DbSet<AdCampaign> AdCampaigns => Set<AdCampaign>();
    public DbSet<AdSettings> AdSettings => Set<AdSettings>();

    protected override void ConfigureModel(ModelBuilder b)
    {
        b.Entity<Channel>(e =>
        {
            e.ToTable("Channels");
            e.Property(x => x.Type).HasConversion<string>().HasMaxLength(30);
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.Url).HasMaxLength(500);
            e.Property(x => x.Notes).HasMaxLength(1000);
            e.HasIndex(x => new { x.TenantId, x.Type }).IsUnique();
        });

        b.Entity<Post>(e =>
        {
            e.ToTable("Posts");
            e.Property(x => x.Title).HasMaxLength(200);
            e.Property(x => x.Message).HasMaxLength(5000);
            e.Property(x => x.LinkPath).HasMaxLength(500);
            e.Property(x => x.ImageUrl).HasMaxLength(1000);
            e.Property(x => x.Campaign).HasMaxLength(100);
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(30);
            e.Property(x => x.CreatedBy).HasMaxLength(200);
            e.Property(x => x.AiModel).HasMaxLength(80);
            e.Property(x => x.Audience).HasMaxLength(100);
            e.Property(x => x.VisualKind).HasMaxLength(20);
            e.Property(x => x.Theme).HasMaxLength(30);
            e.Property(x => x.Pillar).HasMaxLength(40);
            e.Property(x => x.Format).HasMaxLength(40);
            e.Property(x => x.SceneNote).HasMaxLength(400);
            e.Property(x => x.ArtStyle).HasMaxLength(40);
            e.Property(x => x.ReelKind).HasMaxLength(20);
            e.Property(x => x.VideoUrl).HasMaxLength(1000);
            e.Property(x => x.ReelError).HasMaxLength(500);
            e.HasIndex(x => new { x.Status, x.ScheduledAt });
            e.HasIndex(x => new { x.TenantId, x.Campaign });
        });

        b.Entity<PostTarget>(e =>
        {
            e.ToTable("PostTargets");
            e.Property(x => x.Channel).HasConversion<string>().HasMaxLength(30);
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.ExternalId).HasMaxLength(200);
            e.Property(x => x.ExternalUrl).HasMaxLength(500);
            e.Property(x => x.Error).HasMaxLength(1000);
            e.HasIndex(x => new { x.TenantId, x.PostId });
        });

        b.Entity<AutopilotSettings>(e =>
        {
            e.ToTable("AutopilotSettings");
            e.Property(x => x.AudiencesJson).HasMaxLength(4000);
            e.Property(x => x.Channels).HasMaxLength(200);
            e.Property(x => x.Guidelines).HasMaxLength(2000);
            e.Property(x => x.LastRunSummary).HasMaxLength(1000);
            e.Property(x => x.DisabledStyles).HasMaxLength(1000);
            e.HasIndex(x => x.TenantId).IsUnique();
        });

        b.Entity<GoogleReview>(e =>
        {
            e.ToTable("GoogleReviews");
            e.Property(x => x.ExternalId).HasMaxLength(300);
            e.Property(x => x.Author).HasMaxLength(200);
            e.Property(x => x.Comment).HasMaxLength(4000);
            e.Property(x => x.DraftReply).HasMaxLength(4000);
            e.Property(x => x.Reply).HasMaxLength(4000);
            e.HasIndex(x => new { x.TenantId, x.Channel, x.ExternalId }).IsUnique();
        });

        b.Entity<MaintenanceEntry>(e =>
        {
            e.ToTable("MaintenanceEntries");
            e.Property(x => x.Kind).HasMaxLength(30);
            e.Property(x => x.Message).HasMaxLength(1000);
            e.HasIndex(x => new { x.TenantId, x.At });
        });

        b.Entity<Decision>(e =>
        {
            e.ToTable("Decisions");
            e.Property(x => x.Domain).HasMaxLength(40);
            e.Property(x => x.Policy).HasMaxLength(20);
            e.Property(x => x.Explored).HasMaxLength(40);
            e.Property(x => x.Slot).HasMaxLength(40);
            e.Property(x => x.Format).HasMaxLength(60);
            e.Property(x => x.Audience).HasMaxLength(100);
            e.Property(x => x.Theme).HasMaxLength(60);
            e.Property(x => x.Pillar).HasMaxLength(40);
            e.Property(x => x.VisualKind).HasMaxLength(30);
            e.Property(x => x.Channels).HasMaxLength(100);
            e.Property(x => x.MediaKind).HasMaxLength(20);
            e.Property(x => x.ContextJson).HasMaxLength(2000);
            e.Property(x => x.Cost).HasPrecision(10, 2);
            e.Property(x => x.Note).HasMaxLength(200);
            e.HasIndex(x => new { x.TenantId, x.PostId });
            e.HasIndex(x => new { x.TenantId, x.DecidedAt });
        });

        b.Entity<AdCampaign>(e =>
        {
            e.ToTable("AdCampaigns");
            e.Property(x => x.Kind).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.Objective).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.SourceChannel).HasConversion<string>().HasMaxLength(30);
            e.Property(x => x.Name).HasMaxLength(200);
            e.Property(x => x.Message).HasMaxLength(4000);
            e.Property(x => x.Headline).HasMaxLength(200);
            e.Property(x => x.LinkUrl).HasMaxLength(1000);
            e.Property(x => x.TrackingCampaign).HasMaxLength(100);
            e.Property(x => x.Currency).HasMaxLength(3);
            e.Property(x => x.Budget).HasPrecision(10, 2);
            e.Property(x => x.Spend).HasPrecision(10, 2);
            e.Property(x => x.MetaCampaignId).HasMaxLength(40);
            e.Property(x => x.MetaAdSetId).HasMaxLength(40);
            e.Property(x => x.MetaAdId).HasMaxLength(40);
            e.Property(x => x.StatusDetail).HasMaxLength(1000);
            e.Property(x => x.LaunchedBy).HasMaxLength(200);
            e.Ignore(x => x.Committed);
            e.Ignore(x => x.IsLive);
            e.HasIndex(x => new { x.TenantId, x.CreatedAt });
            e.HasIndex(x => new { x.TenantId, x.PostId });
        });

        b.Entity<AdSettings>(e =>
        {
            e.ToTable("AdSettings");
            e.Property(x => x.AdAccountId).HasMaxLength(40);
            e.Property(x => x.AdAccountName).HasMaxLength(200);
            e.Property(x => x.Currency).HasMaxLength(3);
            e.Property(x => x.MonthlyCap).HasPrecision(10, 2);
            e.HasIndex(x => x.TenantId).IsUnique();
        });

        b.Entity<VideoUsage>(e =>
        {
            e.ToTable("VideoUsages");
            e.Property(x => x.Month).HasMaxLength(7);
            e.Property(x => x.Cost).HasPrecision(10, 2);
            e.HasIndex(x => new { x.TenantId, x.Month }).IsUnique();
        });

        b.Entity<ImageUsage>(e =>
        {
            e.ToTable("ImageUsages");
            e.Property(x => x.Month).HasMaxLength(7);
            e.HasIndex(x => new { x.TenantId, x.Month }).IsUnique();
        });

        b.Entity<MarketingPlan>(e =>
        {
            e.ToTable("MarketingPlans");
            e.Property(x => x.Name).HasMaxLength(150);
            e.Property(x => x.Objective).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.PhasesJson).HasMaxLength(8000);
            e.Property(x => x.PillarsJson).HasMaxLength(4000);
            e.Property(x => x.BaselineJson).HasMaxLength(500);
            e.Property(x => x.TargetsJson).HasMaxLength(500);
            e.Property(x => x.ProposedTargetsJson).HasMaxLength(500);
            e.Property(x => x.ProposedPillarsJson).HasMaxLength(4000);
            e.Property(x => x.ProposalNote).HasMaxLength(2000);
            e.Property(x => x.LastReviewedPhase).HasMaxLength(100);
            e.Property(x => x.InsightsNote).HasMaxLength(1000);
            e.HasIndex(x => new { x.TenantId, x.Status });
        });

        b.Entity<PostStats>(e =>
        {
            e.ToTable("PostStats");
            e.Property(x => x.Channel).HasConversion<string>().HasMaxLength(30);
            e.HasIndex(x => new { x.TenantId, x.PostId, x.Channel }).IsUnique();
        });

        b.Entity<AudienceSnapshot>(e =>
        {
            e.ToTable("AudienceSnapshots");
            e.Property(x => x.Channel).HasConversion<string>().HasMaxLength(30);
            e.HasIndex(x => new { x.TenantId, x.Channel, x.Day }).IsUnique();
        });
    }
}

public sealed class ChannelsDbContextDesignFactory : IDesignTimeDbContextFactory<ChannelsDbContext>
{
    public ChannelsDbContext CreateDbContext(string[] args) =>
        new(DesignTime.Options<ChannelsDbContext>(ChannelsDbContext.SchemaName), DesignTime.Tenant());
}

public static class ChannelsModule
{
    public static IServiceCollection AddChannelsModule(this IServiceCollection services, IConfiguration configuration, string connectionString)
    {
        services.AddModuleDbContext<ChannelsDbContext>(connectionString, ChannelsDbContext.SchemaName);
        services.AddRepository<ChannelsDbContext, Channel>();
        services.Configure<ChannelsOptions>(configuration.GetSection(ChannelsOptions.Section));
        services.Configure<FacebookOptions>(configuration.GetSection(FacebookOptions.Section));

        // Un connecteur par réseau : ajouter Instagram ou Google Business = une ligne ici.
        services.AddHttpClient<ISocialPublisher, FacebookPagePublisher>(c => c.Timeout = TimeSpan.FromSeconds(60));
        services.AddHttpClient<ISocialPublisher, InstagramPublisher>(c => c.Timeout = TimeSpan.FromSeconds(60));
        services.Configure<GoogleOptions>(configuration.GetSection(GoogleOptions.Section));
        services.AddHttpClient<ISocialPublisher, GoogleBusinessPublisher>(c => c.Timeout = TimeSpan.FromSeconds(60));

        services.AddScoped<IPublisherResolver, PublisherResolver>();
        services.AddScoped<IChannelConnections, ChannelConnections>();
        services.AddScoped<PostPublishing>();
        services.AddScoped<IPostService, PostService>();
        services.AddScoped<IPostWriter, PostWriter>();
        services.AddScoped<IPostVisualDesigner, PostVisualDesigner>();
        services.AddScoped<IImageQuota, ImageQuota>();
        services.AddHostedService<PostScheduler>();

        // Pilote automatique : planifie, rédige, illustre et programme les publications de la semaine.
        services.AddScoped<IAutopilotPlanner, AutopilotPlanner>();
        services.AddScoped<AutopilotRunner>();
        services.AddScoped<IAutopilotService, AutopilotService>();
        services.AddSingleton<AutopilotTrigger>();
        services.AddSingleton<Application.Marketing.AutopilotProgress>();
        services.AddScoped<Application.Marketing.IMarketingCalendar, Application.Marketing.MarketingCalendar>();
        services.AddHostedService<AutopilotWorker>();

        // Plan marketing : le pilote le suit ; les chiffres des réseaux sont relevés chaque jour.
        services.AddScoped<PlanInsightsCollector>();
        services.AddScoped<Application.Marketing.MarketingGuardian>();
        services.Configure<Application.Decisions.DecisionOptions>(configuration.GetSection(Application.Decisions.DecisionOptions.Section));
        services.AddSingleton<Application.Decisions.IDecisionAdapter, Application.Decisions.PublicationDecisions>();
        services.AddScoped<Application.Decisions.DecisionJournal>();
        services.Configure<Application.Reels.ReelOptions>(configuration.GetSection(Application.Reels.ReelOptions.Section));
        services.AddScoped<Application.Reels.IReelService, Application.Reels.ReelService>();
        services.AddScoped<Application.Decisions.IDecisionJournal>(sp => sp.GetRequiredService<Application.Decisions.DecisionJournal>());
        services.AddScoped<Application.Reviews.IReviewService, Application.Reviews.ReviewService>();

        // Publicités payantes Meta (boost et campagnes) : rien n'est dépensé sans le clic « Lancer » de l'administrateur.
        services.AddHttpClient<MetaAdsPlatform>(c => c.Timeout = TimeSpan.FromSeconds(90));
        services.AddSingleton<SimulatedAdsPlatform>();
        services.AddScoped<Application.Ads.IAdsPlatform>(sp => sp.GetRequiredService<IOptions<ChannelsOptions>>().Value.Simulate
            ? sp.GetRequiredService<SimulatedAdsPlatform>()
            : sp.GetRequiredService<MetaAdsPlatform>());
        services.AddScoped<Application.Ads.IAdService, Application.Ads.AdService>();
        services.AddHostedService<Application.Marketing.MarketingGuardianWorker>();
        services.AddScoped<IMarketingPlanService, MarketingPlanService>();
        services.AddHostedService<PlanInsightsWorker>();
        services.AddSingleton<IModuleInitializer, ChannelsInitializer>();
        return services;
    }
}

internal sealed class ChannelsInitializer : IModuleInitializer
{
    public int Order => 50;

    public async Task InitializeAsync(IServiceProvider services, CancellationToken ct = default)
    {
        var factory = services.GetRequiredService<IDbContextFactory<ChannelsDbContext>>();
        await using var db = await factory.CreateDbContextAsync(ct);
        await db.Database.MigrateAsync(ct);
    }
}
