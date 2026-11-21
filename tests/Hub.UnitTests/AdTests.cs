using System.Text.Json.Nodes;
using Hub.Modules.Channels;
using Hub.Modules.Channels.Application.Ads;
using Hub.Modules.Channels.Domain;
using Hub.Modules.Channels.Publishers;

namespace Hub.UnitTests;

public class AdTests
{
    private static readonly AdMonth NoCap = new(0, null);

    private static AdDraft Boost() => new()
    {
        Kind = AdKind.Boost, PostId = Guid.NewGuid(), Source = ChannelType.Facebook, Objective = AdObjective.Engagement, Budget = 20, Days = 7,
    };

    private static AdDraft Campaign() => new()
    {
        Kind = AdKind.Campaign, PostId = Guid.NewGuid(), Objective = AdObjective.Quotes, Budget = 50, Days = 10,
        Headline = "Ménage à Québec : devis gratuit", Message = "Une maison propre sans y passer vos fins de semaine.", LinkPath = "/devis",
    };

    [Fact]
    public void Un_boost_et_une_campagne_valides_passent()
    {
        Assert.Empty(AdRules.Validate(Boost(), NoCap));
        Assert.Empty(AdRules.Validate(Campaign(), NoCap));
    }

    [Fact]
    public void Le_budget_respecte_le_minimum_de_Meta_et_le_plafond_facultatif()
    {
        var tooSmall = Boost();
        tooSmall.Budget = 10;
        tooSmall.Days = 7;
        Assert.Contains("14 $", AdRules.Validate(tooSmall, NoCap)[nameof(AdDraft.Budget)]);

        // Sans plafond : seul le budget de chaque publicité compte.
        var big = Boost();
        big.Budget = 900;
        Assert.Empty(AdRules.Validate(big, new AdMonth(4000, null)));

        // Avec un plafond de 100 $ et 90 $ engagés : 20 $ de plus est refusé, 10 $ passe.
        var month = new AdMonth(90, 100);
        Assert.Contains("plafond", AdRules.Validate(Boost(), month)[nameof(AdDraft.Budget)]);
        var ten = Boost();
        ten.Budget = 10;
        ten.Days = 5;
        Assert.Empty(AdRules.Validate(ten, month));
    }

    [Fact]
    public void Les_champs_obligatoires_et_le_public_sont_verifies()
    {
        var c = Campaign();
        c.Headline = "";
        c.Message = null;
        c.Objective = AdObjective.Engagement;
        c.Facebook = c.Instagram = false;
        c.RadiusKm = 200;
        c.AgeMin = 40;
        c.AgeMax = 30;
        var e = AdRules.Validate(c, NoCap);
        Assert.True(e.ContainsKey(nameof(AdDraft.Headline)) && e.ContainsKey(nameof(AdDraft.Message)) && e.ContainsKey(nameof(AdDraft.Objective)));
        Assert.True(e.ContainsKey(nameof(AdDraft.Facebook)) && e.ContainsKey(nameof(AdDraft.RadiusKm)) && e.ContainsKey(nameof(AdDraft.AgeMin)));

        var b = Boost();
        b.Source = ChannelType.GoogleBusiness;
        b.Objective = AdObjective.Quotes;
        var eb = AdRules.Validate(b, NoCap);
        Assert.True(eb.ContainsKey(nameof(AdDraft.Source)) && eb.ContainsKey(nameof(AdDraft.Objective)));
    }

    [Fact]
    public void Les_dates_ne_commencent_jamais_dans_le_passe()
    {
        var now = new DateTimeOffset(2026, 11, 21, 15, 0, 0, TimeSpan.Zero);
        var d = Boost();
        d.StartAt = now.AddDays(-1);
        var (start, end) = AdRules.Window(d, now);
        Assert.Equal(now.AddMinutes(5), start);
        Assert.Equal(start.AddDays(7), end);
        d.StartAt = now.AddDays(2);
        Assert.Equal(now.AddDays(2), AdRules.Window(d, now).Start);
    }

    [Theory]
    [InlineData(AdStatus.InReview, "ACTIVE", 1, AdStatus.Active)]
    [InlineData(AdStatus.InReview, "PENDING_REVIEW", 1, AdStatus.InReview)]
    [InlineData(AdStatus.InReview, "DISAPPROVED", 1, AdStatus.Rejected)]
    [InlineData(AdStatus.Active, "CAMPAIGN_PAUSED", 1, AdStatus.Paused)]
    [InlineData(AdStatus.Active, "ACTIVE", -1, AdStatus.Completed)]
    [InlineData(AdStatus.Stopped, "ACTIVE", 1, AdStatus.Stopped)]
    [InlineData(AdStatus.Active, "WITH_ISSUES", 1, AdStatus.Active)]
    public void L_etat_suit_Meta_et_les_dates(AdStatus current, string effective, int daysLeft, AdStatus expected)
    {
        var now = new DateTimeOffset(2026, 11, 21, 15, 0, 0, TimeSpan.Zero);
        Assert.Equal(expected, AdRules.Map(current, effective, now.AddDays(daysLeft), now));
    }

    [Fact]
    public void Le_lien_de_l_annonce_est_mesure()
    {
        Assert.Equal("https://www.scintinet.com/devis?utm_source=meta&utm_medium=paid&utm_campaign=pub-1",
            AdRules.Link("https://www.scintinet.com/", "/devis", "pub-1"));
        Assert.Null(AdRules.Link(null, "/devis", "pub-1"));
    }

    [Fact]
    public void Le_public_reste_dans_le_rayon_sans_elargissement_automatique()
    {
        var t = MetaAdsPlatform.Targeting(new AdAudience(46.8139, -71.208, 35, 25, 65, true, false));
        var place = t["geo_locations"]!["custom_locations"]![0]!;
        Assert.Equal(35, place["radius"]!.GetValue<int>());
        Assert.Equal("kilometer", place["distance_unit"]!.GetValue<string>());
        Assert.Equal(0, t["targeting_automation"]!["advantage_audience"]!.GetValue<int>());
        Assert.Equal(["facebook"], t["publisher_platforms"]!.AsArray().Select(x => x!.GetValue<string>()));
        Assert.Equal(65, t["age_max"]!.GetValue<int>());
    }

    [Fact]
    public void Objectifs_montants_et_boutons_au_format_de_Meta()
    {
        Assert.Equal(("OUTCOME_ENGAGEMENT", "POST_ENGAGEMENT", "ON_POST"), MetaAdsPlatform.Plan(AdObjective.Engagement));
        Assert.Equal(("OUTCOME_AWARENESS", "REACH", (string?)null), MetaAdsPlatform.Plan(AdObjective.Reach));
        Assert.Equal(("OUTCOME_TRAFFIC", "LINK_CLICKS", "WEBSITE"), MetaAdsPlatform.Plan(AdObjective.Quotes));
        Assert.Equal("2050", MetaAdsPlatform.Cents(20.5m));
        Assert.Equal("GET_QUOTE", MetaAdsPlatform.CallToAction(AdObjective.Quotes, "https://x")["type"]!.GetValue<string>());
        var spec = MetaAdsPlatform.StorySpec("123", null, "link_data", new JsonObject { ["link"] = "https://x" });
        Assert.Null(spec["instagram_user_id"]);
        Assert.Equal("https://x", spec["link_data"]!["link"]!.GetValue<string>());
    }

    [Fact]
    public void Les_chiffres_et_les_refus_de_Meta_sont_lus()
    {
        var ad = JsonNode.Parse("""{"effective_status":"DISAPPROVED","ad_review_feedback":{"global":{"Texte trompeur":"L'annonce promet un résultat garanti."}}}""")!;
        var insights = JsonNode.Parse("""{"data":[{"spend":"12.34","reach":"2100","impressions":"3500","inline_link_clicks":"41","actions":[{"action_type":"post_engagement","value":"77"},{"action_type":"link_click","value":"41"}]}]}""")!;
        var s = MetaAdsPlatform.Snapshot(ad, insights);
        Assert.Equal("DISAPPROVED", s.EffectiveStatus);
        Assert.Contains("résultat garanti", s.ReviewMessage);
        Assert.Equal(12.34m, s.Spend);
        Assert.Equal((2100, 3500, 41, 77), (s.Reach, s.Impressions, s.Clicks, s.Engagements));
        var empty = MetaAdsPlatform.Snapshot(JsonNode.Parse("""{"effective_status":"PENDING_REVIEW"}""")!, JsonNode.Parse("""{"data":[]}""")!);
        Assert.Equal((0m, 0), (empty.Spend, empty.Reach));

        Assert.Contains("reconnectez", MetaAdsPlatform.ErrorOf(JsonNode.Parse("""{"error":{"code":190,"message":"x"}}""")));
        Assert.Contains("ads_management", MetaAdsPlatform.ErrorOf(JsonNode.Parse("""{"error":{"code":200,"message":"no"}}""")));
        Assert.Contains("Budget trop bas", MetaAdsPlatform.ErrorOf(JsonNode.Parse("""{"error":{"code":100,"message":"Invalid","error_user_msg":"Budget trop bas"}}""")));
    }

    [Fact]
    public void Kairos_suggere_le_meilleur_taux_d_interaction_avec_assez_de_portee()
    {
        BoostTarget T(string title, int reach, int interactions) => new(Guid.NewGuid(), title, null, false, [ChannelType.Facebook], reach, interactions);
        var best = AdService.Suggest([T("peu vue", 10, 9), T("bonne", 200, 30), T("moyenne", 1000, 50), T("rien", 500, 0)]);
        Assert.Equal("bonne", best?.Title);
        Assert.Null(AdService.Suggest([T("peu vue", 10, 9)]));
    }

    [Fact]
    public void Le_plafond_compte_le_budget_engage_ou_la_depense_reelle()
    {
        Assert.Equal(50, new AdCampaign { Status = AdStatus.Active, Budget = 50, Spend = 12 }.Committed);
        Assert.Equal(12, new AdCampaign { Status = AdStatus.Stopped, Budget = 50, Spend = 12 }.Committed);
        Assert.Equal(0, new AdCampaign { Status = AdStatus.Failed, Budget = 50 }.Committed);
        Assert.Equal(40, new AdMonth(60, 100).Remaining);
        Assert.Null(new AdMonth(60, null).Remaining);
    }
}
