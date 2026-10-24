using Hub.Modules.Platform.Domain;
using Hub.SharedKernel.Data;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Hub.Modules.Platform.Infrastructure;

public sealed class PlatformDbContext(DbContextOptions<PlatformDbContext> options) : IdentityDbContext<HubUser>(options)
{
    public const string SchemaName = "platform";

    public DbSet<Tenant> Tenants => Set<Tenant>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);
        builder.HasDefaultSchema(SchemaName);

        builder.Entity<Tenant>(t =>
        {
            t.HasKey(x => x.Id);
            t.HasIndex(x => x.Slug).IsUnique();
            t.Property(x => x.Slug).HasMaxLength(64);
            t.Property(x => x.Name).HasMaxLength(200);
            t.Property(x => x.Domains).HasMaxLength(1000);
            t.Property(x => x.PrimaryColor).HasMaxLength(16);
            t.Property(x => x.SecondaryColor).HasMaxLength(16);
            t.Property(x => x.AccentColor).HasMaxLength(16);
            t.Property(x => x.Phone).HasMaxLength(40);
            t.Property(x => x.PublicEmail).HasMaxLength(256);
            t.Property(x => x.AdminNotificationEmail).HasMaxLength(256);
        });

        builder.Entity<HubUser>(u => u.HasIndex(x => x.TenantId));
    }
}

public sealed class PlatformDbContextDesignFactory : IDesignTimeDbContextFactory<PlatformDbContext>
{
    public PlatformDbContext CreateDbContext(string[] args) =>
        new(DesignTime.Options<PlatformDbContext>(PlatformDbContext.SchemaName));
}
