using System.Text.Json;
using Hub.Modules.Channels.Application;
using Hub.SharedKernel;
using Hub.SharedKernel.Ai;

namespace Hub.UnitTests;

public class SvgSanitizerTests
{
    [Fact]
    public void Les_elements_et_attributs_dangereux_sont_retires()
    {
        const string svg = """
            <svg xmlns="http://www.w3.org/2000/svg" xmlns:xlink="http://www.w3.org/1999/xlink" viewBox="0 0 10 10" onload="alert(1)">
              <script>alert(1)</script>
              <foreignObject><div>x</div></foreignObject>
              <rect width="10" height="10" fill="#0097CE" onclick="steal()" />
              <image href="https://evil.example/a.png" />
              <a href="javascript:alert(1)"><text>lien</text></a>
              <text x="1" y="5">Bonjour</text>
              <rect style="fill:url(https://evil.example/x)" />
              <use xlink:href="#ok" />
            </svg>
            """;
        var clean = SvgSanitizer.Clean(svg)!;

        Assert.DoesNotContain("script", clean, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("foreignObject", clean);
        Assert.DoesNotContain("onload", clean);
        Assert.DoesNotContain("onclick", clean);
        Assert.DoesNotContain("evil.example", clean);
        Assert.DoesNotContain("javascript:", clean);
        Assert.Contains("Bonjour", clean);
        Assert.Contains("#0097CE", clean);
        Assert.Contains("width=\"1080\"", clean);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("pas du svg")]
    [InlineData("<html><body/></html>")]
    [InlineData("<!DOCTYPE svg [<!ENTITY x \"y\">]><svg xmlns=\"http://www.w3.org/2000/svg\">&x;</svg>")]
    public void Un_contenu_invalide_est_refuse(string? svg) => Assert.Null(SvgSanitizer.Clean(svg));
}

public class PostVisualDesignerTests
{
    private sealed class FakeAi(bool available, string json) : IAiTextGenerator
    {
        public AiJsonRequest? Last { get; private set; }
        public bool IsAvailable => available;
        public string ModelName => "fake";
        public Task<Result<AiJsonResponse>> GenerateJsonAsync(AiJsonRequest request, CancellationToken ct = default)
        {
            Last = request;
            return Task.FromResult(Result<AiJsonResponse>.Success(new AiJsonResponse(json, "fake-model", 10, 20)));
        }
    }

    private static readonly VisualIdea Idea = new("ScintiNet", "418 555-0100", "#0097CE", "#E2E735",
        "Grand ménage du printemps", "Un texte de publication.", "-20 %", "Promotion");

    [Fact]
    public async Task Sans_IA_un_modele_aux_couleurs_de_la_marque_est_produit()
    {
        var result = await new PostVisualDesigner(new FakeAi(false, "")).DesignAsync(Idea);

        var svg = result.Value!.Svg;
        Assert.Contains("#0097CE", svg);
        Assert.Contains("printemps", svg);
        Assert.Contains("-20 %", svg);
        Assert.NotNull(SvgSanitizer.Clean(svg));
    }

    [Fact]
    public async Task Avec_IA_le_svg_est_nettoye_et_les_faits_transmis()
    {
        var svg = """<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 1080 1080"><script>x</script><text x="10" y="50">-20 %</text></svg>""";
        var ai = new FakeAi(true, JsonSerializer.Serialize(new { titre = "Printemps", svg }));
        var result = await new PostVisualDesigner(ai).DesignAsync(Idea);

        Assert.Equal("Printemps", result.Value!.Headline);
        Assert.Equal("fake-model", result.Value.Model);
        Assert.DoesNotContain("<script", result.Value.Svg);
        Assert.Contains("-20 %", ai.Last!.User);
        Assert.Contains("#E2E735", ai.Last.System);
        Assert.Contains("Mise en page :", ai.Last.User);
        Assert.Contains("Illustration :", ai.Last.User);
    }

    [Fact]
    public void Les_directions_artistiques_varient_d_une_proposition_a_l_autre()
    {
        var random = new Random(42);
        var picks = Enumerable.Range(0, 20).Select(_ => ArtDirection.Pick("Aînés", random)).ToList();

        Assert.True(picks.Select(p => p.Layout).Distinct().Count() >= 4);
        Assert.True(picks.Select(p => p.Scene).Distinct().Count() >= 2);
        Assert.NotNull(ArtDirection.Pick("Inconnu", random).Scene);
    }

    [Fact]
    public async Task Un_svg_illisible_de_l_IA_est_signale() =>
        Assert.True((await new PostVisualDesigner(new FakeAi(true, """{"titre":"x","svg":"rien"}""")).DesignAsync(Idea)).IsFailure);

    [Fact]
    public async Task Un_sujet_vide_est_refuse() =>
        Assert.True((await new PostVisualDesigner(new FakeAi(true, "{}")).DesignAsync(Idea with { Topic = "" })).IsFailure);
}
