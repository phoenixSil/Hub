using System.Text.Json;
using Hub.Modules.Quotes.Domain;
using Hub.Modules.Quotes.Infrastructure;
using Hub.SharedKernel;
using Hub.SharedKernel.Email;
using Hub.SharedKernel.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Hub.Modules.Quotes.Application;

public sealed class QuoteOptions
{
    public const string Section = "Quotes";

    /// <summary>Préparer une proposition dès qu'une demande de devis arrive.</summary>
    public bool AutoGenerate { get; set; } = true;

    /// <summary>Plancher horaire (coût de revient) par catégorie : « services » et « aines ».</summary>
    public Dictionary<string, decimal> FloorHourlyRates { get; set; } = new() { ["services"] = 31m, ["aines"] = 35m };

    /// <summary>Mention ajoutée sous le tableau des prix envoyé au client.</summary>
    public string TaxNote { get; set; } = "Prix avant taxes. TPS et TVQ en sus, s'il y a lieu.";
}

/// <summary>Une proposition prête à afficher dans le back-office.</summary>
public sealed record ProposalView(QuoteProposal Proposal, IReadOnlyList<QuoteLine> Lines);

public interface IQuoteService
{
    /// <summary>Indique si le vrai modèle (Claude) est configuré ; sinon, le modèle simulé est utilisé.</summary>
    bool UsesRealModel { get; }
    string ModelName { get; }

    Task<Result<Guid>> GenerateAsync(Guid leadId, CancellationToken ct = default);
    Task<IReadOnlyList<ProposalView>> ListForLeadAsync(Guid leadId, CancellationToken ct = default);

    /// <summary>Envoie au client le message et les lignes validés par l'administrateur.</summary>
    Task<Result> ApproveAndSendAsync(Guid proposalId, IReadOnlyList<QuoteLine> lines, string message, string author, CancellationToken ct = default);
    Task DiscardAsync(Guid proposalId, string author, CancellationToken ct = default);
}

internal sealed class QuoteService(
    IDbContextFactory<QuotesDbContext> factory,
    IQuoteContextSource contextSource,
    IQuoteModel model,
    IEmailSender email,
    ITenantInfoProvider tenantInfo,
    IOptions<QuoteOptions> options,
    ILogger<QuoteService> logger) : IQuoteService
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public bool UsesRealModel => model is not SimulatedQuoteModel;
    public string ModelName => model.Name;

    public async Task<Result<Guid>> GenerateAsync(Guid leadId, CancellationToken ct = default)
    {
        var context = await contextSource.GetAsync(leadId, ct);
        if (context is null) return Result<Guid>.Failure("Demande introuvable.");

        var proposal = new QuoteProposal { LeadId = leadId, Model = model.Name };
        try
        {
            var quote = await model.ProposeAsync(context, ct);
            var guarded = PricingGuard.Apply(quote, context);

            proposal.Model = quote.Model;
            proposal.EstimatedHours = quote.EstimatedHours;
            proposal.LinesJson = JsonSerializer.Serialize(guarded.Lines, Json);
            proposal.ProposedTotal = guarded.Total;
            proposal.Reasoning = quote.Reasoning;
            proposal.Assumptions = Join(quote.Assumptions);
            proposal.Questions = Join(quote.Questions);
            proposal.Warnings = Join(guarded.Warnings);
            proposal.Confidence = quote.Confidence;
            proposal.CustomerMessage = quote.CustomerMessage;
            proposal.InputTokens = quote.InputTokens;
            proposal.OutputTokens = quote.OutputTokens;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "L'assistant n'a pas pu proposer de devis pour la demande {LeadId}", leadId);
            proposal.Status = ProposalStatus.Failed;
            proposal.Error = ex is QuoteModelException or HttpRequestException ? Clip(ex.Message, 1000) : "Erreur inattendue de l'assistant (voir le journal).";
        }

        await using var db = await factory.CreateDbContextAsync(ct);
        db.Proposals.Add(proposal);
        await db.SaveChangesAsync(ct);
        logger.LogInformation("Proposition de devis {Status} pour la demande {LeadId} ({Model}, {Total} $)",
            proposal.Status, leadId, proposal.Model, proposal.ProposedTotal);

        return proposal.Status == ProposalStatus.Failed
            ? Result<Guid>.Failure(proposal.Error ?? "Échec de l'assistant.")
            : Result<Guid>.Success(proposal.Id);
    }

    public async Task<IReadOnlyList<ProposalView>> ListForLeadAsync(Guid leadId, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var items = await db.Proposals.AsNoTracking()
            .Where(p => p.LeadId == leadId)
            .OrderByDescending(p => p.CreatedAt)
            .ToListAsync(ct);
        return items.Select(p => new ProposalView(p, ReadLines(p.FinalLinesJson ?? p.LinesJson))).ToList();
    }

    public async Task<Result> ApproveAndSendAsync(Guid proposalId, IReadOnlyList<QuoteLine> lines, string message, string author, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var proposal = await db.Proposals.FirstOrDefaultAsync(p => p.Id == proposalId, ct);
        if (proposal is null) return Result.Failure("Proposition introuvable.");
        if (proposal.Status != ProposalStatus.Draft) return Result.Failure("Cette proposition a déjà été traitée.");
        if (string.IsNullOrWhiteSpace(message)) return Result.Failure("Le message au client est vide.");

        var clean = lines.Where(l => !string.IsNullOrWhiteSpace(l.Description) && l.Quantity > 0).ToList();
        if (clean.Count == 0) return Result.Failure("Ajoutez au moins une ligne de prix.");

        var context = await contextSource.GetAsync(proposal.LeadId, ct);
        if (context is null) return Result.Failure("Demande introuvable.");

        var info = await tenantInfo.GetCurrentAsync(ct);
        var total = clean.Sum(l => l.Total);
        var html = QuoteEmailTemplate.Render(info, message, clean, total, options.Value.TaxNote);
        var sent = await email.SendAsync(new EmailMessage(
            context.Lead.CustomerEmail, $"{info.Name} — votre proposition de prix", html,
            ReplyTo: info.PublicEmail, FromName: info.Name), ct);
        if (sent.IsFailure) return Result.Failure($"Le courriel n'a pas pu être envoyé : {sent.Error}");

        proposal.Status = ProposalStatus.Sent;
        proposal.FinalLinesJson = JsonSerializer.Serialize(clean, Json);
        proposal.FinalTotal = total;
        proposal.FinalMessage = message;
        proposal.DecidedBy = author;
        proposal.DecidedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
        return Result.Success();
    }

    public async Task DiscardAsync(Guid proposalId, string author, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var proposal = await db.Proposals.FirstOrDefaultAsync(p => p.Id == proposalId, ct);
        if (proposal is null || proposal.Status != ProposalStatus.Draft) return;
        proposal.Status = ProposalStatus.Discarded;
        proposal.DecidedBy = author;
        proposal.DecidedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
    }

    internal static IReadOnlyList<QuoteLine> ReadLines(string json)
    {
        try { return JsonSerializer.Deserialize<List<QuoteLine>>(json, Json) ?? []; }
        catch (JsonException) { return []; }
    }

    private static string? Join(IReadOnlyList<string> items) => items.Count == 0 ? null : string.Join("\n", items);
    private static string Clip(string s, int max) => s.Length <= max ? s : s[..max];
}
