using System.Text.Json.Nodes;
using Hub.Modules.Channels.Application.Reels;
using Hub.Modules.Channels.Domain;
using Hub.SharedKernel.Time;
using Hub.Web.Infrastructure.Reels;

namespace Hub.UnitTests;

public class ReelTests
{
    private static DateTimeOffset Local(int d, int h) => BusinessTime.FromLocal(new DateTime(2026, 11, d, h, 0, 0));

    private static readonly ReelCapacity Full = new(AiReels: 100, StockAvailable: true);

    private static List<ReelSlot> Week(int perDay, string? format = "astuce") =>
        Enumerable.Range(16, 7).SelectMany(d => new[] { 9, 12, 18 }.Take(perDay).Select(h => new ReelSlot(Local(d, h), format))).ToList();

    [Theory]
    [InlineData(1, 4)]   // 7 publications : 45 % = 3,15 → 4 Reels, sur 4 jours différents
    [InlineData(2, 7)]   // 14 publications : 45 % = 6,3 → 7 Reels, un par jour
    [InlineData(3, 7)]   // 21 publications : 45 % = 9,45 → plafonné à un par jour
    public void Au_moins_45_pour_cent_de_reels_et_jamais_plus_d_un_par_jour(int perDay, int expected)
    {
        for (var seed = 0; seed < 50; seed++)
        {
            var slots = Week(perDay);
            var plan = ReelPlanner.Assign(slots, 45, new HashSet<DateOnly>(), Full, new Random(seed));
            Assert.Equal(slots.Count, plan.Count);
            var reels = plan.Select((m, i) => (m, i)).Where(x => x.m.IsReel).ToList();
            Assert.Equal(expected, reels.Count);
            Assert.All(reels.GroupBy(x => ReelPlanner.DayOf(slots[x.i].At)), g => Assert.Single(g));
            Assert.All(plan, m => Assert.InRange(m.Propensity, 0.0001, 1));
        }
    }

    [Fact]
    public void Les_probabilites_notees_sont_justes()
    {
        // 2 publications par jour, 7 Reels sur 7 jours : chaque publication a 1 chance sur 2 d'être le Reel du jour.
        var plan = ReelPlanner.Assign(Week(2), 45, new HashSet<DateOnly>(), Full, new Random(3));
        Assert.All(plan.Where(m => !m.IsReel), m => Assert.Equal(0.5, m.Propensity, 4));
        var weights = ReelPlanner.Weights.ToDictionary(w => w.Kind, w => w.Weight);
        Assert.All(plan.Where(m => m.IsReel), m => Assert.Equal(0.5 * weights[m.Kind], m.Propensity, 4));
    }

    [Fact]
    public void Le_tirage_melange_les_types_et_suit_les_poids()
    {
        var counts = new Dictionary<string, int>();
        for (var seed = 0; seed < 400; seed++)
            foreach (var m in ReelPlanner.Assign(Week(1), 100, new HashSet<DateOnly>(), Full, new Random(seed)).Where(m => m.IsReel))
                counts[m.Kind] = counts.GetValueOrDefault(m.Kind) + 1;
        var total = counts.Values.Sum();
        foreach (var (kind, weight) in ReelPlanner.Weights)
            Assert.InRange(counts.GetValueOrDefault(kind) / (double)total, weight - 0.05, weight + 0.05);
        Assert.False(counts.ContainsKey(ReelKinds.BeforeAfter), "l'avant/après vient de vos propres vidéos");
    }

    [Fact]
    public void Budget_archives_et_formats_preuve_sont_respectes()
    {
        // Plus de budget IA ni de clé Pexels : images animées seulement.
        var none = ReelPlanner.Assign(Week(1), 100, new HashSet<DateOnly>(), new ReelCapacity(0, false), new Random(1));
        Assert.All(none, m => Assert.Equal(ReelKinds.Montage, m.Kind));
        // Budget pour 2 Reels IA seulement : jamais plus de 2.
        for (var seed = 0; seed < 30; seed++)
            Assert.True(ReelPlanner.Assign(Week(1), 100, new HashSet<DateOnly>(), new ReelCapacity(2, true), new Random(seed))
                .Count(m => ReelKinds.IsAi(m.Kind)) <= 2);
        // Coulisses, portrait, avant/après : jamais d'archives ni d'IA (ils ne montrent pas notre équipe).
        var proof = ReelPlanner.Assign(Week(1, "coulisses"), 100, new HashSet<DateOnly>(), Full, new Random(2));
        Assert.All(proof.Where(m => m.IsReel), m => Assert.Equal(ReelKinds.Montage, m.Kind));
    }

    [Fact]
    public void Les_jours_qui_ont_deja_un_reel_et_la_part_nulle_sont_respectes()
    {
        var slots = Week(2);
        var taken = Enumerable.Range(16, 5).Select(d => DateOnly.FromDateTime(new DateTime(2026, 11, d))).ToHashSet();
        var plan = ReelPlanner.Assign(slots, 45, taken, Full, new Random(4));
        // 7 visés, 5 déjà prévus : 2 de plus, sur les deux jours libres (21 et 22).
        var reels = plan.Select((m, i) => (m, i)).Where(x => x.m.IsReel).ToList();
        Assert.Equal(2, reels.Count);
        Assert.All(reels, x => Assert.DoesNotContain(ReelPlanner.DayOf(slots[x.i].At), taken));
        Assert.All(plan.Select((m, i) => (m, i)).Where(x => taken.Contains(ReelPlanner.DayOf(slots[x.i].At))), x => Assert.Equal(1, x.m.Propensity));
        Assert.All(ReelPlanner.Assign(slots, 0, new HashSet<DateOnly>(), Full, new Random(4)), m => Assert.False(m.IsReel));
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
        Assert.Contains("realistic", prompt);
        var film = ReelService.ScenePrompt(new Post { Message = "Une cuisine qui brille" }, 7, cinematic: true);
        Assert.Contains("golden-hour", film);
        Assert.Contains("summer", film);
    }

    [Fact]
    public void Les_recherches_d_archives_suivent_le_sujet_et_la_saison()
    {
        var q = ReelService.StockQueries(new Post { Message = "Vos planchers sans calcium et une cuisine qui brille" }, 12);
        Assert.Equal(3, q.Count);
        Assert.Contains("cleaning kitchen counter", q);
        Assert.Contains("mopping floor", q);
        Assert.Equal("snowy house winter", q[^1]);
        Assert.Equal(["house cleaning", "clean bright living room", "autumn leaves house"], ReelService.StockQueries(new Post { Message = "Bonjour" }, 10));
    }

    [Fact]
    public void Pexels_donne_un_fichier_vertical_jamais_deux_fois_la_meme_video()
    {
        var json = JsonNode.Parse("""
            {"videos":[
              {"id":1,"duration":3,"video_files":[{"file_type":"video/mp4","width":1080,"height":1920,"link":"https://v/court.mp4"}]},
              {"id":2,"duration":12,"video_files":[
                {"file_type":"video/mp4","width":1920,"height":1080,"link":"https://v/paysage.mp4"},
                {"file_type":"video/mp4","width":540,"height":960,"link":"https://v/petit.mp4"},
                {"file_type":"video/mp4","width":720,"height":1280,"link":"https://v/720.mp4"},
                {"file_type":"video/mp4","width":1080,"height":1920,"link":"https://v/1080.mp4"}]}
            ]}
            """);
        var used = new HashSet<string>();
        Assert.Equal("https://v/1080.mp4", PexelsVideoSource.PickFile(json, used, new Random(1)));
        Assert.Null(PexelsVideoSource.PickFile(json, used, new Random(1)));
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
