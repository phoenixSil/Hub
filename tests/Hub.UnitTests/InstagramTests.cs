using System.Net;
using System.Text;
using Hub.Modules.Channels;
using Hub.Modules.Channels.Publishers;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Hub.UnitTests;

public class InstagramPublisherTests
{
    /// <summary>Faux Graph API : répond selon le début du chemin et garde la trace des appels.</summary>
    private sealed class FakeGraph(params (string Method, string PathStart, HttpStatusCode Status, string Body)[] routes) : HttpMessageHandler
    {
        public List<(string Method, string Path, string? Body)> Calls { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var path = request.RequestUri!.AbsolutePath.Replace("/v27.0/", "") + request.RequestUri.Query;
            var body = request.Content is null ? null : await request.Content.ReadAsStringAsync(ct);
            Calls.Add((request.Method.Method, path, body));
            var route = routes.FirstOrDefault(r => r.Method == request.Method.Method && path.StartsWith(r.PathStart, StringComparison.Ordinal));
            return route.PathStart is null
                ? new HttpResponseMessage(HttpStatusCode.NotFound) { Content = new StringContent("""{"error":{"message":"route","code":100}}""") }
                : new HttpResponseMessage(route.Status) { Content = new StringContent(route.Body, Encoding.UTF8, "application/json") };
        }
    }

    private static InstagramPublisher Create(FakeGraph graph) =>
        new(new HttpClient(graph), Options.Create(new FacebookOptions { GraphVersion = "v27.0" }), NullLogger<InstagramPublisher>.Instance)
        {
            PollInterval = TimeSpan.FromMilliseconds(1),
            ProcessingTimeout = TimeSpan.FromSeconds(2),
        };

    private static readonly ChannelCredentials Account = new("1784", "jeton-page");

    [Fact]
    public async Task Publie_en_trois_temps_media_attente_publication()
    {
        var graph = new FakeGraph(
            ("POST", "1784/media_publish", HttpStatusCode.OK, """{"id":"999"}"""),
            ("POST", "1784/media", HttpStatusCode.OK, """{"id":"c1"}"""),
            ("GET", "c1", HttpStatusCode.OK, """{"status_code":"FINISHED"}"""),
            ("GET", "999", HttpStatusCode.OK, """{"permalink":"https://www.instagram.com/p/abc/"}"""));

        var result = await Create(graph).PublishAsync(
            new PublishRequest("Grand ménage d'automne !", "https://www.scintinet.com/devis?utm_source=instagram", "https://www.scintinet.com/media/social.jpg?src=x"), Account);

        Assert.True(result.Success, result.Error);
        Assert.Equal("999", result.ExternalId);
        Assert.Equal("https://www.instagram.com/p/abc/", result.ExternalUrl);
        var media = graph.Calls.First(c => c.Path.StartsWith("1784/media", StringComparison.Ordinal));
        Assert.Contains("image_url=https", media.Body);
        Assert.Contains("scintinet.com", Uri.UnescapeDataString(media.Body!.Replace('+', ' ')));
        Assert.Contains(graph.Calls, c => c.Path.StartsWith("1784/media_publish") && c.Body!.Contains("creation_id=c1"));
    }

    [Fact]
    public async Task Sans_image_rien_n_est_envoye()
    {
        var graph = new FakeGraph();
        var result = await Create(graph).PublishAsync(new PublishRequest("Texte", null, null), Account);

        Assert.False(result.Success);
        Assert.Contains("image", result.Error);
        Assert.Empty(graph.Calls);
    }

    [Fact]
    public async Task Une_image_refusee_par_instagram_est_expliquee()
    {
        var graph = new FakeGraph(
            ("POST", "1784/media", HttpStatusCode.OK, """{"id":"c1"}"""),
            ("GET", "c1", HttpStatusCode.OK, """{"status_code":"ERROR","status":"Error: unsupported format"}"""));

        var result = await Create(graph).PublishAsync(new PublishRequest("Texte", null, "https://x/img.jpg"), Account);

        Assert.False(result.Success);
        Assert.Contains("JPEG", result.Error);
        Assert.DoesNotContain(graph.Calls, c => c.Path.Contains("media_publish"));
    }

    [Fact]
    public async Task Le_compte_relie_est_trouve_depuis_la_page_facebook()
    {
        var graph = new FakeGraph(("GET", "555", HttpStatusCode.OK, """{"instagram_business_account":{"id":"1784","username":"scintinet"},"id":"555"}"""));

        var found = await Create(graph).DeriveAsync(new ChannelCredentials("555", "jeton-page"));

        Assert.True(found.IsSuccess, found.Error);
        Assert.Equal("1784", found.Value!.AccountId);
        Assert.Equal("jeton-page", found.Value.AccessToken);
        Assert.Equal("@scintinet", found.Value.AccountName);
    }

    [Fact]
    public async Task Une_page_sans_compte_instagram_relie_est_expliquee()
    {
        var graph = new FakeGraph(("GET", "555", HttpStatusCode.OK, """{"id":"555"}"""));
        var found = await Create(graph).DeriveAsync(new ChannelCredentials("555", "jeton-page"));

        Assert.True(found.IsFailure);
        Assert.Contains("compte professionnel", found.Error);
    }

    [Fact]
    public async Task Une_autorisation_manquante_est_expliquee()
    {
        var graph = new FakeGraph(("POST", "1784/media", HttpStatusCode.BadRequest, """{"error":{"message":"(#10) permission","code":10}}"""));
        var result = await Create(graph).PublishAsync(new PublishRequest("Texte", null, "https://x/img.jpg"), Account);

        Assert.Contains("instagram_content_publish", result.Error);
    }

    [Theory]
    [InlineData("Bonjour", "https://www.scintinet.com/devis?utm=1", "Bonjour\n\n👉 Infos et devis gratuit : scintinet.com")]
    [InlineData("Visitez scintinet.com", "https://www.scintinet.com/", "Visitez scintinet.com")]
    [InlineData("Sans lien", null, "Sans lien")]
    public void La_legende_ecrit_le_site_en_clair(string message, string? link, string expected) =>
        Assert.Equal(expected, InstagramPublisher.Caption(message, link));
}
