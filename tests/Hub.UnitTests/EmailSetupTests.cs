using Hub.SharedKernel.Email;
using Hub.Web.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Hub.UnitTests;

/// <summary>Changer de fournisseur de courriel ne demande qu'un réglage : le métier ne voit que IEmailSender.</summary>
public class EmailSetupTests
{
    private sealed class TestEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Test";
        public string ApplicationName { get; set; } = "Hub";
        public string ContentRootPath { get; set; } = Path.GetTempPath();
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }

    private static (string Provider, IEmailSender Sender) Build(params (string Key, string Value)[] settings)
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(settings.Select(s => new KeyValuePair<string, string?>(s.Key, s.Value)))
            .Build();
        var services = new ServiceCollection().AddLogging();
        services.AddEmail(config, new TestEnvironment());
        using var sp = services.BuildServiceProvider();
        using var scope = sp.CreateScope();
        return (sp.GetRequiredService<EmailProviderInfo>().Name, scope.ServiceProvider.GetRequiredService<IEmailSender>());
    }

    [Fact]
    public void Sans_configuration_les_courriels_vont_au_journal() =>
        Assert.Equal("Journal", Build().Provider);

    [Fact]
    public void Une_cle_Resend_active_Resend() =>
        Assert.Equal("Resend", Build(("Resend:ApiKey", "re_test")).Provider);

    [Fact]
    public void Azure_Communication_Services_se_choisit_par_reglage()
    {
        var (provider, sender) = Build(
            ("Resend:ApiKey", "re_test"),
            ("Email:Provider", "AzureCommunication"),
            ("AzureCommunication:ConnectionString", "endpoint=https://exemple.communication.azure.com/;accesskey=Zm9v"),
            ("AzureCommunication:FromEmail", "noreply@scintinet.com"));

        Assert.Equal("AzureCommunication", provider);
        Assert.Equal("AzureCommunicationEmailSender", sender.GetType().Name);
    }

    [Fact]
    public void Un_dossier_local_est_detecte_en_developpement() =>
        Assert.Equal("Dossier", Build(("DevEmail:Folder", "App_Data/courriels")).Provider);

    [Fact]
    public void Un_fournisseur_inconnu_est_refuse_au_demarrage() =>
        Assert.Throws<InvalidOperationException>(() => Build(("Email:Provider", "Pigeon")));

    [Fact]
    public void Un_fournisseur_choisi_mais_incomplet_est_refuse_au_demarrage() =>
        Assert.Throws<InvalidOperationException>(() => Build(("Email:Provider", "AzureCommunication")));
}
