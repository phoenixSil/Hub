using Hub.Modules.Leads.Contracts;
using Hub.Modules.Leads.Domain;
using Hub.Modules.Leads.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Hub.Modules.Leads.Application;

/// <summary>D'où vient une demande (lien mesuré) et où elle en est : sert aux résultats du marketing.</summary>
public sealed record LeadOrigin(Guid Id, DateTimeOffset CreatedAt, string? Source, string? Medium, string? Campaign, LeadKind Kind, LeadStatus Status);

public interface ILeadAttribution
{
    /// <summary>Demandes reçues depuis une date (pourriels exclus).</summary>
    Task<IReadOnlyList<LeadOrigin>> ListSinceAsync(DateTimeOffset since, CancellationToken ct = default);

    /// <summary>Demandes arrivées par ces campagnes (utm_campaign), quelle que soit leur date.</summary>
    Task<IReadOnlyList<LeadOrigin>> ListForCampaignsAsync(IReadOnlyCollection<string> campaigns, CancellationToken ct = default);
}

internal sealed class LeadAttribution(IDbContextFactory<LeadsDbContext> factory) : ILeadAttribution
{
    public async Task<IReadOnlyList<LeadOrigin>> ListSinceAsync(DateTimeOffset since, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        return await db.Leads.AsNoTracking()
            .Where(l => l.CreatedAt >= since && l.Status != LeadStatus.Spam)
            .Select(l => new LeadOrigin(l.Id, l.CreatedAt, l.Source, l.Medium, l.Campaign, l.Kind, l.Status))
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<LeadOrigin>> ListForCampaignsAsync(IReadOnlyCollection<string> campaigns, CancellationToken ct = default)
    {
        if (campaigns.Count == 0) return [];
        await using var db = await factory.CreateDbContextAsync(ct);
        return await db.Leads.AsNoTracking()
            .Where(l => l.Campaign != null && campaigns.Contains(l.Campaign) && l.Status != LeadStatus.Spam)
            .Select(l => new LeadOrigin(l.Id, l.CreatedAt, l.Source, l.Medium, l.Campaign, l.Kind, l.Status))
            .ToListAsync(ct);
    }
}
