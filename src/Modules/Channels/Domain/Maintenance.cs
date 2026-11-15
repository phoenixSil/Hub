using Hub.SharedKernel.Domain;

namespace Hub.Modules.Channels.Domain;

/// <summary>
/// Journal du gardien (vérification toutes les 30 minutes) : chaque relance ou problème détecté,
/// pour que l'administrateur sache toujours ce que le système a fait seul.
/// </summary>
public sealed class MaintenanceEntry : TenantEntity
{
    public DateTimeOffset At { get; set; }

    /// <summary>Publication, Blocage, Preparation, Semaine, Chiffres, Alerte, Verification.</summary>
    public string Kind { get; set; } = string.Empty;

    public string Message { get; set; } = string.Empty;

    /// <summary>Vrai : réglé tout seul. Faux : l'administrateur doit agir.</summary>
    public bool Success { get; set; }

    public Guid? PostId { get; set; }
}
