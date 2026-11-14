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

    [Fact]
    public void Une_communication_intensive_publie_jusqu_a_trois_fois_par_jour_a_des_heures_differentes()
    {
        var slots = AutopilotSchedule.FreeSlots(Now, 21, 0, new Dictionary<DateOnly, int>());
        Assert.Equal(21, slots.Count);
        var local = slots.Select(s => TimeZoneInfo.ConvertTime(s, BusinessTime.Zone)).ToList();
        Assert.All(local.GroupBy(l => l.Date), g => Assert.Equal(new[] { 8, 12, 19 }, g.Select(l => l.Hour).OrderBy(h => h)));

        var twice = AutopilotSchedule.FreeSlots(Now, 14, 0, new Dictionary<DateOnly, int>())
            .Select(s => TimeZoneInfo.ConvertTime(s, BusinessTime.Zone)).ToList();
        Assert.Equal(14, twice.Count);
        Assert.All(twice.GroupBy(l => l.Date), g => Assert.Equal(new[] { 9, 18 }, g.Select(l => l.Hour).OrderBy(h => h)));

        // Un jour qui a déjà deux publications ne reçoit plus que la troisième, le soir.
        var busy = new Dictionary<DateOnly, int> { [new DateOnly(2026, 11, 7)] = 2 };
        var withBusy = AutopilotSchedule.FreeSlots(Now, 21, 0, busy).Select(s => TimeZoneInfo.ConvertTime(s, BusinessTime.Zone)).ToList();
        Assert.Equal(19, withBusy.Count);
        Assert.Equal(new[] { 19 }, withBusy.Where(l => l.Date == new DateTime(2026, 11, 7)).Select(l => l.Hour));

        // L'heure préférée du public ne s'applique qu'aux créneaux d'une seule publication par jour.
        Assert.Equal(slots[0], AutopilotPlanner.AtHour(slots[0], 17));
        Assert.Equal(17, TimeZoneInfo.ConvertTime(AutopilotPlanner.AtHour(AutopilotSchedule.FreeDays(Now, 3, 0, [])[0], 17), BusinessTime.Zone).Hour);
        Assert.Equal(3, AutopilotSchedule.PerDay(15));
        Assert.Equal(1, AutopilotSchedule.PerDay(7));
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
    public void Toute_police_demandee_est_servie_par_la_police_de_la_marque()
    {
        var bold = BrandFont.Instance.FromFamilyName("Arial", SKFontStyleWeight.Bold, SKFontStyleWidth.Normal, SKFontStyleSlant.Upright)!;
        var regular = BrandFont.Instance.FromFamilyName("Helvetica", SKFontStyleWeight.Normal, SKFontStyleWidth.Normal, SKFontStyleSlant.Upright)!;

        Assert.Equal("Poppins", bold.FamilyName);
        Assert.Equal(700, bold.FontWeight);
        Assert.Equal(400, regular.FontWeight);
        Assert.True(bold.GlyphCount > 100);
        using var font = new SKFont(bold, 100);
        Assert.True(font.MeasureText("Entretien") > 300, "les glyphes de la police doivent être lisibles");
    }

    [Fact]
    public void Le_texte_du_svg_est_dessine_avec_la_police_de_la_marque()
    {
        const string svg = """<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 1080 1080"><text x="40" y="300" font-family="Arial, sans-serif" font-size="100" font-weight="700" fill="#000000">Entretien</text></svg>""";
        var png = new SkiaVisualRenderer(NullLogger<SkiaVisualRenderer>.Instance).RenderPng(svg)!;
        using var bitmap = SKBitmap.Decode(png);
        int left = int.MaxValue, right = -1;
        for (var y = 150; y < 350; y++)
        for (var x = 0; x < 1080; x++)
        {
            if (bitmap.GetPixel(x, y).Red >= 128) continue;
            left = Math.Min(left, x);
            right = Math.Max(right, x);
        }

        using var font = new SKFont(BrandFont.Instance.Bold, 100);
        font.MeasureText("Entretien", out var expected);
        Assert.InRange(right - left + 1, expected.Width - 4, expected.Width + 4);
    }

    [Fact]
    public void Un_svg_invalide_ne_donne_pas_d_image() =>
        Assert.Null(new SkiaVisualRenderer(NullLogger<SkiaVisualRenderer>.Instance).RenderPng("pas du svg"));
}
