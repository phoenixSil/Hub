namespace Hub.SharedKernel.Email;

public sealed record EmailMessage(
    string To,
    string Subject,
    string HtmlBody,
    string? ReplyTo = null,
    string? FromName = null,
    IReadOnlyList<EmailAttachment>? Attachments = null);

/// <summary>Fichier joint à un courriel (offre en PDF, photos…).</summary>
public sealed record EmailAttachment(string FileName, string ContentType, byte[] Content)
{
    /// <summary>Limite des fournisseurs de courriel (Resend, Azure) : 40 Mo au total, on reste prudents.</summary>
    public const long MaxTotalBytes = 15 * 1024 * 1024;

    public static readonly IReadOnlyDictionary<string, string> AllowedTypes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        [".pdf"] = "application/pdf",
        [".jpg"] = "image/jpeg", [".jpeg"] = "image/jpeg", [".png"] = "image/png", [".webp"] = "image/webp",
        [".docx"] = "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
        [".xlsx"] = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
    };
}

public interface IEmailSender
{
    Task<Result> SendAsync(EmailMessage message, CancellationToken ct = default);
}
