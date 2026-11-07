using Hub.Web.Infrastructure;

namespace Hub.UnitTests;

public class LegacyRedirectTests
{
    private static readonly string[] Live =
    [
        "/", "/devis", "/contact", "/faq", "/secteurs", "/a-propos", "/confidentialite",
        "/services/menage-residentiel", "/services/grand-menage", "/aines/aide-a-domicile", "/secteurs/laval",
    ];

    private static string? Go(string path) => LegacyRedirectResolver.Resolve(path, Live, LegacyRedirectResolver.DefaultAliases);

    [Theory]
    [InlineData("/devis")]
    [InlineData("/services/grand-menage")]
    [InlineData("/")]
    [InlineData("/css/site.css")]
    [InlineData("/admin/publications")]
    [InlineData("/une-page-qui-na-jamais-existe")]
    public void Ne_touche_pas_aux_bonnes_adresses_ni_a_l_inconnu(string path) => Assert.Null(Go(path));

    [Theory]
    [InlineData("/Devis/", "/devis")]
    [InlineData("/contact.html", "/contact")]
    [InlineData("/fr/faq", "/faq")]
    [InlineData("/index.php", "/")]
    [InlineData("/contactez-nous/", "/contact")]
    [InlineData("/qui-sommes-nous", "/a-propos")]
    [InlineData("/soumission-gratuite", "/devis")]
    [InlineData("/politique-de-confidentialite", "/confidentialite")]
    [InlineData("/service/menage-residentiel", "/services/menage-residentiel")]
    [InlineData("/menage-residentiel", "/services/menage-residentiel")]
    [InlineData("/menage-residentiel-montreal", "/services/menage-residentiel")]
    [InlineData("/aide-a-domicile/", "/aines/aide-a-domicile")]
    [InlineData("/laval", "/secteurs/laval")]
    public void Redirige_les_anciennes_adresses(string path, string expected) => Assert.Equal(expected, Go(path));
}
