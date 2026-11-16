using System.Text.Json.Nodes;
using Hub.Modules.Channels.Application.Reels;
using Hub.Modules.Channels.Domain;
using Hub.SharedKernel.Time;
using Hub.Web.Infrastructure.Reels;

namespace Hub.UnitTests;

public class ReelTests
{
    private static DateTimeOffset Local(int d, int h) => BusinessTime.FromLocal(new DateTime(2026, 11, d, h, 0, 0));

    [Fact]
    public void Deux_reels_par_semaine_le_premier_par_l_IA_et_repartis()
    {
        // Semaine du lundi 16 au dimanche 22 novembre : 7 publications.
        var slots = Enumerable.Range(16, 7).Select(d => Local(d, 12)).ToList();
        var plan = ReelPlanner.Assign(slots, 2, new Dictionary<string, (int, int)>(), aiAllowed: true);
        Assert.Equal(2, plan.Count);
        Assert.Equal([ReelKinds.Ai, ReelKinds.Montage], plan.OrderBy(p => p.Key).Select(p => p.Value));
        Assert.True(plan.Keys.Max() - plan.Keys.Min() >= 3, "les deux Reels sont espacés dans la semaine");
    }

    [Fact]
    public void Les_reels_deja_prevus_et_le_budget_sont_respectes()
    {
        var slots = Enumerable.Range(16, 7).Select(d => Local(d, 12)).ToList();
        var week = ReelPlanner.WeekOf(slots[0]);
        Assert.Empty(ReelPlanner.Assign(slots, 2, new Dictionary<string, (int, int)> { [week] = (2, 1) }, true));
        var one = ReelPlanner.Assign(slots, 2, new Dictionary<string, (int, int)> { [week] = (1, 1) }, true);
        Assert.Equal(ReelKinds.Montage, Assert.Single(one).Value);
        Assert.All(ReelPlanner.Assign(slots, 3, new Dictionary<string, (int, int)>(), aiAllowed: false), p => Assert.Equal(ReelKinds.Montage, p.Value));
        Assert.Empty(ReelPlanner.Assign(slots, 0, new Dictionary<string, (int, int)>(), true));
    }

    [Fact]
    public void Une_semaine_a_cheval_compte_a_part()
    {
        // Samedi 21 et dimanche 22 (semaine 47), lundi 23 (semaine 48).
        var slots = new[] { Local(21, 9), Local(22, 9), Local(23, 9) };
        var plan = ReelPlanner.Assign(slots, 1, new Dictionary<string, (int, int)>(), true);
        Assert.Equal(2, plan.Count);
        Assert.All(plan.Values, k => Assert.Equal(ReelKinds.Ai, k));
    }

    [Theory]
    [InlineData("✨ Votre maison brille cet hiver ! Réservez dès maintenant https://scintinet.com #menage", "Votre maison brille cet hiver")]
    [InlineData("Court.\nUn ménage complet, planchers sans calcium, entrée et tapis entretenus, maison prête pour les Fêtes et vos invités", "Un ménage complet, planchers sans calcium, entrée et tapis…")]
    public void L_accroche_est_la_premiere_phrase_nettoyee(string message, string expected)
    {
        var hook = ReelService.Hook(message);
        Assert.Equal(expected, hook);
        Assert.True(hook.Length <= 71);
    }

    [Fact]
    public void La_fin_affiche_le_site_et_la_scene_ne_demande_aucun_texte()
    {
        Assert.Equal("Devis gratuit · scintinet.com", ReelService.Cta("https://www.scintinet.com"));
        Assert.Equal("Devis gratuit", ReelService.Cta(null));
        var prompt = ReelService.ScenePrompt(new Post { Message = "Une cuisine qui brille", SceneNote = "cuisine lumineuse nettoyée" }, 12);
        Assert.Contains("9:16", prompt);
        Assert.Contains("winter", prompt);
        Assert.Contains("cuisine lumineuse nettoyée", prompt);
        Assert.Contains("No text", prompt);
    }

    [Fact]
    public void Les_segments_s_enchainent_par_un_fondu()
    {
        var filter = new List<string> { "a", "b", "c" };
        var (output, seconds) = ReelStudio.Join(filter, [5, 5, 4], 0.5);
        Assert.Equal("[x2]", output);
        Assert.Equal(13, seconds, 3);
        Assert.Contains("[v0][v1]xfade=transition=fade:duration=0.5:offset=4.5[x1]", filter);
        Assert.Contains("[x1][v2]xfade=transition=fade:duration=0.5:offset=9[x2]", filter);
        Assert.Equal(("[v0]", 9.0), ReelStudio.Join([], [9], 0.5));
    }

    [Fact]
    public void Le_format_d_image_se_lit_dans_les_premiers_octets()
    {
        Assert.Equal("png", ReelStudio.Extension([0x89, 0x50, 0x4E, 0x47, 0, 0, 0, 0, 0, 0, 0, 0, 0]));
        Assert.Equal("jpg", ReelStudio.Extension([0xFF, 0xD8, 0xFF, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0]));
        Assert.Equal("webp", ReelStudio.Extension("RIFF0000WEBPVP8 "u8.ToArray()));
    }

    [Fact]
    public void La_reponse_de_Google_donne_l_adresse_de_la_video()
    {
        var done = JsonNode.Parse("""{"done":true,"response":{"generateVideoResponse":{"generatedSamples":[{"video":{"uri":"https://exemple/v.mp4"}}]}}}""")!;
        Assert.Equal("https://exemple/v.mp4", GeminiVideoGenerator.VideoUri(done));
        Assert.Contains("Video__Model", GeminiVideoGenerator.Error(null, 404, "veo-x"));
        Assert.Contains("facturation", GeminiVideoGenerator.Error(null, 403, "veo-x"));
    }
}
