namespace Hub.SharedKernel.Tenancy;

/// <summary>Entreprise courante pour la portée (requête HTTP ou circuit Blazor).</summary>
public interface ITenantContext
{
    Guid TenantId { get; }
    bool IsResolved { get; }
    void Set(Guid tenantId);
}

/// <summary>
/// Entreprise par défaut, connue au démarrage. Sert de repli quand aucune
/// entreprise n'a été résolue (tâches de fond, circuits Blazor).
/// </summary>
public sealed class TenantDefaults
{
    public Guid? DefaultTenantId { get; set; }
}

public sealed class TenantContext(TenantDefaults defaults) : ITenantContext
{
    private Guid? _tenantId;

    public Guid TenantId =>
        _tenantId ?? defaults.DefaultTenantId
        ?? throw new InvalidOperationException("Aucune entreprise n'a été résolue pour cette requête.");

    public bool IsResolved => _tenantId.HasValue || defaults.DefaultTenantId.HasValue;

    public void Set(Guid tenantId) => _tenantId = tenantId;
}

/// <summary>Informations de l'entreprise utiles aux autres modules (courriels, gabarits).</summary>
public sealed record TenantInfo(
    Guid Id,
    string Name,
    string? Tagline,
    string PublicEmail,
    string Phone,
    string AdminNotificationEmail,
    string PrimaryColor,
    string? SiteUrl);

public interface ITenantInfoProvider
{
    Task<TenantInfo> GetCurrentAsync(CancellationToken ct = default);
}
