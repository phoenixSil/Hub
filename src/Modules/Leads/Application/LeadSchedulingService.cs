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

/// <summary>Disponibilités pour les rendez-vous : réglages généraux et jours fermés.</summary>
internal sealed class LeadSchedulingService(IDbContextFactory<LeadsDbContext> factory, TimeProvider clock) : ILeadScheduling
{
    private DateOnly Today => BusinessTime.Today(clock);

    public async Task<AppointmentAvailability> GetAvailabilityAsync(CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var s = await GetOrCreateSchedulingAsync(db, ct);
        var closed = await db.ClosedDays.Where(d => d.Date >= Today).OrderBy(d => d.Date).Select(d => d.Date).ToListAsync(ct);
        return new AppointmentAvailability(
            Today.AddDays(s.MinimumNoticeDays), Today.AddDays(s.MaximumDaysAhead),
            s.OpenDaySet(), closed, s.EnabledSlots().ToList());
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

    public async Task<IReadOnlyList<DateOnly>> GetClosedDaysAsync(CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        return await db.ClosedDays.Where(d => d.Date >= Today).OrderBy(d => d.Date).Select(d => d.Date).ToListAsync(ct);
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

}
