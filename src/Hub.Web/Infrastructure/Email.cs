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

/// <summary>
/// En local : chaque courriel devient un fichier .html (ouvrable dans le navigateur)
/// dans le dossier configuré par DevEmail:Folder. Rien ne part vers de vraies adresses.
/// </summary>
internal sealed class FileEmailSender(string folder, ILogger<FileEmailSender> logger) : IEmailSender
{
    public async Task<Result> SendAsync(EmailMessage message, CancellationToken ct = default)
    {
        Directory.CreateDirectory(folder);
        var safeTo = new string(message.To.Select(c => char.IsLetterOrDigit(c) ? c : '_').ToArray());
        var file = Path.Combine(folder, $"{DateTime.Now:yyyyMMdd-HHmmss-fff}_{safeTo}.html");
        var header = $"""
            <div style="font:13px/1.5 monospace;background:#fff8d6;border-bottom:1px solid #e5d9a0;padding:10px 16px">
              <b>Courriel local (non envoyé)</b><br>
              À : {System.Net.WebUtility.HtmlEncode(message.To)}<br>
              Répondre à : {System.Net.WebUtility.HtmlEncode(message.ReplyTo ?? "")}<br>
              Sujet : {System.Net.WebUtility.HtmlEncode(message.Subject)}
            </div>
            """;
        await File.WriteAllTextAsync(file, header + message.HtmlBody, ct);
        logger.LogInformation("Courriel local enregistré : {File}", file);
        return Result.Success();
    }
}

/// <summary>Sans clé Resend ni dossier local : le courriel est écrit dans le journal.</summary>
internal sealed class LoggingEmailSender(ILogger<LoggingEmailSender> logger) : IEmailSender
{
    public Task<Result> SendAsync(EmailMessage message, CancellationToken ct = default)
    {
        logger.LogInformation("Courriel (non envoyé, aucune clé Resend) à {To} : {Subject}", message.To, message.Subject);
        return Task.FromResult(Result.Success());
    }
}
