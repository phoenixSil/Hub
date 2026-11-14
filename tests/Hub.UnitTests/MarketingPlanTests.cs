using System.Text.Json.Nodes;
using Hub.Modules.Channels.Application;
using Hub.Modules.Channels.Application.Autopilot;
using Hub.Modules.Channels.Application.Planning;
using Hub.Modules.Channels.Domain;
using Hub.Modules.Channels.Publishers;
using Hub.SharedKernel.Ai;
using Hub.SharedKernel.Time;
using Hub.Web.Infrastructure;

namespace Hub.UnitTests;

public class MarketingPlanTests
{
    private static readonly DateOnly Start = new(2026, 11, 9); // un lundi

    [Fact]
    public void Le_modele_visibilite_couvre_13_semaines_en_trois_etapes_qui_se_suivent()
    {
        var (phases, pillars, end) = PlanTemplates.Visibility(Start);

        Assert.Equal(new DateOnly(2027, 2, 7), end);
        Assert.Equal(["Se faire connaître", "Accélérer", "Amplifier"], phases.Select(p => p.Name));
        Assert.Equal(Start, phases[0].From);
        Assert.Equal(new DateOnly(2026, 12, 6), phases[0].To);
        Assert.Equal(new DateOnly(2026, 12, 7), phases[1].From);
        Assert.Equal(new DateOnly(2027, 1, 4), phases[2].From);
        Assert.Equal(end, phases[2].To);
        Assert.Contains("Fêtes", phases[0].Theme);
        Assert.Contains("crédit d'impôt", phases[2].Theme);
        Assert.Equal(100, pillars.Sum(p => p.Share));
        Assert.Null(PlanLogic.ValidatePillars(pillars));
    }

    [Fact]
    public void Les_piliers_suivent_la_repartition_visee()
    {
        var pillars = PlanTemplates.DefaultPillars;
        var week = PlanLogic.AssignPillars(pillars, new Dictionary<string, int>(), 20);

        Assert.Equal(7, week.Count(p => p == "Conseiller"));
        Assert.Equal(5, week.Count(p => p == "Montrer"));
        Assert.Equal(4, week.Count(p => p == "Rassurer"));
        Assert.Equal(4, week.Count(p => p == "Proposer"));
    }

    [Fact]
    public void Un_pilier_en_retard_passe_en_premier()
    {
        var used = new Dictionary<string, int> { ["Conseiller"] = 5, ["Montrer"] = 3, ["Rassurer"] = 2, ["Proposer"] = 0 };
        var next = PlanLogic.AssignPillars(PlanTemplates.DefaultPillars, used, 1);
        Assert.Equal(["Proposer"], next);
    }

    [Fact]
    public void Les_cibles_proposees_doublent_la_portee_et_visent_25_pourcent_d_abonnes_en_plus()
    {
        var t = PlanLogic.ProposeTargets(new PlanNumbers(800, 340, 3.4));
        Assert.Equal(1600, t.WeeklyReach);
        Assert.Equal(425, t.Followers);
        Assert.Equal(3.4, t.EngagementRate);

        var small = PlanLogic.ProposeTargets(new PlanNumbers(100, 20, 0.5));
        Assert.Equal(400, small.WeeklyReach); // au moins +300
        Assert.Equal(50, small.Followers);    // au moins +30
        Assert.Equal(2.0, small.EngagementRate);
    }

    [Fact]
    public void La_trajectoire_va_en_ligne_droite_du_depart_aux_cibles()
    {
        var e = PlanLogic.Expected(new PlanNumbers(1000, 300, 2), new PlanNumbers(2000, 400, 3), new DateOnly(2026, 11, 16), new DateOnly(2027, 2, 6), new DateOnly(2026, 12, 27));
        Assert.Equal(1500, e.WeeklyReach);
        Assert.Equal(350, e.Followers);
        Assert.Equal(2.5, e.EngagementRate);
    }

    [Fact]
    public void L_ajustement_de_fin_d_etape_favorise_le_pilier_qui_atteint_le_plus_de_monde()
    {
        var results = new Dictionary<string, (int Posts, double AvgReach)>
        {
            ["Conseiller"] = (8, 900), ["Montrer"] = (6, 400), ["Rassurer"] = (5, 300), ["Proposer"] = (5, 600),
        };
        var proposal = PlanLogic.ProposePillars(PlanTemplates.DefaultPillars, results);

        Assert.NotNull(proposal);
        var shares = proposal!.Value.Pillars.ToDictionary(p => p.Name, p => p.Share);
        Assert.Equal(40, shares["Conseiller"]);
        Assert.Equal(15, shares["Rassurer"]);
        Assert.Equal(100, shares.Values.Sum());
        Assert.Contains("Conseiller", proposal.Value.Note);
    }

    [Fact]
    public void Pas_d_ajustement_sans_donnees_suffisantes_ni_ecart_net()
    {
        Assert.Null(PlanLogic.ProposePillars(PlanTemplates.DefaultPillars, new Dictionary<string, (int, double)> { ["Conseiller"] = (1, 900) }));
        Assert.Null(PlanLogic.ProposePillars(PlanTemplates.DefaultPillars, new Dictionary<string, (int, double)>
        {
            ["Conseiller"] = (4, 500), ["Montrer"] = (4, 480),
        }));
    }

    [Fact]
    public void Apres_la_premiere_semaine_le_point_de_depart_est_mesure_et_des_cibles_proposees()
    {
        var (phases, pillars, end) = PlanTemplates.Visibility(Start);
        var plan = new MarketingPlan
        {
            Status = PlanStatus.Active, StartsOn = Start, EndsOn = end,
            PhasesJson = PlanLogic.Write(phases), PillarsJson = PlanLogic.Write(pillars),
        };
        var now = BusinessTime.FromLocal(new DateTime(2026, 11, 16, 9, 0, 0));
        var posts = Enumerable.Range(0, 7)
            .Select(i => new MeasuredPost(Guid.NewGuid(), now.AddDays(-i - 0.5), "Conseiller", 200, 10))
            .ToList();

        PlanReviewer.Review(plan, posts, 340, new DateOnly(2026, 11, 16), now);

        var baseline = PlanLogic.Numbers(plan.BaselineJson);
        Assert.Equal(new PlanNumbers(1400, 340, 5.0), baseline);
        Assert.Equal(new DateOnly(2026, 11, 16), plan.BaselineOn);
        Assert.Equal(2800, PlanLogic.Numbers(plan.ProposedTargetsJson)!.WeeklyReach);
        Assert.Null(plan.TargetsJson); // rien ne s'applique sans accord
    }

    [Fact]
    public void A_la_fin_d_une_etape_une_nouvelle_repartition_est_proposee_une_seule_fois()
    {
        var (phases, pillars, end) = PlanTemplates.Visibility(Start);
        var plan = new MarketingPlan
        {
            Status = PlanStatus.Active, StartsOn = Start, EndsOn = end, BaselineJson = "{}",
            PhasesJson = PlanLogic.Write(phases), PillarsJson = PlanLogic.Write(pillars),
        };
        var at = BusinessTime.FromLocal(new DateTime(2026, 11, 20, 12, 0, 0));
        var posts = new List<MeasuredPost>();
        for (var i = 0; i < 4; i++)
        {
            posts.Add(new MeasuredPost(Guid.NewGuid(), at.AddDays(i), "Conseiller", 900, 40));
            posts.Add(new MeasuredPost(Guid.NewGuid(), at.AddDays(i), "Rassurer", 200, 5));
        }
        var now = BusinessTime.FromLocal(new DateTime(2026, 12, 8, 9, 0, 0));

        PlanReviewer.Review(plan, posts, 400, new DateOnly(2026, 12, 8), now);
        Assert.Equal("Se faire connaître", plan.LastReviewedPhase);
        Assert.NotNull(plan.ProposedPillarsJson);
        Assert.Contains("Fin de l'étape", plan.ProposalNote);

        plan.ProposedPillarsJson = null;
        PlanReviewer.Review(plan, posts, 400, new DateOnly(2026, 12, 9), now.AddDays(1));
        Assert.Null(plan.ProposedPillarsJson);
    }

    [Fact]
    public void Le_plan_se_termine_apres_sa_date_de_fin()
    {
        var plan = new MarketingPlan { Status = PlanStatus.Active, StartsOn = Start, EndsOn = new DateOnly(2027, 2, 7) };
        PlanReviewer.Review(plan, [], 0, new DateOnly(2027, 2, 8), DateTimeOffset.UtcNow);
        Assert.Equal(PlanStatus.Completed, plan.Status);
    }

    [Fact]
    public void Le_pilote_recoit_le_pilier_de_chaque_creneau_et_l_etape_du_plan()
    {
        var (phases, pillars, _) = PlanTemplates.Visibility(Start);
        var slots = Enumerable.Range(0, 3).Select(i => BusinessTime.FromLocal(new DateTime(2026, 11, 10 + i))).ToList();
        var context = new AutopilotContext("ScintiNet", "Québec", "418", "#0B5375", "#7CC242", "https://www.scintinet.com", "a@b.c",
            [new AutopilotPage("Ménage résidentiel", "/services/menage", "Ménage régulier", null, false, null)]);
        var request = new AutopilotPlanRequest(context, AutopilotService.DefaultAudiences, slots, [], null, slots[0].AddDays(-1),
            new PlanBrief(PlanObjective.Visibility, phases[0], pillars, ["Conseiller", "Montrer", "Proposer"]));

        var rotation = AutopilotPlanner.Rotation(request, AutopilotService.DefaultAudiences);
        Assert.Equal(["Conseiller", "Montrer", "Proposer"], rotation.Select(p => p.Pillar));
        Assert.Contains("Approche :", rotation[0].Angle);

        var prompt = AutopilotPlanner.UserPrompt(request, AutopilotService.DefaultAudiences);
        Assert.Contains("VISIBILITÉ", prompt);
        Assert.Contains("Se faire connaître", prompt);
        Assert.Contains("pilier « Montrer »", prompt);
    }

    [Fact]
    public void Les_chiffres_meta_sont_lus_dans_les_reponses_insights()
    {
        var insights = JsonNode.Parse("""{"data":[{"name":"post_total_media_view_unique","period":"lifetime","values":[{"value":523}]}]}""");
        Assert.Equal(523, FacebookPagePublisher.MetricValue(insights, FacebookPagePublisher.ReachMetric));

        var counts = JsonNode.Parse("""{"reactions":{"data":[],"summary":{"total_count":12}},"comments":{"data":[],"summary":{"total_count":3}},"shares":{"count":2}}""");
        Assert.Equal(17, FacebookPagePublisher.Interactions(counts));

        var ig = JsonNode.Parse("""{"data":[{"name":"reach","values":[{"value":310}]},{"name":"total_interactions","values":[{"value":"25"}]}]}""");
        Assert.Equal(310, FacebookPagePublisher.MetricValue(ig, "reach"));
        Assert.Equal(25, FacebookPagePublisher.MetricValue(ig, "total_interactions"));
        Assert.Equal(0, FacebookPagePublisher.MetricValue(null, "reach"));
    }

    [Fact]
    public void Le_resume_du_lundi_montre_ou_en_est_le_plan()
    {
        var (phases, pillars, end) = PlanTemplates.Visibility(Start);
        var view = new MarketingPlanView(Guid.NewGuid(), "Visibilité", PlanObjective.Visibility, PlanStatus.Active, Start, end, 7,
            phases, pillars, phases[0], 3, 13,
            Current: new PlanNumbers(1250, 360, 4.2), Baseline: new PlanNumbers(1000, 340, 3.5), BaselineOn: Start.AddDays(7),
            Targets: new PlanNumbers(2000, 425, 3.5), Expected: new PlanNumbers(1100, 350, 3.5),
            Weeks: [], Mix: [], ProposedTargets: null, ProposedPillars: null, ProposalNote: null,
            SuggestAutoPublish: true, InsightsAt: null, InsightsNote: null);

        var html = System.Net.WebUtility.HtmlDecode(WeeklyReportEmail.PlanSection(view, "https://www.scintinet.com", "#0B5375"));
        Assert.Contains("semaine 3 sur 13", html);
        Assert.Contains("cible 2", html);
        Assert.Contains("dans les temps", html);
        Assert.Contains("/admin/plan-marketing", html);
    }

    [Fact]
    public void Le_tableau_de_bord_resume_la_semaine_du_plan()
    {
        var (phases, pillars, end) = PlanTemplates.Visibility(Start);
        var view = new MarketingPlanView(Guid.NewGuid(), "Visibilité", PlanObjective.Visibility, PlanStatus.Active, Start, end, 7,
            phases, pillars, phases[0], 2, 13,
            Current: new PlanNumbers(0, 0, 0), Baseline: null, BaselineOn: null, Targets: null, Expected: null,
            Weeks: [], Mix: [], ProposedTargets: null, ProposedPillars: null, ProposalNote: null,
            SuggestAutoPublish: false, InsightsAt: null, InsightsNote: null);
        // Semaine 2 : du lundi 16 au dimanche 22 novembre. On est le jeudi 19 à midi.
        var now = BusinessTime.FromLocal(new DateTime(2026, 11, 19, 12, 0, 0));
        DateTimeOffset At(int day, int hour = 10) => BusinessTime.FromLocal(new DateTime(2026, 11, day, hour, 0, 0));
        PostView P(PostStatus s, DateTimeOffset at, bool published = false) =>
            new(new Post { Status = s, ScheduledAt = at, PublishedAt = published ? at : null, FromAutopilot = true }, []);
        var posts = new[]
        {
            P(PostStatus.Published, At(16), true), P(PostStatus.Published, At(17), true),
            P(PostStatus.Cancelled, At(18)),
            P(PostStatus.Scheduled, At(20)), P(PostStatus.AwaitingApproval, At(21)), P(PostStatus.AwaitingApproval, At(22)),
            P(PostStatus.AwaitingApproval, At(24)), // semaine suivante : compte seulement dans « à valider »
            P(PostStatus.Published, At(12), true),  // semaine précédente : ignorée
        };

        var w = PlanWeekSummary.For(view, posts, new DateOnly(2026, 11, 19), now)!;

        Assert.Equal(2, w.Week);
        Assert.Equal(new DateOnly(2026, 11, 16), w.From);
        Assert.Equal(new DateOnly(2026, 11, 22), w.To);
        Assert.Equal(7, w.Planned);
        Assert.Equal(2, w.Published);
        Assert.Equal(3, w.Upcoming);
        Assert.Equal(3, w.ToApprove);
        Assert.Equal(1, w.Missed);
        Assert.Null(PlanWeekSummary.For(view with { Status = PlanStatus.Draft }, posts, new DateOnly(2026, 11, 19), now));
    }

    [Fact]
    public void Le_nom_propose_suit_les_dates_reelles_du_plan()
    {
        var name = MarketingPlanService.DefaultName(Start, new DateOnly(2027, 2, 7));
        Assert.Equal("Visibilité sur les réseaux, du 13 novembre 2026 au 7 février 2027",
            MarketingPlanService.NameFor(name, new DateOnly(2026, 11, 13), new DateOnly(2027, 2, 7)));
        Assert.Equal("Mon plan", MarketingPlanService.NameFor("Mon plan", new DateOnly(2026, 11, 13), new DateOnly(2027, 2, 7)));
    }

    [Fact]
    public void Un_plan_decale_garde_ses_durees_et_ses_etapes()
    {
        var (phases, pillars, end) = PlanTemplates.Visibility(Start);
        var plan = new MarketingPlan { StartsOn = Start, EndsOn = end, PhasesJson = PlanLogic.Write(phases), PillarsJson = PlanLogic.Write(pillars) };

        PlanLogic.ShiftTo(plan, new DateOnly(2027, 2, 8)); // le lendemain de la fin d'un plan de 13 semaines

        Assert.Equal(new DateOnly(2027, 2, 8), plan.StartsOn);
        Assert.Equal(end.AddDays(91), plan.EndsOn);
        var shifted = PlanLogic.Phases(plan.PhasesJson);
        Assert.Equal(plan.StartsOn, shifted[0].From);
        Assert.Equal(plan.EndsOn, shifted[^1].To);
        Assert.Equal(phases.Select(p => p.To.DayNumber - p.From.DayNumber), shifted.Select(p => p.To.DayNumber - p.From.DayNumber));
    }
}
