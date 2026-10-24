using System.Net.Http.Headers;
using System.Net.Http.Json;
using Hub.SharedKernel;
using Hub.SharedKernel.Email;
using Microsoft.Extensions.Options;

namespace Hub.Web.Infrastructure;

public sealed class ResendOptions
{
    public const string Section = "Resend";
    public string ApiKey { get; set; } = string.Empty;
    public string FromEmail { get; set; } = "noreply@scintinet.com";
}

/// <summary>Envoi par l'API Resend (repris de l'ancienne version .NET).</summary>
internal sealed class ResendEmailSender(HttpClient http, IOptions<ResendOptions> options, ILogger<ResendEmailSender> logger) : IEmailSender
{
    public async Task<Result> SendAsync(EmailMessage message, CancellationToken ct = default)
    {
        var opts = options.Value;
        var from = string.IsNullOrWhiteSpace(message.FromName) ? opts.FromEmail : $"{message.FromName} <{opts.FromEmail}>";
        var payload = new Dictionary<string, object?>
        {
            ["from"] = from,
            ["to"] = new[] { message.To },
            ["subject"] = message.Subject,
            ["html"] = message.HtmlBody,
        };
        if (!string.IsNullOrWhiteSpace(message.ReplyTo))
            payload["reply_to"] = message.ReplyTo;

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, "https://api.resend.com/emails")
            {
                Content = JsonContent.Create(payload),
            };
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", opts.ApiKey);
            using var response = await http.SendAsync(request, ct);
            if (response.IsSuccessStatusCode) return Result.Success();

            var body = await response.Content.ReadAsStringAsync(ct);
            logger.LogError("Resend a répondu {Status} : {Body}", (int)response.StatusCode, body);
            return Result.Failure($"Le service de courriel a répondu {(int)response.StatusCode}.");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Échec de l'envoi à {To}", message.To);
            return Result.Failure("Le service de courriel est injoignable.");
        }
    }
}

/// <summary>En développement, sans clé Resend : le courriel est écrit dans le journal.</summary>
internal sealed class LoggingEmailSender(ILogger<LoggingEmailSender> logger) : IEmailSender
{
    public Task<Result> SendAsync(EmailMessage message, CancellationToken ct = default)
    {
        logger.LogInformation("Courriel (non envoyé, aucune clé Resend) à {To} : {Subject}", message.To, message.Subject);
        return Task.FromResult(Result.Success());
    }
}
