namespace Hub.Modules.Channels.Application;

/// <summary>
/// Un format publicitaire : une façon de raconter (technique de rédaction) et une façon de montrer
/// (direction photo, mise en page). Varier les formats, c'est ce qui évite des publications qui se ressemblent toutes.
/// </summary>
/// <param name="Key">Identifiant stable (enregistré sur la publication).</param>
/// <param name="Name">Nom lisible.</param>
/// <param name="Pillars">Piliers du plan marketing auxquels ce format convient.</param>
/// <param name="Copy">Technique de rédaction imposée au rédacteur.</param>
/// <param name="Photo">Direction photo imposée (en anglais, pour l'IA d'image).</param>
/// <param name="People">Vrai si la photo montre des personnes.</param>
/// <param name="Layouts">Mises en page qui conviennent à ce format (texte en haut ou en bas).</param>
/// <param name="Audience">Si renseigné, le format est réservé à ce type de public (ex. « aîné »).</param>
public sealed record CreativeFormat(
    string Key,
    string Name,
    string[] Pillars,
    string Copy,
    string Photo,
    bool People,
    string[] Layouts,
    string? Audience = null);

public static class CreativeFormats
{
    public const string Bottom = "Bas", Band = "Bandeau", Card = "Carte", Top = "Haut", Poster = "Affiche";
    private static readonly string[] TextBelow = [Bottom, Band, Card];
    private static readonly string[] TextAbove = [Top, Poster];
    private static readonly string[] AnyLayout = [Bottom, Band, Card, Top, Poster];

    public static IReadOnlyList<CreativeFormat> All { get; } =
    [
        new("avant-apres", "Avant / après", ["Montrer", "Proposer"],
            "Contraste avant/après : une première ligne qui fait revivre le « avant » (le désordre, la fatigue), puis le soulagement du « après ». Phrases courtes, rythme rapide.",
            "Split-screen diptych of the SAME room from the SAME angle: left half cluttered, dusty and dull, right half spotless, tidy and glowing with light; a crisp vertical dividing line in the middle. No people.",
            false, [Band, Poster]),
        new("gros-plan", "Gros plan qui brille", ["Montrer", "Conseiller"],
            "Sensoriel : fais voir, sentir et toucher la propreté (l'odeur du propre, la lumière qui se reflète). Une accroche visuelle, puis le bénéfice.",
            "Extreme close-up macro product-style photograph of a gleaming clean surface (choose one: chrome faucet with water droplets, polished hardwood floor with window reflection, sparkling glass shower door, glossy kitchen counter, freshly made white bed linen texture). Studio-quality lighting, shallow depth of field, rich textures. No people.",
            false, AnyLayout),
        new("vu-du-dessus", "Vu du dessus", ["Conseiller"],
            "Astuce pratique : un problème courant, puis 3 étapes numérotées (1️⃣ 2️⃣ 3️⃣) simples à faire soi-même. Invite à enregistrer la publication.",
            "Top-down flat lay photograph, neatly arranged on a solid colored backdrop in the brand's colors: unbranded cleaning essentials (microfiber cloths, glass spray bottle with no label, baking soda in a jar, lemon halves, white vinegar in a carafe, wooden brush). Graphic, symmetrical, lots of negative space, soft even light. No people, no labels.",
            false, AnyLayout),
        new("humour", "Situation exagérée (humour)", ["Proposer", "Conseiller"],
            "Humour bienveillant et autodérision québécoise : une situation que tout le monde reconnaît, exagérée. Chute drôle, puis la solution. Jamais moqueur envers le client.",
            "Playful, cinematic, slightly exaggerated comedic scene: an adult overwhelmed in a comically absurd household situation (choose one: buried under a mountain of laundry, juggling a vacuum, a phone and a coffee, staring in disbelief at an overflowing sink after a party, a pet proudly sitting in a cloud of dust bunnies). Expressive face, vivid colors, wide angle.",
            true, AnyLayout),
        new("temps-retrouve", "Le temps retrouvé", ["Proposer", "Rassurer"],
            "Problème – agitation – solution : nomme la corvée, fais sentir le temps perdu, puis montre ce qu'on fait à la place quand c'est fait par nous.",
            "Lifestyle photograph of people enjoying free time instead of chores, OUTSIDE the home or away from cleaning (choose one: friends skating on an outdoor rink in Québec City, a couple at a cozy café with snow outside, a parent and child building a snowman, someone reading by a fireplace, a family board game night). Candid, joyful, authentic.",
            true, AnyLayout),
        new("coulisses", "Coulisses de l'équipe", ["Rassurer", "Montrer"],
            "Coulisses : montre le soin, la méthode et les personnes derrière le service (sans inventer de nom ni de témoignage). Ton humain et fier.",
            "Candid documentary-style photograph of a friendly professional house cleaner at work (plain navy or white uniform with NO logo), choose one: arriving at a front door with a tidy cleaning caddy, carefully folding towels, wiping a window with sunlight streaming in, checking a room before leaving. Respectful, warm, real.",
            true, AnyLayout),
        new("portrait", "Portrait et confiance", ["Rassurer"],
            "Mini-histoire présentée comme un exemple (« Imaginez… », « Comme beaucoup de familles… ») : une situation typique, ce qui a changé, l'émotion. Aucune citation attribuée, aucun faux témoignage.",
            "Warm environmental portrait of one satisfied adult at home, looking at the camera with a genuine relaxed smile, the clean tidy room softly blurred behind; choose a setting different from a living-room sofa (kitchen island, entryway, sunroom, home office, staircase).",
            true, TextBelow),
        new("question", "Question à la communauté", ["Conseiller", "Montrer"],
            "Engagement : pose une question ouverte et amusante qui donne envie de répondre en commentaire (ex. « Quelle pièce détestez-vous le plus nettoyer ? »). Propose 2 ou 3 réponses avec émojis. Termine en invitant à commenter.",
            "Bold, colorful, slightly whimsical photograph that illustrates a question, choose one: two contrasting rooms side by side, a single rubber glove waving, a doorway opening onto a messy versus a tidy room, a lone sock on a staircase. Strong graphic composition, playful.",
            false, TextAbove),
        new("saison", "Québec en saison", ["Montrer", "Conseiller"],
            "Ancré dans la saison et le quotidien à Québec : un détail que tout le monde vit en ce moment, et le conseil ou le service qui va avec.",
            "Atmospheric seasonal photograph in Québec City, choose one: an entryway with wet winter boots, salt marks and a warm mat; colorful houses on a snowy street at dusk; a frosty window with a cozy clean interior glowing behind; a porch with a holiday wreath. Rich seasonal atmosphere.",
            false, AnyLayout),
        new("offre", "Offre en vedette", ["Proposer"],
            "Publicité directe AIDA : attention (accroche choc), intérêt, désir, action. Appel à l'action très clair et un léger sentiment d'urgence, sans inventer de chiffre ni de date.",
            "Minimalist bold advertising still life: a single hero object (choose one: an unbranded spray bottle, a stack of fresh folded towels, a gleaming bucket with a microfiber cloth, a house-shaped key ring) on a vivid solid background in the brand color, dramatic studio light, generous empty space. No people.",
            false, TextAbove),
        new("aines", "Moment de vie (aînés)", ["Rassurer", "Proposer"],
            "Émotion et sérénité : parle aux aînés ET à leurs proches. Ce que l'aide à domicile rend possible (rester chez soi, profiter de la famille). Ton tendre, respectueux, jamais condescendant.",
            "Tender lifestyle photograph of an older adult (70+) enjoying a meaningful moment at home, choose one: video call with grandchildren on a tablet, tending indoor plants, playing cards with a grandchild, laughing with a caregiver over tea; a clean bright home around them.",
            true, AnyLayout, Audience: "aîné"),
        new("astuce", "Astuce éclair", ["Conseiller"],
            "Astuce éclair : « Le saviez-vous ? » puis UNE astuce concrète et surprenante, expliquée en 2 ou 3 phrases. Termine en invitant à partager avec quelqu'un qui en a besoin.",
            "Editorial close-up of the single household object the tip is about (oven window, shower grout, baseboard, mattress, stainless steel fridge, couch cushion), half dirty and half sparkling clean to show the result, crisp natural light. No people.",
            false, AnyLayout),
    ];

    public static CreativeFormat? Find(string? key) => All.FirstOrDefault(f => f.Key == key);

    /// <summary>
    /// Choisit un format par créneau : compatible avec le pilier, jamais deux fois le même dans la semaine,
    /// en priorité ceux qui n'ont pas servi depuis longtemps.
    /// </summary>
    /// <param name="slotPillars">Pilier de chaque créneau (ou null).</param>
    /// <param name="recent">Formats des dernières publications, du plus récent au plus ancien.</param>
    public static IReadOnlyList<string> Assign(IReadOnlyList<string?> slotPillars, IReadOnlyList<string?> recent, Random random)
    {
        var lastUse = new Dictionary<string, int>();
        for (var i = recent.Count - 1; i >= 0; i--)
            if (recent[i] is { } k) lastUse[k] = i; // plus l'indice est petit, plus c'est récent

        var used = new HashSet<string>();
        var result = new List<string>();
        foreach (var pillar in slotPillars)
        {
            var candidates = All.Where(f => f.Audience is null && !used.Contains(f.Key)).ToList();
            var matching = pillar is null ? candidates : candidates.Where(f => f.Pillars.Contains(pillar, StringComparer.OrdinalIgnoreCase)).ToList();
            if (matching.Count == 0) matching = candidates.Count > 0 ? candidates : All.ToList();
            var pick = matching
                .OrderByDescending(f => lastUse.TryGetValue(f.Key, out var at) ? at : int.MaxValue) // jamais utilisé, ou le plus ancien
                .ThenBy(_ => random.Next())
                .First();
            used.Add(pick.Key);
            result.Add(pick.Key);
        }
        return result;
    }

    /// <summary>Le format adapté au public : un public d'aînés prend le format qui leur est réservé une fois par semaine.</summary>
    public static string ForAudience(string format, string? audience, ISet<string> usedThisWeek)
    {
        var special = All.FirstOrDefault(f => f.Audience is not null && audience?.Contains(f.Audience, StringComparison.OrdinalIgnoreCase) == true);
        if (special is null || usedThisWeek.Contains(special.Key)) return format;
        usedThisWeek.Add(special.Key);
        return special.Key;
    }

    // --- Ingrédients tirés au hasard pour que deux photos d'un même format ne se ressemblent pas ---

    public static readonly string[] Cameras =
    [
        "eye-level shot, 35mm lens", "low-angle shot, 24mm wide lens", "high-angle shot looking down, 35mm lens",
        "over-the-shoulder shot, 50mm lens", "wide establishing shot, 24mm lens", "intimate close shot, 85mm lens, f/1.8",
        "symmetrical straight-on composition, 35mm lens", "dynamic diagonal composition, 28mm lens",
    ];

    public static readonly string[] Lights =
    [
        "soft golden morning light", "bright airy midday daylight", "warm late-afternoon sun with long shadows",
        "blue hour outside with warm lamps inside", "crisp snowy daylight bouncing through the windows", "cozy evening light from table lamps",
        "dramatic window light with strong contrast", "soft overcast daylight, gentle and even",
    ];

    public static readonly string[] Places =
    [
        "modern condo kitchen", "Victorian-style townhouse living room in Old Québec", "bright bathroom with white tiles",
        "entryway of a family house", "home office corner", "suburban bungalow living room", "laundry room", "staircase and hallway",
        "open-plan loft with exposed brick", "cozy bedroom with large window", "sunroom with plants", "renovated basement family room",
    ];

    public static readonly string[] Palettes =
    [
        "touches of the brand blue and lime green in props", "warm wood and cream tones", "crisp whites with a single bold color accent",
        "soft pastel tones", "rich saturated colors", "Scandinavian neutral tones with green plants",
    ];

    public static readonly string[] People =
    [
        "a woman in her thirties", "a man in his forties", "a couple in their fifties", "a young professional in their twenties",
        "a father with a school-age child", "two roommates", "a woman in her sixties", "a mixed-generation family",
    ];

    /// <summary>Accroches à varier d'une publication à l'autre.</summary>
    public static readonly string[] Hooks =
    [
        "une question directe au lecteur", "une phrase choc ou surprenante", "une scène du quotidien décrite en une ligne",
        "un « Et si… ? »", "un contraste avant/après en une ligne", "une confidence ou un aveu amusant", "un « Le saviez-vous ? »",
    ];
}
