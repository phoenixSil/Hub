using Hub.Modules.Leads.Contracts;
using Hub.Modules.Leads.Domain;
using Hub.Modules.Leads.Infrastructure;
using Hub.SharedKernel;
using Hub.SharedKernel.Email;
using Hub.SharedKernel.Events;
using Hub.SharedKernel.Tenancy;
using Hub.SharedKernel.Time;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;

namespace Hub.Modules.Leads.Application;

/// <summary>
/// Reçoit une demande du site : limite de débit, validation, enregistrement,
/// événement pour les autres modules, puis courriels (avis et accusé de réception).
/// </summary>
internal sealed class LeadIntakeService(
    IDbContextFactory<LeadsDbContext> factory,
    ILeadScheduling schedulingService,
    ITenantContext tenant,
    ITenantInfoProvider tenantInfo,
    IEmailSender email,
    IEventPublisher events,
    IMemoryCache cache,
    TimeProvider clock,
    ILogger<LeadIntakeService> logger) : ILeadIntake
{
    private const int MaxSubmissionsPerWindow = 5;
    private static readonly TimeSpan Window = TimeSpan.FromMinutes(10);

    private DateOnly Today => BusinessTime.Today(clock);

    public async Task<Result<Guid>> SubmitAsync(LeadSubmission s, CancellationToken ct = default)
    {
        if (!string.IsNullOrEmpty(s.IpHash))
        {
            var key = $"leads:rate:{s.IpHash}";
            var count = cache.GetOrCreate(key, e => { e.AbsoluteExpirationRelativeToNow = Window; return new Counter(); })!;
            if (Interlocked.Increment(ref count.Value) > MaxSubmissionsPerWindow)
                return Result<Guid>.Failure("Trop de demandes envoyées en peu de temps. Réessayez dans quelques minutes ou appelez-nous.");
        }

        var scheduling = await schedulingService.GetSchedulingAsync(ct);
        var closed = await schedulingService.GetClosedDaysAsync(ct);
        await using var db = await factory.CreateDbContextAsync(ct);

        var errors = LeadValidator.Validate(s, scheduling, closed, Today);
        if (errors.Count > 0)
            return Result<Guid>.Invalid(errors);

        var lead = new Lead
        {
            Kind = s.Kind,
            FullName = s.FullName.Trim(),
            Email = s.Email.Trim().ToLowerInvariant(),
            Phone = LeadText.Clean(s.Phone),
            Address = LeadText.Clean(s.Address),
            AreaSlug = LeadText.Clean(s.AreaSlug),
            Services = s.Services.Count == 0 ? null : string.Join("; ", s.Services.Select(x => x.Trim()).Distinct()),
            PropertyType = LeadText.Clean(s.PropertyType),
            PropertySize = LeadText.Clean(s.PropertySize),
            Frequency = LeadText.Clean(s.Frequency),
            Subject = LeadText.Clean(s.Subject),
            Message = LeadText.Clean(s.Message),
            PreferredDate = s.Kind == LeadKind.Appointment ? s.PreferredDate : null,
            PreferredSlot = s.Kind == LeadKind.Appointment ? s.PreferredSlot : null,
            Source = LeadText.Clip(s.Source, 120),
            Medium = LeadText.Clip(s.Medium, 120),
            Campaign = LeadText.Clip(s.Campaign, 160),
            Referrer = LeadText.Clip(s.Referrer, 500),
            LandingPage = LeadText.Clip(s.LandingPage, 500),
            VisitorId = LeadText.Clip(s.VisitorId, 64),
            ConsentGiven = s.ConsentGiven,
            IpHash = s.IpHash,
        };
        db.Leads.Add(lead);
        db.Activities.Add(new LeadActivity { LeadId = lead.Id, Author = "Site web", Action = "Demande reçue" });
        await db.SaveChangesAsync(ct);

        await events.PublishAsync(new LeadSubmitted(
            tenant.TenantId, lead.Id, lead.Kind, lead.Source, lead.Medium, lead.Campaign,
            lead.LandingPage, lead.VisitorId, clock.GetUtcNow()), ct);

        await SendSubmissionEmailsAsync(lead, LeadText.Clean(s.AreaName), ct);
        return Result<Guid>.Success(lead.Id);
    }

    private async Task SendSubmissionEmailsAsync(Lead lead, string? areaName, CancellationToken ct)
    {
        try
        {
            var info = await tenantInfo.GetCurrentAsync(ct);

            var (adminSubject, adminHtml) = LeadEmails.AdminNotification(lead, info, areaName);
            var adminResult = await email.SendAsync(new EmailMessage(info.AdminNotificationEmail, adminSubject, adminHtml, ReplyTo: lead.Email, FromName: info.Name), ct);

            var (ackSubject, ackHtml) = LeadEmails.Acknowledgement(lead, info);
            var ackResult = await email.SendAsync(new EmailMessage(lead.Email, ackSubject, ackHtml, ReplyTo: info.PublicEmail, FromName: info.Name), ct);

            if (adminResult.IsFailure || ackResult.IsFailure)
                logger.LogWarning("Courriel non envoyé pour la demande {LeadId} : {Admin} / {Ack}", lead.Id, adminResult.Error, ackResult.Error);
        }
        catch (Exception ex)
        {
            // La demande est déjà enregistrée : un échec d'envoi ne doit pas la faire perdre.
            logger.LogError(ex, "Échec d'envoi des courriels pour la demande {LeadId}", lead.Id);
        }
    }

    private sealed class Counter
    {
        public int Value;
    }
}
