using System.Globalization;
using System.Net.Mail;
using System.Security.Cryptography;
using Hub.Modules.GiftCards.Domain;
using Hub.Modules.GiftCards.Infrastructure;
using Hub.SharedKernel;
using Hub.SharedKernel.Email;
using Hub.SharedKernel.Time;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Hub.Modules.GiftCards.Application;

/// <summary>Ce que les courriels des cartes-cadeaux affichent de l'entreprise (fourni par l'hôte).</summary>
public sealed record GiftCardContext(
    string CompanyName,
    string Phone,
    string PublicEmail,
    string AdminEmail,
    string SiteUrl,
    string PrimaryColor,
    string AccentColor,
    string? LogoUrl,
    string? PaymentInstructions);

public interface IGiftCardContextSource
{
    Task<GiftCardContext> GetAsync(CancellationToken ct = default);
}

/// <summary>Demande de carte-cadeau faite sur le site.</summary>
public sealed class GiftCardRequest
{
    public decimal Amount { get; set; }
    public string BuyerName { get; set; } = string.Empty;
    public string BuyerEmail { get; set; } = string.Empty;
    public string? BuyerPhone { get; set; }
    public string RecipientName { get; set; } = string.Empty;
    public string? RecipientEmail { get; set; }
    public string? Message { get; set; }
    public DateOnly? DeliverOn { get; set; }
    public bool ConsentGiven { get; set; }
}

public sealed record GiftCardUseView(DateTimeOffset At, decimal Amount, string? Note, string? By);

public sealed record GiftCardView(
    Guid Id, string Code, decimal Amount, decimal Balance, GiftCardStatus Status,
    string BuyerName, string BuyerEmail, string? BuyerPhone, string RecipientName, string? RecipientEmail,
    string? Message, DateOnly? DeliverOn, DateTimeOffset CreatedAt, DateTimeOffset? PaidAt, DateTimeOffset? SentAt,
    string? LastError, IReadOnlyList<GiftCardUseView> Uses);

/// <summary>Ce qu'un visiteur voit en vérifiant son solde (rien sur l'acheteur).</summary>
public sealed record GiftCardBalance(string Code, decimal Amount, decimal Balance, GiftCardStatus Status);

public interface IGiftCardService
{
    Task<Result<Guid>> RequestAsync(GiftCardRequest request, CancellationToken ct = default);
    Task<IReadOnlyList<GiftCardView>> ListAsync(GiftCardStatus? status = null, CancellationToken ct = default);
    Task<GiftCardBalance?> CheckBalanceAsync(string code, CancellationToken ct = default);

    /// <summary>Paiement reçu : la carte est envoyée tout de suite, ou à la date choisie par l'acheteur.</summary>
    Task<Result> MarkPaidAsync(Guid id, string? note, CancellationToken ct = default);
    Task<Result> ResendAsync(Guid id, CancellationToken ct = default);

    /// <summary>Déduit un montant du solde (service rendu payé avec la carte).</summary>
    Task<Result<decimal>> RedeemAsync(string code, decimal amount, string? note, string? by, CancellationToken ct = default);
    Task<Result> CancelAsync(Guid id, CancellationToken ct = default);

    /// <summary>Envoie les cartes payées dont la date d'envoi est arrivée.</summary>
    Task<int> SendDueAsync(CancellationToken ct = default);
}

internal sealed class GiftCardService(
    IDbContextFactory<GiftCardsDbContext> factory,
    IGiftCardContextSource source,
    IEmailSender email,
    TimeProvider clock,
    ILogger<GiftCardService> logger) : IGiftCardService
{
    public const decimal MinAmount = 25, MaxAmount = 1000;
    public static readonly decimal[] SuggestedAmounts = [50, 75, 100, 150, 200];

    public async Task<Result<Guid>> RequestAsync(GiftCardRequest r, CancellationToken ct = default)
    {
        var today = BusinessTime.Today(clock);
        var errors = Validate(r, today);
        if (errors.Count > 0) return Result<Guid>.Invalid(errors);

        await using var db = await factory.CreateDbContextAsync(ct);
        var card = new GiftCard
        {
            Code = await UniqueCodeAsync(db, ct),
            Amount = r.Amount,
            Balance = r.Amount,
            BuyerName = r.BuyerName.Trim(),
            BuyerEmail = r.BuyerEmail.Trim(),
            BuyerPhone = Clean(r.BuyerPhone, 40),
            RecipientName = r.RecipientName.Trim(),
            RecipientEmail = Clean(r.RecipientEmail, 256),
            Message = Clean(r.Message, 400),
            DeliverOn = r.DeliverOn > today ? r.DeliverOn : null,
        };
        db.GiftCards.Add(card);
        await db.SaveChangesAsync(ct);

        var context = await source.GetAsync(ct);
        var buyer = GiftCardEmails.RequestReceived(card, context);
        var admin = GiftCardEmails.AdminNotice(card, context);
        await SendQuietlyAsync(new EmailMessage(card.BuyerEmail, buyer.Subject, buyer.Html, ReplyTo: context.PublicEmail, FromName: context.CompanyName), ct);
        await SendQuietlyAsync(new EmailMessage(context.AdminEmail, admin.Subject, admin.Html, ReplyTo: card.BuyerEmail, FromName: context.CompanyName), ct);
        return Result<Guid>.Success(card.Id);
    }

    internal static Dictionary<string, string> Validate(GiftCardRequest r, DateOnly today)
    {
        var errors = new Dictionary<string, string>();
        if (r.Amount < MinAmount || r.Amount > MaxAmount || r.Amount % 5 != 0)
            errors[nameof(r.Amount)] = $"Choisissez un montant entre {Money(MinAmount)} et {Money(MaxAmount)}, par tranches de 5 $.";
        if (string.IsNullOrWhiteSpace(r.BuyerName) || r.BuyerName.Length > 160) errors[nameof(r.BuyerName)] = "Indiquez votre nom.";
        if (!IsEmail(r.BuyerEmail)) errors[nameof(r.BuyerEmail)] = "Indiquez un courriel valide.";
        if (string.IsNullOrWhiteSpace(r.RecipientName) || r.RecipientName.Length > 160) errors[nameof(r.RecipientName)] = "Indiquez le nom de la personne qui reçoit la carte.";
        if (!string.IsNullOrWhiteSpace(r.RecipientEmail) && !IsEmail(r.RecipientEmail)) errors[nameof(r.RecipientEmail)] = "Ce courriel n'est pas valide.";
        if (r.Message?.Length > 400) errors[nameof(r.Message)] = "400 caractères au plus.";
        if (r.DeliverOn is { } d && (d < today || d > today.AddDays(366))) errors[nameof(r.DeliverOn)] = "Choisissez une date dans les douze prochains mois.";
        if (!r.ConsentGiven) errors[nameof(r.ConsentGiven)] = "Votre accord est nécessaire pour traiter la demande.";
        return errors;
    }

    public async Task<IReadOnlyList<GiftCardView>> ListAsync(GiftCardStatus? status = null, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var query = db.GiftCards.AsNoTracking().Include(c => c.Uses).AsQueryable();
        if (status is { } s) query = query.Where(c => c.Status == s);
        var cards = await query.OrderByDescending(c => c.CreatedAt).Take(300).ToListAsync(ct);
        return cards.Select(View).ToList();
    }

    public async Task<GiftCardBalance?> CheckBalanceAsync(string code, CancellationToken ct = default)
    {
        var normalized = GiftCardCodes.Normalize(code);
        if (normalized is null) return null;
        await using var db = await factory.CreateDbContextAsync(ct);
        var card = await db.GiftCards.AsNoTracking().FirstOrDefaultAsync(c => c.Code == normalized, ct);
        return card is null || card.Status is GiftCardStatus.AwaitingPayment or GiftCardStatus.Cancelled
            ? null : new GiftCardBalance(card.Code, card.Amount, card.Balance, card.Status);
    }

    public async Task<Result> MarkPaidAsync(Guid id, string? note, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var card = await db.GiftCards.FirstOrDefaultAsync(c => c.Id == id, ct);
        if (card is null) return Result.Failure("Carte introuvable.");
        if (card.Status != GiftCardStatus.AwaitingPayment) return Result.Failure("Cette carte n'attend pas de paiement.");
        card.Status = GiftCardStatus.Active;
        card.PaidAt = clock.GetUtcNow();
        card.PaidNote = Clean(note, 300);
        await db.SaveChangesAsync(ct);
        if (card.DeliverOn is null || card.DeliverOn <= BusinessTime.Today(clock))
            return await DeliverAsync(db, card, ct);
        return Result.Success();
    }

    public async Task<Result> ResendAsync(Guid id, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var card = await db.GiftCards.FirstOrDefaultAsync(c => c.Id == id, ct);
        if (card is null) return Result.Failure("Carte introuvable.");
        if (card.Status is GiftCardStatus.AwaitingPayment or GiftCardStatus.Cancelled) return Result.Failure("Cette carte n'est pas payée.");
        return await DeliverAsync(db, card, ct);
    }

    public async Task<Result<decimal>> RedeemAsync(string code, decimal amount, string? note, string? by, CancellationToken ct = default)
    {
        var normalized = GiftCardCodes.Normalize(code);
        if (normalized is null) return Result<decimal>.Failure("Code invalide.");
        if (amount <= 0) return Result<decimal>.Failure("Indiquez un montant positif.");
        await using var db = await factory.CreateDbContextAsync(ct);
        var card = await db.GiftCards.FirstOrDefaultAsync(c => c.Code == normalized, ct);
        if (card is null) return Result<decimal>.Failure("Aucune carte avec ce code.");
        if (card.Status != GiftCardStatus.Active) return Result<decimal>.Failure(card.Status switch
        {
            GiftCardStatus.AwaitingPayment => "Cette carte n'est pas encore payée.",
            GiftCardStatus.Used => "Le solde de cette carte est déjà épuisé.",
            _ => "Cette carte est annulée.",
        });
        if (amount > card.Balance) return Result<decimal>.Failure($"Solde insuffisant : il reste {Money(card.Balance)}.");
        card.Balance -= amount;
        if (card.Balance == 0) card.Status = GiftCardStatus.Used;
        db.GiftCardUses.Add(new GiftCardUse { GiftCardId = card.Id, Amount = amount, Note = Clean(note, 300), RecordedBy = Clean(by, 200) });
        await db.SaveChangesAsync(ct);
        return Result<decimal>.Success(card.Balance);
    }

    public async Task<Result> CancelAsync(Guid id, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var card = await db.GiftCards.FirstOrDefaultAsync(c => c.Id == id, ct);
        if (card is null) return Result.Failure("Carte introuvable.");
        if (card.Status == GiftCardStatus.Used) return Result.Failure("Une carte entièrement utilisée ne peut pas être annulée.");
        card.Status = GiftCardStatus.Cancelled;
        await db.SaveChangesAsync(ct);
        return Result.Success();
    }

    public async Task<int> SendDueAsync(CancellationToken ct = default)
    {
        var today = BusinessTime.Today(clock);
        await using var db = await factory.CreateDbContextAsync(ct);
        var due = await db.GiftCards
            .Where(c => c.Status == GiftCardStatus.Active && c.SentAt == null && (c.DeliverOn == null || c.DeliverOn <= today))
            .ToListAsync(ct);
        var sent = 0;
        foreach (var card in due)
            if ((await DeliverAsync(db, card, ct)).IsSuccess) sent++;
        return sent;
    }

    /// <summary>Envoie la carte (au destinataire, sinon à l'acheteur) puis prévient l'acheteur.</summary>
    private async Task<Result> DeliverAsync(GiftCardsDbContext db, GiftCard card, CancellationToken ct)
    {
        var context = await source.GetAsync(ct);
        var to = card.RecipientEmail ?? card.BuyerEmail;
        var mail = GiftCardEmails.Card(card, context);
        var result = await email.SendAsync(new EmailMessage(to, mail.Subject, mail.Html, ReplyTo: context.PublicEmail, FromName: context.CompanyName), ct);
        if (result.IsFailure)
        {
            card.LastError = result.Error?.Length > 500 ? result.Error[..500] : result.Error;
            await db.SaveChangesAsync(ct);
            return result;
        }
        card.SentAt = clock.GetUtcNow();
        card.LastError = null;
        await db.SaveChangesAsync(ct);
        if (card.RecipientEmail is not null)
        {
            var notice = GiftCardEmails.Delivered(card, context);
            await SendQuietlyAsync(new EmailMessage(card.BuyerEmail, notice.Subject, notice.Html, ReplyTo: context.PublicEmail, FromName: context.CompanyName), ct);
        }
        return Result.Success();
    }

    private async Task SendQuietlyAsync(EmailMessage message, CancellationToken ct)
    {
        var result = await email.SendAsync(message, ct);
        if (result.IsFailure) logger.LogWarning("Courriel de carte-cadeau non envoyé à {To} : {Error}", message.To, result.Error);
    }

    private static async Task<string> UniqueCodeAsync(GiftCardsDbContext db, CancellationToken ct)
    {
        for (var i = 0; i < 10; i++)
        {
            var code = GiftCardCodes.New();
            if (!await db.GiftCards.IgnoreQueryFilters().AnyAsync(c => c.Code == code, ct)) return code;
        }
        throw new InvalidOperationException("Impossible de générer un code de carte unique.");
    }

    private static GiftCardView View(GiftCard c) => new(
        c.Id, c.Code, c.Amount, c.Balance, c.Status, c.BuyerName, c.BuyerEmail, c.BuyerPhone, c.RecipientName, c.RecipientEmail,
        c.Message, c.DeliverOn, c.CreatedAt, c.PaidAt, c.SentAt, c.LastError,
        c.Uses.OrderByDescending(u => u.CreatedAt).Select(u => new GiftCardUseView(u.CreatedAt, u.Amount, u.Note, u.RecordedBy)).ToList());

    private static string? Clean(string? value, int max)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var v = value.Trim();
        return v.Length > max ? v[..max] : v;
    }

    private static bool IsEmail(string? value) =>
        !string.IsNullOrWhiteSpace(value) && value.Length <= 256 && MailAddress.TryCreate(value.Trim(), out var a) && a.Host.Contains('.');

    public static string Money(decimal amount) =>
        amount.ToString(amount % 1 == 0 ? "#,0" : "#,0.00", CultureInfo.GetCultureInfo("fr-CA")) + " $";
}

/// <summary>Codes de carte : 12 caractères sans ambiguïté (pas de 0/O ni de 1/I), soit environ 10^18 combinaisons.</summary>
public static class GiftCardCodes
{
    private const string Alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";

    public static string New()
    {
        Span<char> c = stackalloc char[12];
        for (var i = 0; i < c.Length; i++) c[i] = Alphabet[RandomNumberGenerator.GetInt32(Alphabet.Length)];
        return $"{c[..4]}-{c[4..8]}-{c[8..]}";
    }

    /// <summary>Remet un code saisi à la main au bon format (majuscules, tirets), ou null s'il ne peut pas être valide.</summary>
    public static string? Normalize(string? input)
    {
        if (string.IsNullOrWhiteSpace(input)) return null;
        var chars = input.ToUpperInvariant().Where(char.IsLetterOrDigit).ToArray();
        if (chars.Length != 12 || chars.Any(ch => !Alphabet.Contains(ch))) return null;
        var s = new string(chars);
        return $"{s[..4]}-{s[4..8]}-{s[8..]}";
    }
}
