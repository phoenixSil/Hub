using Hub.SharedKernel.Domain;

namespace Hub.Modules.Content.Domain;

/// <summary>Secteur desservi, avec sa page publique (/secteurs/{slug}).</summary>
public sealed class ServiceArea : TenantEntity, ILocalized
{
    public string Name { get; set; } = string.Empty;
    public string Slug { get; set; } = string.Empty;

    /// <summary>Texte propre au secteur : c'est lui qui distingue la page aux yeux de Google.</summary>
    public string? Intro { get; set; }
    public int SortOrder { get; set; }
    public bool IsActive { get; set; } = true;
    public string Locale { get; set; } = Locales.Default;
}

/// <summary>Page libre (à propos, confidentialité, etc.).</summary>
public sealed class Page : TenantEntity, ILocalized
{
    public string Slug { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string? Lead { get; set; }

    /// <summary>Texte simple : « ## » pour un intertitre, « - » pour une liste, ligne vide entre paragraphes.</summary>
    public string Body { get; set; } = string.Empty;
    public string? SeoTitle { get; set; }
    public string? SeoDescription { get; set; }
    public bool IsPublished { get; set; } = true;
    public string Locale { get; set; } = Locales.Default;
}

/// <summary>Diapositive du carrousel d'accueil (offre saisonnière, promotion).</summary>
public sealed class Promotion : TenantEntity, ILocalized
{
    public string Badge { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string? Highlight { get; set; }
    public string Text { get; set; } = string.Empty;
    public string? ImageUrl { get; set; }
    public string PrimaryLabel { get; set; } = "Demander un devis";
    public string PrimaryUrl { get; set; } = "/devis";
    public string? SecondaryLabel { get; set; }
    public string? SecondaryUrl { get; set; }
    public DateOnly? StartsOn { get; set; }
    public DateOnly? EndsOn { get; set; }
    public int SortOrder { get; set; }
    public bool IsActive { get; set; } = true;
    public string Locale { get; set; } = Locales.Default;

    public bool IsLiveOn(DateOnly day) =>
        IsActive && (StartsOn is null || StartsOn <= day) && (EndsOn is null || EndsOn >= day);
}

public sealed class FaqItem : TenantEntity, ILocalized
{
    public string Question { get; set; } = string.Empty;
    public string Answer { get; set; } = string.Empty;

    /// <summary>Slug du service concerné ; vide = question générale.</summary>
    public string? ServiceSlug { get; set; }
    public int SortOrder { get; set; }
    public bool IsActive { get; set; } = true;
    public string Locale { get; set; } = Locales.Default;
}

public enum ReviewSource
{
    Direct = 0,
    Google = 1,
    Facebook = 2,
}

/// <summary>Avis d'un vrai client. Seuls les avis approuvés sont publiés.</summary>
public sealed class Review : TenantEntity
{
    public string AuthorName { get; set; } = string.Empty;
    public string? AuthorContext { get; set; }
    public int Rating { get; set; } = 5;
    public string Text { get; set; } = string.Empty;
    public ReviewSource Source { get; set; } = ReviewSource.Direct;
    public string? SourceUrl { get; set; }
    public DateOnly ReviewedOn { get; set; } = DateOnly.FromDateTime(DateTime.UtcNow);
    public bool IsApproved { get; set; }
}

/// <summary>Bloc de texte d'une section du site (ex. « home.about »).</summary>
public sealed class ContentBlock : TenantEntity, ILocalized
{
    public string Key { get; set; } = string.Empty;
    public string? Eyebrow { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? Body { get; set; }
    public string Locale { get; set; } = Locales.Default;
}

/// <summary>Élément d'une liste mise en avant : valeurs, avantages, étapes.</summary>
public sealed class HighlightItem : TenantEntity, ILocalized
{
    /// <summary>Groupe : « values », « advantages », « steps ».</summary>
    public string Group { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Text { get; set; } = string.Empty;
    public string? Icon { get; set; }
    public int SortOrder { get; set; }
    public string Locale { get; set; } = Locales.Default;
}

public static class HighlightGroups
{
    public const string Values = "values";
    public const string Advantages = "advantages";
    public const string Steps = "steps";
}
