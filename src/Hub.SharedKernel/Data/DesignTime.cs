using Hub.SharedKernel.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace Hub.SharedKernel.Data;

/// <summary>
/// Aide pour les fabriques « design-time » utilisées par <c>dotnet ef migrations</c>.
/// La chaîne de connexion vient de la variable HUB_DESIGN_CONNECTION, sinon LocalDB.
/// </summary>
public static class DesignTime
{
    public const string DefaultConnection =
        "Server=(localdb)\\mssqllocaldb;Database=Hub;Trusted_Connection=True;MultipleActiveResultSets=true";

    public static DbContextOptions<TContext> Options<TContext>(string schema) where TContext : DbContext =>
        new DbContextOptionsBuilder<TContext>()
            .UseSqlServer(
                Environment.GetEnvironmentVariable("HUB_DESIGN_CONNECTION") ?? DefaultConnection,
                sql => sql.MigrationsHistoryTable("__EFMigrationsHistory", schema))
            .Options;

    public static ITenantContext Tenant() =>
        new TenantContext(new TenantDefaults { DefaultTenantId = Guid.Empty });
}
