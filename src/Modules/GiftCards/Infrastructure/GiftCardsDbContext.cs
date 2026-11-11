using Hub.Modules.GiftCards.Domain;
using Hub.SharedKernel.Data;
using Hub.SharedKernel.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Hub.Modules.GiftCards.Infrastructure;

public sealed class GiftCardsDbContext(DbContextOptions<GiftCardsDbContext> options, ITenantContext tenant)
    : ModuleDbContext(options, tenant)
{
    public const string SchemaName = "giftcards";
    public override string Schema => SchemaName;

    public DbSet<GiftCard> GiftCards => Set<GiftCard>();
    public DbSet<GiftCardUse> GiftCardUses => Set<GiftCardUse>();

    protected override void ConfigureModel(ModelBuilder b)
    {
        b.Entity<GiftCard>(e =>
        {
            e.ToTable("GiftCards");
            e.Property(x => x.Code).HasMaxLength(20);
            e.Property(x => x.Amount).HasPrecision(10, 2);
            e.Property(x => x.Balance).HasPrecision(10, 2);
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.BuyerName).HasMaxLength(160);
            e.Property(x => x.BuyerEmail).HasMaxLength(256);
            e.Property(x => x.BuyerPhone).HasMaxLength(40);
            e.Property(x => x.RecipientName).HasMaxLength(160);
            e.Property(x => x.RecipientEmail).HasMaxLength(256);
            e.Property(x => x.Message).HasMaxLength(400);
            e.Property(x => x.PaidNote).HasMaxLength(300);
            e.Property(x => x.LastError).HasMaxLength(500);
            e.HasIndex(x => x.Code).IsUnique();
            e.HasIndex(x => new { x.TenantId, x.Status });
            e.HasMany(x => x.Uses).WithOne().HasForeignKey(u => u.GiftCardId).OnDelete(DeleteBehavior.Cascade);
        });
        b.Entity<GiftCardUse>(e =>
        {
            e.ToTable("GiftCardUses");
            e.Property(x => x.Amount).HasPrecision(10, 2);
            e.Property(x => x.Note).HasMaxLength(300);
            e.Property(x => x.RecordedBy).HasMaxLength(200);
        });
    }
}

public sealed class GiftCardsDbContextDesignFactory : IDesignTimeDbContextFactory<GiftCardsDbContext>
{
    public GiftCardsDbContext CreateDbContext(string[] args) =>
        new(DesignTime.Options<GiftCardsDbContext>(GiftCardsDbContext.SchemaName), DesignTime.Tenant());
}
