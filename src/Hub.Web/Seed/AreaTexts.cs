namespace Hub.Web.Seed;

/// <summary>
/// Textes de départ des pages de secteurs : une description propre à chaque quartier,
/// pour que Google distingue les pages. À relire et enrichir dans le back-office
/// (clients servis, anecdotes locales) : c'est ce qui les rendra vraiment uniques.
/// </summary>
public static class AreaTexts
{
    public static readonly IReadOnlyDictionary<string, string> BySlug = new Dictionary<string, string>
    {
        ["vieux-quebec-cap-blanc"] = """
            Entre les fortifications du Vieux-Québec et les maisons du Cap-Blanc au pied de la falaise, on trouve des logements anciens : pierre, boiseries, planchers d'origine, escaliers étroits. Ces matériaux demandent des produits doux et une main attentive.

            Nous organisons nos visites en tenant compte des rues étroites et du stationnement limité du secteur, et nous arrivons avec tout notre matériel.

            - Entretien régulier de condos et d'appartements dans des bâtiments patrimoniaux
            - Grand ménage et lavage de vitres anciennes, à l'intérieur comme à l'extérieur
            - Aide à domicile pour les aînés qui souhaitent rester dans leur quartier
            """,
        ["saint-jean-baptiste"] = """
            Le faubourg Saint-Jean-Baptiste, c'est des maisons en rangée, des logements sur plusieurs étages et des escaliers à n'en plus finir, tout près de la rue Saint-Jean. Les petits espaces s'encrassent vite et chaque recoin compte.

            Nous intervenons dans les appartements comme dans les parties communes des petits immeubles du faubourg.

            - Ménage régulier ou ponctuel des appartements
            - Entretien des entrées, corridors et escaliers des plex
            - Ménage de fin de bail, très demandé autour du 1er juillet
            """,
        ["montcalm"] = """
            Près des plaines d'Abraham et de l'avenue Cartier, Montcalm compte de grands appartements et des maisons de ville anciennes, souvent avec planchers de bois franc et grandes fenêtres.

            Nous adaptons nos produits aux surfaces anciennes et aux boiseries, et nous offrons un horaire régulier pour les résidents qui veulent un logement toujours impeccable.

            - Entretien ménager hebdomadaire ou aux deux semaines
            - Lavage de vitres après l'hiver
            - Soutien à domicile pour les aînés du quartier
            """,
        ["saint-roch"] = """
            Au cœur de la basse-ville, Saint-Roch mélange condos récents, lofts et bureaux autour de la rue Saint-Joseph. Les grandes surfaces vitrées et les espaces ouverts y sont fréquents.

            Que vous habitiez un condo ou que vous gériez un petit immeuble, nous proposons un entretien régulier et soigné.

            - Ménage de condos et de lofts
            - Entretien des aires communes de petits immeubles
            - Lavage de vitres intérieures
            """,
        ["saint-sauveur"] = """
            Quartier ouvrier aux rues serrées, Saint-Sauveur est fait de plex, de maisons en rangée et de logements familiaux. Beaucoup de résidents y vivent depuis longtemps.

            Nous y offrons un service fiable, à prix juste, pour les locataires, les propriétaires de plex et les aînés.

            - Ménage régulier ou grand ménage du printemps
            - Entretien des escaliers et entrées de plex
            - Aide domestique et accompagnement des aînés
            """,
        ["limoilou"] = """
            Limoilou, ce sont ses plex aux escaliers extérieurs, ses ruelles vertes et ses nombreuses jeunes familles. Les logements y sont souvent spacieux, avec beaucoup de fenêtres et de boiseries.

            Nous nous adaptons aux horaires chargés des familles et aux besoins des propriétaires de plex.

            - Entretien ménager régulier des logements
            - Entretien des parties communes des plex
            - Ménage de fin de bail et grand ménage
            """,
        ["vanier"] = """
            Secteur résidentiel bien desservi, Vanier compte de nombreux logements locatifs et petits immeubles, ainsi que des maisons familiales.

            Nous y offrons un entretien régulier abordable et nous travaillons aussi avec les propriétaires d'immeubles pour l'entretien des aires communes.

            - Ménage résidentiel ponctuel ou régulier
            - Entretien d'immeubles à logements
            - Ménage avant ou après un déménagement
            """,
        ["charlesbourg"] = """
            Du Trait-Carré historique aux rues de bungalows et de duplex, Charlesbourg est un secteur résidentiel où vivent autant de familles que d'aînés.

            Nous y offrons l'entretien ménager régulier et un soutien à domicile pour permettre aux aînés de rester chez eux plus longtemps.

            - Entretien ménager des maisons unifamiliales et des duplex
            - Aide à domicile et aide domestique pour les aînés
            - Lavage de vitres et de voiture à domicile
            """,
        ["orsainville"] = """
            Au nord de Charlesbourg, Orsainville est un secteur paisible de maisons unifamiliales et de quartiers familiaux, avec de grands terrains.

            Nous nous déplaçons chez vous avec notre matériel, aux heures qui conviennent à votre famille.

            - Entretien ménager régulier des maisons
            - Grand ménage saisonnier
            - Lavage de voiture à domicile
            """,
        ["beauport"] = """
            Du Vieux-Beauport aux quartiers familiaux près de la chute Montmorency, Beauport est un grand arrondissement où dominent les maisons unifamiliales.

            Nous y offrons un entretien régulier des maisons, ainsi qu'un soutien à domicile pour les aînés qui souhaitent rester dans leur quartier.

            - Ménage résidentiel régulier ou ponctuel
            - Aide à domicile pour les aînés
            - Lavage de vitres et lavage de voiture à domicile
            """,
        ["sainte-foy"] = """
            Autour de l'Université Laval et du boulevard Laurier, Sainte-Foy réunit tours d'habitation, condos, maisons familiales et de nombreux aînés.

            Nous y intervenons autant pour l'entretien des logements que pour le soutien à domicile des personnes âgées et l'entretien des aires communes d'immeubles.

            - Ménage de condos, d'appartements et de maisons
            - Aide à domicile et accompagnement des aînés
            - Entretien des aires communes des immeubles
            """,
        ["sillery"] = """
            Le long du chemin Saint-Louis, Sillery compte de grandes maisons, des propriétés anciennes et de nombreux résidents aînés attachés à leur quartier.

            Nous offrons un entretien soigné des grandes demeures et un accompagnement bienveillant pour les aînés qui veulent rester chez eux.

            - Entretien ménager régulier des maisons
            - Aide à domicile, aide domestique et maintien à domicile
            - Lavage de vitres, à l'intérieur comme à l'extérieur
            """,
        ["cap-rouge"] = """
            À l'ouest de la ville, au bord du fleuve, Cap-Rouge est un secteur de maisons familiales entouré de verdure.

            Nous nous déplaçons à Cap-Rouge pour l'entretien des maisons et pour les grands ménages de saison.

            - Ménage régulier des maisons unifamiliales
            - Grand ménage du printemps et lavage de vitres
            - Lavage de voiture à domicile
            """,
        ["l-ancienne-lorette"] = """
            Ville voisine de Québec, près de l'aéroport, L'Ancienne-Lorette est un milieu résidentiel de maisons unifamiliales où vivent beaucoup de familles et d'aînés.

            Nous nous y déplaçons comme dans tous les quartiers de Québec, sans frais supplémentaires.

            - Entretien ménager des maisons
            - Aide à domicile pour les aînés
            - Lavage de voiture à domicile
            """,
        ["loretteville"] = """
            Ancien village au bord de la rivière Saint-Charles, Loretteville mêle maisons anciennes et quartiers résidentiels plus récents.

            Nous y offrons l'entretien ménager régulier et un soutien à domicile pour les aînés.

            - Ménage résidentiel régulier ou ponctuel
            - Aide domestique et accompagnement des aînés
            - Grand ménage saisonnier
            """,
        ["val-belair"] = """
            Au nord-ouest de la ville, Val-Bélair compte de nombreux quartiers récents et de jeunes familles aux horaires chargés.

            Nous libérons du temps aux familles avec un entretien régulier, planifié selon vos disponibilités.

            - Entretien ménager hebdomadaire ou aux deux semaines
            - Ménage avant ou après un déménagement
            - Lavage de voiture à domicile
            """,
        ["la-haute-saint-charles"] = """
            Arrondissement du nord de la ville, La Haute-Saint-Charles allie quartiers résidentiels, lacs et forêts. Les maisons y sont souvent grandes et entourées de nature.

            Nous nous y déplaçons avec tout notre matériel pour l'entretien régulier et les grands ménages.

            - Entretien ménager des maisons
            - Grand ménage saisonnier et lavage de vitres
            - Soutien à domicile pour les aînés
            """,
        ["saint-emile"] = """
            Secteur résidentiel du nord de Québec, Saint-Émile est composé surtout de maisons unifamiliales et de quartiers familiaux.

            Nous y offrons un service de ménage fiable et ponctuel, avec la même équipe d'une visite à l'autre autant que possible.

            - Ménage régulier des maisons
            - Grand ménage du printemps
            - Aide à domicile pour les aînés
            """,
        ["neufchatel"] = """
            Neufchâtel est un secteur résidentiel de maisons et de petits immeubles, apprécié des familles pour sa tranquillité.

            Nous y intervenons pour l'entretien des logements et des aires communes de petits immeubles.

            - Entretien ménager résidentiel
            - Entretien d'immeubles à logements
            - Lavage de voiture à domicile
            """,
        ["duberger"] = """
            Près du parc linéaire de la rivière Saint-Charles, Duberger est un quartier de maisons unifamiliales et de bungalows, où beaucoup de propriétaires vivent depuis des années.

            Nous y offrons un entretien régulier et un soutien aux aînés qui souhaitent rester dans leur maison.

            - Entretien ménager des maisons
            - Aide domestique et maintien à domicile des aînés
            - Lavage de vitres après l'hiver
            """,
        ["les-saules"] = """
            Quartier résidentiel tranquille, Les Saules compte surtout des maisons familiales et des bungalows.

            Nous nous déplaçons chez vous pour un entretien ponctuel ou régulier, avec tout le matériel nécessaire.

            - Ménage résidentiel régulier ou ponctuel
            - Aide à domicile pour les aînés
            - Lavage de voiture à domicile
            """,
        ["lebourgneuf"] = """
            Autour des Galeries de la Capitale, Lebourgneuf réunit condos récents, immeubles à logements et bureaux.

            Nous y offrons l'entretien des condos et des appartements, ainsi que celui des aires communes d'immeubles.

            - Ménage de condos et d'appartements
            - Entretien des aires communes d'immeubles
            - Lavage de vitres intérieures
            """,
        ["lac-saint-charles"] = """
            Autour du lac qui alimente une partie de la ville en eau potable, Lac-Saint-Charles est un secteur de maisons entourées de nature.

            Par respect pour ce milieu, nous privilégions des produits doux et biodégradables. Nous nous y déplaçons pour l'entretien régulier et les grands ménages.

            - Entretien ménager des maisons
            - Grand ménage saisonnier
            - Aide à domicile pour les aînés
            """,
        ["wendake"] = """
            Communauté huronne-wendat voisine de Loretteville, Wendake est un milieu de vie à part entière, avec ses maisons et ses commerces.

            Nous nous y déplaçons comme dans tous les quartiers de Québec, pour l'entretien des maisons et le soutien à domicile des aînés.

            - Entretien ménager résidentiel
            - Aide à domicile et accompagnement des aînés
            - Lavage de vitres et lavage de voiture à domicile
            """,
    };
}
