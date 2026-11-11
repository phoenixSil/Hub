using Hub.SharedKernel.Domain;

namespace Hub.Modules.GiftCards.Domain;

public enum GiftCardStatus
{
    /// <summary>Demandée sur le site : en attente du paiement.</summary>
    AwaitingPayment = 0,
    /// <summary>Payée : envoyée, ou prévue pour la date choisie.</summary>
    Active = 1,
    /// <summary>Solde entièrement utilisé.</summary>
    Used = 2,
    Cancelled = 3,
}

/// <summary>
/// Carte-cadeau : un montant offert, utilisable sur les services de l'entreprise, sans date d'expiration
/// (au Québec, une carte-cadeau à montant ne peut pas expirer).
/// </summary>
public sealed class GiftCard : TenantEntity
{
    /// <summary>Code à donner pour l'utiliser (12 caractères, ex. « K7PQ-4MZX-9TRA »).</summary>
    public string Code { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public decimal Balance { get; set; }
    public GiftCardStatus Status { get; set; } = GiftCardStatus.AwaitingPayment;

    public string BuyerName { get; set; } = string.Empty;
    public string BuyerEmail { get; set; } = string.Empty;
    public string? BuyerPhone { get; set; }

    public string RecipientName { get; set; } = string.Empty;

    /// <summary>Courriel du destinataire ; vide = la carte est envoyée à l'acheteur, qui l'offre lui-même.</summary>
    public string? RecipientEmail { get; set; }
    public string? Message { get; set; }

    /// <summary>Date d'envoi souhaitée (ex. le 24 décembre) ; vide = dès le paiement reçu.</summary>
    public DateOnly? DeliverOn { get; set; }

    public DateTimeOffset? PaidAt { get; set; }
    public string? PaidNote { get; set; }
    public DateTimeOffset? SentAt { get; set; }
    public string? LastError { get; set; }

    public List<GiftCardUse> Uses { get; set; } = [];
}

/// <summary>Une utilisation de la carte (montant déduit du solde).</summary>
public sealed class GiftCardUse : TenantEntity
{
    public Guid GiftCardId { get; set; }
    public decimal Amount { get; set; }
    public string? Note { get; set; }
    public string? RecordedBy { get; set; }
}
