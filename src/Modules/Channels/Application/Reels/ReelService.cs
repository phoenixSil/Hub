using System.Text.RegularExpressions;
using Hub.Modules.Channels.Application.Autopilot;
using Hub.Modules.Channels.Domain;
using Hub.SharedKernel;
using Hub.SharedKernel.Time;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Hub.Modules.Channels.Application.Reels;

internal sealed partial class ReelService(
    IDbContextFactory<ChannelsDbContext> factory,
    IReelStudio studio,
    IAiVideoGenerator generator,
    IAutopilotContextSource context,
    IOptions<ReelOptions> options,
    TimeProvider clock,
    ILogger<ReelService> logger) : IReelService
{
    public bool IsAvailable => studio.IsAvailable;
    public bool AiConfigured => generator.IsConfigured;
    public decimal AiCostPerReel => Math.Round(generator.PricePerSecond * generator.ClipSeconds, 2);

    private string Month => TimeZoneInfo.ConvertTime(clock.GetUtcNow(), BusinessTime.Zone).ToString("yyyy-MM");

    public async Task<ReelBudget> BudgetAsync(CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var month = Month;
        var usage = await db.VideoUsages.AsNoTracking().FirstOrDefaultAsync(u => u.Month == month, ct);
        return new ReelBudget(usage?.Cost ?? 0, options.Value.MonthlyAiBudget, usage?.Count ?? 0);
    }

    public async Task<Result> RenderAsync(Guid postId, string kind, CancellationToken ct = default)
    {
        if (!studio.IsAvailable) return Result.Failure("L'outil de montage vidéo n'est pas disponible sur le serveur.");
        await using var db = await factory.CreateDbContextAsync(ct);
        var post = await db.Posts.FirstOrDefaultAsync(p => p.Id == postId, ct);
        if (post is null) return Result.Failure("Publication introuvable.");
        if (post.Status is PostStatus.Published or PostStatus.Publishing or PostStatus.PartiallyPublished)
            return Result.Failure("Cette publication est déjà partie.");

        var ctx = await context.GetAsync(ct);
        var brand = Brand(ctx);
        var text = new ReelText(Hook(post.Message, post.Title), Cta(ctx.SiteUrl));
        Result<RenderedReel> rendered;
        var finalKind = kind;
        string? note = null;

        if (kind == ReelKinds.Ai)
        {
            var ai = await RenderAiAsync(post, text, brand, ct);
            if (ai.IsSuccess) rendered = ai;
            else
            {
                // Budget atteint ou IA indisponible : on ne laisse pas la publication sans vidéo, on monte ses images.
                logger.LogWarning("Reel IA impossible pour {Post} : {Error}", post.Id, ai.Error);
                note = $"{ai.Error} Reel monté à partir des images à la place.";
                finalKind = ReelKinds.Montage;
                rendered = await RenderMontageAsync(post, ctx, text, brand, ct);
            }
        }
        else if (kind == ReelKinds.Montage)
            rendered = await RenderMontageAsync(post, ctx, text, brand, ct);
        else
            return Result.Failure("Ce type de Reel se crée en téléversant des vidéos avant/après.");

        if (rendered.IsFailure)
        {
            post.ReelKind = kind;
            post.ReelError = rendered.Error;
            await db.SaveChangesAsync(ct);
            return Result.Failure(rendered.Error!);
        }

        post.ReelKind = finalKind;
        post.VideoUrl = rendered.Value!.VideoUrl;
        post.ReelError = note;
        if (string.IsNullOrWhiteSpace(post.ImageUrl)) post.ImageUrl = rendered.Value.CoverUrl;
        await db.SaveChangesAsync(ct);
        return Result.Success();
    }

    private async Task<Result<RenderedReel>> RenderAiAsync(Post post, ReelText text, ReelBrand brand, CancellationToken ct)
    {
        if (!generator.IsConfigured) return Result<RenderedReel>.Failure("La génération de vidéos par l'IA n'est pas configurée.");
        var budget = await BudgetAsync(ct);
        if (budget.Used + AiCostPerReel > budget.Cap)
            return Result<RenderedReel>.Failure($"Plafond mensuel des vidéos IA atteint ({budget.Used:0.00} $ sur {budget.Cap:0} $).");

        var clip = await generator.GenerateAsync(ScenePrompt(post, TimeZoneInfo.ConvertTime(clock.GetUtcNow(), BusinessTime.Zone).Month), ct);
        if (clip.IsFailure) return Result<RenderedReel>.Failure(clip.Error!);
        try
        {
            // La vidéo est facturée dès qu'elle est générée, même si le montage échoue ensuite.
            await RecordAsync(AiCostPerReel, generator.ClipSeconds, ct);
            return await studio.FromClipsAsync([clip.Value!], [""], text, brand, ct);
        }
        finally { TryDelete(clip.Value!); }
    }

    private async Task<Result<RenderedReel>> RenderMontageAsync(Post post, AutopilotContext ctx, ReelText text, ReelBrand brand, CancellationToken ct)
    {
        var images = new List<string>();
        if (!string.IsNullOrWhiteSpace(post.ImageUrl)) images.Add(post.ImageUrl);
        // L'image de la page du site visée par la publication, pour un deuxième plan.
        var page = ctx.Pages.FirstOrDefault(p => post.LinkPath is not null && p.Url.EndsWith(post.LinkPath, StringComparison.OrdinalIgnoreCase));
        if (page?.ImageUrl is { Length: > 0 } pageImage && !images.Contains(pageImage)) images.Add(pageImage);
        if (images.Count == 0) return Result<RenderedReel>.Failure("Ajoutez d'abord une image à la publication : le Reel monté l'anime.");
        return await studio.MontageAsync(images, text, brand, ct);
    }

    private async Task RecordAsync(decimal cost, double seconds, CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var month = Month;
        var usage = await db.VideoUsages.FirstOrDefaultAsync(u => u.Month == month, ct);
        if (usage is null) db.VideoUsages.Add(usage = new VideoUsage { Month = month });
        usage.Count++;
        usage.Seconds += seconds;
        usage.Cost += cost;
        await db.SaveChangesAsync(ct);
    }

    public async Task<Result> RemoveAsync(Guid postId, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var post = await db.Posts.FirstOrDefaultAsync(p => p.Id == postId, ct);
        if (post is null) return Result.Failure("Publication introuvable.");
        post.ReelKind = null;
        post.VideoUrl = null;
        post.ReelError = null;
        await db.SaveChangesAsync(ct);
        return Result.Success();
    }

    public async Task<Result<Guid>> CreateBeforeAfterAsync(BeforeAfterRequest r, CancellationToken ct = default)
    {
        if (!studio.IsAvailable) return Result<Guid>.Failure("L'outil de montage vidéo n'est pas disponible sur le serveur.");
        if (string.IsNullOrWhiteSpace(r.Message)) return Result<Guid>.Failure("Écrivez le texte de la publication.");
        var ctx = await context.GetAsync(ct);
        var clips = r.AfterPath is null ? new List<string> { r.BeforePath } : [r.BeforePath, r.AfterPath];
        var labels = r.AfterPath is null ? new List<string> { "" } : ["AVANT", "APRÈS"];
        var rendered = await studio.FromClipsAsync(clips, labels, new ReelText(Hook(r.Message, r.Title), Cta(ctx.SiteUrl)), Brand(ctx), ct);
        if (rendered.IsFailure) return Result<Guid>.Failure(rendered.Error!);

        await using var db = await factory.CreateDbContextAsync(ct);
        var post = new Post
        {
            Title = string.IsNullOrWhiteSpace(r.Title) ? "Reel avant/après" : r.Title.Trim(),
            Message = r.Message.Trim(),
            LinkPath = r.LinkPath,
            ImageUrl = rendered.Value!.CoverUrl,
            VideoUrl = rendered.Value.VideoUrl,
            ReelKind = ReelKinds.BeforeAfter,
            VisualKind = "Importée",
            Format = "avant-apres",
            Campaign = $"reel-{clock.GetUtcNow():yyyyMMddHHmm}-{Guid.NewGuid().ToString("N")[..4]}",
            ScheduledAt = r.ScheduledAt,
            CreatedBy = r.Author,
            Status = PostStatus.Draft,
        };
        db.Posts.Add(post);
        IReadOnlyList<ChannelType> channels = r.Channels.Count == 0 ? new[] { ChannelType.Facebook, ChannelType.Instagram } : r.Channels;
        foreach (var channel in channels.Distinct())
            db.PostTargets.Add(new PostTarget { PostId = post.Id, Channel = channel });
        await db.SaveChangesAsync(ct);
        return Result<Guid>.Success(post.Id);
    }

    public async Task<int> RenderPendingAsync(CancellationToken ct = default)
    {
        if (!studio.IsAvailable) return 0;
        var now = clock.GetUtcNow();
        List<(Guid Id, string Kind)> pending;
        await using (var db = await factory.CreateDbContextAsync(ct))
        {
            pending = (await db.Posts.AsNoTracking()
                .Where(p => p.ReelKind != null && p.VideoUrl == null && p.ReelError == null
                    && (p.Status == PostStatus.AwaitingApproval || p.Status == PostStatus.Scheduled || p.Status == PostStatus.Draft)
                    && (p.ScheduledAt == null || p.ScheduledAt > now))
                .OrderBy(p => p.ScheduledAt).Take(options.Value.MaxPerPass)
                .Select(p => new { p.Id, p.ReelKind }).ToListAsync(ct))
                .Select(p => (p.Id, p.ReelKind!)).ToList();
        }
        var done = 0;
        foreach (var (id, kind) in pending)
            if ((await RenderAsync(id, kind, ct)).IsSuccess) done++;
        return done;
    }

    private static ReelBrand Brand(AutopilotContext c) => new(c.CompanyName, c.Phone, c.SiteUrl, c.LogoUrl, c.PrimaryColor, c.AccentColor);

    /// <summary>Fin de la vidéo : « Devis gratuit · scintinet.com ».</summary>
    internal static string Cta(string? siteUrl) =>
        Uri.TryCreate(siteUrl, UriKind.Absolute, out var u) ? $"Devis gratuit · {u.Host.Replace("www.", "")}" : "Devis gratuit";

    [GeneratedRegex(@"https?://\S+|#\S+|[\p{So}\p{Cs}️‍]")]
    private static partial Regex Noise();

    /// <summary>Accroche à l'écran : la première phrase du texte, sans liens, mots-clics ni émojis, 70 caractères au plus.</summary>
    internal static string Hook(string message, string? title = null)
    {
        var clean = Noise().Replace(message ?? "", " ");
        var first = clean.Split(['\n', '.', '!', '?'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .FirstOrDefault(l => l.Length >= 8) ?? title ?? "";
        first = Regex.Replace(first, @"\s+", " ").Trim(' ', ':', '-', '—', ',');
        if (first.Length <= 70) return first;
        var cut = first[..70];
        var space = cut.LastIndexOf(' ');
        return (space > 40 ? cut[..space] : cut).TrimEnd(',', ' ') + "…";
    }

    /// <summary>Consigne de la scène pour l'IA vidéo : réaliste, verticale, sans texte (les textes sont ajoutés au montage).</summary>
    internal static string ScenePrompt(Post post, int month)
    {
        var season = month is 12 or 1 or 2 or 3 ? "in winter, snow visible through the windows"
            : month is 4 or 5 ? "in spring" : month is 6 or 7 or 8 ? "in summer" : "in autumn";
        var subject = !string.IsNullOrWhiteSpace(post.SceneNote) ? post.SceneNote! : Hook(post.Message, post.Title);
        return "Vertical 9:16 realistic video for a home cleaning company in Québec City, Canada, " + season + ". "
            + $"Scene: {subject}. "
            + "Slow, smooth cinematic camera movement; bright natural light; spotless, welcoming home; "
            + "a professional cleaner in a neat uniform working calmly, seen from behind or at a distance. "
            + "No text, no captions, no logos, no watermark, no brand names.";
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }
}
