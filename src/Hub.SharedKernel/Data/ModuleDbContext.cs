using System.Linq.Expressions;
using Hub.SharedKernel.Domain;
using Hub.SharedKernel.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace Hub.SharedKernel.Data;

/// <summary>
/// Contexte EF Core d'un module : un schéma SQL par module, un filtre global
/// par entreprise sur toutes les entités <see cref="TenantEntity"/>, et les
/// dates de création et de modification tenues à jour automatiquement.
/// </summary>
public abstract class ModuleDbContext(DbContextOptions options, ITenantContext tenant) : DbContext(options)
{
    public abstract string Schema { get; }

    /// <summary>Lu par le filtre global à chaque requête (paramètre SQL).</summary>
    public Guid CurrentTenantId => tenant.TenantId;

    protected abstract void ConfigureModel(ModelBuilder modelBuilder);

    protected sealed override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);
        ConfigureModel(modelBuilder);

        foreach (var entityType in modelBuilder.Model.GetEntityTypes())
        {
            if (!typeof(TenantEntity).IsAssignableFrom(entityType.ClrType) || entityType.BaseType is not null)
                continue;

            var e = Expression.Parameter(entityType.ClrType, "e");
            var body = Expression.Equal(
                Expression.Property(e, nameof(TenantEntity.TenantId)),
                Expression.Property(Expression.Constant(this), nameof(CurrentTenantId)));
            modelBuilder.Entity(entityType.ClrType).HasQueryFilter(Expression.Lambda(body, e));
            modelBuilder.Entity(entityType.ClrType).HasIndex(nameof(TenantEntity.TenantId));
        }
    }

    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        StampEntities();
        return base.SaveChangesAsync(cancellationToken);
    }

    public override int SaveChanges()
    {
        StampEntities();
        return base.SaveChanges();
    }

    private void StampEntities()
    {
        var now = DateTimeOffset.UtcNow;
        foreach (var entry in ChangeTracker.Entries<TenantEntity>())
        {
            if (entry.State == EntityState.Added)
            {
                if (entry.Entity.TenantId == Guid.Empty)
                    entry.Entity.TenantId = tenant.TenantId;
                entry.Entity.CreatedAt = now;
                entry.Entity.UpdatedAt = now;
            }
            else if (entry.State == EntityState.Modified)
            {
                if (entry.Entity.TenantId != tenant.TenantId)
                    throw new InvalidOperationException("Modification refusée : la donnée appartient à une autre entreprise.");
                entry.Property(x => x.CreatedAt).IsModified = false;
                entry.Property(x => x.TenantId).IsModified = false;
                entry.Entity.UpdatedAt = now;
            }
        }
    }
}
