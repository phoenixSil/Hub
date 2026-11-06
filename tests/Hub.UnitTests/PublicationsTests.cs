using System.Net;
using System.Text;
using Hub.Modules.Channels;
using Hub.Modules.Channels.Application;
using Hub.Modules.Channels.Publishers;
using Hub.SharedKernel;
using Hub.SharedKernel.Ai;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Hub.UnitTests;

public class TrackedLinkTests
{
    [Fact]
    public void Le_lien_porte_la_source_le_support_et_la_campagne() =>
        Assert.Equal(
            "https://www.scintinet.com/services/nettoyage-residentiel?utm_source=facebook&utm_medium=social&utm_campaign=2026-11-menage",
            TrackedLink.Build("https://www.scintinet.com/", "/services/nettoyage-residentiel", ChannelType.Facebook, "2026-11-menage"));

    [Fact]
    public void Un_lien_qui_a_deja_des_parametres_est_complete() =>
        Assert.EndsWith("/devis?type=menage&utm_source=instagram&utm_medium=social&utm_campaign=c",
            TrackedLink.Build("https://www.scintinet.com", "/devis?type=menage", ChannelType.Instagram, "c"));

    [Fact]
    public void Sans_adresse_du_site_aucun_lien_relatif() =>
        Assert.Null(TrackedLink.Build(null, "/devis", ChannelType.Facebook, "c"));

    [Fact]
    public void Une_image_relative_devient_absolue() =>
        Assert.Equal("https://www.scintinet.com/uploads/a.webp", TrackedLink.Absolute("https://www.scintinet.com", "/uploads/a.webp"));

    [Fact]
    public void La_campagne_est_lisible_et_unique()
    {
        var now = new DateTimeOffset(2026, 11, 6, 12, 0, 0, TimeSpan.Zero);
        var a = TrackedLink.NewCampaign("Grand ménage d'automne !", now);
        var b = TrackedLink.NewCampaign("Grand ménage d'automne !", now);
        Assert.StartsWith("2026-11-grand-menage-d-automne-", a);
        Assert.NotEqual(a, b);
        Assert.True(a.Length <= 100);
    }
}

public class FacebookPublisherTests
{
    private sealed class FakeHandler(HttpStatusCode status, string body) : HttpMessageHandler
    {
        public HttpRequestMessage? Request { get; private set; }
        public string? Body { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Request = request;
            Body = request.Content is null ? null : await request.Content.ReadAsStringAsync(ct);
            return new HttpResponseMessage(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
        }
    }

    private static (FacebookPagePublisher, FakeHandler) Create(HttpStatusCode status, string body)
    {
        var handler = new FakeHandler(status, body);
        var publisher = new FacebookPagePublisher(new HttpClient(handler),
            Options.Create(new FacebookOptions { GraphVersion = "v23.0" }), NullLogger<FacebookPagePublisher>.Instance);
        return (publisher, handler);
    }

    private static readonly ChannelCredentials Page = new("123", "jeton-secret");

    [Fact]
    public async Task Un_texte_avec_lien_part_sur_le_fil_de_la_page()
    {
        var (fb, handler) = Create(HttpStatusCode.OK, """{"id":"123_456"}""");
        var result = await fb.PublishAsync(new PublishRequest("Bonjour", "https://www.scintinet.com/?utm_source=facebook", null), Page);

        Assert.True(result.Success);
        Assert.Equal("123_456", result.ExternalId);
        Assert.Equal("https://www.facebook.com/123_456", result.ExternalUrl);
        Assert.Equal("https://graph.facebook.com/v23.0/123/feed", handler.Request!.RequestUri!.ToString());
        Assert.Contains("message=Bonjour", handler.Body);
        Assert.Contains("link=https", handler.Body);
        Assert.Contains("access_token=jeton-secret", handler.Body);
    }

    [Fact]
    public async Task Avec_une_image_la_publication_est_une_photo_avec_legende()
    {
        var (fb, handler) = Create(HttpStatusCode.OK, """{"id":"789","post_id":"123_999"}""");
        var result = await fb.PublishAsync(new PublishRequest("Texte", "https://www.scintinet.com/devis", "https://www.scintinet.com/a.webp"), Page);

        Assert.Equal("123_999", result.ExternalId);
        Assert.EndsWith("/123/photos", handler.Request!.RequestUri!.AbsolutePath);
        Assert.Contains("url=https", handler.Body);
        Assert.Contains("caption=Texte", handler.Body);
        Assert.Contains("devis", handler.Body); // le lien est ajouté à la légende
    }

    [Fact]
    public async Task Un_jeton_expire_donne_un_message_clair()
    {
        var (fb, _) = Create(HttpStatusCode.BadRequest, """{"error":{"message":"Error validating access token","type":"OAuthException","code":190}}""");
        var result = await fb.PublishAsync(new PublishRequest("Texte", null, null), Page);

        Assert.False(result.Success);
        Assert.Contains("reconnectez", result.Error);
    }

    [Fact]
    public async Task Le_test_de_connexion_renvoie_le_nom_de_la_page()
    {
        var (fb, handler) = Create(HttpStatusCode.OK, """{"name":"ScintiNet","id":"123"}""");
        var result = await fb.TestAsync(Page);

        Assert.Equal("ScintiNet", result.Value);
        Assert.Contains("fields=name", handler.Request!.RequestUri!.Query);
    }
}

public class PostWriterTests
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

    private static readonly PostIdea Idea = new("ScintiNet", "Québec", "Grand ménage du printemps", "Produits écologiques", "Chaleureux", ChannelType.Facebook);

    [Fact]
    public async Task Sans_IA_trois_modeles_sont_proposes()
    {
        var result = await new PostWriter(new FakeAi(false, "")).SuggestAsync(Idea);

        Assert.Equal(3, result.Value!.Count);
        Assert.All(result.Value, s => Assert.Contains("Grand ménage du printemps", s.Text));
        Assert.All(result.Value, s => Assert.Contains("#Québec", s.Text));
    }

    [Fact]
    public async Task Avec_IA_les_variantes_sont_lues_et_les_regles_transmises()
    {
        var ai = new FakeAi(true, """{"variantes":[{"texte":"Un"},{"texte":"Deux"},{"texte":" "}]}""");
        var result = await new PostWriter(ai).SuggestAsync(Idea);

        Assert.Equal(["Un", "Deux"], result.Value!.Select(s => s.Text));
        Assert.Equal("fake-model", result.Value![0].Model);
        Assert.Contains("N'écris PAS de lien", ai.Last!.System);
        Assert.Contains("Produits écologiques", ai.Last.User);
    }

    [Fact]
    public async Task Un_sujet_vide_est_refuse() =>
        Assert.True((await new PostWriter(new FakeAi(true, "{}")).SuggestAsync(Idea with { Topic = " " })).IsFailure);
}

public class PublisherResolverTests
{
    [Fact]
    public void En_mode_simule_aucun_reseau_reel_n_est_appele()
    {
        var real = new FacebookPagePublisher(new HttpClient(), Options.Create(new FacebookOptions()), NullLogger<FacebookPagePublisher>.Instance);
        var resolver = new PublisherResolver([real], Options.Create(new ChannelsOptions { Simulate = true }), NullLoggerFactory.Instance);

        Assert.IsType<SimulatedPublisher>(resolver.For(ChannelType.Facebook));
        Assert.Contains(ChannelType.Facebook, resolver.Supported);
    }

    [Fact]
    public void Hors_simulation_le_vrai_connecteur_est_utilise()
    {
        var real = new FacebookPagePublisher(new HttpClient(), Options.Create(new FacebookOptions()), NullLogger<FacebookPagePublisher>.Instance);
        var resolver = new PublisherResolver([real], Options.Create(new ChannelsOptions()), NullLoggerFactory.Instance);

        Assert.Same(real, resolver.For(ChannelType.Facebook));
        Assert.Throws<InvalidOperationException>(() => resolver.For(ChannelType.TikTok));
    }
}
