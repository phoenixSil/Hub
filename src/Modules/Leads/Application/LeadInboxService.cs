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

/// <summary>Suivi des demandes dans le back-office : liste, fiche, statut, notes, rendez-vous.</summary>
internal sealed class LeadInboxService(
    IDbContextFactory<LeadsDbContext> factory,
    ITenantInfoProvider tenantInfo,
    IEmailSender email) : ILeadInbox
{
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

    public async Task<Result> SendReplyAsync(Guid id, string subject, string body, IReadOnlyList<EmailAttachment> attachments,
        string author, CancellationToken ct = default)
    {
        var errors = LeadEmails.ValidateReply(subject, body, attachments);
        if (errors.Count > 0) return Result.Invalid(errors);

        await using var db = await factory.CreateDbContextAsync(ct);
        var lead = await db.Leads.FirstOrDefaultAsync(l => l.Id == id, ct);
        if (lead is null) return Result.Failure("Demande introuvable.");

        var info = await tenantInfo.GetCurrentAsync(ct);
        var html = LeadEmails.Reply(body, info);
        var sent = await email.SendAsync(new EmailMessage(lead.Email, subject.Trim(), html,
            ReplyTo: info.PublicEmail, FromName: info.Name, Attachments: attachments), ct);
        if (sent.IsFailure) return Result.Failure("Le courriel n'est pas parti : " + sent.Error);

        var files = attachments.Count == 0 ? "" : "\nPièces jointes : " + string.Join(", ", attachments.Select(a => a.FileName));
        var note = $"Sujet : {subject.Trim()}{files}\n\n{body.Trim()}";
        db.Activities.Add(new LeadActivity
        {
            LeadId = id, Author = author,
            Action = attachments.Count == 0 ? "Courriel envoyé au client" : $"Courriel envoyé au client ({attachments.Count} pièce{(attachments.Count > 1 ? "s" : "")} jointe{(attachments.Count > 1 ? "s" : "")})",
            Note = note.Length > 4000 ? note[..3997] + "…" : note,
        });
        if (lead.Status == LeadStatus.New) lead.Status = LeadStatus.Contacted;
        await db.SaveChangesAsync(ct);
        return Result.Success();
    }
}
