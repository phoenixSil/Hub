using System.Text.Json.Nodes;
using Hub.Modules.Channels.Application;
using Hub.SharedKernel.Ai;
using Hub.Web.Infrastructure;
using Microsoft.Extensions.Logging.Abstractions;
using SkiaSharp;

namespace Hub.UnitTests;

public class CreativeEngineTests
{
    private static readonly SkiaPhotoComposer Composer = new(NullLogger<SkiaPhotoComposer>.Instance);

    [Fact]
    public void La_bible_compte_vingt_styles_distincts()
    {
        Assert.Equal(20, CreativeStyles.All.Count);
        Assert.Equal(20, CreativeStyles.All.Select(s => s.Key).Distinct().Count());
        Assert.Contains(CreativeStyles.All, s => !s.Photographic);
    }

    [Fact]
    public void Le_tirage_ecarte_les_styles_desactives_recents_et_deja_essayes()
    {
        var disabled = CreativeStyles.All.Take(8).Select(s => s.Key).ToHashSet();
        var recent = CreativeStyles.All.Skip(8).Take(6).Select(s => s.Key).ToList();
        var tried = new[] { CreativeStyles.All[14].Key };
        var random = new Random(5);
        for (var i = 0; i < 50; i++)
        {
            var s = CreativeStyles.Pick(random, disabled, recent, tried);
            Assert.DoesNotContain(s.Key, disabled);
            Assert.DoesNotContain(s.Key, recent);
            Assert.NotEqual(tried[0], s.Key);
        }
        Assert.Equal(["cinema", "macro"], CreativeStyles.Parse("cinema, inconnu ,macro"));
    }

    [Fact]
    public void L_affiche_impose_le_texte_exact_et_le_coin_du_logo()
    {
        var brief = new PostVisualDesigner.PhotoBrief("A bold Swiss poster of a gleaming kitchen.", "Votre cuisine, comme neuve", null,
            "On s'occupe de tout après le souper.", "ASTUCE", "haut-gauche");
        var prompt = PostVisualDesigner.PosterPrompt(brief);
        Assert.Contains("\"Votre cuisine, comme neuve\"", prompt);
        Assert.Contains("\"On s'occupe de tout après le souper.\"", prompt);
        Assert.Contains("top-left corner", prompt);
        Assert.Contains("No other words", prompt);
    }

    [Theory]
    [InlineData("""{"texte_exact":true,"fautes":"","image_la_plus_proche":2,"meme_lieu":true,"meme_composition":false,"memes_couleurs":false,"meme_style":false}""", true, true, false)]
    [InlineData("""{"texte_exact":true,"fautes":"","image_la_plus_proche":2,"meme_lieu":true,"meme_composition":true,"memes_couleurs":false,"meme_style":false}""", true, false, false)]
    [InlineData("""{"texte_exact":false,"fautes":"« menage » sans accent","image_la_plus_proche":0,"meme_lieu":false,"meme_composition":false,"memes_couleurs":false,"meme_style":false}""", true, false, true)]
    [InlineData("""{"texte_exact":false,"fautes":"x","image_la_plus_proche":0,"meme_lieu":false,"meme_composition":false,"memes_couleurs":false,"meme_style":false}""", false, true, false)]
    public void Le_garde_fou_refuse_un_texte_faux_ou_deux_criteres_identiques(string json, bool poster, bool ok, bool textWrong)
    {
        var check = PostVisualDesigner.ParseCheck(json, poster)!;
        Assert.Equal(ok, check.Ok);
        Assert.Equal(textWrong, check.TextWrong);
        if (!ok) Assert.False(string.IsNullOrWhiteSpace(check.Reason));
    }

    [Fact]
    public void Le_vrai_logo_est_pose_dans_le_coin_reserve()
    {
        var poster = PhotoVisualTests.Photo(1600, 1600); // fond beige clair
        var logo = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "logo-test.png"));
        var png = Composer.FinishPoster(poster, new PosterFinish("haut-gauche", logo, null, "418 953-3284", "scintinet.com", "#0097CE"))!;
        using var bitmap = SKBitmap.Decode(png);
        Assert.Equal(1080, bitmap.Width);
        var beige = new SKColor(0xC8, 0xB8, 0xA0);
        var changedTopLeft = 0;
        for (var y = 36; y < 130; y += 3)
        for (var x = 36; x < 320; x += 3)
            if (bitmap.GetPixel(x, y) != beige) changedTopLeft++;
        Assert.True(changedTopLeft > 300, $"logo absent ({changedTopLeft})");
        Assert.Equal(beige, bitmap.GetPixel(1000, 80)); // coin opposé intact
        Assert.NotEqual(beige, bitmap.GetPixel(540, 1080 - 36 - 23)); // coordonnées en bas

        var output = Environment.GetEnvironmentVariable("HUB_TEST_OUTPUT");
        if (!string.IsNullOrEmpty(output))
        {
            Directory.CreateDirectory(output);
            File.WriteAllBytes(Path.Combine(output, "affiche-finition.png"), png);
        }
    }

    [Fact]
    public void Claude_recoit_les_images_avant_le_texte()
    {
        var content = ClaudeTextGenerator.Content(new AiJsonRequest("s", "Compare.", "{}", 100, [new AiImageInput([1, 2, 3])]));
        var parts = content.AsArray();
        Assert.Equal("image", parts[0]!["type"]!.GetValue<string>());
        Assert.Equal("AQID", parts[0]!["source"]!["data"]!.GetValue<string>());
        Assert.Equal("text", parts[1]!["type"]!.GetValue<string>());
        Assert.Equal("Compare.", ClaudeTextGenerator.Content(new AiJsonRequest("s", "Compare.", "{}")).GetValue<string>());
    }
}
