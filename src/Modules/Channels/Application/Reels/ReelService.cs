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
    IStockVideoSource stock,
    IAutopilotContextSource context,
    IOptions<ReelOptions> options,
    TimeProvider clock,
    ILogger<ReelService> logger) : IReelService
{
    public bool IsAvailable => studio.IsAvailable;
    public bool AiConfigured => generator.IsConfigured;
    public bool StockConfigured => stock.IsConfigured;
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

        if (ReelKinds.IsAi(kind))
        {
            rendered = await RenderAiAsync(post, kind == ReelKinds.Cinematic, text, brand, ct);
            if (rendered.IsFailure)
            {
                // Budget atteint ou IA indisponible : on ne laisse pas la publication sans vidéo.
                logger.LogWarning("Reel IA impossible pour {Post} : {Error}", post.Id, rendered.Error);
                (finalKind, rendered, note) = await FallbackAsync(post, ctx, text, brand, rendered.Error!, ct);
            }
        }
        else if (kind == ReelKinds.Stock)
        {
            rendered = await RenderStockAsync(post, text, brand, ct);
            if (rendered.IsFailure)
            {
                logger.LogWarning("Reel d'archives impossible pour {Post} : {Error}", post.Id, rendered.Error);
                note = $"{rendered.Error} Reel en images animées à la place.";
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

    /// <summary>Repli d'un Reel IA : vidéos d'archives si possible (sauf format « preuve »), sinon images animées.</summary>
    private async Task<(string Kind, Result<RenderedReel> Rendered, string Note)> FallbackAsync(Post post, AutopilotContext ctx, ReelText text,
        ReelBrand brand, string error, CancellationToken ct)
    {
        if (stock.IsConfigured && (post.Format is null || !ReelPlanner.ProofFormats.Contains(post.Format)))
        {
            var archive = await RenderStockAsync(post, text, brand, ct);
            if (archive.IsSuccess) return (ReelKinds.Stock, archive, $"{error} Reel en vidéos d'archives à la place.");
        }
        return (ReelKinds.Montage, await RenderMontageAsync(post, ctx, text, brand, ct), $"{error} Reel en images animées à la place.");
    }

    private async Task<Result<RenderedReel>> RenderAiAsync(Post post, bool cinematic, ReelText text, ReelBrand brand, CancellationToken ct)
    {
        if (!generator.IsConfigured) return Result<RenderedReel>.Failure("La génération de vidéos par l'IA n'est pas configurée.");
        var budget = await BudgetAsync(ct);
        if (budget.Used + AiCostPerReel > budget.Cap)
            return Result<RenderedReel>.Failure($"Plafond mensuel des vidéos IA atteint ({budget.Used:0.00} $ sur {budget.Cap:0} $).");

        var clip = await generator.GenerateAsync(ScenePrompt(post, TimeZoneInfo.ConvertTime(clock.GetUtcNow(), BusinessTime.Zone).Month, cinematic), ct);
        if (clip.IsFailure) return Result<RenderedReel>.Failure(clip.Error!);
        try
        {
            // La vidéo est facturée dès qu'elle est générée, même si le montage échoue ensuite.
            await RecordAsync(AiCostPerReel, generator.ClipSeconds, ct);
            return await studio.FromClipsAsync([clip.Value!], [""], text, brand, ct);
        }
        finally { TryDelete(clip.Value!); }
    }

    /// <summary>Reel de vraies vidéos d'archives : trois plans (le sujet, un détail, l'ambiance de saison).</summary>
    private async Task<Result<RenderedReel>> RenderStockAsync(Post post, ReelText text, ReelBrand brand, CancellationToken ct)
    {
        if (!stock.IsConfigured) return Result<RenderedReel>.Failure("Les vidéos d'archives (Pexels) ne sont pas configurées.");
        var clips = await stock.DownloadAsync(StockQueries(post, TimeZoneInfo.ConvertTime(clock.GetUtcNow(), BusinessTime.Zone).Month), ct);
        if (clips.IsFailure) return Result<RenderedReel>.Failure(clips.Error!);
        try
        {
            if (clips.Value!.Count == 0) return Result<RenderedReel>.Failure("Aucune vidéo d'archives trouvée pour ce sujet.");
            return await studio.FromClipsAsync(clips.Value, clips.Value.Select(_ => "").ToList(), text, brand, ct);
        }
        finally { foreach (var path in clips.Value!) TryDelete(path); }
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

    /// <summary>
    /// Consigne de la scène pour l'IA vidéo : verticale, sans texte (les textes sont ajoutés au montage).
    /// Réaliste : lumière naturelle, caméra posée. Cinématique : rendu « film », lumière dorée, ralenti, travelling lent.
    /// </summary>
    internal static string ScenePrompt(Post post, int month, bool cinematic = false)
    {
        var season = month is 12 or 1 or 2 or 3 ? "in winter, snow visible through the windows"
            : month is 4 or 5 ? "in spring" : month is 6 or 7 or 8 ? "in summer" : "in autumn";
        var subject = !string.IsNullOrWhiteSpace(post.SceneNote) ? post.SceneNote! : Hook(post.Message, post.Title);
        var look = cinematic
            ? "Cinematic film look: slow dolly-in and gentle parallax, golden-hour light, soft haze in the light beams, shallow depth of field, "
              + "subtle slow motion, rich but natural colour grading, 24 fps film feel; "
            : "Realistic, documentary feel: steady handheld camera, bright natural daylight, true-to-life colours; ";
        return $"Vertical 9:16 {(cinematic ? "cinematic" : "realistic")} video for a home cleaning company in Québec City, Canada, " + season + ". "
            + $"Scene: {subject}. "
            + look
            + "spotless, welcoming home; a professional cleaner in a neat uniform working calmly, seen from behind or at a distance. "
            + "No text, no captions, no logos, no watermark, no brand names.";
    }

    /// <summary>Mots du texte (français) → recherches de vidéos d'archives (anglais).</summary>
    private static readonly (string[] Words, string Query)[] StockTopics =
    [
        (["cuisine", "comptoir", "four", "frigo", "réfrigérateur"], "cleaning kitchen counter"),
        (["salle de bain", "salle de bains", "douche", "bain", "toilette", "lavabo"], "cleaning bathroom"),
        (["fenêtre", "vitre", "vitres", "fenêtres"], "cleaning window"),
        (["plancher", "planchers", "sol", "calcium", "vadrouille"], "mopping floor"),
        (["tapis", "aspirateur", "moquette"], "vacuum cleaning carpet"),
        (["lit", "literie", "draps", "chambre"], "making bed bedroom"),
        (["déménagement", "déménager", "boîtes"], "moving boxes empty apartment"),
        (["bureau", "bureaux", "commercial", "entreprise"], "office cleaning"),
        (["aîné", "aînés", "aînée", "parents"], "senior woman relaxing at home"),
        (["poussière", "époussetage", "étagère"], "dusting shelves"),
        (["rangement", "désencombrer", "organiser", "garde-robe"], "organizing closet"),
        (["temps libre", "temps retrouvé", "famille", "détente"], "family relaxing living room"),
    ];

    /// <summary>Trois recherches : le sujet du texte, un geste de ménage, l'ambiance de la saison au Québec.</summary>
    internal static IReadOnlyList<string> StockQueries(Post post, int month)
    {
        var text = $" {post.SceneNote} {post.Title} {post.Message} ".ToLowerInvariant();
        var topics = StockTopics.Where(t => t.Words.Any(w => Regex.IsMatch(text, $@"\b{Regex.Escape(w)}\b"))).Select(t => t.Query).Take(2).ToList();
        if (topics.Count == 0) topics.Add("house cleaning");
        if (topics.Count == 1) topics.Add(topics[0] == "house cleaning" ? "clean bright living room" : "house cleaning");
        topics.Add(month switch
        {
            12 or 1 or 2 or 3 => "snowy house winter",
            4 or 5 => "spring sunlight window",
            6 or 7 or 8 => "sunny summer house",
            _ => "autumn leaves house",
        });
        return topics;
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }
}
