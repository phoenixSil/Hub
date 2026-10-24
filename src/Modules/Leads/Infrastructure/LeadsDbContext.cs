using Hub.Modules.Leads.Domain;
using Hub.SharedKernel.Data;
using Hub.SharedKernel.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Hub.Modules.Leads.Infrastructure;

public sealed class LeadsDbContext(DbContextOptions<LeadsDbContext> options, ITenantContext tenant)
    : ModuleDbContext(options, tenant)
{
    public const string SchemaName = "leads";
    public override string Schema => SchemaName;

    public DbSet<Lead> Leads => Set<Lead>();
    public DbSet<LeadActivity> Activities => Set<LeadActivity>();
    public DbSet<ClosedDay> ClosedDays => Set<ClosedDay>();
    public DbSet<SchedulingSettings> Scheduling => Set<SchedulingSettings>();

    protected override void ConfigureModel(ModelBuilder b)
    {
        b.Entity<Lead>(e =>
        {
            e.ToTable("Leads");
            e.Property(x => x.Kind).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.PreferredSlot).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.FullName).HasMaxLength(160);
            e.Property(x => x.Email).HasMaxLength(256);
            e.Property(x => x.Phone).HasMaxLength(40);
            e.Property(x => x.Address).HasMaxLength(300);
            e.Property(x => x.AreaSlug).HasMaxLength(120);
            e.Property(x => x.Services).HasMaxLength(1000);
            e.Property(x => x.PropertyType).HasMaxLength(80);
            e.Property(x => x.PropertySize).HasMaxLength(80);
            e.Property(x => x.Frequency).HasMaxLength(80);
            e.Property(x => x.Subject).HasMaxLength(200);
            e.Property(x => x.Message).HasMaxLength(4000);
            e.Property(x => x.Source).HasMaxLength(120);
            e.Property(x => x.Medium).HasMaxLength(120);
            e.Property(x => x.Campaign).HasMaxLength(160);
            e.Property(x => x.Referrer).HasMaxLength(500);
            e.Property(x => x.LandingPage).HasMaxLength(500);
            e.Property(x => x.VisitorId).HasMaxLength(64);
            e.Property(x => x.IpHash).HasMaxLength(64);
            e.HasIndex(x => new { x.TenantId, x.Status });
            e.HasIndex(x => new { x.TenantId, x.CreatedAt });
        });

        b.Entity<LeadActivity>(e =>
        {
            e.ToTable("LeadActivities");
            e.Property(x => x.Author).HasMaxLength(160);
            e.Property(x => x.Action).HasMaxLength(200);
            e.Property(x => x.Note).HasMaxLength(4000);
            e.HasIndex(x => x.LeadId);
        });

        b.Entity<ClosedDay>(e =>
        {
            e.ToTable("ClosedDays");
            e.Property(x => x.Reason).HasMaxLength(200);
            e.HasIndex(x => new { x.TenantId, x.Date }).IsUnique();
        });

        b.Entity<SchedulingSettings>(e =>
        {
            e.ToTable("SchedulingSettings");
            e.Property(x => x.OpenDays).HasMaxLength(20);
        });
    }
}

public sealed class LeadsDbContextDesignFactory : IDesignTimeDbContextFactory<LeadsDbContext>
{
    public LeadsDbContext CreateDbContext(string[] args) =>
        new(DesignTime.Options<LeadsDbContext>(LeadsDbContext.SchemaName), DesignTime.Tenant());
}
