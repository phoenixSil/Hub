using Hub.Modules.Quotes.Application;
using Hub.Modules.Quotes.Domain;
using Hub.SharedKernel.Tenancy;

namespace Hub.UnitTests;

public class QuoteAssistantTests
{
    private static readonly IReadOnlyList<PriceGridItem> Grid =
    [
        new("Nettoyage résidentiel", "Entretien et nettoyage", "Hourly", 37m, null, 3m, null, null) { CategoryKey = "services" },
        new("Lavage de vitres", "Entretien et nettoyage", "OnAssessment", null, null, null, null, null) { CategoryKey = "services" },
        new("Lavage de voiture à domicile", "Entretien et nettoyage", "Flat", 120m, 180m, null, null, null) { CategoryKey = "services" },
        new("Aide à domicile", "Aînés et soutien", "PerShift", 40m, null, null, 8m, null) { CategoryKey = "aines" },
        new("Accompagnement des aînés", "Aînés et soutien", "Hourly", 44m, null, 3m, null, null) { CategoryKey = "aines" },
    ];

    private static readonly Dictionary<string, decimal> Floors = new() { ["services"] = 31m, ["aines"] = 35m };

    private static QuoteContext Context(string? size = "1 500 pi²", params string[] services) =>
        new("ScintiNet", "418 953-3284",
            new QuoteLeadFacts(Guid.NewGuid(), "Marie Tremblay", "marie@example.com", services, "Maison", size, "Chaque semaine", "Sillery", null),
            Grid, Floors);

    private static ModelQuote Quote(params QuoteLine[] lines) =>
        new(3, lines, "", [], [], "Bonjour", "moyenne", "test");

    [Theory]
    [InlineData(null, 3)]
    [InlineData("1 500 pi²", 3)]
    [InlineData("2000 pi2", 4)]
    [InlineData("2 300 pieds carrés", 5)]
    [InlineData("150 m²", 3.5)]
    [InlineData("10 000 pi²", 8)]
    [InlineData("grande maison", 3)]
    public void Les_heures_suivent_la_superficie(string? size, double expected) =>
        Assert.Equal((decimal)expected, SimulatedQuoteModel.EstimateHours(size));

    [Fact]
    public async Task Le_modele_simule_chiffre_avec_la_grille()
    {
        var quote = await new SimulatedQuoteModel().ProposeAsync(
            Context("2000 pi²", "Nettoyage résidentiel", "Lavage de vitres", "Lavage de voiture à domicile", "Aide à domicile"));

        Assert.Contains(quote.Lines, l => l.Service == "Nettoyage résidentiel" && l.Quantity == 4 && l.UnitPrice == 37m);
        Assert.Contains(quote.Lines, l => l.Service == "Lavage de vitres" && l.UnitPrice == 0);
        Assert.Contains(quote.Lines, l => l.Service == "Lavage de voiture à domicile" && l.UnitPrice == 150m);
        Assert.Contains(quote.Lines, l => l.Service == "Aide à domicile" && l.UnitPrice == 320m);
        Assert.StartsWith("Bonjour Marie", quote.CustomerMessage);
        Assert.DoesNotContain("$", quote.CustomerMessage);
    }

    [Fact]
    public void Un_taux_sous_le_plancher_est_releve_et_signale()
    {
        var result = PricingGuard.Apply(
            Quote(new QuoteLine("Nettoyage", 4, "heure", 25m, "Nettoyage résidentiel")), Context());

        Assert.Equal(31m, result.Lines[0].UnitPrice);
        Assert.Equal(124m, result.Total);
        Assert.Contains(result.Warnings, w => w.Contains("plancher"));
    }

    [Fact]
    public void Le_plancher_des_aines_est_celui_de_leur_categorie()
    {
        var result = PricingGuard.Apply(
            Quote(new QuoteLine("Accompagnement", 3, "heure", 33m, "Accompagnement des aînés")), Context());

        Assert.Equal(35m, result.Lines[0].UnitPrice);
    }

    [Fact]
    public void Le_minimum_d_heures_est_respecte()
    {
        var result = PricingGuard.Apply(
            Quote(new QuoteLine("Ménage", 2, "heure", 37m, "Nettoyage résidentiel")), Context());

        Assert.Equal(3m, result.Lines[0].Quantity);
        Assert.Equal(111m, result.Total);
        Assert.Contains(result.Warnings, w => w.Contains("minimum"));
    }

    [Fact]
    public void Une_ligne_horaire_hors_grille_prend_le_plancher_le_plus_eleve()
    {
        var result = PricingGuard.Apply(Quote(new QuoteLine("Grand ménage du printemps", 5, "heure", 20m)), Context());

        Assert.Equal(35m, result.Lines[0].UnitPrice);
    }

    [Fact]
    public void Un_prix_conforme_n_est_pas_modifie()
    {
        var result = PricingGuard.Apply(
            Quote(new QuoteLine("Ménage hebdomadaire", 3, "heure", 37m, "Nettoyage résidentiel"),
                  new QuoteLine("Lavage de voiture", 1, "forfait", 150m, "Lavage de voiture à domicile")), Context());

        Assert.Empty(result.Warnings);
        Assert.Equal(261m, result.Total);
    }

    [Fact]
    public void Les_valeurs_negatives_sont_ramenees_a_zero()
    {
        var result = PricingGuard.Apply(Quote(new QuoteLine("Rabais", 1, "forfait", -50m)), Context());

        Assert.Equal(0m, result.Total);
    }

    [Fact]
    public void Le_courriel_au_client_contient_le_tableau_et_echappe_le_html()
    {
        var tenant = new TenantInfo(Guid.NewGuid(), "ScintiNet", null, "contact@scintinet.com", "418 953-3284", "admin@x.com", "#0097CE", null);
        var html = QuoteService.BuildEmail(tenant, "Bonjour <b>Marie</b>,\n\nVoici notre offre.",
            [new QuoteLine("Ménage", 3, "heure", 37m), new QuoteLine("Évaluation", 1, "visite", 0)], 111m, "Taxes en sus.");

        Assert.Contains("&lt;b&gt;Marie&lt;/b&gt;", html);
        Assert.Contains("111,00", html);
        Assert.Contains("Gratuit", html);
        Assert.Contains("Taxes en sus.", html);
    }

    [Fact]
    public void Le_prompt_ne_demande_jamais_de_montant_dans_le_message()
    {
        var prompt = ClaudeQuoteModel.SystemPrompt(Context());

        Assert.Contains("N'écris AUCUN montant", prompt);
        Assert.Contains("Nettoyage résidentiel", prompt);
        Assert.Contains("31", prompt);
    }
}

public class ClaudeQuoteModelTests
{
    private sealed class FakeHandler(string response) : HttpMessageHandler
    {
        public string? RequestBody { get; private set; }
        public HttpRequestMessage? Request { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Request = request;
            RequestBody = await request.Content!.ReadAsStringAsync(ct);
            return new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new StringContent(response, System.Text.Encoding.UTF8, "application/json"),
            };
        }
    }

    [Fact]
    public async Task La_reponse_structuree_de_Claude_est_lue()
    {
        var proposal = """
            {"heures_estimees":3.5,"lignes":[{"service":"Nettoyage résidentiel","description":"Ménage hebdomadaire","quantite":3.5,"unite":"heure","prix_unitaire":37}],
             "raisonnement":"Maison de 1500 pi²","hypotheses":["Deux salles de bain"],"questions_au_client":[],"message_client":"Bonjour Marie,","confiance":"moyenne"}
            """;
        var response = new System.Text.Json.Nodes.JsonObject
        {
            ["stop_reason"] = "end_turn",
            ["content"] = new System.Text.Json.Nodes.JsonArray(new System.Text.Json.Nodes.JsonObject { ["type"] = "text", ["text"] = proposal }),
            ["usage"] = new System.Text.Json.Nodes.JsonObject { ["input_tokens"] = 900, ["output_tokens"] = 300 },
        }.ToJsonString();

        var handler = new FakeHandler(response);
        var model = new ClaudeQuoteModel(new HttpClient(handler),
            Microsoft.Extensions.Options.Options.Create(new AnthropicOptions { ApiKey = "test", Model = "claude-sonnet-5-5" }));

        var ctx = new QuoteContext("ScintiNet", "418",
            new QuoteLeadFacts(Guid.NewGuid(), "Marie", "m@x.com", ["Nettoyage résidentiel"], "Maison", "1500 pi²", null, null, null),
            [], new Dictionary<string, decimal> { ["services"] = 31m });
        var quote = await model.ProposeAsync(ctx);

        Assert.Equal(3.5m, quote.EstimatedHours);
        Assert.Single(quote.Lines);
        Assert.Equal("Nettoyage résidentiel", quote.Lines[0].Service);
        Assert.Equal(37m, quote.Lines[0].UnitPrice);
        Assert.Equal("Bonjour Marie,", quote.CustomerMessage);
        Assert.Equal(900, quote.InputTokens);
        Assert.Contains("\"output_config\"", handler.RequestBody);
        Assert.Contains("\"json_schema\"", handler.RequestBody);
        Assert.DoesNotContain("tool_choice", handler.RequestBody);
        Assert.True(handler.Request!.Headers.Contains("x-api-key"));
    }
}
