using System.Text.RegularExpressions;

namespace Hub.Modules.Leads.Application;

/// <summary>Verdict du filtre anti-pourriel : pourriel ou non, et pourquoi (noté dans le suivi de la demande).</summary>
public sealed record SpamVerdict(bool IsSpam, int Score, IReadOnlyList<string> Reasons);

/// <summary>
/// Filtre anti-pourriel côté serveur, en plus du piège invisible et de Cloudflare Turnstile.
/// Additionne des points de suspicion ; à partir de <see cref="Threshold"/>, la demande est classée « Pourriel » :
/// elle est gardée (au cas où), mais n'envoie aucun courriel et ne compte nulle part.
/// </summary>
public static partial class SpamFilter
{
    public const int Threshold = 3;

    /// <summary>Formules des robots qui écrivent « au sujet du prix » à des milliers d'entreprises (« RobertBuimi »…).</summary>
    [GeneratedRegex(@"\b(writ\w*|wrote)\b[^.!?\n]{0,40}\bprices?\b|\bprices?\s+for\s+(a\s+)?resell|\b(aloha|hallo)\b[^.!?\n]{0,60}\bprices?\b", RegexOptions.IgnoreCase)]
    private static partial Regex KnownPhrase();

    [GeneratedRegex(@"https?://|www\.|\b[a-z0-9-]+\.(ru|xyz|top|click|shop|online|site)\b", RegexOptions.IgnoreCase)]
    private static partial Regex Link();

    [GeneratedRegex(@"[\p{IsCyrillic}\p{IsCJKUnifiedIdeographs}\p{IsHangulSyllables}\p{IsThai}]")]
    private static partial Regex ForeignScript();

    [GeneratedRegex(@"[a-zàâçéèêëîïôûùüÿœ']+", RegexOptions.IgnoreCase)]
    private static partial Regex Word();

    private static readonly HashSet<string> English = ["i", "am", "the", "your", "you", "about", "hello", "hi", "we", "my", "is", "are", "want", "writing", "wrote", "aloha", "hallo"];
    private static readonly HashSet<string> French = ["je", "le", "la", "les", "vous", "mon", "ma", "pour", "bonjour", "est", "une", "des", "un", "et", "de", "du", "nous", "merci", "j'ai", "voudrais", "ménage", "prix"];

    /// <param name="sameSenderRecently">Demandes déjà reçues du même nom ou du même courriel dans les dernières 24 h.</param>
    public static SpamVerdict Check(string? fullName, string? subject, string? message, string? phone, int sameSenderRecently)
    {
        var text = $"{subject} {message}";
        var reasons = new List<string>();
        var score = 0;

        if (KnownPhrase().IsMatch(text)) { score += 3; reasons.Add("formule connue des robots (« writing about the price »)"); }

        var words = Word().Matches(text.ToLowerInvariant()).Select(m => m.Value).ToList();
        var en = words.Count(English.Contains);
        var fr = words.Count(French.Contains);
        if (en >= 2 && fr == 0)
        {
            score += 1; reasons.Add("message en anglais");
            if (string.IsNullOrWhiteSpace(phone)) { score += 1; reasons.Add("sans téléphone"); }
        }

        var links = Link().Matches(text).Count;
        if (links > 0) { score += Math.Min(2, links); reasons.Add(links == 1 ? "contient un lien" : $"contient {links} liens"); }

        if (ForeignScript().IsMatch($"{fullName} {text}")) { score += 2; reasons.Add("alphabet étranger"); }

        if (sameSenderRecently >= 2) { score += 2; reasons.Add($"{sameSenderRecently + 1} envois du même expéditeur en 24 h"); }

        return new SpamVerdict(score >= Threshold, score, reasons);
    }

    public static string Describe(SpamVerdict v) => "Classée en pourriel automatiquement : " + string.Join(", ", v.Reasons) + ".";
}
