using Hub.Modules.Content.Domain;
using Hub.SharedKernel.Data;
using Hub.SharedKernel.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Hub.Modules.Content.Infrastructure;

public sealed class ContentDbContext(DbContextOptions<ContentDbContext> options, ITenantContext tenant)
    : ModuleDbContext(options, tenant)
{
    public const string SchemaName = "content";
    public override string Schema => SchemaName;

    public DbSet<ServiceArea> Areas => Set<ServiceArea>();
    public DbSet<Page> Pages => Set<Page>();
    public DbSet<Promotion> Promotions => Set<Promotion>();
    public DbSet<FaqItem> Faq => Set<FaqItem>();
    public DbSet<Review> Reviews => Set<Review>();
    public DbSet<ContentBlock> Blocks => Set<ContentBlock>();
    public DbSet<HighlightItem> Highlights => Set<HighlightItem>();

    protected override void ConfigureModel(ModelBuilder b)
    {
        b.Entity<ServiceArea>(e =>
        {
            e.ToTable("ServiceAreas");
            e.Property(x => x.Name).HasMaxLength(120);
            e.Property(x => x.Slug).HasMaxLength(120);
            e.Property(x => x.Locale).HasMaxLength(10);
            e.HasIndex(x => new { x.TenantId, x.Locale, x.Slug }).IsUnique();
        });

        b.Entity<Page>(e =>
        {
            e.ToTable("Pages");
            e.Property(x => x.Slug).HasMaxLength(120);
            e.Property(x => x.Title).HasMaxLength(200);
            e.Property(x => x.SeoTitle).HasMaxLength(70);
            e.Property(x => x.SeoDescription).HasMaxLength(170);
            e.Property(x => x.Locale).HasMaxLength(10);
            e.HasIndex(x => new { x.TenantId, x.Locale, x.Slug }).IsUnique();
        });

        b.Entity<Promotion>(e =>
        {
            e.ToTable("Promotions");
            e.Property(x => x.Badge).HasMaxLength(120);
            e.Property(x => x.Title).HasMaxLength(200);
            e.Property(x => x.Highlight).HasMaxLength(120);
            e.Property(x => x.PrimaryLabel).HasMaxLength(60);
            e.Property(x => x.PrimaryUrl).HasMaxLength(300);
            e.Property(x => x.SecondaryLabel).HasMaxLength(60);
            e.Property(x => x.SecondaryUrl).HasMaxLength(300);
            e.Property(x => x.Locale).HasMaxLength(10);
        });

        b.Entity<FaqItem>(e =>
        {
            e.ToTable("FaqItems");
            e.Property(x => x.Question).HasMaxLength(300);
            e.Property(x => x.ServiceSlug).HasMaxLength(120);
            e.Property(x => x.Locale).HasMaxLength(10);
        });

        b.Entity<Review>(e =>
        {
            e.ToTable("Reviews");
            e.Property(x => x.AuthorName).HasMaxLength(120);
            e.Property(x => x.AuthorContext).HasMaxLength(160);
            e.Property(x => x.Source).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.SourceUrl).HasMaxLength(500);
        });

        b.Entity<ContentBlock>(e =>
        {
            e.ToTable("ContentBlocks");
            e.Property(x => x.Key).HasMaxLength(80);
            e.Property(x => x.Eyebrow).HasMaxLength(80);
            e.Property(x => x.Title).HasMaxLength(200);
            e.Property(x => x.Locale).HasMaxLength(10);
            e.HasIndex(x => new { x.TenantId, x.Locale, x.Key }).IsUnique();
        });

        b.Entity<HighlightItem>(e =>
        {
            e.ToTable("HighlightItems");
            e.Property(x => x.Group).HasMaxLength(40);
            e.Property(x => x.Title).HasMaxLength(160);
            e.Property(x => x.Icon).HasMaxLength(40);
            e.Property(x => x.Locale).HasMaxLength(10);
        });
    }
}

public sealed class ContentDbContextDesignFactory : IDesignTimeDbContextFactory<ContentDbContext>
{
    public ContentDbContext CreateDbContext(string[] args) =>
        new(DesignTime.Options<ContentDbContext>(ContentDbContext.SchemaName), DesignTime.Tenant());
}
