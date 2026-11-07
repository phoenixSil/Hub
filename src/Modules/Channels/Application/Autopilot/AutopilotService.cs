using System.Text.Json;
using System.Threading.Channels;
using Hub.Modules.Channels.Domain;
using Hub.SharedKernel;
using Hub.SharedKernel.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Hub.Modules.Channels.Application.Autopilot;

/// <summary>Ce que l'administrateur règle dans la page « Pilote automatique ».</summary>
public sealed class AutopilotForm
{
    public bool Enabled { get; set; }
    public bool PublishWithoutApproval { get; set; }
    public int PostsPerWeek { get; set; } = 3;
    public List<AudienceForm> Audiences { get; set; } = [];
    public List<ChannelType> Channels { get; set; } = [ChannelType.Facebook];
    public string? Guidelines { get; set; }
    public DateTimeOffset? LastRunAt { get; set; }
    public string? LastRunSummary { get; set; }
}

public sealed class AudienceForm
{
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public int Hour { get; set; } = 12;
}

public interface IAutopilotService
{
    Task<AutopilotForm> GetAsync(CancellationToken ct = default);
    Task<Result> SaveAsync(AutopilotForm form, CancellationToken ct = default);

    /// <summary>Demande une préparation immédiate (en arrière-plan) des publications manquantes de la semaine.</summary>
    Task<Result> RunNowAsync(CancellationToken ct = default);

    Task<Result> ApproveAsync(Guid postId, CancellationToken ct = default);
    Task<Result> RejectAsync(Guid postId, CancellationToken ct = default);
    Task<int> PendingCountAsync(CancellationToken ct = default);
}

internal sealed class AutopilotService(
    IDbContextFactory<ChannelsDbContext> factory,
    AutopilotTrigger trigger,
    ITenantContext tenant,
    TimeProvider clock) : IAutopilotService
{
    /// <summary>Publics proposés au départ (modifiables) : les clientèles types d'une entreprise de services.</summary>
    public static readonly AutopilotAudience[] DefaultAudiences =
    [
        new("Familles occupées", "Parents qui travaillent et manquent de temps pour l'entretien de la maison", 19),
        new("Aînés et leurs proches", "Personnes âgées qui veulent rester chez elles, et leurs enfants adultes qui cherchent une aide fiable", 10),
        new("Propriétaires et gestionnaires", "Propriétaires de logements locatifs ou de condos, gestionnaires d'immeubles et de bureaux", 12),
    ];

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static IReadOnlyList<AutopilotAudience> ReadAudiences(string? json)
    {
        try { return JsonSerializer.Deserialize<List<AutopilotAudience>>(json ?? "[]", Json) ?? []; }
        catch (JsonException) { return []; }
    }

    public static IReadOnlyList<ChannelType> ReadChannels(string? value) =>
        (value ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(v => Enum.TryParse<ChannelType>(v, out var c) ? c : (ChannelType?)null)
            .OfType<ChannelType>().Distinct().DefaultIfEmpty(ChannelType.Facebook).ToList();

    public async Task<AutopilotForm> GetAsync(CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var s = await db.AutopilotSettings.AsNoTracking().FirstOrDefaultAsync(ct);
        var audiences = s is null ? DefaultAudiences : ReadAudiences(s.AudiencesJson);
        return new AutopilotForm
        {
            Enabled = s?.Enabled ?? false,
            PublishWithoutApproval = s?.PublishWithoutApproval ?? false,
            PostsPerWeek = s?.PostsPerWeek ?? 3,
            Audiences = audiences.Select(a => new AudienceForm { Name = a.Name, Description = a.Description, Hour = a.Hour }).ToList(),
            Channels = ReadChannels(s?.Channels).ToList(),
            Guidelines = s?.Guidelines,
            LastRunAt = s?.LastRunAt,
            LastRunSummary = s?.LastRunSummary,
        };
    }

    public async Task<Result> SaveAsync(AutopilotForm f, CancellationToken ct = default)
    {
        var audiences = f.Audiences
            .Where(a => !string.IsNullOrWhiteSpace(a.Name))
            .Select(a => new AutopilotAudience(a.Name.Trim().Truncate(60), (a.Description ?? "").Trim().Truncate(300), Math.Clamp(a.Hour, 6, 21)))
            .ToList();
        var errors = new Dictionary<string, string>();
        if (f.PostsPerWeek is < 1 or > 7) errors["PostsPerWeek"] = "Entre 1 et 7 publications par semaine.";
        if (audiences.Count == 0) errors["Audiences"] = "Décrivez au moins un public.";
        if (audiences.Count > 6) errors["Audiences"] = "6 publics au plus.";
        if (f.Channels.Count == 0) errors["Channels"] = "Choisissez au moins un réseau.";
        if ((f.Guidelines?.Length ?? 0) > 2000) errors["Guidelines"] = "Consignes trop longues (2 000 caractères au plus).";
        if (errors.Count > 0) return Result.Invalid(errors);

        await using var db = await factory.CreateDbContextAsync(ct);
        var s = await db.AutopilotSettings.FirstOrDefaultAsync(ct);
        if (s is null) db.AutopilotSettings.Add(s = new AutopilotSettings());
        s.Enabled = f.Enabled;
        s.PublishWithoutApproval = f.PublishWithoutApproval;
        s.PostsPerWeek = f.PostsPerWeek;
        s.AudiencesJson = JsonSerializer.Serialize(audiences, Json);
        s.Channels = string.Join(",", f.Channels.Distinct());
        s.Guidelines = string.IsNullOrWhiteSpace(f.Guidelines) ? null : f.Guidelines.Trim();
        await db.SaveChangesAsync(ct);
        return Result.Success();
    }

    public async Task<Result> RunNowAsync(CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        if (!await db.AutopilotSettings.AnyAsync(ct)) return Result.Failure("Enregistrez d'abord les réglages du pilote.");
        trigger.Request(tenant.TenantId);
        return Result.Success();
    }

    public async Task<Result> ApproveAsync(Guid postId, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var post = await db.Posts.FirstOrDefaultAsync(p => p.Id == postId, ct);
        if (post is null || post.Status != PostStatus.AwaitingApproval) return Result.Failure("Cette publication n'attend pas d'accord.");
        var now = clock.GetUtcNow();
        post.Status = PostStatus.Scheduled;
        // Créneau déjà passé : elle part à la prochaine minute.
        if (post.ScheduledAt is null || post.ScheduledAt < now) post.ScheduledAt = now;
        await db.SaveChangesAsync(ct);
        return Result.Success();
    }

    public async Task<Result> RejectAsync(Guid postId, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var post = await db.Posts.FirstOrDefaultAsync(p => p.Id == postId, ct);
        if (post is null || post.Status != PostStatus.AwaitingApproval) return Result.Failure("Cette publication n'attend pas d'accord.");
        post.Status = PostStatus.Cancelled;
        await db.SaveChangesAsync(ct);
        return Result.Success();
    }

    public async Task<int> PendingCountAsync(CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        return await db.Posts.CountAsync(p => p.Status == PostStatus.AwaitingApproval, ct);
    }
}

/// <summary>File des demandes « Préparer maintenant », lue par <see cref="AutopilotWorker"/>.</summary>
internal sealed class AutopilotTrigger
{
    private readonly System.Threading.Channels.Channel<Guid> requests = System.Threading.Channels.Channel.CreateUnbounded<Guid>();
    public void Request(Guid tenantId) => requests.Writer.TryWrite(tenantId);
    public ChannelReader<Guid> Reader => requests.Reader;
}

/// <summary>
/// Tourne en arrière-plan : une fois par jour pour chaque entreprise dont le pilote est activé
/// (il complète la semaine à venir), et immédiatement sur demande de l'administrateur.
/// </summary>
internal sealed class AutopilotWorker(IServiceScopeFactory scopes, AutopilotTrigger trigger, TimeProvider clock, ILogger<AutopilotWorker> logger)
    : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(30);
    private static readonly TimeSpan RunEvery = TimeSpan.FromHours(20);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try { await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken); }
        catch (OperationCanceledException) { return; }

        while (!stoppingToken.IsCancellationRequested)
        {
            while (trigger.Reader.TryRead(out var tenantId)) await RunAsync(tenantId, force: true, stoppingToken);

            try { await RunDueAsync(stoppingToken); }
            catch (Exception ex) when (ex is not OperationCanceledException) { logger.LogError(ex, "Pilote automatique"); }

            using var wake = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
            try
            {
                await Task.WhenAny(Task.Delay(Interval, wake.Token), trigger.Reader.WaitToReadAsync(wake.Token).AsTask());
            }
            catch (OperationCanceledException) { }
            finally { await wake.CancelAsync(); }
        }
    }

    private async Task RunDueAsync(CancellationToken ct)
    {
        var limit = clock.GetUtcNow() - RunEvery;
        List<Guid> due;
        await using (var scope = scopes.CreateAsyncScope())
        {
            var factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<ChannelsDbContext>>();
            await using var db = await factory.CreateDbContextAsync(ct);
            due = await db.AutopilotSettings.IgnoreQueryFilters()
                .Where(s => s.Enabled && (s.LastRunAt == null || s.LastRunAt < limit))
                .Select(s => s.TenantId).ToListAsync(ct);
        }
        foreach (var tenantId in due) await RunAsync(tenantId, force: false, ct);
    }

    private async Task RunAsync(Guid tenantId, bool force, CancellationToken ct)
    {
        try
        {
            await using var scope = scopes.CreateAsyncScope();
            scope.ServiceProvider.GetRequiredService<ITenantContext>().Set(tenantId);
            await scope.ServiceProvider.GetRequiredService<AutopilotRunner>().RunAsync(force, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Pilote automatique de l'entreprise {TenantId}", tenantId);
        }
    }
}
