namespace Hub.SharedKernel.Email;

public sealed record EmailMessage(
    string To,
    string Subject,
    string HtmlBody,
    string? ReplyTo = null,
    string? FromName = null);

public interface IEmailSender
{
    Task<Result> SendAsync(EmailMessage message, CancellationToken ct = default);
}
