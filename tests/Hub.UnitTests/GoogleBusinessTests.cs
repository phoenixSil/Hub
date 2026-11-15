using System.Text.Json.Nodes;
using Hub.Modules.Channels.Application.Reviews;
using Hub.Modules.Channels.Domain;
using Hub.Modules.Channels.Publishers;

namespace Hub.UnitTests;

public class GoogleBusinessTests
{
    [Fact]
    public void Une_publication_Google_perd_les_numeros_de_telephone_et_les_mots_clics()
    {
        var text = GoogleBusinessPublisher.Summary("Votre maison brille ✨\nAppelez-nous : 418 555-1234\n\n#menage #quebec");
        Assert.DoesNotContain("555", text);
        Assert.DoesNotContain("#menage", text);
        Assert.StartsWith("Votre maison brille", text);
    }

    [Fact]
    public void Une_publication_Google_est_coupee_a_1500_caracteres()
    {
        var text = GoogleBusinessPublisher.Summary(new string('a', 3000));
        Assert.True(text.Length <= 1500);
        Assert.EndsWith("…", text);
    }

    [Fact]
    public void La_fiche_choisie_est_celle_qui_porte_le_nom_de_l_entreprise()
    {
        ChannelTarget[] fiches =
        [
            new("accounts/1/locations/9", "GIBRALTECH SARL", "Douala"),
            new("accounts/1/locations/7", "Entretien Scintinet S.E.N.C", "12 rue Boucher, Québec"),
        ];
        Assert.Equal("accounts/1/locations/7", GoogleBusinessPublisher.BestTarget(fiches, "Scintinet").Id);
        Assert.Equal("accounts/1/locations/9", GoogleBusinessPublisher.BestTarget(fiches, null).Id);
    }

    [Theory]
    [InlineData("FIVE", 5)]
    [InlineData("ONE", 1)]
    [InlineData("STAR_RATING_UNSPECIFIED", 0)]
    [InlineData(null, 0)]
    public void Les_etoiles_de_Google_deviennent_un_chiffre(string? rating, int stars) =>
        Assert.Equal(stars, GoogleBusinessPublisher.Stars(rating));

    [Fact]
    public void Les_vues_additionnent_recherche_et_Maps()
    {
        var json = JsonNode.Parse("""
            { "multiDailyMetricTimeSeries": [ { "dailyMetricTimeSeries": [
              { "dailyMetric": "BUSINESS_IMPRESSIONS_MOBILE_SEARCH", "timeSeries": { "datedValues": [ { "value": "10" }, { "value": "5" } ] } },
              { "dailyMetric": "BUSINESS_IMPRESSIONS_DESKTOP_MAPS", "timeSeries": { "datedValues": [ { "value": "3" }, {} ] } },
              { "dailyMetric": "CALL_CLICKS", "timeSeries": { "datedValues": [ { "value": "2" } ] } },
              { "dailyMetric": "WEBSITE_CLICKS", "timeSeries": { "datedValues": [ { "value": "4" } ] } }
            ] } ] }
            """)!;
        Assert.Equal(new LocationMetrics(18, 2, 0, 4), GoogleBusinessPublisher.Metrics(json));
    }

    [Fact]
    public void Un_refus_de_droits_est_signale_comme_permission()
    {
        Assert.Contains("permission", GoogleBusinessPublisher.Error(null, 403));
        Assert.Contains("permission", GoogleBusinessPublisher.Error(null, 401));
    }

    [Fact]
    public void La_reponse_de_repli_depend_de_la_note()
    {
        var good = ReviewService.Fallback(new GoogleReview { Author = "Marie Tremblay", Rating = 5 }, "Scintinet");
        var bad = ReviewService.Fallback(new GoogleReview { Author = "J.", Rating = 2 }, "Scintinet");
        Assert.StartsWith("Bonjour Marie,", good);
        Assert.Contains("merci beaucoup", good);
        Assert.StartsWith("Bonjour,", bad);
        Assert.Contains("désolés", bad);
        Assert.EndsWith("L'équipe Scintinet", bad);
    }
}
