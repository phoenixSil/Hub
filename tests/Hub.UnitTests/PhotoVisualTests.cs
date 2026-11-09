using System.Text.Json;
using Hub.Modules.Channels.Application;
using Hub.SharedKernel;
using Hub.SharedKernel.Ai;
using Hub.Web.Infrastructure;
using Microsoft.Extensions.Logging.Abstractions;
using SkiaSharp;

namespace Hub.UnitTests;

public class PhotoVisualTests
{
    private sealed class FakeAi(string json) : IAiTextGenerator
    {
        public bool IsAvailable => true;
        public string ModelName => "fake";
        public AiJsonRequest? Last { get; private set; }
        public Task<Result<AiJsonResponse>> GenerateJsonAsync(AiJsonRequest request, CancellationToken ct = default)
        {
            Last = request;
            return Task.FromResult(Result<AiJsonResponse>.Success(new AiJsonResponse(json, "claude-test", 1, 1)));
        }
    }

    private sealed class FakeImages(bool fail = false) : IAiImageGenerator
    {
        public string? LastPrompt { get; private set; }
        public bool IsAvailable => true;
        public string ModelName => "image-test";
        public int MonthlyLimit => 5;
        public Task<Result<AiImage>> GenerateAsync(AiImageRequest request, CancellationToken ct = default)
        {
            LastPrompt = request.Prompt;
            return Task.FromResult(fail ? Result<AiImage>.Failure("refus") : Result<AiImage>.Success(new AiImage(Photo(1400, 1000), "image/png", ModelName)));
        }
    }

    private sealed class FakeQuota(int used) : IImageQuota
    {
        public int Recorded { get; private set; }
        public Task<int> UsedThisMonthAsync(CancellationToken ct = default) => Task.FromResult(used);
        public Task RecordAsync(CancellationToken ct = default) { Recorded++; return Task.CompletedTask; }
    }

    internal static byte[] Photo(int w, int h)
    {
        using var bitmap = new SKBitmap(w, h);
        using (var canvas = new SKCanvas(bitmap)) canvas.Clear(new SKColor(0xC8, 0xB8, 0xA0));
        using var data = SKImage.FromBitmap(bitmap).Encode(SKEncodedImageFormat.Png, 90);
        return data.ToArray();
    }

    private static readonly SkiaPhotoComposer Composer = new(NullLogger<SkiaPhotoComposer>.Instance);

    private static readonly VisualIdea Idea = new("ScintiNet", "418 953-3284", "#0097CE", "#E2E735",
        "Grand ménage d'automne", "Texte de la publication.", "-15 %", "Promotion", "Familles occupées", "https://www.scintinet.com", VisualKind.Photo);

    private static string Brief(string titre, string badge = "OFFRE DU MOIS") => JsonSerializer.Serialize(new
    {
        scene = "Photorealistic editorial lifestyle photograph of a relieved parent in a bright living room",
        titre,
        mot_cle = "votre maison",
        sous_titre = "On s'occupe de tout, vous profitez du week-end.",
        badge,
    });

    [Fact]
    public async Task La_photo_est_creee_mise_en_page_et_comptee()
    {
        var ai = new FakeAi(Brief("Retrouvez votre maison, -15 %"));
        var images = new FakeImages();
        var quota = new FakeQuota(0);
        var result = await new PostVisualDesigner(ai, images, Composer, quota, TimeProvider.System).DesignAsync(Idea);

        Assert.True(result.IsSuccess, result.Error);
        Assert.Null(result.Value!.Svg);
        using var bitmap = SKBitmap.Decode(result.Value.Png);
        Assert.Equal(1080, bitmap.Width);
        Assert.Equal(1080, bitmap.Height);
        Assert.Equal(1, quota.Recorded);
        Assert.Contains("No text", images.LastPrompt);
        Assert.Contains("Familles occupées", ai.Last!.User);
        Assert.Contains("claude-test", result.Value.Model);
        Assert.Contains("image-test", result.Value.Model);
    }

    [Fact]
    public void Un_chiffre_invente_par_l_IA_est_ecarte()
    {
        var brief = PostVisualDesigner.ParseBrief(Brief("Économisez 50 % ce mois-ci", badge: "2 POUR 1"), Idea)!;

        Assert.Equal("Grand ménage d'automne", brief.Title);
        Assert.Null(brief.Badge);
        Assert.True(PostVisualDesigner.OnlyAllowedNumbers("Jusqu'à -15 % !", "-15 %"));
        Assert.False(PostVisualDesigner.OnlyAllowedNumbers("-20 %", "-15 %"));
    }

    [Fact]
    public async Task Le_plafond_mensuel_bloque_la_photo_et_l_automatique_se_rabat_sur_une_illustration()
    {
        var designer = new PostVisualDesigner(new FakeAi(Brief("x")), new FakeImages(), Composer, new FakeQuota(5), TimeProvider.System);

        var photo = await designer.DesignAsync(Idea);
        Assert.True(photo.IsFailure);
        Assert.Contains("Plafond", photo.Error);

        var auto = await new PostVisualDesigner(new FakeAi("""{"concept":"c","titre":"t","svg":"<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 1080 1080\"><rect width=\"10\" height=\"10\"/></svg>"}"""),
            new FakeImages(fail: true), Composer, new FakeQuota(0), TimeProvider.System).DesignAsync(Idea with { Kind = VisualKind.Auto });
        Assert.True(auto.IsSuccess, auto.Error);
        Assert.NotNull(auto.Value!.Svg);
    }

    [Theory]
    [InlineData(SkiaPhotoComposer.Bottom)]
    [InlineData(SkiaPhotoComposer.Band)]
    [InlineData(SkiaPhotoComposer.Card)]
    public void Chaque_mise_en_page_pose_le_texte_dans_le_tiers_inferieur(string layout)
    {
        var overlay = new PhotoOverlay("Commandez le vendredi, économisez 10 % !", "économisez 10 %", "Une réduction pour démarrer votre week-end du bon pied.",
            "OFFRE EXCLUSIVE", "ScintiNet", "418 953-3284", "scintinet.com", "#0097CE", "#E2E735", layout);

        var png = Composer.Compose(Photo(2048, 2048), overlay)!;
        using var bitmap = SKBitmap.Decode(png);
        Assert.Equal(1080, bitmap.Width);

        // Le haut garde la photo intacte ; le bas a changé (texte, bandeau ou carte).
        Assert.Equal(new SKColor(0xC8, 0xB8, 0xA0), bitmap.GetPixel(540, 120));
        var changed = 0;
        for (var y = 760; y < 1070; y += 4)
        for (var x = 60; x < 1020; x += 4)
            if (bitmap.GetPixel(x, y) != new SKColor(0xC8, 0xB8, 0xA0)) changed++;
        Assert.True(changed > 5000, $"{layout} : texte absent ({changed})");

        var output = Environment.GetEnvironmentVariable("HUB_TEST_OUTPUT");
        if (!string.IsNullOrEmpty(output))
        {
            Directory.CreateDirectory(output);
            File.WriteAllBytes(Path.Combine(output, $"photo-{layout}.png"), png);
        }
    }

    [Fact]
    public void Les_mots_cles_de_l_accroche_sont_marques_pour_la_couleur()
    {
        using var font = new SKFont(BrandFont.Instance.Bold, 80);
        var lines = SkiaPhotoComposer.WrapTitle("Commandez le vendredi, économisez 10 % !", "économisez 10 %", font, 960);

        var words = lines.SelectMany(l => l).ToList();
        Assert.Equal(["économisez", "10", "%"], words.Where(w => w.Strong).Select(w => w.Word));
        Assert.True(lines.Count >= 2);
        Assert.All(lines, l => Assert.True(l.Sum(w => font.MeasureText(w.Word)) < 960));
    }

    [Fact]
    public void La_reponse_de_Google_est_lue()
    {
        var data = Convert.ToBase64String(Photo(10, 10));
        var ok = GeminiImageGenerator.Parse($$$"""{"candidates":[{"content":{"parts":[{"text":"voici"},{"inlineData":{"mimeType":"image/png","data":"{{{data}}}"}}]}}]}""", "m");
        Assert.True(ok.IsSuccess);
        Assert.Equal("image/png", ok.Value!.MimeType);

        var blocked = GeminiImageGenerator.Parse("""{"promptFeedback":{"blockReason":"SAFETY"}}""", "m");
        Assert.True(blocked.IsFailure);
        Assert.Contains("SAFETY", blocked.Error);
    }

    [Theory]
    [InlineData(SkiaPhotoComposer.Top)]
    [InlineData(SkiaPhotoComposer.Poster)]
    public void Les_mises_en_page_du_haut_laissent_le_bas_de_la_photo_libre(string layout)
    {
        var overlay = new PhotoOverlay("Quelle pièce détestez-vous nettoyer ?", "détestez-vous", null,
            "QUESTION", "ScintiNet", "418 953-3284", "scintinet.com", "#0097CE", "#E2E735", layout);

        var png = Composer.Compose(Photo(2048, 2048), overlay)!;
        using var bitmap = SKBitmap.Decode(png);
        Assert.Equal(new SKColor(0xC8, 0xB8, 0xA0), bitmap.GetPixel(540, 1000));
        var changed = 0;
        for (var y = 10; y < 330; y += 4)
        for (var x = 60; x < 1020; x += 4)
            if (bitmap.GetPixel(x, y) != new SKColor(0xC8, 0xB8, 0xA0)) changed++;
        Assert.True(changed > 5000, $"{layout} : texte absent ({changed})");

        var output = Environment.GetEnvironmentVariable("HUB_TEST_OUTPUT");
        if (!string.IsNullOrEmpty(output))
        {
            Directory.CreateDirectory(output);
            File.WriteAllBytes(Path.Combine(output, $"photo-{layout}.png"), png);
        }
    }

    [Fact]
    public async Task Le_format_impose_et_les_scenes_recentes_guident_la_photo()
    {
        var ai = new FakeAi(Brief("Avant, après : la différence"));
        var idea = Idea with { Format = "avant-apres", AvoidScenes = ["Photorealistic ... sofa in a bright living room"] };
        var result = await new PostVisualDesigner(ai, new FakeImages(), Composer, new FakeQuota(0), TimeProvider.System).DesignAsync(idea);

        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal("avant-apres", result.Value!.Format);
        Assert.NotNull(result.Value.Scene);
        Assert.Contains("Avant / après", ai.Last!.User);
        Assert.Contains("Split-screen", ai.Last.User);
        Assert.Contains("NE PAS refaire", ai.Last.User);
        Assert.Contains("sofa in a bright living room", ai.Last.User);
        Assert.Contains("cadrage :", ai.Last.User);
    }

    [Fact]
    public void Deux_directions_tirees_ne_sont_presque_jamais_identiques()
    {
        var random = new Random(42);
        var picks = Enumerable.Range(0, 30)
            .Select(_ => PhotoDirection.Pick(Idea, Composer.Layouts, random))
            .Select(d => $"{d.Format.Key}|{d.Camera}|{d.Light}|{d.Place}|{d.Palette}")
            .ToList();
        Assert.True(picks.Distinct().Count() >= 28);
        Assert.True(picks.Select(p => p.Split('|')[0]).Distinct().Count() >= 6);
    }

    [Fact]
    public void Sans_IA_la_consigne_photo_suit_le_format()
    {
        var d = PhotoDirection.Pick(Idea with { Format = "vu-du-dessus" }, Composer.Layouts, new Random(1));
        var brief = PostVisualDesigner.TemplateBrief(Idea, d, DateTimeOffset.UtcNow);
        Assert.Contains("flat lay", brief.Scene);
        Assert.Contains("No text", brief.Scene);
    }
}
