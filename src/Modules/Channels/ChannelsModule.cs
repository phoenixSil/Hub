using Hub.SharedKernel.Data;
using Hub.SharedKernel.Domain;
using Hub.SharedKernel.Modules;
using Hub.SharedKernel.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
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
    }
}

public sealed class ChannelsDbContextDesignFactory : IDesignTimeDbContextFactory<ChannelsDbContext>
{
    public ChannelsDbContext CreateDbContext(string[] args) =>
        new(DesignTime.Options<ChannelsDbContext>(ChannelsDbContext.SchemaName), DesignTime.Tenant());
}

public static class ChannelsModule
{
    public static IServiceCollection AddChannelsModule(this IServiceCollection services, string connectionString)
    {
        services.AddModuleDbContext<ChannelsDbContext>(connectionString, ChannelsDbContext.SchemaName);
        services.AddRepository<ChannelsDbContext, Channel>();
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
