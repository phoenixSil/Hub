using Hub.Modules.Channels.Domain;
using Hub.Modules.Channels.Publishers;
using Hub.SharedKernel;
using Hub.SharedKernel.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Hub.Modules.Channels.Application;

public sealed record PostView(Post Post, IReadOnlyList<PostTarget> Targets);

/// <summary>Ce que l'administrateur saisit pour une publication.</summary>
public sealed class PostDraft
{
    public Guid? Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public string? LinkPath { get; set; }
    public string? ImageUrl { get; set; }
    public List<ChannelType> Channels { get; set; } = [ChannelType.Facebook];
    public string? AiModel { get; set; }
    public string? Audience { get; set; }
    public string? VisualKind { get; set; }
    public string? Theme { get; set; }
}

/// <summary>Une publication partie, pour les résultats : réseaux où elle a été publiée.</summary>
public sealed record PublishedPost(
    Guid Id, string Title, DateTimeOffset PublishedAt, string Campaign, string? Audience, bool FromAutopilot,
    string? VisualKind, string? Theme, IReadOnlyList<ChannelType> Channels, string? ImageUrl);

public interface IPostService
{
    Task<IReadOnlyList<PostView>> ListAsync(CancellationToken ct = default);

    /// <summary>Publications parties depuis une date (au moins un réseau publié).</summary>
    Task<IReadOnlyList<PublishedPost>> ListPublishedAsync(DateTimeOffset since, CancellationToken ct = default);
    Task<PostView?> GetAsync(Guid id, CancellationToken ct = default);
    Task<Result<Guid>> SaveDraftAsync(PostDraft draft, string author, CancellationToken ct = default);
    Task<Result> ScheduleAsync(Guid id, DateTimeOffset when, CancellationToken ct = default);
    Task<Result> PublishNowAsync(Guid id, CancellationToken ct = default);
    Task<Result> CancelAsync(Guid id, CancellationToken ct = default);
    Task<Result> DeleteAsync(Guid id, CancellationToken ct = default);

    /// <summary>Le lien exact qui partira sur ce réseau (aperçu).</summary>
    Task<string?> PreviewLinkAsync(string? linkPath, ChannelType channel, string campaign, CancellationToken ct = default);
}

internal sealed class PostService(
    IDbContextFactory<ChannelsDbContext> factory,
    PostPublishing publishing,
    ITenantInfoProvider tenantInfo,
    TimeProvider clock) : IPostService
{
    public async Task<IReadOnlyList<PostView>> ListAsync(CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var posts = await db.Posts.AsNoTracking().OrderByDescending(p => p.ScheduledAt ?? p.PublishedAt ?? p.CreatedAt).Take(200).ToListAsync(ct);
        var ids = posts.Select(p => p.Id).ToList();
        var targets = await db.PostTargets.AsNoTracking().Where(t => ids.Contains(t.PostId)).ToListAsync(ct);
        return posts.Select(p => new PostView(p, targets.Where(t => t.PostId == p.Id).ToList())).ToList();
    }

    public async Task<IReadOnlyList<PublishedPost>> ListPublishedAsync(DateTimeOffset since, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var posts = await db.Posts.AsNoTracking()
            .Where(p => p.PublishedAt != null && p.PublishedAt >= since
                        && (p.Status == PostStatus.Published || p.Status == PostStatus.PartiallyPublished))
            .OrderByDescending(p => p.PublishedAt).ToListAsync(ct);
        var ids = posts.Select(p => p.Id).ToList();
        var targets = await db.PostTargets.AsNoTracking()
            .Where(t => ids.Contains(t.PostId) && t.Status == PostTargetStatus.Published).ToListAsync(ct);
        return posts.Select(p => new PublishedPost(p.Id, p.Title, p.PublishedAt!.Value, p.Campaign, p.Audience, p.FromAutopilot,
            p.VisualKind, p.Theme, targets.Where(t => t.PostId == p.Id).Select(t => t.Channel).Distinct().ToList(), p.ImageUrl)).ToList();
    }

    public async Task<PostView?> GetAsync(Guid id, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var post = await db.Posts.AsNoTracking().FirstOrDefaultAsync(p => p.Id == id, ct);
        if (post is null) return null;
        var targets = await db.PostTargets.AsNoTracking().Where(t => t.PostId == id).ToListAsync(ct);
        return new PostView(post, targets);
    }

    public async Task<Result<Guid>> SaveDraftAsync(PostDraft d, string author, CancellationToken ct = default)
    {
        var errors = new Dictionary<string, string>();
        if (string.IsNullOrWhiteSpace(d.Title)) errors["Title"] = "Donnez un titre interne à la publication.";
        if (string.IsNullOrWhiteSpace(d.Message)) errors["Message"] = "Le texte est vide.";
        else if (d.Message.Length > 5000) errors["Message"] = "Texte trop long (5 000 caractères au plus).";
        if (d.Channels.Count == 0) errors["Channels"] = "Choisissez au moins un réseau.";
        if (d.Channels.Contains(ChannelType.Instagram) && string.IsNullOrWhiteSpace(d.ImageUrl))
            errors["ImageUrl"] = "Instagram exige une image : créez un visuel ou importez une image.";
        if (errors.Count > 0) return Result<Guid>.Invalid(errors);

        await using var db = await factory.CreateDbContextAsync(ct);
        Post? post = null;
        if (d.Id is { } id)
        {
            post = await db.Posts.FirstOrDefaultAsync(p => p.Id == id, ct);
            if (post is null) return Result<Guid>.Failure("Publication introuvable.");
            if (post.Status is not (PostStatus.Draft or PostStatus.Scheduled or PostStatus.Failed or PostStatus.AwaitingApproval))
                return Result<Guid>.Failure("Une publication déjà partie ne peut plus être modifiée.");
        }
        else
        {
            post = new Post { CreatedBy = author, Campaign = TrackedLink.NewCampaign(d.Title, clock.GetUtcNow()) };
            db.Posts.Add(post);
        }

        post.Title = d.Title.Trim();
        post.Message = d.Message.Trim();
        post.LinkPath = string.IsNullOrWhiteSpace(d.LinkPath) ? null : d.LinkPath.Trim();
        post.ImageUrl = string.IsNullOrWhiteSpace(d.ImageUrl) ? null : d.ImageUrl.Trim();
        post.AiModel = d.AiModel ?? post.AiModel;
        post.Audience = string.IsNullOrWhiteSpace(d.Audience) ? post.Audience : d.Audience.Trim();
        post.Theme = string.IsNullOrWhiteSpace(d.Theme) ? post.Theme : d.Theme.Trim();
        post.VisualKind = post.ImageUrl is null ? null : d.VisualKind ?? post.VisualKind ?? "Importée";
        if (post.Status == PostStatus.Failed) post.Status = PostStatus.Draft;

        // Les réseaux visés : on garde ceux déjà publiés, on ajuste les autres.
        var existing = await db.PostTargets.Where(t => t.PostId == post.Id).ToListAsync(ct);
        db.PostTargets.RemoveRange(existing.Where(t => t.Status != PostTargetStatus.Published && !d.Channels.Contains(t.Channel)));
        foreach (var channel in d.Channels.Distinct().Where(c => existing.All(t => t.Channel != c)))
            db.PostTargets.Add(new PostTarget { PostId = post.Id, Channel = channel });
        foreach (var t in existing.Where(t => t.Status == PostTargetStatus.Failed && d.Channels.Contains(t.Channel)))
        {
            t.Status = PostTargetStatus.Pending;
            t.Error = null;
        }

        await db.SaveChangesAsync(ct);
        return Result<Guid>.Success(post.Id);
    }

    public async Task<Result> ScheduleAsync(Guid id, DateTimeOffset when, CancellationToken ct = default)
    {
        if (when < clock.GetUtcNow().AddMinutes(-1)) return Result.Failure("Choisissez une date à venir.");
        await using var db = await factory.CreateDbContextAsync(ct);
        var post = await db.Posts.FirstOrDefaultAsync(p => p.Id == id, ct);
        if (post is null) return Result.Failure("Publication introuvable.");
        if (post.Status is not (PostStatus.Draft or PostStatus.Scheduled or PostStatus.AwaitingApproval)) return Result.Failure("Cette publication ne peut plus être programmée.");
        post.ScheduledAt = when;
        post.Status = PostStatus.Scheduled;
        await db.SaveChangesAsync(ct);
        return Result.Success();
    }

    public Task<Result> PublishNowAsync(Guid id, CancellationToken ct = default) => publishing.PublishAsync(id, ct);

    public async Task<Result> CancelAsync(Guid id, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var post = await db.Posts.FirstOrDefaultAsync(p => p.Id == id, ct);
        if (post is null || post.Status != PostStatus.Scheduled) return Result.Failure("Seule une publication programmée peut être annulée.");
        post.Status = PostStatus.Draft;
        post.ScheduledAt = null;
        await db.SaveChangesAsync(ct);
        return Result.Success();
    }

    public async Task<Result> DeleteAsync(Guid id, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var post = await db.Posts.FirstOrDefaultAsync(p => p.Id == id, ct);
        if (post is null) return Result.Success();
        if (post.Status is PostStatus.Published or PostStatus.PartiallyPublished or PostStatus.Publishing)
            return Result.Failure("Une publication déjà partie reste dans l'historique (supprimez-la directement sur le réseau si besoin).");
        db.PostTargets.RemoveRange(db.PostTargets.Where(t => t.PostId == id));
        db.Posts.Remove(post);
        await db.SaveChangesAsync(ct);
        return Result.Success();
    }

    public async Task<string?> PreviewLinkAsync(string? linkPath, ChannelType channel, string campaign, CancellationToken ct = default)
    {
        var info = await tenantInfo.GetCurrentAsync(ct);
        return TrackedLink.Build(info.SiteUrl, linkPath, channel, campaign);
    }
}

/// <summary>Envoie une publication sur chacun de ses réseaux et note le résultat. Utilisé par « Publier maintenant » et par le planificateur.</summary>
internal sealed class PostPublishing(
    IDbContextFactory<ChannelsDbContext> factory,
    IPublisherResolver publishers,
    IChannelConnections connections,
    ITenantInfoProvider tenantInfo,
    TimeProvider clock,
    ILogger<PostPublishing> logger)
{
    public const int MaxAttempts = 3;

    public async Task<Result> PublishAsync(Guid postId, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var post = await db.Posts.FirstOrDefaultAsync(p => p.Id == postId, ct);
        if (post is null) return Result.Failure("Publication introuvable.");
        if (post.Status is PostStatus.Published or PostStatus.Publishing or PostStatus.Cancelled)
            return Result.Failure("Cette publication est déjà partie.");

        post.Status = PostStatus.Publishing;
        await db.SaveChangesAsync(ct);

        var info = await tenantInfo.GetCurrentAsync(ct);
        var targets = await db.PostTargets.Where(t => t.PostId == postId && t.Status != PostTargetStatus.Published).ToListAsync(ct);
        foreach (var target in targets)
        {
            target.Attempts++;
            var credentials = await connections.GetCredentialsAsync(target.Channel, ct);
            if (credentials is null && !publishers.IsSimulated)
            {
                target.Status = PostTargetStatus.Failed;
                target.Error = $"{target.Channel} n'est pas connecté : back-office → Canaux.";
                continue;
            }

            var request = new PublishRequest(
                post.Message,
                TrackedLink.Build(info.SiteUrl, post.LinkPath, target.Channel, post.Campaign),
                TrackedLink.SocialImage(info.SiteUrl, post.ImageUrl));
            var result = await publishers.For(target.Channel).PublishAsync(request, credentials ?? new ChannelCredentials("simulé", "simulé"), ct);

            target.Status = result.Success ? PostTargetStatus.Published : PostTargetStatus.Failed;
            target.ExternalId = result.ExternalId;
            target.ExternalUrl = result.ExternalUrl;
            target.Error = result.Error;
            target.PublishedAt = result.Success ? clock.GetUtcNow() : null;
            logger.LogInformation("Publication {PostId} sur {Channel} : {Outcome}", post.Id, target.Channel, result.Success ? "publiée" : result.Error);
        }

        var all = await db.PostTargets.Where(t => t.PostId == postId).ToListAsync(ct);
        var published = all.Count(t => t.Status == PostTargetStatus.Published);
        post.Status = published == all.Count ? PostStatus.Published
            : published > 0 ? PostStatus.PartiallyPublished
            : PostStatus.Failed;
        if (published > 0) post.PublishedAt ??= clock.GetUtcNow();
        await db.SaveChangesAsync(ct);

        return post.Status == PostStatus.Published
            ? Result.Success()
            : Result.Failure(string.Join(" ", all.Where(t => t.Error is not null).Select(t => t.Error)));
    }
}

/// <summary>Publie les publications programmées dont l'heure est venue (vérification chaque minute).</summary>
internal sealed class PostScheduler(IServiceScopeFactory scopes, TimeProvider clock, ILogger<PostScheduler> logger)
    : Microsoft.Extensions.Hosting.BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(1));
        do
        {
            try { await RunOnceAsync(stoppingToken); }
            catch (Exception ex) when (ex is not OperationCanceledException) { logger.LogError(ex, "Planificateur des publications"); }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    internal async Task RunOnceAsync(CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        List<(Guid TenantId, Guid PostId)> due;
        await using (var scope = scopes.CreateAsyncScope())
        {
            var factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<ChannelsDbContext>>();
            await using var db = await factory.CreateDbContextAsync(ct);
            due = (await db.Posts.IgnoreQueryFilters()
                    .Where(p => p.Status == PostStatus.Scheduled && p.ScheduledAt <= now)
                    .Select(p => new { p.TenantId, p.Id })
                    .ToListAsync(ct))
                .Select(x => (x.TenantId, x.Id)).ToList();
        }

        foreach (var (tenantId, postId) in due)
        {
            await using var scope = scopes.CreateAsyncScope();
            scope.ServiceProvider.GetRequiredService<ITenantContext>().Set(tenantId);
            await scope.ServiceProvider.GetRequiredService<PostPublishing>().PublishAsync(postId, ct);
        }
    }
}
