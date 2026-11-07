using System.Text.Json;
using Hub.Modules.Channels.Application;
using Hub.Modules.Channels.Application.Autopilot;
using Hub.Modules.Channels.Domain;
using Hub.SharedKernel;
using Hub.SharedKernel.Ai;
using Hub.SharedKernel.Time;
using Hub.Web.Infrastructure;
using Microsoft.Extensions.Logging.Abstractions;
using SkiaSharp;

namespace Hub.UnitTests;

public class AutopilotScheduleTests
{
    private static readonly DateTimeOffset Now = BusinessTime.FromLocal(new DateTime(2026, 11, 6, 20, 0, 0)); // un vendredi soir

    [Fact]
    public void Les_publications_sont_reparties_sur_les_jours_libres_de_la_semaine()
    {
        var days = AutopilotSchedule.FreeDays(Now, 3, 0, []);

        Assert.Equal(3, days.Count);
        var local = days.Select(AutopilotSchedule.LocalDay).ToList();
        Assert.Equal(3, local.Distinct().Count());
        Assert.All(local, d => Assert.InRange(d, new DateOnly(2026, 11, 7), new DateOnly(2026, 11, 13)));
        Assert.True(local[1].DayNumber - local[0].DayNumber >= 2, "les publications ne doivent pas se suivre");
    }

    [Fact]
    public void Les_jours_occupes_et_les_publications_deja_prevues_sont_respectes()
    {
        var busy = new HashSet<DateOnly> { new(2026, 11, 8), new(2026, 11, 10) };
        var days = AutopilotSchedule.FreeDays(Now, 4, 1, busy).Select(AutopilotSchedule.LocalDay).ToList();

        Assert.Equal(3, days.Count);
        Assert.DoesNotContain(new DateOnly(2026, 11, 8), days);
        Assert.DoesNotContain(new DateOnly(2026, 11, 10), days);
        Assert.Empty(AutopilotSchedule.FreeDays(Now, 3, 3, []));
    }
}

public class AutopilotPlannerTests
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

    private static readonly AutopilotContext Context = new("ScintiNet", "Québec", "418 555-0100", "#0097CE", "#E2E735",
        "https://www.scintinet.com", "admin@test",
        [
            new AutopilotPage("Grand ménage", "/promo", "Offre d'automne", null, true, "-15 %"),
            new AutopilotPage("Nettoyage résidentiel", "/services/residentiel", "Maisons et condos", "/img/r.webp", false, null),
        ]);

    private static readonly AutopilotAudience[] Audiences =
    [
        new("Familles occupées", "Parents pressés", 19),
        new("Aînés et leurs proches", "Maintien à domicile", 10),
    ];

    private static AutopilotPlanRequest Request(int slots) => new(Context, Audiences,
        AutopilotSchedule.FreeDays(DateTimeOffset.UtcNow, slots, 0, []), [new AutopilotHistory("Ancien sujet", null, null, 12, 2)], "Jamais de prix", DateTimeOffset.UtcNow);

    [Fact]
    public async Task Sans_IA_les_pages_et_les_publics_tournent_a_l_heure_de_chaque_public()
    {
        var plan = (await new AutopilotPlanner(new FakeAi(false, "")).PlanAsync(Request(2))).Value!;

        Assert.Equal(2, plan.Count);
        Assert.Equal(["Familles occupées", "Aînés et leurs proches"], plan.Select(p => p.Audience));
        Assert.Equal(19, TimeZoneInfo.ConvertTime(plan[0].At, BusinessTime.Zone).Hour);
        Assert.Equal(10, TimeZoneInfo.ConvertTime(plan[1].At, BusinessTime.Zone).Hour);
        Assert.Equal("-15 %", plan.Single(p => p.LinkPath == "/promo").Facts);
    }

    [Fact]
    public async Task Avec_IA_les_liens_les_publics_et_les_chiffres_inventes_sont_ecartes()
    {
        var json = JsonSerializer.Serialize(new
        {
            publications = new[]
            {
                new { @public = "Inconnu", sujet = "Sujet A", angle = "Angle A -50 %", lien = "/inexistant", style = "Bizarre" },
                new { @public = "Familles occupées", sujet = "Promo", angle = "Angle B", lien = "/promo", style = "Promotion" },
            },
        });
        var ai = new FakeAi(true, json);
        var plan = (await new AutopilotPlanner(ai).PlanAsync(Request(2))).Value!;

        Assert.Null(plan[0].LinkPath);
        Assert.Null(plan[0].Facts);
        Assert.Contains(plan[0].Audience, Audiences.Select(a => a.Name));
        Assert.Equal("Service", plan[0].Style);
        Assert.Equal("/promo", plan[1].LinkPath);
        Assert.Equal("-15 %", plan[1].Facts);
        Assert.Contains("Jamais de prix", ai.Last!.User);
        Assert.Contains("12 visites, 2 demandes", ai.Last.User);
    }

    [Fact]
    public async Task Sans_page_publiee_le_pilote_refuse_de_planifier() =>
        Assert.True((await new AutopilotPlanner(new FakeAi(false, "")).PlanAsync(Request(1) with { Context = Context with { Pages = [] } })).IsFailure);
}

public class VisualRendererTests
{
    [Fact]
    public void Le_visuel_est_dessine_en_png_avec_le_texte()
    {
        var idea = new VisualIdea("ScintiNet", "418 555-0100", "#0097CE", "#E2E735", "Grand ménage", "Texte", null, "Service");
        var svg = PostVisualDesigner.Template(idea).Svg;

        var png = new SkiaVisualRenderer(NullLogger<SkiaVisualRenderer>.Instance).RenderPng(svg);

        Assert.NotNull(png);
        using var bitmap = SKBitmap.Decode(png);
        Assert.Equal(1080, bitmap.Width);
        // Le titre est écrit en blanc sur le dégradé : sans police, aucun pixel blanc dans cette zone.
        var white = 0;
        for (var y = 330; y < 600; y += 2)
        for (var x = 100; x < 980; x += 2)
        {
            var c = bitmap.GetPixel(x, y);
            if (c.Red > 235 && c.Green > 235 && c.Blue > 235) white++;
        }
        Assert.True(white > 500, $"texte du titre non dessiné ({white} pixels blancs)");

        var output = Environment.GetEnvironmentVariable("HUB_TEST_OUTPUT");
        if (!string.IsNullOrEmpty(output))
        {
            Directory.CreateDirectory(output);
            File.WriteAllBytes(Path.Combine(output, "visuel-modele.png"), png!);
        }
    }

    [Fact]
    public void Un_svg_invalide_ne_donne_pas_d_image() =>
        Assert.Null(new SkiaVisualRenderer(NullLogger<SkiaVisualRenderer>.Instance).RenderPng("pas du svg"));
}
