using Hub.Modules.Analytics.Domain;
using Hub.SharedKernel.Data;
using Hub.SharedKernel.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Hub.Modules.Analytics.Infrastructure;

public sealed class AnalyticsDbContext(DbContextOptions<AnalyticsDbContext> options, ITenantContext tenant)
    : ModuleDbContext(options, tenant)
{
    public const string SchemaName = "analytics";
    public override string Schema => SchemaName;

    public DbSet<AnalyticsEvent> Events => Set<AnalyticsEvent>();

    protected override void ConfigureModel(ModelBuilder b)
    {
        b.Entity<AnalyticsEvent>(e =>
        {
            e.ToTable("Events");
            e.Property(x => x.Type).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.Path).HasMaxLength(300);
            e.Property(x => x.Source).HasMaxLength(120);
            e.Property(x => x.Medium).HasMaxLength(120);
            e.Property(x => x.Campaign).HasMaxLength(160);
            e.Property(x => x.ReferrerHost).HasMaxLength(200);
            e.Property(x => x.Device).HasMaxLength(20);
            e.Property(x => x.VisitorId).HasMaxLength(64);
            e.Property(x => x.Detail).HasMaxLength(200);
            e.HasIndex(x => new { x.TenantId, x.OccurredAt });
        });
    }
}

public sealed class AnalyticsDbContextDesignFactory : IDesignTimeDbContextFactory<AnalyticsDbContext>
{
    public AnalyticsDbContext CreateDbContext(string[] args) =>
        new(DesignTime.Options<AnalyticsDbContext>(AnalyticsDbContext.SchemaName), DesignTime.Tenant());
}
