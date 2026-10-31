using Hub.SharedKernel.Domain;

namespace Hub.Modules.Quotes.Domain;

public enum ProposalStatus
{
    /// <summary>Proposée par l'assistant, en attente de validation.</summary>
    Draft = 0,
    /// <summary>Validée par un humain et envoyée au client.</summary>
    Sent = 1,
    /// <summary>Écartée par un humain.</summary>
    Discarded = 2,
    /// <summary>L'assistant n'a pas pu produire de proposition.</summary>
    Failed = 3,
}

/// <summary>
/// Proposition de prix préparée par l'assistant pour une demande de devis.
/// Rien n'est envoyé au client sans validation humaine. Chaque proposition est
/// conservée avec ce qu'en a fait l'humain, pour améliorer l'assistant.
/// </summary>
public sealed class QuoteProposal : TenantEntity
{
    public Guid LeadId { get; set; }
    public ProposalStatus Status { get; set; } = ProposalStatus.Draft;

    /// <summary>Modèle utilisé (ex. « claude-sonnet-5-5 » ou « simulé »).</summary>
    public string Model { get; set; } = string.Empty;
    public decimal EstimatedHours { get; set; }

    /// <summary>Lignes du devis, en JSON (<see cref="QuoteLine"/>).</summary>
    public string LinesJson { get; set; } = "[]";

    /// <summary>Total proposé par l'assistant, avant taxes, après contrôle du plancher.</summary>
    public decimal ProposedTotal { get; set; }

    /// <summary>Raisonnement de l'assistant, pour l'administrateur seulement.</summary>
    public string? Reasoning { get; set; }

    /// <summary>Hypothèses et questions à poser au client, une par ligne.</summary>
    public string? Assumptions { get; set; }
    public string? Questions { get; set; }

    /// <summary>Alertes du contrôle des prix (plancher, minimum d'heures…), une par ligne.</summary>
    public string? Warnings { get; set; }
    public string? Confidence { get; set; }

    /// <summary>Brouillon du message au client (sans les prix : le tableau est ajouté à l'envoi).</summary>
    public string CustomerMessage { get; set; } = string.Empty;

    // Ce qu'en a fait l'humain
    public decimal? FinalTotal { get; set; }
    public string? FinalLinesJson { get; set; }
    public string? FinalMessage { get; set; }
    public string? DecidedBy { get; set; }
    public DateTimeOffset? DecidedAt { get; set; }

    public string? Error { get; set; }
    public int InputTokens { get; set; }
    public int OutputTokens { get; set; }
}

/// <summary>Une ligne de devis : quantité × prix unitaire, avant taxes. <paramref name="Service"/> : nom exact dans la grille.</summary>
public sealed record QuoteLine(string Description, decimal Quantity, string Unit, decimal UnitPrice, string? Service = null)
{
    public decimal Total => Math.Round(Quantity * UnitPrice, 2);
}
