using Hub.Modules.Channels.Application.Autopilot;
using Hub.Modules.Channels.Application.Marketing;
using Hub.Modules.Channels.Domain;
using Hub.SharedKernel;
using Hub.SharedKernel.Time;
using Microsoft.EntityFrameworkCore;

namespace Hub.Modules.Channels.Application.Planning;

/// <summary>Assistant « Nouvelle campagne » : création, aperçu de la première semaine, abandon.</summary>
internal sealed partial class MarketingPlanService
{
    public async Task<Result<Guid>> CreateCampaignAsync(CampaignRequest r, CancellationToken ct = default)
    {
        var errors = new Dictionary<string, string>();
        if (r.PostsPerWeek is < 1 or > AutopilotSchedule.MaxPostsPerWeek) errors["PostsPerWeek"] = "Choisissez une intensité.";
        if (r.Weeks is < 4 or > 52) errors["Weeks"] = "Choisissez une durée.";
        var channels = r.Channels.Where(c => c is ChannelType.Facebook or ChannelType.Instagram).Distinct().ToList();
        if (channels.Count == 0) errors["Channels"] = "Choisissez au moins un réseau.";
        if (errors.Count > 0) return Result<Guid>.Invalid(errors);

        await using var db = await factory.CreateDbContextAsync(ct);
        var connected = await db.Channels.Where(c => c.SettingsJson != null).Select(c => c.Type).ToListAsync(ct);
        var notConnected = channels.Where(c => !connected.Contains(c)).ToList();
        if (notConnected.Count > 0)
            return Result<Guid>.Failure($"{string.Join(" et ", notConnected)} n'est pas connecté : connectez-le dans Canaux, ou décochez-le.");

        var active = await ActiveAsync(db, tracking: false, ct);
        var today = BusinessTime.Today(clock);
        DateOnly start;
        if (active is not null)
        {
            // Jamais deux campagnes en même temps : celle-ci prend le relais à la fin de la campagne en cours.
            start = active.EndsOn.AddDays(1);
            await ClearAutoStartAsync(db, ct);
        }
        else
        {
            start = today;
            // Les campagnes d'aperçu abandonnées sans avoir été démarrées ne restent pas.
            foreach (var old in await db.MarketingPlans.Where(p => p.Status == PlanStatus.Draft && p.FromWizard && !p.StartsAutomatically).ToListAsync(ct))
            {
                foreach (var post in await db.Posts.Where(p => p.PlanId == old.Id && p.Status == PostStatus.AwaitingApproval).ToListAsync(ct))
                    post.Status = PostStatus.Cancelled;
                db.MarketingPlans.Remove(old);
            }
        }

        var (phases, pillars, end) = PlanTemplates.For(r.Objective, start, r.Weeks);
        var plan = new MarketingPlan
        {
            Name = CampaignName(r.Objective, start, end),
            Objective = r.Objective,
            StartsOn = start,
            EndsOn = end,
            PostsPerWeek = r.PostsPerWeek,
            PhasesJson = PlanLogic.Write(phases),
            PillarsJson = PlanLogic.Write(pillars),
            StartsAutomatically = active is not null && r.AfterCurrent,
            FromWizard = true,
        };
        db.MarketingPlans.Add(plan);

        // Le rythme, les réseaux et la validation ne changent tout de suite que s'il n'y a pas de campagne en cours.
        if (active is null)
        {
            var settings = await db.AutopilotSettings.FirstOrDefaultAsync(ct);
            if (settings is null)
                db.AutopilotSettings.Add(settings = new AutopilotSettings { AudiencesJson = PlanLogic.Write(AutopilotService.DefaultAudiences) });
            settings.PostsPerWeek = r.PostsPerWeek;
            settings.Channels = string.Join(",", channels);
            settings.PublishWithoutApproval = !r.ApproveEachWeek;
            settings.Enabled = false; // la préparation automatique reprend au démarrage de la campagne
        }
        await db.SaveChangesAsync(ct);
        return Result<Guid>.Success(plan.Id);
    }

    public static string CampaignName(PlanObjective objective, DateOnly start, DateOnly end) =>
        objective == PlanObjective.Leads
            ? string.Create(Fr, $"Demandes de devis, du {start:d MMMM yyyy} au {end:d MMMM yyyy}")
            : DefaultName(start, end);

    public async Task<Result> PreviewAsync(Guid id, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var plan = await db.MarketingPlans.AsNoTracking().FirstOrDefaultAsync(p => p.Id == id, ct);
        if (plan is null || plan.Status != PlanStatus.Draft) return Result.Failure("Campagne introuvable.");
        if (await ActiveAsync(db, tracking: false, ct) is not null)
            return Result.Failure("Une campagne est en cours : l'aperçu sera préparé quand celle-ci démarrera.");
        progress.Queue(tenant.TenantId, clock.GetUtcNow(), id);
        trigger.Request(tenant.TenantId, planId: id);
        return Result.Success();
    }

    public async Task<Result> AbandonAsync(Guid id, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var plan = await db.MarketingPlans.FirstOrDefaultAsync(p => p.Id == id && p.Status == PlanStatus.Draft, ct);
        if (plan is null) return Result.Failure("Seule une campagne pas encore démarrée peut être abandonnée.");
        var preview = await db.Posts
            .Where(p => p.PlanId == plan.Id && (p.Status == PostStatus.AwaitingApproval || p.Status == PostStatus.Scheduled) && p.PublishedAt == null)
            .ToListAsync(ct);
        foreach (var post in preview) post.Status = PostStatus.Cancelled;
        db.MarketingPlans.Remove(plan);
        await db.SaveChangesAsync(ct);
        return Result.Success();
    }

    public async Task<bool> IsPausedAsync(CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        return !await db.AutopilotSettings.AnyAsync(s => s.Enabled, ct);
    }

    public async Task<Result> SetPausedAsync(bool paused, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var settings = await db.AutopilotSettings.FirstOrDefaultAsync(ct);
        if (settings is null) return Result.Failure("Créez d'abord une campagne.");
        settings.Enabled = !paused;
        await db.SaveChangesAsync(ct);
        if (!paused) trigger.Request(tenant.TenantId); // reprise : la semaine est complétée tout de suite
        return Result.Success();
    }
}
