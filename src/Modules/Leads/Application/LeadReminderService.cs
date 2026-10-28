using Hub.Modules.Leads.Domain;
using Hub.Modules.Leads.Infrastructure;
using Hub.SharedKernel.Email;
using Hub.SharedKernel.Tenancy;
using Hub.SharedKernel.Time;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Hub.Modules.Leads.Application;

/// <summary>
/// Une fois par heure, entre 8 h et 20 h (heure de Québec), envoie à chaque entreprise
/// un courriel listant les demandes restées « Nouvelles » plus de 24 heures.
/// Chaque demande ne déclenche qu'un seul rappel.
/// </summary>
internal sealed class LeadReminderService(
    IServiceScopeFactory scopes, TimeProvider clock, IConfiguration configuration, ILogger<LeadReminderService> logger) : BackgroundService
{
    private static readonly TimeSpan Every = TimeSpan.FromHours(1);
    private static readonly TimeSpan WaitingFor = TimeSpan.FromHours(24);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Premier passage peu après le démarrage, puis toutes les heures.
        try { await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken); }
        catch (OperationCanceledException) { return; }

        using var timer = new PeriodicTimer(Every);
        do
        {
            try
            {
                var hour = BusinessTime.Now(clock).Hour;
                // « Leads:RemindersAnyHour » : pour les tests, rappels à toute heure.
                if (hour is >= 8 and < 20 || configuration.GetValue<bool>("Leads:RemindersAnyHour"))
                    await RunOnceAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Échec de l'envoi des rappels de demandes.");
            }
        }
        while (await WaitAsync(timer, stoppingToken));
    }

    private static async Task<bool> WaitAsync(PeriodicTimer timer, CancellationToken ct)
    {
        try { return await timer.WaitForNextTickAsync(ct); }
        catch (OperationCanceledException) { return false; }
    }

    internal async Task<int> RunOnceAsync(CancellationToken ct)
    {
        var cutoff = clock.GetUtcNow() - WaitingFor;
        List<Guid> tenantIds;
        await using (var scope = scopes.CreateAsyncScope())
        {
            var factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<LeadsDbContext>>();
            await using var db = await factory.CreateDbContextAsync(ct);
            tenantIds = await db.Leads.IgnoreQueryFilters()
                .Where(l => l.Status == LeadStatus.New && l.ReminderSentAt == null && l.CreatedAt < cutoff)
                .Select(l => l.TenantId).Distinct().ToListAsync(ct);
        }

        var sent = 0;
        foreach (var tenantId in tenantIds)
        {
            await using var scope = scopes.CreateAsyncScope();
            scope.ServiceProvider.GetRequiredService<ITenantContext>().Set(tenantId);
            var factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<LeadsDbContext>>();
            await using var db = await factory.CreateDbContextAsync(ct);

            var leads = await db.Leads
                .Where(l => l.Status == LeadStatus.New && l.ReminderSentAt == null && l.CreatedAt < cutoff)
                .OrderBy(l => l.CreatedAt).ToListAsync(ct);
            if (leads.Count == 0) continue;

            var info = await scope.ServiceProvider.GetRequiredService<ITenantInfoProvider>().GetCurrentAsync(ct);
            var (subject, html) = LeadEmails.Reminder(leads, info);
            var result = await scope.ServiceProvider.GetRequiredService<IEmailSender>()
                .SendAsync(new EmailMessage(info.AdminNotificationEmail, subject, html, FromName: info.Name), ct);
            if (result.IsFailure)
            {
                logger.LogWarning("Rappel non envoyé pour l'entreprise {TenantId} : {Error}", tenantId, result.Error);
                continue;
            }

            var now = clock.GetUtcNow();
            foreach (var lead in leads)
            {
                lead.ReminderSentAt = now;
                db.Activities.Add(new LeadActivity { LeadId = lead.Id, Author = "Système", Action = "Rappel envoyé : demande sans réponse depuis 24 h" });
            }
            await db.SaveChangesAsync(ct);
            sent += leads.Count;
        }

        if (sent > 0) logger.LogInformation("Rappel envoyé pour {Count} demande(s) en attente.", sent);
        return sent;
    }
}
