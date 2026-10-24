using System.Text.Json;
using Hub.Modules.Content.Domain;
using Hub.Modules.Platform.Domain;

namespace Hub.Web.Infrastructure;

public static class SiteFormat
{
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = false };

    public static string Digits(string? phone) => new((phone ?? string.Empty).Where(char.IsDigit).ToArray());

    public static string PhoneHref(string? phone)
    {
        var d = Digits(phone);
        return d.Length == 10 ? $"tel:+1{d}" : $"tel:+{d}";
    }

    public static string? WhatsAppHref(string? number)
    {
        var d = Digits(number);
        if (d.Length == 0) return null;
        return $"https://wa.me/{(d.Length == 10 ? "1" + d : d)}";
    }

    public static string Logo(Tenant t) => string.IsNullOrWhiteSpace(t.LogoUrl) ? "/images/site/logo.png" : t.LogoUrl!;

    public static string LocalityLine(Tenant t) =>
        string.Join(", ", new[] { t.City, t.Region }.Where(x => !string.IsNullOrWhiteSpace(x)));

    /// <summary>Données structurées LocalBusiness pour Google.</summary>
    public static string LocalBusinessJsonLd(Tenant t, string baseUrl, IEnumerable<ServiceArea> areas, double? rating, int reviewCount)
    {
        var data = new Dictionary<string, object?>
        {
            ["@context"] = "https://schema.org",
            ["@type"] = "HomeAndConstructionBusiness",
            ["@id"] = baseUrl + "/#entreprise",
            ["name"] = t.Name,
            ["slogan"] = t.Tagline,
            ["url"] = baseUrl + "/",
            ["telephone"] = t.Phone,
            ["email"] = t.PublicEmail,
            ["image"] = baseUrl + Logo(t),
            ["logo"] = baseUrl + Logo(t),
            ["address"] = new Dictionary<string, object?>
            {
                ["@type"] = "PostalAddress",
                ["streetAddress"] = t.ShowStreetAddress ? t.StreetAddress : null,
                ["addressLocality"] = t.City,
                ["addressRegion"] = t.Region,
                ["postalCode"] = t.ShowStreetAddress ? t.PostalCode : null,
                ["addressCountry"] = t.Country,
            },
            ["areaServed"] = areas.Select(a => new Dictionary<string, object?> { ["@type"] = "Place", ["name"] = $"{a.Name}, {t.City}" }).ToList(),
            ["sameAs"] = new[] { t.FacebookUrl, t.InstagramUrl, t.YouTubeUrl, t.TikTokUrl, t.GoogleBusinessUrl }
                .Where(u => !string.IsNullOrWhiteSpace(u)).ToList(),
        };
        if (rating is { } r && reviewCount > 0)
        {
            data["aggregateRating"] = new Dictionary<string, object?>
            {
                ["@type"] = "AggregateRating",
                ["ratingValue"] = Math.Round(r, 1),
                ["reviewCount"] = reviewCount,
            };
        }
        return JsonSerializer.Serialize(data, Json);
    }

    public static string ServiceJsonLd(Tenant t, string baseUrl, string name, string description, string url, string? areaName = null) =>
        JsonSerializer.Serialize(new Dictionary<string, object?>
        {
            ["@context"] = "https://schema.org",
            ["@type"] = "Service",
            ["name"] = name,
            ["description"] = description,
            ["url"] = baseUrl + url,
            ["provider"] = new Dictionary<string, object?> { ["@id"] = baseUrl + "/#entreprise" },
            ["areaServed"] = new Dictionary<string, object?> { ["@type"] = "City", ["name"] = areaName ?? t.City },
        }, Json);

    public static string FaqJsonLd(IEnumerable<FaqItem> faq) =>
        JsonSerializer.Serialize(new Dictionary<string, object?>
        {
            ["@context"] = "https://schema.org",
            ["@type"] = "FAQPage",
            ["mainEntity"] = faq.Select(f => new Dictionary<string, object?>
            {
                ["@type"] = "Question",
                ["name"] = f.Question,
                ["acceptedAnswer"] = new Dictionary<string, object?> { ["@type"] = "Answer", ["text"] = f.Answer },
            }).ToList(),
        }, Json);

    public static string BaseUrl(Microsoft.AspNetCore.Components.NavigationManager nav) => nav.BaseUri.TrimEnd('/');
}
