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

public sealed record LeadListFilter(LeadStatus? Status = null, LeadKind? Kind = null, string? Search = null, int Take = 200);

public sealed record LeadDetail(Lead Lead, IReadOnlyList<LeadActivity> Activities);

public sealed record AppointmentAvailability(
    DateOnly FirstDate, DateOnly LastDate, IReadOnlySet<DayOfWeek> OpenDays,
    IReadOnlyList<DateOnly> ClosedDays, IReadOnlyList<TimeSlot> Slots);

public interface ILeadService
{
    Task<Result<Guid>> SubmitAsync(LeadSubmission submission, CancellationToken ct = default);
    Task<AppointmentAvailability> GetAvailabilityAsync(CancellationToken ct = default);

    // Back-office
    Task<List<Lead>> ListAsync(LeadListFilter filter, CancellationToken ct = default);
    Task<LeadDetail?> GetAsync(Guid id, CancellationToken ct = default);
    Task<Dictionary<LeadStatus, int>> CountByStatusAsync(CancellationToken ct = default);
    Task ChangeStatusAsync(Guid id, LeadStatus status, string author, CancellationToken ct = default);
    Task AddNoteAsync(Guid id, string note, string author, CancellationToken ct = default);
    Task<Result> ConfirmAppointmentAsync(Guid id, DateTimeOffset when, string? note, string author, CancellationToken ct = default);
    Task<SchedulingSettings> GetSchedulingAsync(CancellationToken ct = default);
    Task SaveSchedulingAsync(SchedulingSettings settings, CancellationToken ct = default);
}

internal sealed class LeadService(
    IDbContextFactory<LeadsDbContext> factory,
    ITenantContext tenant,
    ITenantInfoProvider tenantInfo,
    IEmailSender email,
    IEventPublisher events,
    IMemoryCache cache,
    TimeProvider clock,
    ILogger<LeadService> logger) : ILeadService
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

        await using var db = await factory.CreateDbContextAsync(ct);
        var scheduling = await GetOrCreateSchedulingAsync(db, ct);
        var closed = await db.ClosedDays.Where(d => d.Date >= Today).Select(d => d.Date).ToListAsync(ct);

        var errors = LeadValidator.Validate(s, scheduling, closed, Today);
        if (errors.Count > 0)
            return Result<Guid>.Invalid(errors);

        var lead = new Lead
        {
            Kind = s.Kind,
            FullName = s.FullName.Trim(),
            Email = s.Email.Trim().ToLowerInvariant(),
            Phone = Clean(s.Phone),
            Address = Clean(s.Address),
            AreaSlug = Clean(s.AreaSlug),
            Services = s.Services.Count == 0 ? null : string.Join("; ", s.Services.Select(x => x.Trim()).Distinct()),
            PropertyType = Clean(s.PropertyType),
            PropertySize = Clean(s.PropertySize),
            Frequency = Clean(s.Frequency),
            Subject = Clean(s.Subject),
            Message = Clean(s.Message),
            PreferredDate = s.Kind == LeadKind.Appointment ? s.PreferredDate : null,
            PreferredSlot = s.Kind == LeadKind.Appointment ? s.PreferredSlot : null,
            Source = Clip(s.Source, 120),
            Medium = Clip(s.Medium, 120),
            Campaign = Clip(s.Campaign, 160),
            Referrer = Clip(s.Referrer, 500),
            LandingPage = Clip(s.LandingPage, 500),
            VisitorId = Clip(s.VisitorId, 64),
            ConsentGiven = s.ConsentGiven,
            IpHash = s.IpHash,
        };
        db.Leads.Add(lead);
        db.Activities.Add(new LeadActivity { LeadId = lead.Id, Author = "Site web", Action = "Demande reçue" });
        await db.SaveChangesAsync(ct);

        await events.PublishAsync(new LeadSubmitted(
            tenant.TenantId, lead.Id, lead.Kind, lead.Source, lead.Medium, lead.Campaign,
            lead.LandingPage, lead.VisitorId, clock.GetUtcNow()), ct);

        await SendSubmissionEmailsAsync(lead, ct);
        return Result<Guid>.Success(lead.Id);
    }

    private async Task SendSubmissionEmailsAsync(Lead lead, CancellationToken ct)
    {
        try
        {
            var info = await tenantInfo.GetCurrentAsync(ct);

            var (adminSubject, adminHtml) = LeadEmails.AdminNotification(lead, info);
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

    public async Task<AppointmentAvailability> GetAvailabilityAsync(CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var s = await GetOrCreateSchedulingAsync(db, ct);
        var closed = await db.ClosedDays.Where(d => d.Date >= Today).OrderBy(d => d.Date).Select(d => d.Date).ToListAsync(ct);
        return new AppointmentAvailability(
            Today.AddDays(s.MinimumNoticeDays), Today.AddDays(s.MaximumDaysAhead),
            s.OpenDaySet(), closed, s.EnabledSlots().ToList());
    }

    public async Task<List<Lead>> ListAsync(LeadListFilter filter, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var q = db.Leads.AsNoTracking();
        if (filter.Status is { } status) q = q.Where(l => l.Status == status);
        else q = q.Where(l => l.Status != LeadStatus.Spam);
        if (filter.Kind is { } kind) q = q.Where(l => l.Kind == kind);
        if (!string.IsNullOrWhiteSpace(filter.Search))
        {
            var term = filter.Search.Trim();
            q = q.Where(l => l.FullName.Contains(term) || l.Email.Contains(term) || (l.Phone != null && l.Phone.Contains(term)));
        }
        return await q.OrderByDescending(l => l.CreatedAt).Take(filter.Take).ToListAsync(ct);
    }

    public async Task<LeadDetail?> GetAsync(Guid id, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var lead = await db.Leads.AsNoTracking().FirstOrDefaultAsync(l => l.Id == id, ct);
        if (lead is null) return null;
        var activities = await db.Activities.AsNoTracking().Where(a => a.LeadId == id)
            .OrderByDescending(a => a.CreatedAt).ToListAsync(ct);
        return new LeadDetail(lead, activities);
    }

    public async Task<Dictionary<LeadStatus, int>> CountByStatusAsync(CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var rows = await db.Leads.GroupBy(l => l.Status)
            .Select(g => new { Status = g.Key, Count = g.Count() })
            .ToListAsync(ct);
        return rows.ToDictionary(r => r.Status, r => r.Count);
    }

    public async Task ChangeStatusAsync(Guid id, LeadStatus status, string author, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var lead = await db.Leads.FirstOrDefaultAsync(l => l.Id == id, ct);
        if (lead is null || lead.Status == status) return;
        var before = lead.Status;
        lead.Status = status;
        db.Activities.Add(new LeadActivity
        {
            LeadId = id, Author = author,
            Action = $"Statut : {LeadLabels.Of(before)} → {LeadLabels.Of(status)}",
        });
        await db.SaveChangesAsync(ct);
    }

    public async Task AddNoteAsync(Guid id, string note, string author, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(note)) return;
        await using var db = await factory.CreateDbContextAsync(ct);
        if (!await db.Leads.AnyAsync(l => l.Id == id, ct)) return;
        db.Activities.Add(new LeadActivity { LeadId = id, Author = author, Action = "Note", Note = note.Trim() });
        await db.SaveChangesAsync(ct);
    }

    public async Task<Result> ConfirmAppointmentAsync(Guid id, DateTimeOffset when, string? note, string author, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var lead = await db.Leads.FirstOrDefaultAsync(l => l.Id == id, ct);
        if (lead is null) return Result.Failure("Demande introuvable.");

        lead.ConfirmedFor = when;
        lead.Status = LeadStatus.Scheduled;
        db.Activities.Add(new LeadActivity
        {
            LeadId = id, Author = author,
            Action = $"Rendez-vous confirmé pour le {when:yyyy-MM-dd HH:mm}",
            Note = string.IsNullOrWhiteSpace(note) ? null : note.Trim(),
        });
        await db.SaveChangesAsync(ct);

        var info = await tenantInfo.GetCurrentAsync(ct);
        var (subject, html) = LeadEmails.AppointmentConfirmed(lead, info, when, note);
        var sent = await email.SendAsync(new EmailMessage(lead.Email, subject, html, ReplyTo: info.PublicEmail, FromName: info.Name), ct);
        return sent.IsSuccess
            ? Result.Success()
            : Result.Failure("Rendez-vous enregistré, mais le courriel de confirmation n'a pas pu partir : " + sent.Error);
    }

    public async Task<SchedulingSettings> GetSchedulingAsync(CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        return await GetOrCreateSchedulingAsync(db, ct);
    }

    public async Task SaveSchedulingAsync(SchedulingSettings settings, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        if (await db.Scheduling.AnyAsync(x => x.Id == settings.Id, ct)) db.Update(settings);
        else db.Add(settings);
        await db.SaveChangesAsync(ct);
    }

    private static async Task<SchedulingSettings> GetOrCreateSchedulingAsync(LeadsDbContext db, CancellationToken ct)
    {
        var settings = await db.Scheduling.AsNoTracking().FirstOrDefaultAsync(ct);
        if (settings is not null) return settings;
        settings = new SchedulingSettings();
        db.Scheduling.Add(settings);
        await db.SaveChangesAsync(ct);
        db.Entry(settings).State = EntityState.Detached;
        return settings;
    }

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string? Clip(string? value, int max)
    {
        var v = Clean(value);
        return v is null ? null : v.Length <= max ? v : v[..max];
    }

    private sealed class Counter
    {
        public int Value;
    }
}
