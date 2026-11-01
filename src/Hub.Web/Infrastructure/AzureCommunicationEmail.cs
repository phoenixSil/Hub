using Azure;
using Azure.Communication.Email;
using Hub.SharedKernel;
using Hub.SharedKernel.Email;
using Microsoft.Extensions.Options;
using AcsEmailMessage = Azure.Communication.Email.EmailMessage;
using HubEmailMessage = Hub.SharedKernel.Email.EmailMessage;

namespace Hub.Web.Infrastructure;

public sealed class AzureCommunicationOptions
{
    public const string Section = "AzureCommunication";

    /// <summary>Chaîne de connexion de la ressource Azure Communication Services.</summary>
    public string ConnectionString { get; set; } = string.Empty;

    /// <summary>Adresse d'expédition, sur un domaine relié à la ressource (ex. noreply@scintinet.com).</summary>
    public string FromEmail { get; set; } = string.Empty;
}

/// <summary>Envoi par Azure Communication Services (Email), pour tout garder dans Azure.</summary>
internal sealed class AzureCommunicationEmailSender(
    IOptions<AzureCommunicationOptions> options,
    ILogger<AzureCommunicationEmailSender> logger) : IEmailSender
{
    private readonly Lazy<EmailClient> _client = new(() => new EmailClient(options.Value.ConnectionString));

    public async Task<Result> SendAsync(HubEmailMessage message, CancellationToken ct = default)
    {
        try
        {
            var email = new AcsEmailMessage(
                options.Value.FromEmail,
                message.To,
                new EmailContent(message.Subject) { Html = message.HtmlBody });
            if (!string.IsNullOrWhiteSpace(message.ReplyTo))
                email.ReplyTo.Add(new EmailAddress(message.ReplyTo));

            // WaitUntil.Started : le service accepte le courriel ; l'envoi se poursuit de son côté.
            await _client.Value.SendAsync(WaitUntil.Started, email, ct);
            return Result.Success();
        }
        catch (RequestFailedException ex)
        {
            logger.LogError(ex, "Azure Communication Services a refusé le courriel à {To} ({Code})", message.To, ex.ErrorCode);
            return Result.Failure($"Le service de courriel a refusé l'envoi ({ex.ErrorCode ?? ex.Status.ToString()}).");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Échec de l'envoi à {To}", message.To);
            return Result.Failure("Le service de courriel est injoignable.");
        }
    }
}
