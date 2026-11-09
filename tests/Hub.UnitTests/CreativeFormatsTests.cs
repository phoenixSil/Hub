using Hub.Modules.Channels.Application;
using Hub.Modules.Channels.Application.Autopilot;
using Hub.SharedKernel.Time;

namespace Hub.UnitTests;

public class CreativeFormatsTests
{
    [Fact]
    public void Une_semaine_de_sept_publications_n_utilise_jamais_deux_fois_le_meme_format()
    {
        var pillars = new string?[] { "Conseiller", "Montrer", "Conseiller", "Rassurer", "Proposer", "Conseiller", "Montrer" };
        var formats = CreativeFormats.Assign(pillars, [], new Random(3));

        Assert.Equal(7, formats.Count);
        Assert.Equal(7, formats.Distinct().Count());
        for (var i = 0; i < pillars.Length; i++)
            Assert.Contains(pillars[i]!, CreativeFormats.Find(formats[i])!.Pillars);
    }

    [Fact]
    public void Les_formats_les_moins_recents_passent_en_premier()
    {
        // « astuce » et « vu-du-dessus » viennent de servir : un pilier Conseiller prend un autre format.
        var next = CreativeFormats.Assign(["Conseiller"], ["astuce", "vu-du-dessus", "gros-plan"], new Random(1));
        Assert.DoesNotContain(next[0], new[] { "astuce", "vu-du-dessus", "gros-plan" });
    }

    [Fact]
    public void Le_public_des_aines_recoit_son_format_une_fois_par_semaine()
    {
        var used = new HashSet<string>();
        Assert.Equal("aines", CreativeFormats.ForAudience("humour", "Aînés et leurs proches", used));
        Assert.Equal("humour", CreativeFormats.ForAudience("humour", "Aînés et leurs proches", used));
        Assert.Equal("question", CreativeFormats.ForAudience("question", "Familles occupées", used));
    }

    [Fact]
    public void Chaque_format_a_une_technique_une_direction_photo_et_des_mises_en_page()
    {
        Assert.True(CreativeFormats.All.Count >= 10);
        Assert.Equal(CreativeFormats.All.Count, CreativeFormats.All.Select(f => f.Key).Distinct().Count());
        foreach (var f in CreativeFormats.All)
        {
            Assert.False(string.IsNullOrWhiteSpace(f.Copy));
            Assert.False(string.IsNullOrWhiteSpace(f.Photo));
            Assert.NotEmpty(f.Layouts);
            Assert.NotEmpty(f.Pillars);
        }
    }

    [Fact]
    public void Le_pilote_impose_un_format_par_creneau_dans_sa_consigne()
    {
        var slots = Enumerable.Range(0, 3).Select(i => BusinessTime.FromLocal(new DateTime(2026, 11, 10 + i))).ToList();
        var context = new AutopilotContext("ScintiNet", "Québec", "418", "#0B5375", "#7CC242", "https://www.scintinet.com", "a@b.c",
            [new AutopilotPage("Ménage résidentiel", "/services/menage", "Ménage régulier", null, false, null)]);
        var request = new AutopilotPlanRequest(context, AutopilotService.DefaultAudiences, slots, [], null, slots[0].AddDays(-1),
            Formats: ["humour", "question", "avant-apres"]);

        var prompt = AutopilotPlanner.UserPrompt(request, AutopilotService.DefaultAudiences);
        Assert.Contains("format « Situation exagérée (humour) »", prompt);
        Assert.Contains("format « Question à la communauté »", prompt);

        var rotation = AutopilotPlanner.Rotation(request, AutopilotService.DefaultAudiences);
        Assert.Equal("humour", rotation[0].Format);
        Assert.Equal("aines", rotation[1].Format); // 2e public par défaut : les aînés
        Assert.Equal("avant-apres", rotation[2].Format);
    }
}
