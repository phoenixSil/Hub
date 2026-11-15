using System.Net;
using System.Text.Json;
using Hub.Modules.Channels.Application.Autopilot;
using Hub.Modules.Channels.Domain;
using Hub.Modules.Channels.Publishers;
using Hub.SharedKernel;
using Hub.SharedKernel.Ai;
using Hub.SharedKernel.Email;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Hub.Modules.Channels.Application.Reviews;

public sealed record ReviewView(Guid Id, string Author, int Rating, string? Comment, DateTimeOffset WrittenAt, string? DraftReply, string? Reply, DateTimeOffset? RepliedAt);

public sealed record ReviewSummary(int Count, double Average, int Unanswered);

/// <summary>Avis Google : relevés automatiquement, réponse proposée par l'IA, publiée seulement après validation.</summary>
public interface IReviewService
{
    /// <summary>Vrai quand un réseau avec avis (Google Business) est connecté.</summary>
    Task<bool> IsConnectedAsync(CancellationToken ct = default);

    Task<IReadOnlyList<ReviewView>> ListAsync(CancellationToken ct = default);
    Task<ReviewSummary> SummaryAsync(CancellationToken ct = default);

    /// <summary>Relève les avis : renvoie les nouveaux (pour prévenir l'administrateur).</summary>
    Task<Result<IReadOnlyList<ReviewView>>> SyncAsync(CancellationToken ct = default);

    /// <summary>L'IA propose une réponse (gardée en brouillon, rien n'est publié).</summary>
    Task<Result<string>> ProposeReplyAsync(Guid id, CancellationToken ct = default);

    /// <summary>Publie la réponse validée par l'administrateur sur la fiche.</summary>
    Task<Result> ReplyAsync(Guid id, string reply, CancellationToken ct = default);

    /// <summary>Chiffres de la fiche Google sur les derniers jours ; null si la fiche n'est pas connectée.</summary>
    Task<Result<LocationMetrics>?> LocationMetricsAsync(int days, CancellationToken ct = default);
}

internal sealed class ReviewService(
    IDbContextFactory<ChannelsDbContext> factory,
    IChannelConnections connections,
    IPublisherResolver publishers,
    IAiTextGenerator ai,
    IAutopilotContextSource source,
    IEmailSender email,
    TimeProvider clock,
    ILogger<ReviewService> logger) : IReviewService
{
    public const int MaxReply = 4000;
    private const ChannelType Channel = ChannelType.GoogleBusiness;

    public async Task<bool> IsConnectedAsync(CancellationToken ct = default) =>
        publishers.ReviewsFor(Channel) is not null && (await connections.GetStatusAsync(Channel, ct)).IsConnected;

    public async Task<IReadOnlyList<ReviewView>> ListAsync(CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        return (await db.Reviews.AsNoTracking().OrderBy(r => r.Reply != null).ThenByDescending(r => r.WrittenAt).Take(200).ToListAsync(ct))
            .Select(View).ToList();
    }

    public async Task<ReviewSummary> SummaryAsync(CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var ratings = await db.Reviews.AsNoTracking().Select(r => new { r.Rating, Answered = r.Reply != null }).ToListAsync(ct);
        var rated = ratings.Where(r => r.Rating > 0).ToList();
        return new ReviewSummary(ratings.Count, rated.Count == 0 ? 0 : Math.Round(rated.Average(r => r.Rating), 1), ratings.Count(r => !r.Answered));
    }

    private static ReviewView View(GoogleReview r) => new(r.Id, r.Author, r.Rating, r.Comment, r.WrittenAt, r.DraftReply, r.Reply, r.RepliedAt);

    public async Task<Result<IReadOnlyList<ReviewView>>> SyncAsync(CancellationToken ct = default)
    {
        var reader = publishers.ReviewsFor(Channel);
        var credentials = await connections.GetCredentialsAsync(Channel, ct);
        if (reader is null || credentials is null) return Result<IReadOnlyList<ReviewView>>.Success([]);
        var found = await reader.ListReviewsAsync(credentials, ct);
        if (found.IsFailure) return Result<IReadOnlyList<ReviewView>>.Failure(found.Error!);

        await using var db = await factory.CreateDbContextAsync(ct);
        var known = await db.Reviews.Where(r => r.Channel == Channel).ToDictionaryAsync(r => r.ExternalId, ct);
        var added = new List<GoogleReview>();
        foreach (var x in found.Value!)
        {
            if (known.TryGetValue(x.Id, out var row))
            {
                // Réponse faite ailleurs (sur Google directement) : on la reprend.
                if (x.Reply is not null && row.Reply != x.Reply) { row.Reply = x.Reply; row.RepliedAt = x.RepliedAt; row.DraftReply = null; }
                row.Comment = x.Comment;
                row.Rating = x.Rating;
                continue;
            }
            var review = new GoogleReview
            {
                Channel = Channel, ExternalId = x.Id, Author = x.Author.Truncate(200), Rating = x.Rating,
                Comment = x.Comment?.Truncate(4000), WrittenAt = x.CreatedAt, Reply = x.Reply?.Truncate(4000), RepliedAt = x.RepliedAt,
            };
            db.Reviews.Add(review);
            added.Add(review);
        }
        await db.SaveChangesAsync(ct);

        // Le tout premier relevé reprend l'historique : pas de courriel pour les anciens avis.
        var fresh = added.Where(r => r.Reply is null && known.Count > 0).ToList();
        if (fresh.Count > 0) await NotifyAsync(fresh, ct);
        return Result<IReadOnlyList<ReviewView>>.Success(added.Select(View).ToList());
    }

    private async Task NotifyAsync(IReadOnlyList<GoogleReview> reviews, CancellationToken ct)
    {
        var context = await source.GetAsync(ct);
        if (string.IsNullOrWhiteSpace(context.AdminEmail)) return;
        var link = $"{(context.SiteUrl ?? "").TrimEnd('/')}/admin/avis";
        var items = string.Join("", reviews.Select(r =>
            $"<li><strong>{new string('★', Math.Clamp(r.Rating, 0, 5))}{new string('☆', 5 - Math.Clamp(r.Rating, 0, 5))}</strong> {WebUtility.HtmlEncode(r.Author)} : « {WebUtility.HtmlEncode(r.Comment ?? "(sans commentaire)")} »</li>"));
        var html = $"""
            <p>Bonjour,</p>
            <p>{reviews.Count} nouvel(s) avis Google pour {WebUtility.HtmlEncode(context.CompanyName)} :</p>
            <ul>{items}</ul>
            <p>Une réponse rapide montre aux futurs clients que vous êtes à l'écoute. <a href="{WebUtility.HtmlEncode(link)}">Répondre (l'IA vous propose un texte)</a></p>
            """;
        var subject = reviews.Count == 1 ? $"Nouvel avis Google : {reviews[0].Rating} étoile(s)" : $"{reviews.Count} nouveaux avis Google";
        var sent = await email.SendAsync(new EmailMessage(context.AdminEmail, subject, html), ct);
        if (sent.IsFailure) logger.LogWarning("Courriel « nouvel avis » non envoyé : {Error}", sent.Error);
    }

    private const string Schema = """
        {"type":"object","properties":{"reponse":{"type":"string"}},"required":["reponse"],"additionalProperties":false}
        """;

    public async Task<Result<string>> ProposeReplyAsync(Guid id, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var review = await db.Reviews.FirstOrDefaultAsync(r => r.Id == id, ct);
        if (review is null) return Result<string>.Failure("Avis introuvable.");
        var context = await source.GetAsync(ct);
        string text;
        if (ai.IsAvailable)
        {
            var response = await ai.GenerateJsonAsync(new AiJsonRequest(SystemPrompt(context.CompanyName), UserPrompt(review), Schema, 700), ct);
            if (response.IsFailure) return Result<string>.Failure(response.Error!);
            try { text = JsonDocument.Parse(response.Value!.Json).RootElement.GetProperty("reponse").GetString() ?? ""; }
            catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException) { return Result<string>.Failure("Réponse de l'IA illisible. Réessayez."); }
        }
        else text = Fallback(review, context.CompanyName);
        text = text.Trim().Truncate(MaxReply);
        if (text.Length == 0) return Result<string>.Failure("L'IA n'a rien proposé. Réessayez.");
        review.DraftReply = text;
        await db.SaveChangesAsync(ct);
        return Result<string>.Success(text);
    }

    public static string SystemPrompt(string company) => $"""
        Tu réponds, au nom de {company} (entreprise de ménage et d'aide à domicile au Québec), à un avis client publié sur sa fiche Google.
        Règles :
        - Français du Québec, vouvoiement, ton chaleureux et professionnel, 2 à 4 phrases, sans émojis.
        - Remercie la personne par son prénom si on le connaît ; reprends un détail précis de son avis (jamais une formule passe-partout).
        - Avis positif : remercie et invite à revenir. Avis mitigé ou négatif : remercie, reconnais le problème sans te justifier ni accuser,
          dis ce qui sera fait, et propose d'en parler directement (sans donner de numéro de téléphone ni de courriel).
        - N'invente rien : pas de rabais, pas de promesse précise, pas de nom d'employé.
        - Termine par la signature : « L'équipe {company} ».
        """;

    public static string UserPrompt(GoogleReview r) =>
        $"Avis de {r.Author}, {r.Rating} étoile(s) sur 5 :\n« {(string.IsNullOrWhiteSpace(r.Comment) ? "(note seulement, sans commentaire)" : r.Comment)} »";

    /// <summary>Réponse de repli sans IA (selon la note).</summary>
    public static string Fallback(GoogleReview r, string company)
    {
        var first = r.Author.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "";
        var hello = first.Length > 1 && !first.EndsWith('.') ? $"Bonjour {first}," : "Bonjour,";
        return r.Rating >= 4
            ? $"{hello} merci beaucoup pour votre avis ! Nous sommes ravis que notre travail vous ait plu et serons heureux de vous retrouver bientôt.\nL'équipe {company}"
            : $"{hello} merci d'avoir pris le temps de nous écrire. Nous sommes désolés que tout n'ait pas été à la hauteur et aimerions en discuter avec vous pour corriger la situation.\nL'équipe {company}";
    }

    public async Task<Result<LocationMetrics>?> LocationMetricsAsync(int days, CancellationToken ct = default)
    {
        var reader = publishers.LocationFor(Channel);
        var credentials = await connections.GetCredentialsAsync(Channel, ct);
        if (reader is null || credentials is null) return null;
        // Google publie ses chiffres avec quelques jours de retard : la période s'arrête avant-hier.
        var to = Hub.SharedKernel.Time.BusinessTime.Today(clock).AddDays(-2);
        return await reader.GetLocationAsync(credentials, to.AddDays(-(days - 1)), to, ct);
    }

    public async Task<Result> ReplyAsync(Guid id, string reply, CancellationToken ct = default)
    {
        reply = (reply ?? "").Trim();
        if (reply.Length == 0) return Result.Failure("La réponse est vide.");
        if (reply.Length > MaxReply) return Result.Failure($"Réponse trop longue ({MaxReply} caractères au plus).");
        var reader = publishers.ReviewsFor(Channel);
        var credentials = await connections.GetCredentialsAsync(Channel, ct);
        if (reader is null || credentials is null) return Result.Failure("Google Business n'est pas connecté : Canaux → Connecter avec Google.");
        await using var db = await factory.CreateDbContextAsync(ct);
        var review = await db.Reviews.FirstOrDefaultAsync(r => r.Id == id, ct);
        if (review is null) return Result.Failure("Avis introuvable.");
        var sent = await reader.ReplyAsync(credentials, review.ExternalId, reply, ct);
        if (sent.IsFailure) { review.DraftReply = reply; await db.SaveChangesAsync(ct); return sent; }
        review.Reply = reply;
        review.RepliedAt = clock.GetUtcNow();
        review.DraftReply = null;
        await db.SaveChangesAsync(ct);
        return Result.Success();
    }
}
