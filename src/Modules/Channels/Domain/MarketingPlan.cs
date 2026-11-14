using Hub.SharedKernel.Domain;

namespace Hub.Modules.Channels.Domain;

public enum PlanObjective
{
    /// <summary>Être vu : personnes atteintes, abonnés, interactions.</summary>
    Visibility = 0,
    /// <summary>Obtenir des demandes de devis.</summary>
    Leads = 1,
}

public enum PlanStatus
{
    /// <summary>Brouillon ou plan en réserve : modifiable, pas encore lancé.</summary>
    Draft = 0,
    Active = 1,
    Completed = 2,
    /// <summary>Arrêté avant sa date de fin par l'administrateur (gardé dans l'historique).</summary>
    Stopped = 3,
}

/// <summary>Une étape du plan (souvent un mois) : ce qu'on cherche à faire, et le thème de saison.</summary>
public sealed record PlanPhase(string Name, DateOnly From, DateOnly To, string Focus, string Theme);

/// <summary>Un pilier de contenu et sa part visée des publications (en %).</summary>
public sealed record PlanPillar(string Name, string Description, int Share);

/// <summary>Chiffres de visibilité : personnes atteintes par semaine, abonnés (tous réseaux), taux d'interaction (%).</summary>
public sealed record PlanNumbers(int WeeklyReach, int Followers, double EngagementRate);

/// <summary>
/// Plan marketing d'une entreprise sur une période (ex. 3 mois) : un objectif, des étapes, des piliers de contenu,
/// un rythme, un point de départ mesuré et des cibles. Le pilote automatique le suit chaque semaine ;
/// les ajustements qu'il propose ne s'appliquent qu'après l'accord de l'administrateur.
/// </summary>
public sealed class MarketingPlan : TenantEntity
{
    public string Name { get; set; } = string.Empty;
    public PlanObjective Objective { get; set; } = PlanObjective.Visibility;
    public PlanStatus Status { get; set; } = PlanStatus.Draft;
    public DateOnly StartsOn { get; set; }
    public DateOnly EndsOn { get; set; }
    public int PostsPerWeek { get; set; } = 5;

    /// <summary>
    /// Plan en réserve qui démarre tout seul : à la fin du plan en cours, ou à sa date de début s'il n'y a pas de plan en cours.
    /// Un seul plan à la fois peut l'être.
    /// </summary>
    public bool StartsAutomatically { get; set; }

    /// <summary>Jour où le plan a été arrêté (statut <see cref="PlanStatus.Stopped"/>).</summary>
    public DateOnly? StoppedOn { get; set; }

    /// <summary>Étapes, en JSON (liste de <see cref="PlanPhase"/>).</summary>
    public string PhasesJson { get; set; } = "[]";

    /// <summary>Piliers de contenu, en JSON (liste de <see cref="PlanPillar"/>).</summary>
    public string PillarsJson { get; set; } = "[]";

    /// <summary>Point de départ mesuré après la première semaine (<see cref="PlanNumbers"/>), ou vide.</summary>
    public string? BaselineJson { get; set; }
    public DateOnly? BaselineOn { get; set; }

    /// <summary>Cibles approuvées (<see cref="PlanNumbers"/>), ou vide.</summary>
    public string? TargetsJson { get; set; }

    /// <summary>Cibles proposées par le système, en attente d'accord.</summary>
    public string? ProposedTargetsJson { get; set; }

    /// <summary>Nouvelle répartition des piliers proposée en fin d'étape, en attente d'accord.</summary>
    public string? ProposedPillarsJson { get; set; }

    /// <summary>Pourquoi l'ajustement est proposé (lisible par l'administrateur).</summary>
    public string? ProposalNote { get; set; }

    /// <summary>Dernière étape (nom) pour laquelle un ajustement a été proposé.</summary>
    public string? LastReviewedPhase { get; set; }

    /// <summary>Date à partir de laquelle on propose de passer en publication automatique.</summary>
    public DateOnly? ApprovalReviewOn { get; set; }

    /// <summary>Dernier relevé des chiffres auprès des réseaux, et le problème rencontré s'il y en a eu un.</summary>
    public DateTimeOffset? InsightsAt { get; set; }
    public string? InsightsNote { get; set; }
}

/// <summary>Chiffres d'une publication sur un réseau, relevés chaque jour auprès du réseau.</summary>
public sealed class PostStats : TenantEntity
{
    public Guid PostId { get; set; }
    public ChannelType Channel { get; set; }

    /// <summary>Personnes atteintes (vues uniques).</summary>
    public int Reach { get; set; }

    /// <summary>Réactions, commentaires, partages et enregistrements.</summary>
    public int Interactions { get; set; }
    public DateTimeOffset MeasuredAt { get; set; }
}

/// <summary>Nombre d'abonnés d'un compte, relevé une fois par jour.</summary>
public sealed class AudienceSnapshot : TenantEntity
{
    public ChannelType Channel { get; set; }
    public DateOnly Day { get; set; }
    public int Followers { get; set; }
}
