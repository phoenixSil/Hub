using Hub.Modules.Quotes.Domain;
using Hub.SharedKernel.Data;
using Hub.SharedKernel.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Hub.Modules.Quotes.Infrastructure;

public sealed class QuotesDbContext(DbContextOptions<QuotesDbContext> options, ITenantContext tenant)
    : ModuleDbContext(options, tenant)
{
    public const string SchemaName = "quotes";
    public override string Schema => SchemaName;

    public DbSet<QuoteProposal> Proposals => Set<QuoteProposal>();

    protected override void ConfigureModel(ModelBuilder b)
    {
        b.Entity<QuoteProposal>(e =>
        {
            e.ToTable("Proposals");
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.Model).HasMaxLength(80);
            e.Property(x => x.EstimatedHours).HasPrecision(6, 2);
            e.Property(x => x.ProposedTotal).HasPrecision(10, 2);
            e.Property(x => x.FinalTotal).HasPrecision(10, 2);
            e.Property(x => x.Confidence).HasMaxLength(20);
            e.Property(x => x.DecidedBy).HasMaxLength(200);
            e.Property(x => x.Error).HasMaxLength(1000);
            e.HasIndex(x => new { x.TenantId, x.LeadId });
        });
    }
}

public sealed class QuotesDbContextDesignFactory : IDesignTimeDbContextFactory<QuotesDbContext>
{
    public QuotesDbContext CreateDbContext(string[] args) =>
        new(DesignTime.Options<QuotesDbContext>(QuotesDbContext.SchemaName), DesignTime.Tenant());
}
