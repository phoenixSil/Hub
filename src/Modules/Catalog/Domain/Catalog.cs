using Hub.SharedKernel.Domain;

namespace Hub.Modules.Catalog.Domain;

/// <summary>Regroupe les services (ex. « Entretien » sous /services, « Aînés et soutien » sous /aines).</summary>
public sealed class ServiceCategory : TenantEntity, ILocalized
{
    public string Name { get; set; } = string.Empty;
    public string Slug { get; set; } = string.Empty;

    /// <summary>Premier segment de l'URL publique des services de la catégorie.</summary>
    public string RouteSegment { get; set; } = "services";
    public string? Intro { get; set; }
    public int SortOrder { get; set; }
    public string Locale { get; set; } = Locales.Default;
}

/// <summary>Un service offert, avec sa propre page publique.</summary>
public sealed class Service : TenantEntity, ILocalized
{
    public Guid CategoryId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Slug { get; set; } = string.Empty;
    public string Summary { get; set; } = string.Empty;

    /// <summary>Contenu de la page, en texte simple : un paragraphe par ligne vide, « - » pour une liste.</summary>
    public string? Body { get; set; }
    public string? ImageUrl { get; set; }
    public string? Icon { get; set; }
    public string? SeoTitle { get; set; }
    public string? SeoDescription { get; set; }
    public int SortOrder { get; set; }
    public bool IsActive { get; set; } = true;
    public bool ShowInQuoteForm { get; set; } = true;
    public string Locale { get; set; } = Locales.Default;
}

public enum PricingKind
{
    OnAssessment = 0,
    Hourly = 1,
    PerShift = 2,
    Flat = 3,
    Monthly = 4,
}

/// <summary>
/// Façon de facturer un service. Masqué du site tant que l'analyse des prix
/// n'est pas faite ; c'est aussi la base de travail du futur agent IA.
/// </summary>
public sealed class PricingModel : TenantEntity
{
    public Guid ServiceId { get; set; }
    public PricingKind Kind { get; set; } = PricingKind.OnAssessment;
    public decimal? MinAmount { get; set; }
    public decimal? MaxAmount { get; set; }
    public decimal? MinimumHours { get; set; }
    public decimal? ShiftHours { get; set; }

    /// <summary>Notes internes (hypothèses, sources) : jamais affichées sur le site.</summary>
    public string? Notes { get; set; }

    /// <summary>Précision affichée sous le prix sur le site (ex. « Taxes en sus »).</summary>
    public string? PublicNote { get; set; }
    public bool IsPublic { get; set; }
}
