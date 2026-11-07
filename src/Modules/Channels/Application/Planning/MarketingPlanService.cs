using Hub.Modules.Channels.Application.Autopilot;
using Hub.Modules.Channels.Domain;
using Hub.SharedKernel;
using Hub.SharedKernel.Time;
using Microsoft.EntityFrameworkCore;

namespace Hub.Modules.Channels.Application.Planning;

/// <summary>Chiffres d'une semaine du plan (publications parues cette semaine-là).</summary>
public sealed record PlanWeek(DateOnly WeekOf, int Posts, int Reach, int Interactions);

/// <summary>Part réelle d'un pilier, comparée à sa part visée, et ce qu'il atteint en moyenne.</summary>
public sealed record PillarActual(string Name, int TargetShare, int Posts, double ActualShare, double AvgReach);

/// <summary>Tout ce qu'affiche la page « Plan marketing ».</summary>
public sealed record MarketingPlanView(
    Guid Id,
    string Name,
    PlanObjective Objective,
    PlanStatus Status,
    DateOnly StartsOn,
    DateOnly EndsOn,
    int PostsPerWeek,
    IReadOnlyList<PlanPhase> Phases,
    IReadOnlyList<PlanPillar> Pillars,
    PlanPhase? CurrentPhase,
    int Week,
    int TotalWeeks,
    PlanNumbers Current,
    PlanNumbers? Baseline,
    DateOnly? BaselineOn,
    PlanNumbers? Targets,
    PlanNumbers? Expected,
    IReadOnlyList<PlanWeek> Weeks,
    IReadOnlyList<PillarActual> Mix,
    PlanNumbers? ProposedTargets,
    IReadOnlyList<PlanPillar>? ProposedPillars,
    string? ProposalNote,
    bool SuggestAutoPublish,
    DateTimeOffset? InsightsAt,
    string? InsightsNote)
{
    /// <summary>Vrai quand chaque chiffre est au moins à 90 % de la trajectoire visée.</summary>
    public bool? OnTrack => Expected is null ? null
        : Current.WeeklyReach >= Expected.WeeklyReach * 0.9 && Current.Followers >= Expected.Followers * 0.97;
}

/// <summary>Ce que l'administrateur modifie : nom, rythme, étapes (thème, intention) et piliers.</summary>
public sealed class MarketingPlanForm
{
    public string Name { get; set; } = string.Empty;
    public int PostsPerWeek { get; set; } = 7;
    public List<PhaseForm> Phases { get; set; } = [];
    public List<PillarForm> Pillars { get; set; } = [];
}

public sealed class PhaseForm
{
    public string Name { get; set; } = string.Empty;
    public DateOnly From { get; set; }
    public DateOnly To { get; set; }
    public string Focus { get; set; } = string.Empty;
    public string Theme { get; set; } = string.Empty;
}

public sealed class PillarForm
{
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public int Share { get; set; }
}

public interface IMarketingPlanService
{
    /// <summary>Le plan en cours (actif ou brouillon), ou le dernier terminé ; null s'il n'y en a aucun.</summary>
    Task<MarketingPlanView?> GetAsync(CancellationToken ct = default);

    /// <summary>Crée un plan « visibilité » de 13 semaines en brouillon, à partir du modèle.</summary>
    Task<Result> CreateAsync(DateOnly startsOn, int postsPerWeek, CancellationToken ct = default);
    Task<MarketingPlanForm?> GetFormAsync(CancellationToken ct = default);
    Task<Result> SaveAsync(MarketingPlanForm form, CancellationToken ct = default);

    /// <summary>Lance le plan : le pilote automatique le suit (rythme, piliers, thèmes), avec approbation au début.</summary>
    Task<Result> ActivateAsync(CancellationToken ct = default);
    Task<Result> ApproveTargetsAsync(CancellationToken ct = default);
    Task<Result> ApprovePillarsAsync(CancellationToken ct = default);
    Task<Result> DismissProposalAsync(CancellationToken ct = default);

    /// <summary>Après le premier mois : les publications du pilote partent sans attendre l'accord.</summary>
    Task<Result> EnableAutoPublishAsync(CancellationToken ct = default);

    /// <summary>Relève les chiffres auprès des réseaux et met le plan à jour, tout de suite.</summary>
    Task<Result> RefreshAsync(CancellationToken ct = default);
    Task<Result> DeleteDraftAsync(CancellationToken ct = default);
}

internal sealed class MarketingPlanService(
    IDbContextFactory<ChannelsDbContext> factory,
    PlanInsightsCollector collector,
    TimeProvider clock) : IMarketingPlanService
{
    public async Task<MarketingPlanView?> GetAsync(CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var plan = await CurrentAsync(db, tracking: false, ct)
                   ?? await db.MarketingPlans.AsNoTracking().OrderByDescending(p => p.EndsOn).FirstOrDefaultAsync(ct);
        if (plan is null) return null;
        var settings = await db.AutopilotSettings.AsNoTracking().FirstOrDefaultAsync(ct);
        return await PlanMeasures.BuildViewAsync(db, plan, settings?.PublishWithoutApproval ?? false, BusinessTime.Today(clock), clock.GetUtcNow(), ct);
    }

    public async Task<Result> CreateAsync(DateOnly startsOn, int postsPerWeek, CancellationToken ct = default)
    {
        if (postsPerWeek is < 1 or > 7) return Result.Failure("Entre 1 et 7 publications par semaine.");
        await using var db = await factory.CreateDbContextAsync(ct);
        if (await CurrentAsync(db, tracking: false, ct) is not null)
            return Result.Failure("Un plan est déjà en cours : terminez-le ou supprimez le brouillon avant d'en créer un autre.");
        var (phases, pillars, end) = PlanTemplates.Visibility(startsOn);
        db.MarketingPlans.Add(new MarketingPlan
        {
            Name = string.Create(System.Globalization.CultureInfo.GetCultureInfo("fr-CA"), $"Visibilité sur les réseaux, du {startsOn:d MMMM yyyy} au {end:d MMMM yyyy}"),
            Objective = PlanObjective.Visibility,
            StartsOn = startsOn,
            EndsOn = end,
            PostsPerWeek = postsPerWeek,
            PhasesJson = PlanLogic.Write(phases),
            PillarsJson = PlanLogic.Write(pillars),
        });
        await db.SaveChangesAsync(ct);
        return Result.Success();
    }

    public async Task<MarketingPlanForm?> GetFormAsync(CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var plan = await CurrentAsync(db, tracking: false, ct);
        if (plan is null) return null;
        return new MarketingPlanForm
        {
            Name = plan.Name,
            PostsPerWeek = plan.PostsPerWeek,
            Phases = PlanLogic.Phases(plan.PhasesJson).Select(p => new PhaseForm { Name = p.Name, From = p.From, To = p.To, Focus = p.Focus, Theme = p.Theme }).ToList(),
            Pillars = PlanLogic.Pillars(plan.PillarsJson).Select(p => new PillarForm { Name = p.Name, Description = p.Description, Share = p.Share }).ToList(),
        };
    }

    public async Task<Result> SaveAsync(MarketingPlanForm f, CancellationToken ct = default)
    {
        var errors = new Dictionary<string, string>();
        if (string.IsNullOrWhiteSpace(f.Name)) errors["Name"] = "Donnez un nom au plan.";
        if (f.PostsPerWeek is < 1 or > 7) errors["PostsPerWeek"] = "Entre 1 et 7 publications par semaine.";
        var pillars = f.Pillars.Select(p => new PlanPillar(p.Name.Trim().Truncate(40), (p.Description ?? "").Trim().Truncate(300), p.Share)).ToList();
        if (PlanLogic.ValidatePillars(pillars) is { } pillarError) errors["Pillars"] = pillarError;
        var phases = f.Phases.Select(p => new PlanPhase(p.Name.Trim().Truncate(60), p.From, p.To, (p.Focus ?? "").Trim().Truncate(400), (p.Theme ?? "").Trim().Truncate(300))).ToList();
        if (phases.Count == 0 || phases.Any(p => string.IsNullOrWhiteSpace(p.Name) || p.To < p.From))
            errors["Phases"] = "Chaque étape a un nom, et sa fin vient après son début.";
        else if (phases.Zip(phases.Skip(1)).Any(x => x.Second.From <= x.First.To))
            errors["Phases"] = "Les étapes se suivent sans se chevaucher.";
        if (errors.Count > 0) return Result.Invalid(errors);

        await using var db = await factory.CreateDbContextAsync(ct);
        var plan = await CurrentAsync(db, tracking: true, ct);
        if (plan is null) return Result.Failure("Aucun plan en cours.");
        plan.Name = f.Name.Trim().Truncate(150);
        plan.PostsPerWeek = f.PostsPerWeek;
        plan.PhasesJson = PlanLogic.Write(phases);
        plan.PillarsJson = PlanLogic.Write(pillars);
        plan.StartsOn = phases[0].From;
        plan.EndsOn = phases[^1].To;
        if (plan.Status == PlanStatus.Active)
        {
            var settings = await db.AutopilotSettings.FirstOrDefaultAsync(ct);
            if (settings is not null) settings.PostsPerWeek = plan.PostsPerWeek;
        }
        await db.SaveChangesAsync(ct);
        return Result.Success();
    }

    public async Task<Result> ActivateAsync(CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var plan = await CurrentAsync(db, tracking: true, ct);
        if (plan is null) return Result.Failure("Aucun plan à lancer.");
        if (plan.Status == PlanStatus.Active) return Result.Success();
        plan.Status = PlanStatus.Active;
        plan.ApprovalReviewOn = plan.StartsOn.AddDays(28);

        // Le pilote suit le plan : rythme du plan, et chaque publication attend l'accord le premier mois.
        var settings = await db.AutopilotSettings.FirstOrDefaultAsync(ct);
        if (settings is null)
        {
            db.AutopilotSettings.Add(settings = new AutopilotSettings
            {
                AudiencesJson = PlanLogic.Write(AutopilotService.DefaultAudiences),
            });
        }
        settings.Enabled = true;
        settings.PostsPerWeek = plan.PostsPerWeek;
        settings.PublishWithoutApproval = false;
        var channels = AutopilotService.ReadChannels(settings.Channels).ToList();
        var connected = await db.Channels.Where(c => c.SettingsJson != null).Select(c => c.Type).ToListAsync(ct);
        foreach (var c in new[] { ChannelType.Facebook, ChannelType.Instagram })
            if (connected.Contains(c) && !channels.Contains(c)) channels.Add(c);
        settings.Channels = string.Join(",", channels);
        await db.SaveChangesAsync(ct);
        return Result.Success();
    }

    public Task<Result> ApproveTargetsAsync(CancellationToken ct = default) => UpdateAsync(plan =>
    {
        if (plan.ProposedTargetsJson is null) return Result.Failure("Aucune cible proposée.");
        plan.TargetsJson = plan.ProposedTargetsJson;
        plan.ProposedTargetsJson = null;
        if (plan.ProposedPillarsJson is null) plan.ProposalNote = null;
        return Result.Success();
    }, ct);

    public Task<Result> ApprovePillarsAsync(CancellationToken ct = default) => UpdateAsync(plan =>
    {
        if (plan.ProposedPillarsJson is null) return Result.Failure("Aucun ajustement proposé.");
        plan.PillarsJson = plan.ProposedPillarsJson;
        plan.ProposedPillarsJson = null;
        plan.ProposalNote = null;
        return Result.Success();
    }, ct);

    public Task<Result> DismissProposalAsync(CancellationToken ct = default) => UpdateAsync(plan =>
    {
        plan.ProposedPillarsJson = null;
        plan.ProposalNote = plan.ProposedTargetsJson is null ? null : plan.ProposalNote;
        return Result.Success();
    }, ct);

    public async Task<Result> EnableAutoPublishAsync(CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var settings = await db.AutopilotSettings.FirstOrDefaultAsync(ct);
        if (settings is null) return Result.Failure("Le pilote automatique n'est pas encore réglé.");
        settings.PublishWithoutApproval = true;
        await db.SaveChangesAsync(ct);
        return Result.Success();
    }

    public async Task<Result> RefreshAsync(CancellationToken ct = default)
    {
        var note = await collector.CollectAsync(ct);
        return note is null ? Result.Success() : Result.Failure(note);
    }

    public async Task<Result> DeleteDraftAsync(CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var plan = await db.MarketingPlans.FirstOrDefaultAsync(p => p.Status == PlanStatus.Draft, ct);
        if (plan is null) return Result.Failure("Aucun brouillon à supprimer.");
        db.MarketingPlans.Remove(plan);
        await db.SaveChangesAsync(ct);
        return Result.Success();
    }

    private async Task<Result> UpdateAsync(Func<MarketingPlan, Result> change, CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var plan = await CurrentAsync(db, tracking: true, ct);
        if (plan is null) return Result.Failure("Aucun plan en cours.");
        var result = change(plan);
        if (result.IsSuccess) await db.SaveChangesAsync(ct);
        return result;
    }

    internal static Task<MarketingPlan?> CurrentAsync(ChannelsDbContext db, bool tracking, CancellationToken ct)
    {
        var query = db.MarketingPlans.Where(p => p.Status != PlanStatus.Completed);
        if (!tracking) query = query.AsNoTracking();
        return query.OrderBy(p => p.Status == PlanStatus.Active ? 0 : 1).ThenByDescending(p => p.StartsOn).FirstOrDefaultAsync(ct);
    }
}
