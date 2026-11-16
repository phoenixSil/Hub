using Hub.Web.Infrastructure;
using SkiaSharp;

namespace Hub.UnitTests;

public class SeasonalThemeTests
{
    [Theory]
    [InlineData("2026-10-14", null)]
    [InlineData("2026-10-15", "halloween")]
    [InlineData("2026-11-02", "halloween")]
    [InlineData("2026-11-11", "halloween")]
    [InlineData("2026-11-15", "halloween")]
    [InlineData("2026-11-16", "hiver")]
    [InlineData("2026-11-19", "hiver")]
    [InlineData("2026-11-20", "noel")]
    [InlineData("2026-12-25", "noel")]
    [InlineData("2027-01-06", "noel")]
    [InlineData("2027-01-07", "hiver")]
    [InlineData("2027-03-20", "hiver")]
    [InlineData("2027-03-21", null)]
    public void En_automatique_le_theme_suit_les_dates(string day, string? expected) =>
        Assert.Equal(expected, SeasonalThemes.Resolve("auto", DateOnly.Parse(day))?.Key);

    [Fact]
    public void Le_reglage_et_l_apercu_l_emportent_sur_les_dates()
    {
        var july = new DateOnly(2026, 7, 1);
        Assert.Equal("noel", SeasonalThemes.Resolve("noel", july)?.Key);
        Assert.Null(SeasonalThemes.Resolve("aucun", new DateOnly(2026, 12, 25)));
        Assert.Equal("halloween", SeasonalThemes.Resolve("aucun", july, preview: "halloween")?.Key);
        Assert.Null(SeasonalThemes.Resolve("noel", july, preview: "aucun"));
        Assert.Null(SeasonalThemes.Resolve("auto", july, preview: "inconnu"));
        Assert.Equal("hiver", SeasonalThemes.Resolve("aucun", july, preview: "hiver")?.Key);
    }

    [Fact]
    public void Noel_et_l_hiver_habillent_tout_le_site_en_neige()
    {
        Assert.Equal(SeasonalThemes.WinterSkin, SeasonalThemes.Christmas.Skin);
        Assert.Equal(SeasonalThemes.WinterSkin, SeasonalThemes.Winter.Skin);
        Assert.Null(SeasonalThemes.Halloween.Skin);
    }

    [Fact]
    public void Tous_les_textes_des_themes_sont_lisibles()
    {
        foreach (var t in SeasonalThemes.All)
        {
            var bar = SkiaPhotoComposer.Contrast(SKColor.Parse(t.BarBackground), SKColor.Parse(t.BarText));
            var slide = SkiaPhotoComposer.Contrast(SKColor.Parse(t.SlideBackground), SKColor.Parse(t.SlideText));
            Assert.True(bar >= 4.5, $"{t.Name} bandeau : {bar:0.0}:1");
            Assert.True(slide >= 4.5, $"{t.Name} bannière : {slide:0.0}:1");
            // Le mot mis en valeur de la bannière (lime ou bleu profond selon l'habillage) et le bouton vert restent lisibles.
            Assert.True(SkiaPhotoComposer.Contrast(SKColor.Parse(t.SlideBackground), SKColor.Parse(t.HighlightColor)) >= 4.5, $"{t.Name} : mot mis en valeur");
            Assert.True(SkiaPhotoComposer.Contrast(SKColor.Parse("#38d430"), SKColor.Parse("#06260a")) >= 4.5);
        }
    }
}
