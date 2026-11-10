namespace Hub.Modules.Channels.Application;

/// <summary>Un style visuel de la « bible de styles » : son nom et sa consigne pour l'IA d'image.</summary>
/// <param name="Key">Identifiant stable (enregistré sur la publication et dans les styles désactivés).</param>
/// <param name="Name">Nom lisible.</param>
/// <param name="Prompt">Consigne de style, en anglais, pour l'IA d'image.</param>
/// <param name="Photographic">Vrai si c'est un rendu photographique (faux : illustration, 3D, graphisme).</param>
public sealed record VisualStyle(string Key, string Name, string Prompt, bool Photographic);

/// <summary>
/// Bible de styles visuels : combinée aux formats et aux ingrédients, elle donne des milliers de combinaisons.
/// Les styles des dernières images sont écartés, et l'administrateur peut en désactiver.
/// </summary>
public static class CreativeStyles
{
    public static IReadOnlyList<VisualStyle> All { get; } =
    [
        new("cinema", "Image de cinéma", "cinematic film still, anamorphic lens, subtle film grain, rich color grading, dramatic but warm", true),
        new("documentaire", "Photo documentaire", "candid documentary photography, natural available light, authentic unposed moment, 35mm reportage feel", true),
        new("golden", "Lumière dorée argentique", "warm golden-hour lifestyle photography shot on Kodak Portra 400 film, soft grain, glowing highlights", true),
        new("noir-blanc", "Noir et blanc avec accent", "black-and-white editorial photography with a single color accent in the brand blue, high contrast, elegant", true),
        new("macro", "Studio macro", "high-end studio macro product photography, crisp details, droplets and reflections, seamless backdrop", true),
        new("catalogue", "Catalogue vu du dessus", "top-down catalog flat lay, graphic arrangement, pastel seamless backdrop, soft even studio light", true),
        new("magazine", "Couverture de magazine", "glossy lifestyle magazine cover design, bold masthead-style headline, editorial portrait or still life", true),
        new("polaroid", "Collage de polaroïds", "collage of instant photos pinned or taped on a textured board, slightly tilted, nostalgic and personal", true),
        new("argile", "3D pâte à modeler", "3D claymation render, soft plasticine textures, playful miniature diorama, cute and tactile", false),
        new("papier", "Papier découpé", "layered paper-cut art, handcrafted, soft shadows between paper layers, vibrant colored paper", false),
        new("isometrique", "Illustration isométrique", "clean isometric 3D illustration, cutaway view of a home, crisp vector shading, bright palette", false),
        new("rendu-3d", "3D brillant", "glossy 3D render, soft studio lighting, pastel colors, smooth rounded shapes, playful and modern", false),
        new("suisse", "Affiche suisse", "Swiss International Typographic Style poster, strict grid, bold sans-serif typography, flat color blocks", false),
        new("bauhaus", "Bauhaus géométrique", "Bauhaus-inspired geometric composition, circles, triangles and bars, primary-color accents, flat print look", false),
        new("retro-60", "Pub rétro des années 60", "1960s vintage print advertisement, halftone texture, muted retro palette, mid-century illustration and lettering", false),
        new("pop-art", "Pop art bande dessinée", "pop art comic style, Ben-Day dots, bold black outlines, punchy saturated colors, speech-bubble energy", false),
        new("risographie", "Risographie", "risograph print, grainy two-color overprint, slight misregistration, indie poster feel", false),
        new("aquarelle", "Aquarelle", "loose watercolor illustration, soft washes and paper texture, delicate ink lines", false),
        new("conte", "Album jeunesse", "storybook gouache illustration, whimsical and warm, textured brushwork", false),
        new("minimal", "Ultra minimaliste", "ultra-minimalist composition, one subject, vast negative space, soft shadow, calm and premium", true),
    ];

    public static VisualStyle? Find(string? key) => All.FirstOrDefault(s => s.Key == key);

    /// <summary>Références tirées au hasard pour inspirer Claude (époque, pellicule ou mouvement artistique).</summary>
    public static readonly string[] References =
    [
        "esthétique des années 1950", "esthétique des années 1970", "esthétique des années 1990", "rendu Fujifilm Velvia très saturé",
        "pellicule Cinestill 800T, halos nocturnes", "symétrie à la Wes Anderson", "design Memphis des années 1980", "Art déco",
        "minimalisme scandinave", "minimalisme japonais", "affiches de voyage vintage", "photographie de mode des années 2000",
        "couleurs complémentaires très contrastées", "palette monochrome avec un seul accent", "lumière de studio dramatique (clair-obscur)",
    ];

    /// <summary>
    /// Tire un style : jamais un style désactivé, ni un des dernières images ni un déjà essayé pour cette publication.
    /// </summary>
    public static VisualStyle Pick(Random random, IReadOnlyCollection<string>? disabled, IReadOnlyCollection<string>? recent, IReadOnlyCollection<string>? tried = null)
    {
        bool Allowed(VisualStyle s) => disabled?.Contains(s.Key) != true;
        var fresh = All.Where(s => Allowed(s) && recent?.Contains(s.Key) != true && tried?.Contains(s.Key) != true).ToList();
        if (fresh.Count == 0) fresh = All.Where(s => Allowed(s) && tried?.Contains(s.Key) != true).ToList();
        if (fresh.Count == 0) fresh = All.Where(Allowed).ToList();
        if (fresh.Count == 0) fresh = All.ToList();
        return fresh[random.Next(fresh.Count)];
    }

    public static IReadOnlyList<string> Parse(string? csv) =>
        (csv ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Where(k => Find(k) is not null).ToList();
}
