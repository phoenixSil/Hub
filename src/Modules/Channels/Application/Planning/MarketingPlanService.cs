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

/// <summary>Une ligne de la liste des plans (en cours, en réserve, terminés ou arrêtés).</summary>
public sealed record PlanSummary(Guid Id, string Name, PlanStatus Status, DateOnly StartsOn, DateOnly EndsOn, int PostsPerWeek, bool StartsAutomatically, DateOnly? StoppedOn);

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
    /// <summary>Tous les plans : en cours d'abord, puis en réserve, puis l'historique.</summary>
    Task<IReadOnlyList<PlanSummary>> ListAsync(CancellationToken ct = default);

    /// <summary>Le plan à montrer par défaut : en cours, sinon le prochain en réserve, sinon le dernier terminé ; null s'il n'y en a aucun.</summary>
    Task<MarketingPlanView?> GetAsync(CancellationToken ct = default);
    Task<MarketingPlanView?> GetAsync(Guid id, CancellationToken ct = default);

    /// <summary>
    /// Crée un plan « visibilité » de 13 semaines, en réserve (brouillon), même si un autre plan est en cours.
    /// <paramref name="afterCurrent"/> : il démarrera tout seul le lendemain de la fin du plan en cours.
    /// </summary>
    Task<Result<Guid>> CreateAsync(DateOnly startsOn, int postsPerWeek, bool afterCurrent = false, CancellationToken ct = default);
    Task<MarketingPlanForm?> GetFormAsync(Guid id, CancellationToken ct = default);
    Task<Result> SaveAsync(Guid id, MarketingPlanForm form, CancellationToken ct = default);

    /// <summary>Lance un plan en réserve : refusé tant qu'un autre plan est en cours (jamais deux plans en même temps).</summary>
    Task<Result> ActivateAsync(Guid id, CancellationToken ct = default);

    /// <summary>
    /// Arrête le plan en cours : il passe dans l'historique, les publications à venir du pilote sont annulées,
    /// et le pilote se met en pause ou continue sans plan.
    /// </summary>
    Task<Result> StopAsync(Guid id, bool pauseAutopilot, CancellationToken ct = default);

    /// <summary>Démarrage automatique d'un plan en réserve (à la fin du plan en cours, ou à sa date de début).</summary>
    Task<Result> SetAutoStartAsync(Guid id, bool on, CancellationToken ct = default);

    Task<Result> ApproveTargetsAsync(CancellationToken ct = default);
    Task<Result> ApprovePillarsAsync(CancellationToken ct = default);
    Task<Result> DismissProposalAsync(CancellationToken ct = default);

    /// <summary>Après le premier mois : les publications du pilote partent sans attendre l'accord.</summary>
    Task<Result> EnableAutoPublishAsync(CancellationToken ct = default);

    /// <summary>Relève les chiffres auprès des réseaux et met le plan à jour, tout de suite.</summary>
    Task<Result> RefreshAsync(CancellationToken ct = default);
    Task<Result> DeleteDraftAsync(Guid id, CancellationToken ct = default);
}

internal sealed class MarketingPlanService(
    IDbContextFactory<ChannelsDbContext> factory,
    PlanInsightsCollector collector,
    TimeProvider clock) : IMarketingPlanService
{
    private static readonly System.Globalization.CultureInfo Fr = System.Globalization.CultureInfo.GetCultureInfo("fr-CA");

    public async Task<IReadOnlyList<PlanSummary>> ListAsync(CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var plans = await db.MarketingPlans.AsNoTracking().ToListAsync(ct);
        return plans
            .OrderBy(p => p.Status switch { PlanStatus.Active => 0, PlanStatus.Draft => 1, _ => 2 })
            .ThenBy(p => p.Status == PlanStatus.Draft ? p.StartsOn.DayNumber : -p.EndsOn.DayNumber)
            .Select(p => new PlanSummary(p.Id, NameFor(p.Name, p.StartsOn, p.EndsOn), p.Status, p.StartsOn, p.EndsOn, p.PostsPerWeek, p.StartsAutomatically, p.StoppedOn))
            .ToList();
    }

    public async Task<MarketingPlanView?> GetAsync(CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var plan = await ActiveAsync(db, tracking: false, ct)
                   ?? await db.MarketingPlans.AsNoTracking().Where(p => p.Status == PlanStatus.Draft).OrderBy(p => p.StartsOn).FirstOrDefaultAsync(ct)
                   ?? await db.MarketingPlans.AsNoTracking().OrderByDescending(p => p.EndsOn).FirstOrDefaultAsync(ct);
        return plan is null ? null : await ViewAsync(db, plan, ct);
    }

    public async Task<MarketingPlanView?> GetAsync(Guid id, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var plan = await db.MarketingPlans.AsNoTracking().FirstOrDefaultAsync(p => p.Id == id, ct);
        return plan is null ? null : await ViewAsync(db, plan, ct);
    }

    private async Task<MarketingPlanView> ViewAsync(ChannelsDbContext db, MarketingPlan plan, CancellationToken ct)
    {
        var settings = await db.AutopilotSettings.AsNoTracking().FirstOrDefaultAsync(ct);
        return await PlanMeasures.BuildViewAsync(db, plan, settings?.PublishWithoutApproval ?? false, BusinessTime.Today(clock), clock.GetUtcNow(), ct);
    }

    public async Task<Result<Guid>> CreateAsync(DateOnly startsOn, int postsPerWeek, bool afterCurrent = false, CancellationToken ct = default)
    {
        if (postsPerWeek is < 1 or > AutopilotSchedule.MaxPostsPerWeek)
            return Result<Guid>.Failure($"Entre 1 et {AutopilotSchedule.MaxPostsPerWeek} publications par semaine.");
        await using var db = await factory.CreateDbContextAsync(ct);
        var active = await ActiveAsync(db, tracking: false, ct);
        if (afterCurrent)
        {
            if (active is null) return Result<Guid>.Failure("Aucun plan en cours : choisissez simplement la date de début.");
            startsOn = active.EndsOn.AddDays(1);
            await ClearAutoStartAsync(db, ct);
        }
        var (phases, pillars, end) = PlanTemplates.Visibility(startsOn);
        var plan = new MarketingPlan
        {
            Name = DefaultName(startsOn, end),
            Objective = PlanObjective.Visibility,
            StartsOn = startsOn,
            EndsOn = end,
            PostsPerWeek = postsPerWeek,
            PhasesJson = PlanLogic.Write(phases),
            PillarsJson = PlanLogic.Write(pillars),
            StartsAutomatically = afterCurrent,
        };
        db.MarketingPlans.Add(plan);
        await db.SaveChangesAsync(ct);
        return Result<Guid>.Success(plan.Id);
    }

    public async Task<MarketingPlanForm?> GetFormAsync(Guid id, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var plan = await db.MarketingPlans.AsNoTracking().FirstOrDefaultAsync(p => p.Id == id, ct);
        if (plan is null || !IsEditable(plan)) return null;
        return new MarketingPlanForm
        {
            Name = plan.Name,
            PostsPerWeek = plan.PostsPerWeek,
            Phases = PlanLogic.Phases(plan.PhasesJson).Select(p => new PhaseForm { Name = p.Name, From = p.From, To = p.To, Focus = p.Focus, Theme = p.Theme }).ToList(),
            Pillars = PlanLogic.Pillars(plan.PillarsJson).Select(p => new PillarForm { Name = p.Name, Description = p.Description, Share = p.Share }).ToList(),
        };
    }

    private static bool IsEditable(MarketingPlan plan) => plan.Status is PlanStatus.Draft or PlanStatus.Active;

    public async Task<Result> SaveAsync(Guid id, MarketingPlanForm f, CancellationToken ct = default)
    {
        var errors = new Dictionary<string, string>();
        if (string.IsNullOrWhiteSpace(f.Name)) errors["Name"] = "Donnez un nom au plan.";
        if (f.PostsPerWeek is < 1 or > AutopilotSchedule.MaxPostsPerWeek)
            errors["PostsPerWeek"] = $"Entre 1 et {AutopilotSchedule.MaxPostsPerWeek} publications par semaine.";
        var pillars = f.Pillars.Select(p => new PlanPillar(p.Name.Trim().Truncate(40), (p.Description ?? "").Trim().Truncate(300), p.Share)).ToList();
        if (PlanLogic.ValidatePillars(pillars) is { } pillarError) errors["Pillars"] = pillarError;
        var phases = f.Phases.Select(p => new PlanPhase(p.Name.Trim().Truncate(60), p.From, p.To, (p.Focus ?? "").Trim().Truncate(400), (p.Theme ?? "").Trim().Truncate(300))).ToList();
        if (phases.Count == 0 || phases.Any(p => string.IsNullOrWhiteSpace(p.Name) || p.To < p.From))
            errors["Phases"] = "Chaque étape a un nom, et sa fin vient après son début.";
        else if (phases.Zip(phases.Skip(1)).Any(x => x.Second.From <= x.First.To))
            errors["Phases"] = "Les étapes se suivent sans se chevaucher.";
        if (errors.Count > 0) return Result.Invalid(errors);

        await using var db = await factory.CreateDbContextAsync(ct);
        var plan = await db.MarketingPlans.FirstOrDefaultAsync(p => p.Id == id, ct);
        if (plan is null) return Result.Failure("Plan introuvable.");
        if (!IsEditable(plan)) return Result.Failure("Un plan terminé ou arrêté ne se modifie plus.");

        // Un plan qui démarre automatiquement à la fin du plan en cours ne peut pas commencer avant.
        if (plan.StartsAutomatically && plan.Status == PlanStatus.Draft
            && await ActiveAsync(db, tracking: false, ct) is { } active && phases[0].From <= active.EndsOn)
            return Result.Invalid(new Dictionary<string, string>
            {
                ["Phases"] = $"Ce plan démarre à la fin du plan en cours : sa première étape commence au plus tôt le {active.EndsOn.AddDays(1).ToString("d MMMM yyyy", Fr)}.",
            });

        plan.Name = f.Name.Trim().Truncate(150);
        plan.PostsPerWeek = f.PostsPerWeek;
        plan.PhasesJson = PlanLogic.Write(phases);
        plan.PillarsJson = PlanLogic.Write(pillars);
        plan.StartsOn = phases[0].From;
        plan.EndsOn = phases[^1].To;
        plan.Name = NameFor(plan.Name, plan.StartsOn, plan.EndsOn);
        if (plan.Status == PlanStatus.Active)
        {
            var settings = await db.AutopilotSettings.FirstOrDefaultAsync(ct);
            if (settings is not null) settings.PostsPerWeek = plan.PostsPerWeek;
        }
        await db.SaveChangesAsync(ct);
        return Result.Success();
    }

    private const string DefaultNamePrefix = "Visibilité sur les réseaux, du ";

    public static string DefaultName(DateOnly startsOn, DateOnly endsOn) =>
        string.Create(Fr, $"{DefaultNamePrefix}{startsOn:d MMMM yyyy} au {endsOn:d MMMM yyyy}");

    /// <summary>Le nom proposé contient les dates : il suit les dates réelles du plan. Un nom choisi par l'utilisateur est gardé tel quel.</summary>
    public static string NameFor(string name, DateOnly startsOn, DateOnly endsOn) =>
        name.StartsWith(DefaultNamePrefix, StringComparison.Ordinal) ? DefaultName(startsOn, endsOn) : name;

    public async Task<Result> ActivateAsync(Guid id, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var plan = await db.MarketingPlans.FirstOrDefaultAsync(p => p.Id == id, ct);
        if (plan is null) return Result.Failure("Plan introuvable.");
        if (plan.Status == PlanStatus.Active) return Result.Success();
        if (plan.Status != PlanStatus.Draft) return Result.Failure("Seul un plan en réserve peut être lancé.");
        if (await ActiveAsync(db, tracking: false, ct) is { } active)
            return Result.Failure($"Un plan est déjà en cours jusqu'au {active.EndsOn.ToString("d MMMM yyyy", Fr)} : " +
                                  "arrêtez-le d'abord, ou programmez celui-ci pour qu'il démarre automatiquement à sa fin.");
        await StartAsync(db, plan, BusinessTime.Today(clock), ct);
        await db.SaveChangesAsync(ct);
        return Result.Success();
    }

    /// <summary>
    /// Démarre un plan : s'il commence dans le passé, ses étapes sont décalées pour commencer aujourd'hui.
    /// Le pilote suit le plan : rythme du plan, et chaque publication attend l'accord le premier mois.
    /// </summary>
    internal static async Task StartAsync(ChannelsDbContext db, MarketingPlan plan, DateOnly today, CancellationToken ct)
    {
        if (plan.StartsOn < today) PlanLogic.ShiftTo(plan, today);
        plan.Name = NameFor(plan.Name, plan.StartsOn, plan.EndsOn);
        plan.Status = PlanStatus.Active;
        plan.StartsAutomatically = false;
        plan.ApprovalReviewOn = plan.StartsOn.AddDays(28);

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
    }

    /// <summary>
    /// Démarrage automatique (relevé quotidien) : quand aucun plan n'est en cours, le plan en réserve programmé
    /// démarre dès que sa date de début est arrivée.
    /// </summary>
    internal static async Task<MarketingPlan?> StartQueuedAsync(ChannelsDbContext db, DateOnly today, CancellationToken ct)
    {
        if (await db.MarketingPlans.AnyAsync(p => p.Status == PlanStatus.Active, ct)) return null;
        var next = await db.MarketingPlans
            .Where(p => p.Status == PlanStatus.Draft && p.StartsAutomatically && p.StartsOn <= today)
            .OrderBy(p => p.StartsOn).FirstOrDefaultAsync(ct);
        if (next is null) return null;
        await StartAsync(db, next, today, ct);
        await db.SaveChangesAsync(ct);
        return next;
    }

    public async Task<Result> StopAsync(Guid id, bool pauseAutopilot, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var plan = await db.MarketingPlans.FirstOrDefaultAsync(p => p.Id == id, ct);
        if (plan is null) return Result.Failure("Plan introuvable.");
        if (plan.Status != PlanStatus.Active) return Result.Failure("Seul un plan en cours peut être arrêté.");
        var today = BusinessTime.Today(clock);
        var now = clock.GetUtcNow();
        plan.Status = PlanStatus.Stopped;
        plan.StoppedOn = today;
        plan.ProposedTargetsJson = null;
        plan.ProposedPillarsJson = null;
        plan.ProposalNote = null;

        // Les publications du pilote préparées pour ce plan et pas encore parues sont annulées.
        var upcoming = await db.Posts
            .Where(p => p.FromAutopilot && (p.Status == PostStatus.AwaitingApproval || p.Status == PostStatus.Scheduled) && p.ScheduledAt > now)
            .ToListAsync(ct);
        foreach (var post in upcoming) post.Status = PostStatus.Cancelled;

        var settings = await db.AutopilotSettings.FirstOrDefaultAsync(ct);
        if (settings is not null && pauseAutopilot) settings.Enabled = false;
        await db.SaveChangesAsync(ct);
        return Result.Success();
    }

    public async Task<Result> SetAutoStartAsync(Guid id, bool on, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var plan = await db.MarketingPlans.FirstOrDefaultAsync(p => p.Id == id, ct);
        if (plan is null) return Result.Failure("Plan introuvable.");
        if (plan.Status != PlanStatus.Draft) return Result.Failure("Seul un plan en réserve peut démarrer automatiquement.");
        if (on)
        {
            await ClearAutoStartAsync(db, ct);
            // Après le plan en cours : les étapes sont décalées pour commencer le lendemain de sa fin.
            if (await ActiveAsync(db, tracking: false, ct) is { } active && plan.StartsOn <= active.EndsOn)
                PlanLogic.ShiftTo(plan, active.EndsOn.AddDays(1));
            else if (plan.StartsOn < BusinessTime.Today(clock))
                PlanLogic.ShiftTo(plan, BusinessTime.Today(clock));
            plan.Name = NameFor(plan.Name, plan.StartsOn, plan.EndsOn);
        }
        plan.StartsAutomatically = on;
        await db.SaveChangesAsync(ct);
        return Result.Success();
    }

    private static async Task ClearAutoStartAsync(ChannelsDbContext db, CancellationToken ct)
    {
        foreach (var other in await db.MarketingPlans.Where(p => p.StartsAutomatically).ToListAsync(ct))
            other.StartsAutomatically = false;
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

    public async Task<Result> DeleteDraftAsync(Guid id, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var plan = await db.MarketingPlans.FirstOrDefaultAsync(p => p.Id == id && p.Status == PlanStatus.Draft, ct);
        if (plan is null) return Result.Failure("Seul un plan en réserve (brouillon) peut être supprimé.");
        db.MarketingPlans.Remove(plan);
        await db.SaveChangesAsync(ct);
        return Result.Success();
    }

    private async Task<Result> UpdateAsync(Func<MarketingPlan, Result> change, CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var plan = await ActiveAsync(db, tracking: true, ct);
        if (plan is null) return Result.Failure("Aucun plan en cours.");
        var result = change(plan);
        if (result.IsSuccess) await db.SaveChangesAsync(ct);
        return result;
    }

    /// <summary>Le plan en cours (un seul à la fois), ou null.</summary>
    internal static Task<MarketingPlan?> ActiveAsync(ChannelsDbContext db, bool tracking, CancellationToken ct)
    {
        var query = db.MarketingPlans.Where(p => p.Status == PlanStatus.Active);
        if (!tracking) query = query.AsNoTracking();
        return query.OrderByDescending(p => p.StartsOn).FirstOrDefaultAsync(ct);
    }
}
