using Hub.SharedKernel.Domain;

namespace Hub.Modules.Channels.Domain;

/// <summary>
/// Une décision marketing journalisée (noyau décisionnel, étape 1) : le contexte, le choix fait et sa probabilité,
/// puis ce qu'il a rapporté. Sert à apprendre ce qui marche et à évaluer une nouvelle façon de choisir.
/// </summary>
public sealed class Decision : TenantEntity
{
    /// <summary>Domaine (adaptateur) : « Publications » ; plus tard « Tarification »…</summary>
    public string Domain { get; set; } = "Publications";

    public Guid PostId { get; set; }
    public Guid? PlanId { get; set; }
    public DateTimeOffset DecidedAt { get; set; }

    /// <summary>Qui a choisi : Exploration (tirage au hasard), IA, Humain, Historique (publication antérieure au journal).</summary>
    public string Policy { get; set; } = DecisionPolicy.Ai;

    /// <summary>Dimension tirée au hasard (« Créneau »), pour une décision d'exploration.</summary>
    public string? Explored { get; set; }

    /// <summary>Probabilité de la valeur choisie pour la dimension explorée (connue seulement en exploration).</summary>
    public double? Propensity { get; set; }

    /// <summary>Part d'exploration en vigueur au moment de la décision.</summary>
    public double ExplorationRate { get; set; }

    // ----- Action (a) -----
    public string Slot { get; set; } = string.Empty;
    public double Intensity { get; set; }
    public string? Format { get; set; }
    public string? Audience { get; set; }
    public string? Theme { get; set; }
    public string? Pillar { get; set; }
    public string? VisualKind { get; set; }
    public string Channels { get; set; } = string.Empty;

    /// <summary>Vrai si l'administrateur a changé l'heure, la cible ou le thème après la décision.</summary>
    public bool EditedByHuman { get; set; }

    // ----- Contexte (x) et état (s), en JSON -----
    public string ContextJson { get; set; } = "{}";

    // ----- Résultat (y) et récompense (r) -----
    public int Reach { get; set; }
    public int Interactions { get; set; }
    public int Visits { get; set; }
    public int Leads { get; set; }
    public int Appointments { get; set; }
    public int Won { get; set; }
    public decimal Cost { get; set; }
    public double? Reward { get; set; }

    /// <summary>Clôture à J+7 (fenêtre d'attribution), puis mise à jour finale à J+30 (clients gagnés).</summary>
    public DateTimeOffset? ClosedAt { get; set; }
    public DateTimeOffset? FinalizedAt { get; set; }

    /// <summary>Remarque de clôture (ex. « Publication retirée »).</summary>
    public string? Note { get; set; }
}

public static class DecisionPolicy
{
    public const string Exploration = "Exploration";
    public const string Ai = "IA";
    public const string Human = "Humain";
    public const string Historical = "Historique";
}
