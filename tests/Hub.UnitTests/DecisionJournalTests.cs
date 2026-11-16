using Hub.Modules.Channels.Application.Decisions;
using Hub.Modules.Channels.Domain;
using Hub.SharedKernel.Time;

namespace Hub.UnitTests;

public class DecisionJournalTests
{
    private static DateTimeOffset Local(int y, int m, int d, int h, int min = 0) => BusinessTime.FromLocal(new DateTime(y, m, d, h, min, 0));

    [Fact]
    public void Le_creneau_vient_de_l_heure_locale_et_du_jour()
    {
        Assert.Equal("Matin · semaine", PublicationDecisions.Slot(Local(2026, 11, 17, 9)));
        Assert.Equal("Midi · semaine", PublicationDecisions.Slot(Local(2026, 11, 17, 12)));
        Assert.Equal("Soir · fin de semaine", PublicationDecisions.Slot(Local(2026, 11, 21, 18, 30)));
    }

    [Theory]
    [InlineData("Promotion", 1)]
    [InlineData("Service", 0.5)]
    [InlineData("Conseil", 0)]
    [InlineData("Aînés", 0)]
    [InlineData(null, 0)]
    public void L_intensite_vient_du_theme(string? theme, double tau) =>
        Assert.Equal(tau, PublicationDecisions.Intensity(theme));

    [Fact]
    public void Une_demande_pese_autant_que_cent_interactions()
    {
        var o = new DecisionOptions();
        var adapter = new PublicationDecisions();
        var lead = new Decision { Leads = 1 };
        var likes = new Decision { Interactions = 100 };
        Assert.Equal(adapter.Reward(lead, o), adapter.Reward(likes, o));
        Assert.Equal(119.93, adapter.Reward(new Decision { Leads = 1, Interactions = 10, Reach = 1000, Cost = 0.07m }, o));
    }

    [Fact]
    public void L_exploration_tire_un_creneau_du_meme_jour_avec_sa_probabilite()
    {
        var now = Local(2026, 11, 16, 8);
        var planned = Local(2026, 11, 18, 12);
        var random = new Random(42);
        int explored = 0;
        for (var i = 0; i < 2000; i++)
        {
            var r = PublicationDecisions.Explore(planned, 0.15, random, now);
            if (!r.Explored) { Assert.Equal(planned, r.At); Assert.Null(r.Propensity); continue; }
            explored++;
            Assert.Equal(1.0 / 3, r.Propensity);
            Assert.Equal(new DateOnly(2026, 11, 18), DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(r.At, BusinessTime.Zone).DateTime));
        }
        Assert.InRange(explored, 220, 380); // ≈ 15 % de 2000
    }

    [Fact]
    public void L_exploration_ne_tire_jamais_une_heure_passee()
    {
        var now = Local(2026, 11, 16, 13);                     // midi est passé
        var planned = Local(2026, 11, 16, 18, 30);
        var r = PublicationDecisions.Explore(planned, 1, new Random(1), now);
        Assert.False(r.Explored);                               // un seul créneau possible : pas de tirage
        Assert.False(PublicationDecisions.Explore(planned, 0, new Random(1), Local(2026, 11, 15, 8)).Explored);
    }

    [Fact]
    public void Le_contexte_note_la_saison_et_les_fetes()
    {
        Assert.Equal("hiver", PublicationDecisions.Season(12));
        Assert.Equal("Noël", PublicationDecisions.Holiday(new DateOnly(2026, 12, 18)));
        Assert.Null(PublicationDecisions.Holiday(new DateOnly(2026, 11, 16)));
        var json = PublicationDecisions.Context(Local(2026, 12, 20, 9), 14, true, new Dictionary<string, int> { ["Facebook"] = 120 }, 2, 1);
        Assert.Contains("\"saison\":\"hiver\"", json);
        Assert.Contains("\"intensif\":true", json);
    }

    [Theory]
    [InlineData("-12.5", "-12.5")]
    [InlineData("=SOMME(A1)", "'=SOMME(A1)")]
    [InlineData("a;b", "\"a;b\"")]
    public void L_export_CSV_garde_les_nombres_negatifs_et_neutralise_les_formules(string value, string expected) =>
        Assert.Equal(expected, DecisionJournal.Csv(value));
}
