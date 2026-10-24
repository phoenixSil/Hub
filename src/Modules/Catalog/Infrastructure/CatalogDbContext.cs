using Hub.Modules.Catalog.Domain;
using Hub.SharedKernel.Data;
using Hub.SharedKernel.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Hub.Modules.Catalog.Infrastructure;

public sealed class CatalogDbContext(DbContextOptions<CatalogDbContext> options, ITenantContext tenant)
    : ModuleDbContext(options, tenant)
{
    public const string SchemaName = "catalog";
    public override string Schema => SchemaName;

    public DbSet<ServiceCategory> Categories => Set<ServiceCategory>();
    public DbSet<Service> Services => Set<Service>();
    public DbSet<PricingModel> PricingModels => Set<PricingModel>();

    protected override void ConfigureModel(ModelBuilder b)
    {
        b.Entity<ServiceCategory>(e =>
        {
            e.ToTable("Categories");
            e.Property(x => x.Name).HasMaxLength(200);
            e.Property(x => x.Slug).HasMaxLength(120);
            e.Property(x => x.RouteSegment).HasMaxLength(60);
            e.Property(x => x.Locale).HasMaxLength(10);
            e.HasIndex(x => new { x.TenantId, x.Locale, x.Slug }).IsUnique();
        });

        b.Entity<Service>(e =>
        {
            e.ToTable("Services");
            e.Property(x => x.Title).HasMaxLength(200);
            e.Property(x => x.Slug).HasMaxLength(120);
            e.Property(x => x.Summary).HasMaxLength(600);
            e.Property(x => x.Icon).HasMaxLength(40);
            e.Property(x => x.SeoTitle).HasMaxLength(70);
            e.Property(x => x.SeoDescription).HasMaxLength(170);
            e.Property(x => x.Locale).HasMaxLength(10);
            e.HasIndex(x => new { x.TenantId, x.Locale, x.Slug }).IsUnique();
            e.HasIndex(x => x.CategoryId);
        });

        b.Entity<PricingModel>(e =>
        {
            e.ToTable("PricingModels");
            e.Property(x => x.MinAmount).HasPrecision(10, 2);
            e.Property(x => x.MaxAmount).HasPrecision(10, 2);
            e.Property(x => x.MinimumHours).HasPrecision(5, 2);
            e.Property(x => x.ShiftHours).HasPrecision(5, 2);
            e.Property(x => x.Kind).HasConversion<string>().HasMaxLength(20);
            e.HasIndex(x => x.ServiceId);
        });
    }
}

public sealed class CatalogDbContextDesignFactory : IDesignTimeDbContextFactory<CatalogDbContext>
{
    public CatalogDbContext CreateDbContext(string[] args) =>
        new(DesignTime.Options<CatalogDbContext>(CatalogDbContext.SchemaName), DesignTime.Tenant());
}
