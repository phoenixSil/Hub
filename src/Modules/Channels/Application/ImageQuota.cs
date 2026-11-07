using Hub.Modules.Channels.Domain;
using Hub.SharedKernel.Time;
using Microsoft.EntityFrameworkCore;

namespace Hub.Modules.Channels.Application;

internal sealed class ImageQuota(IDbContextFactory<ChannelsDbContext> factory, TimeProvider clock) : IImageQuota
{
    private string Month => TimeZoneInfo.ConvertTime(clock.GetUtcNow(), BusinessTime.Zone).ToString("yyyy-MM");

    public async Task<int> UsedThisMonthAsync(CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var month = Month;
        return await db.ImageUsages.Where(u => u.Month == month).Select(u => u.Count).FirstOrDefaultAsync(ct);
    }

    public async Task RecordAsync(CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var month = Month;
        var usage = await db.ImageUsages.FirstOrDefaultAsync(u => u.Month == month, ct);
        if (usage is null) db.ImageUsages.Add(usage = new ImageUsage { Month = month });
        usage.Count++;
        await db.SaveChangesAsync(ct);
    }
}
