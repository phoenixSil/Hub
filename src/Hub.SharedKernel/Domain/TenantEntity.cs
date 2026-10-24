namespace Hub.SharedKernel.Domain;

/// <summary>
/// Base de toute donnée appartenant à une entreprise (tenant).
/// Le TenantId est renseigné automatiquement à l'enregistrement et
/// un filtre global empêche de lire les données d'une autre entreprise.
/// </summary>
public abstract class TenantEntity
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid TenantId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

/// <summary>Contenu traduisible : la langue est portée par la ligne.</summary>
public interface ILocalized
{
    string Locale { get; set; }
}

public static class Locales
{
    public const string FrenchCanada = "fr-CA";
    public const string Default = FrenchCanada;
}
