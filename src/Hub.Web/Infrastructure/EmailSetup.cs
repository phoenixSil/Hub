using Hub.SharedKernel.Email;

namespace Hub.Web.Infrastructure;

/// <summary>
/// Un fournisseur de courriel : il sait dire s'il est configuré et s'enregistrer.
/// Ajouter un fournisseur = une classe qui implémente cette interface et une ligne
/// dans <see cref="EmailSetup"/>. Le métier ne voit jamais que <see cref="IEmailSender"/>.
/// </summary>
internal interface IEmailProviderSetup
{
    /// <summary>Nom utilisé dans le réglage Email:Provider.</summary>
    string Name { get; }
    bool IsConfigured(IConfiguration configuration);
    void Register(IServiceCollection services, IConfiguration configuration, IHostEnvironment environment);
}

public static class EmailSetup
{
    /// <summary>Ordre de détection automatique quand Email:Provider est vide.</summary>
    private static readonly IEmailProviderSetup[] Providers =
    [
        new ResendSetup(),
        new AzureCommunicationSetup(),
        new FolderSetup(),
        new LogSetup(),
    ];

    /// <summary>
    /// Choisit le fournisseur : celui nommé par Email:Provider (Resend, AzureCommunication, Dossier, Journal),
    /// sinon le premier configuré dans l'ordre ci-dessus.
    /// </summary>
    public static IServiceCollection AddEmail(this IServiceCollection services, IConfiguration configuration, IHostEnvironment environment)
    {
        var requested = configuration["Email:Provider"];
        var provider = string.IsNullOrWhiteSpace(requested)
            ? Providers.First(p => p.IsConfigured(configuration))
            : Providers.FirstOrDefault(p => string.Equals(p.Name, requested, StringComparison.OrdinalIgnoreCase))
              ?? throw new InvalidOperationException(
                  $"Fournisseur de courriel inconnu : « {requested} ». Valeurs possibles : {string.Join(", ", Providers.Select(p => p.Name))}.");

        if (!provider.IsConfigured(configuration))
            throw new InvalidOperationException($"Le fournisseur de courriel « {provider.Name} » est choisi mais incomplet : vérifiez sa configuration.");

        provider.Register(services, configuration, environment);
        services.AddSingleton(new EmailProviderInfo(provider.Name));
        return services;
    }

    private sealed class ResendSetup : IEmailProviderSetup
    {
        public string Name => "Resend";
        public bool IsConfigured(IConfiguration c) => !string.IsNullOrWhiteSpace(c[$"{ResendOptions.Section}:ApiKey"]);
        public void Register(IServiceCollection s, IConfiguration c, IHostEnvironment e)
        {
            s.Configure<ResendOptions>(c.GetSection(ResendOptions.Section));
            s.AddHttpClient<IEmailSender, ResendEmailSender>();
        }
    }

    private sealed class AzureCommunicationSetup : IEmailProviderSetup
    {
        public string Name => "AzureCommunication";
        public bool IsConfigured(IConfiguration c) =>
            !string.IsNullOrWhiteSpace(c[$"{AzureCommunicationOptions.Section}:ConnectionString"])
            && !string.IsNullOrWhiteSpace(c[$"{AzureCommunicationOptions.Section}:FromEmail"]);
        public void Register(IServiceCollection s, IConfiguration c, IHostEnvironment e)
        {
            s.Configure<AzureCommunicationOptions>(c.GetSection(AzureCommunicationOptions.Section));
            s.AddSingleton<IEmailSender, AzureCommunicationEmailSender>();
        }
    }

    private sealed class FolderSetup : IEmailProviderSetup
    {
        public string Name => "Dossier";
        public bool IsConfigured(IConfiguration c) => !string.IsNullOrWhiteSpace(c["DevEmail:Folder"]);
        public void Register(IServiceCollection s, IConfiguration c, IHostEnvironment e)
        {
            var folder = Path.Combine(e.ContentRootPath, c["DevEmail:Folder"]!);
            s.AddSingleton<IEmailSender>(sp => new FileEmailSender(folder, sp.GetRequiredService<ILogger<FileEmailSender>>()));
        }
    }

    private sealed class LogSetup : IEmailProviderSetup
    {
        public string Name => "Journal";
        public bool IsConfigured(IConfiguration c) => true;
        public void Register(IServiceCollection s, IConfiguration c, IHostEnvironment e) =>
            s.AddScoped<IEmailSender, LoggingEmailSender>();
    }
}

/// <summary>Fournisseur de courriel actif (affiché au démarrage).</summary>
public sealed record EmailProviderInfo(string Name);
