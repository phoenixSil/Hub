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
        services.AddHostedService<AutopilotWorker>();

        // Plan marketing : le pilote le suit ; les chiffres des réseaux sont relevés chaque jour.
        services.AddScoped<PlanInsightsCollector>();
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
