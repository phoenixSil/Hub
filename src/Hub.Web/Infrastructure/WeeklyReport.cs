using System.Globalization;
using System.Net;
using Hub.Modules.Channels.Application.Planning;
using Hub.Modules.Channels.Domain;
using Hub.Modules.Platform.Application;
using Hub.SharedKernel.Email;
using Hub.SharedKernel.Tenancy;
using Hub.SharedKernel.Time;

namespace Hub.Web.Infrastructure;

/// <summary>Le résumé de la semaine : 5 chiffres, la meilleure publication, et un lien vers les résultats.</summary>
public static class WeeklyReportEmail
{
    private static readonly CultureInfo Fr = CultureInfo.GetCultureInfo("fr-CA");

    public static (string Subject, string Html) Build(MarketingReport r, string companyName, string? siteUrl, string color, MarketingPlanView? plan = null)
    {
        string E(string? s) => WebUtility.HtmlEncode(s ?? "");
        var link = $"{(siteUrl ?? "").TrimEnd('/')}/admin/resultats";
        string Tile(string label, int value) =>
            $"""<td style="padding:10px;text-align:center;border:1px solid #e3e8ee;border-radius:8px"><div style="font-size:26px;font-weight:700;color:#10233F">{value.ToString("N0", Fr)}</div><div style="font-size:12px;color:#5b6b7b">{label}</div></td>""";

        var best = r.Best is { } b
            ? $"""<p style="margin:18px 0 6px;font-weight:700">Meilleure publication</p><p style="margin:0">{E(b.Post.Title)} : {b.Visits} visite(s), {b.Leads} demande(s){(b.Post.Audience is null ? "" : " · pour " + E(b.Post.Audience))}.</p>"""
            : "";
        var tip = r.PostCount == 0
            ? """<p style="margin:18px 0 0">Aucune publication cette semaine. Le <strong>pilote automatique</strong> peut en préparer pour vous : vous n'avez qu'à les approuver.</p>"""
            : r.ByAudience.FirstOrDefault(g => g.Leads > 0) is { } top
                ? $"""<p style="margin:18px 0 0">Le public qui répond le mieux : <strong>{E(top.Label)}</strong> ({top.LeadsPerPost.ToString("0.#", Fr)} demande(s) par publication).</p>"""
                : "";

        var html = $"""
            <div style="font-family:Arial,sans-serif;max-width:560px;margin:auto;color:#1d2b3a">
              <h1 style="font-size:20px;color:{E(color)};margin:0 0 4px">Votre semaine en chiffres</h1>
              <p style="margin:0 0 16px;color:#5b6b7b">{E(companyName)} · les 7 derniers jours</p>
              <table role="presentation" cellspacing="6" style="width:100%;border-collapse:separate"><tr>
                {Tile("publications", r.PostCount)}{Tile("visites", r.Visits)}{Tile("demandes", r.Leads)}{Tile("rendez-vous", r.Appointments)}{Tile("clients gagnés", r.Won)}
              </tr></table>
              {PlanSection(plan, siteUrl, color)}
              {best}
              {tip}
              <p style="margin:22px 0"><a href="{E(link)}" style="background:{E(color)};color:#fff;padding:10px 18px;border-radius:999px;text-decoration:none;font-weight:700">Voir tous les résultats</a></p>
              <p style="font-size:12px;color:#8a97a5">Résumé envoyé chaque lundi. Pour ne plus le recevoir : back-office → Entreprise.</p>
            </div>
            """;
        var subject = r.Leads > 0
            ? $"Votre semaine : {r.Leads} demande(s), {r.Visits} visite(s) venues des réseaux"
            : $"Votre semaine : {r.PostCount} publication(s), {r.Visits} visite(s) venues des réseaux";
        return (subject, html);
    }

    /// <summary>Où en est le plan marketing : chiffres de la semaine, cibles, trajectoire, et ce qui attend un accord.</summary>
    public static string PlanSection(MarketingPlanView? plan, string? siteUrl, string color)
    {
        if (plan is null || plan.Status != PlanStatus.Active) return "";
        string E(string? s) => WebUtility.HtmlEncode(s ?? "");
        string N(int v) => v.ToString("N0", Fr);
        var c = plan.Current;
        var lines = new List<string>
        {
            $"Personnes atteintes cette semaine : <strong>{N(c.WeeklyReach)}</strong>{(plan.Targets is { } t1 ? $" (cible {N(t1.WeeklyReach)})" : "")}",
            $"Abonnés : <strong>{N(c.Followers)}</strong>{(plan.Targets is { } t2 ? $" (cible {N(t2.Followers)})" : "")}",
            $"Taux d'interaction : <strong>{c.EngagementRate.ToString("0.#", Fr)} %</strong>",
        };
        var status = plan.OnTrack switch
        {
            true => "<span style=\"color:#145a10\">Le plan est dans les temps.</span>",
            false => "<span style=\"color:#b42318\">Le plan prend du retard sur la trajectoire visée.</span>",
            _ => plan.Baseline is null ? "Le point de départ sera mesuré après la première semaine." : "",
        };
        var pending = plan.ProposedTargets is not null || plan.ProposedPillars is not null || plan.SuggestAutoPublish
            ? $"""<p style="margin:8px 0 0"><a href="{E((siteUrl ?? "").TrimEnd('/') + "/admin/plan-marketing")}" style="color:{E(color)};font-weight:700">Une proposition attend votre accord dans le plan marketing.</a></p>"""
            : "";
        return $"""
            <div style="margin:18px 0 0;padding:14px 16px;background:#f4f7fa;border-radius:8px">
              <p style="margin:0 0 6px;font-weight:700">Plan marketing — {E(plan.CurrentPhase?.Name ?? "en cours")} (semaine {plan.Week} sur {plan.TotalWeeks})</p>
              <p style="margin:0;line-height:1.6">{string.Join("<br>", lines)}</p>
              <p style="margin:8px 0 0">{status}</p>
              {pending}
            </div>
            """;
    }
}

/// <summary>Envoie le résumé de la semaine le lundi à partir de 8 h (heure de Québec), une fois par entreprise.</summary>
public sealed class WeeklyReportWorker(IServiceScopeFactory scopes, TimeProvider clock, ILogger<WeeklyReportWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(30), clock);
        do
        {
            try { await RunOnceAsync(stoppingToken); }
            catch (Exception ex) when (ex is not OperationCanceledException) { logger.LogError(ex, "Résumé de la semaine"); }
        }
        while (await Next(timer, stoppingToken));
    }

    private static async Task<bool> Next(PeriodicTimer timer, CancellationToken ct)
    {
        try { return await timer.WaitForNextTickAsync(ct); }
        catch (OperationCanceledException) { return false; }
    }

    /// <summary>Vrai le lundi à partir de 8 h, heure locale.</summary>
    public static bool IsDue(DateTimeOffset now, DateOnly? lastSent, out DateOnly today)
    {
        var local = TimeZoneInfo.ConvertTime(now, BusinessTime.Zone);
        today = DateOnly.FromDateTime(local.DateTime);
        return local.DayOfWeek == DayOfWeek.Monday && local.Hour >= 8 && lastSent != today;
    }

    public async Task RunOnceAsync(CancellationToken ct)
    {
        IReadOnlyList<Hub.Modules.Platform.Domain.Tenant> tenants;
        await using (var scope = scopes.CreateAsyncScope())
            tenants = await scope.ServiceProvider.GetRequiredService<ITenantStore>().ListActiveAsync(ct);

        foreach (var tenant in tenants.Where(t => t.WeeklyReport))
        {
            if (!IsDue(clock.GetUtcNow(), tenant.LastWeeklyReportOn, out var today)) continue;
            await using var scope = scopes.CreateAsyncScope();
            scope.ServiceProvider.GetRequiredService<ITenantContext>().Set(tenant.Id);
            var report = await scope.ServiceProvider.GetRequiredService<IMarketingResults>().GetAsync(7, ct);
            var plan = await scope.ServiceProvider.GetRequiredService<IMarketingPlanService>().GetAsync(ct);
            var to = string.IsNullOrWhiteSpace(tenant.AdminNotificationEmail) ? tenant.PublicEmail : tenant.AdminNotificationEmail;

            // Rien à dire (ni publication ni demande) : pas de courriel, mais la semaine est marquée comme traitée.
            if (report.PostCount > 0 || report.Leads > 0 || plan?.Status == PlanStatus.Active)
            {
                var (subject, html) = WeeklyReportEmail.Build(report, tenant.Name, tenant.SiteUrl, tenant.PrimaryColor, plan);
                var sent = await scope.ServiceProvider.GetRequiredService<IEmailSender>()
                    .SendAsync(new EmailMessage(to, subject, html, FromName: tenant.Name), ct);
                if (sent.IsFailure)
                {
                    logger.LogWarning("Résumé de la semaine non envoyé ({Tenant}) : {Error}", tenant.Slug, sent.Error);
                    continue;
                }
                logger.LogInformation("Résumé de la semaine envoyé à {To} ({Tenant})", to, tenant.Slug);
            }
            await scope.ServiceProvider.GetRequiredService<ITenantStore>().MarkWeeklyReportAsync(tenant.Id, today, ct);
        }
    }
}
