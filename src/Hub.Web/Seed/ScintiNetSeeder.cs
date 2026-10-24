using Hub.Modules.Catalog.Domain;
using Hub.Modules.Catalog.Infrastructure;
using Hub.Modules.Channels;
using Hub.Modules.Content.Domain;
using Hub.Modules.Content.Infrastructure;
using Hub.Modules.Platform.Application;
using Hub.SharedKernel.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace Hub.Web.Seed;

/// <summary>
/// Données de départ de ScintiNet, reprises du site actuel.
/// Les témoignages et chiffres fictifs de l'ancien site ne sont PAS repris.
/// N'agit que si le catalogue de l'entreprise est vide : les modifications
/// faites ensuite dans le back-office ne sont jamais écrasées.
/// </summary>
public static class ScintiNetSeeder
{
    public static async Task SeedAsync(IServiceProvider services, CancellationToken ct = default)
    {
        var tenantContext = services.GetRequiredService<ITenantContext>();
        var store = services.GetRequiredService<ITenantStore>();
        var tenant = await store.GetAsync(tenantContext.TenantId, ct);
        if (tenant is null || tenant.Slug != "scintinet") return;

        var catalogFactory = services.GetRequiredService<IDbContextFactory<CatalogDbContext>>();
        await using var catalog = await catalogFactory.CreateDbContextAsync(ct);
        if (await catalog.Categories.AnyAsync(ct)) return;

        var logger = services.GetRequiredService<ILoggerFactory>().CreateLogger("Seed");
        logger.LogInformation("Préparation des données de départ de ScintiNet.");

        // Entreprise
        if (string.IsNullOrWhiteSpace(tenant.Phone))
        {
            tenant.Tagline = "L'exigence du propre et net !";
            tenant.ServiceAreaSummary = "Entretien ménager, nettoyage spécialisé et soutien à domicile pour les aînés, partout dans la ville de Québec.";
            tenant.Phone = "418 953-3284";
            tenant.PublicEmail = "contact@scintinet.com";
            tenant.WhatsAppNumber = "4189533284";
            tenant.StreetAddress = "12, rue Boucher, app. 8";
            tenant.ShowStreetAddress = false;
            tenant.City = "Québec";
            tenant.Region = "QC";
            tenant.BusinessNumber = "3382027319";
            tenant.BusinessHours = "Lun. au ven. 8 h – 18 h · sam. 9 h – 16 h";
            tenant.FacebookUrl = "https://www.facebook.com/profile.php?id=61583803468197";
            tenant.AdminNotificationEmail = "tsillery@ymail.com";
            tenant.SiteUrl = "https://www.scintinet.com";
            await store.SaveAsync(tenant, ct);
        }

        // Catalogue
        var maintenance = new ServiceCategory
        {
            Name = "Entretien et nettoyage", Slug = "services", RouteSegment = "services", SortOrder = 1,
            Intro = "Maisons, condos, immeubles, vitres et véhicules : un service fiable et soigné, avec notre propre matériel.",
        };
        var seniors = new ServiceCategory
        {
            Name = "Aînés et soutien", Slug = "aines-soutien", RouteSegment = "aines", SortOrder = 2,
            Intro = "Des préposés aux bénéficiaires qui aident vos proches à vivre chez eux en sécurité, avec respect et dignité. À l'heure ou par quart de travail.",
        };
        catalog.Categories.AddRange(maintenance, seniors);

        var order = 0;
        Service S(ServiceCategory c, string slug, string title, string icon, string image, string summary, string body) => new()
        {
            CategoryId = c.Id, Slug = slug, Title = title, Icon = icon, ImageUrl = $"/images/site/{image}",
            Summary = summary, Body = body, SortOrder = ++order * 10,
        };

        catalog.Services.AddRange(
            S(maintenance, "nettoyage-residentiel", "Nettoyage résidentiel", "home", "maisons.webp",
                "Nettoyage complet de votre maison, condo ou appartement, une seule fois ou selon un horaire régulier.",
                """
                Que ce soit pour un grand ménage, un entretien chaque semaine ou un nettoyage avant une visite, nous remettons votre logement propre, sain et agréable à vivre.

                ## Ce que comprend le service
                - Dépoussiérage des surfaces, meubles et plinthes
                - Cuisine : comptoirs, électroménagers (extérieur), évier
                - Salles de bain : douche, bain, toilette, lavabo, miroirs
                - Aspirateur et lavage des planchers
                - Vidage des poubelles

                ## Comment ça se passe
                Nous convenons ensemble des pièces et des tâches prioritaires. Notre équipe arrive à l'heure prévue avec tout le matériel et les produits.

                Un nettoyage en profondeur (intérieur du four, du réfrigérateur, des armoires) peut s'ajouter sur demande.
                """),
            S(maintenance, "entretien-immeubles", "Entretien d'immeubles et d'espaces communs", "building", "immeubles.webp",
                "Entretien régulier des entrées, corridors, escaliers et aires communes pour le confort et la sécurité des résidents.",
                """
                Gestionnaires, syndicats de copropriété, propriétaires de plex : nous prenons en charge la propreté des aires communes selon un calendrier fixe.

                ## Ce que nous entretenons
                - Halls d'entrée, portes vitrées et boîtes aux lettres
                - Corridors, escaliers et rampes
                - Salles communes et buanderies
                - Abords de l'immeuble et stationnements

                ## Pour vous simplifier la vie
                Un horaire stable, une équipe que les résidents reconnaissent et un compte rendu après chaque visite.
                """),
            S(maintenance, "lavage-de-vitres", "Lavage de vitres", "window", "vitres.webp",
                "Des vitres impeccables à l'intérieur et à l'extérieur, idéal après l'hiver québécois.",
                """
                Le calcium, la poussière et les intempéries de l'hiver ternissent vos fenêtres. Nous leur redonnons toute leur clarté.

                ## Ce que comprend le service
                - Vitres intérieures et extérieures accessibles
                - Cadres et rebords
                - Portes-patio et portes vitrées

                Demandez un devis en précisant le nombre de fenêtres et l'étage : nous vous répondons rapidement.
                """),
            S(maintenance, "lavage-auto-a-domicile", "Lavage de voiture à domicile", "car", "voiture.webp",
                "Nettoyage intérieur et extérieur de votre véhicule, chez vous, sans que vous ayez à vous déplacer.",
                """
                Nous nous déplaçons à votre domicile pour un lavage soigné et respectueux de la carrosserie. Pratique pour les familles et les horaires chargés.

                ## Ce que comprend le service
                - Lavage extérieur à la main
                - Aspirateur des sièges, tapis et coffre
                - Nettoyage du tableau de bord et des vitres intérieures
                """),
            S(seniors, "aide-a-domicile", "Aide à domicile", "home", "aide-a-domicile.webp",
                "Un soutien personnalisé pour que votre proche reste chez lui dans le confort et la sécurité.",
                """
                Nos préposés aux bénéficiaires accompagnent les aînés dans leur quotidien, à leur rythme, pour qu'ils puissent continuer à vivre chez eux.

                ## Formules
                - À l'heure, pour quelques visites par semaine
                - Par quart de travail, pour une présence plus longue

                Nous élaborons avec vous un plan d'accompagnement adapté aux besoins et aux habitudes de votre proche, sans engagement.
                """),
            S(seniors, "accompagnement-des-aines", "Accompagnement des aînés", "users", "accompagnement.webp",
                "Sorties, rendez-vous médicaux, promenades : une présence chaleureuse pour briser l'isolement.",
                """
                Une sortie, un rendez-vous chez le médecin, une course ou simplement une promenade : nous accompagnons votre proche avec chaleur et respect.

                - Accompagnement aux rendez-vous
                - Courses et commissions
                - Promenades et activités sociales
                """),
            S(seniors, "assistance-personnes-agees", "Assistance aux personnes âgées", "heart", "assistance.webp",
                "Une présence rassurante et professionnelle pour les besoins du quotidien.",
                """
                Une présence attentive pour aider votre proche dans ses besoins quotidiens, avec patience et bienveillance, et pour vous rassurer lorsque vous ne pouvez pas être là.
                """),
            S(seniors, "soutien-quotidien", "Soutien quotidien à domicile", "clock", "soutien-quotidien.webp",
                "Repas, hygiène, organisation de la journée : une aide adaptée à la routine de chacun.",
                """
                Nous adaptons notre aide à la routine de chaque personne pour un quotidien serein et structuré.

                - Préparation des repas
                - Aide à l'hygiène
                - Organisation de la journée et rappels
                """),
            S(seniors, "maintien-a-domicile", "Maintien à domicile", "shield", "maintien-a-domicile.webp",
                "Permettre à vos proches de rester chez eux le plus longtemps possible, dans un environnement propre et sûr.",
                """
                Rester chez soi le plus longtemps possible, c'est le souhait de la plupart des aînés. Nous combinons entretien du logement et soutien personnel pour le rendre possible, en toute sécurité.
                """),
            S(seniors, "aide-domestique", "Aide domestique", "wrench", "aide-domestique.webp",
                "Ménage léger, lessive, rangement et cuisine légère pour un cadre de vie agréable.",
                """
                Nos intervenants assurent les tâches du quotidien pour que votre proche vive dans un cadre propre et agréable.

                - Ménage léger et rangement
                - Lessive et literie
                - Cuisine légère
                """),
            S(seniors, "perte-d-autonomie", "Aînés en perte d'autonomie", "hand-heart", "perte-autonomie.webp",
                "Un accompagnement doux et professionnel pour les personnes à mobilité réduite.",
                """
                Pour les personnes en perte d'autonomie ou à mobilité réduite, nos préposés aux bénéficiaires offrent un accompagnement doux et professionnel, au rythme de chacun et dans le respect de sa dignité.

                Parlez-nous de la situation de votre proche : nous vous proposerons une formule à l'heure ou par quart de travail.
                """));
        await catalog.SaveChangesAsync(ct);

        // Contenu
        var contentFactory = services.GetRequiredService<IDbContextFactory<ContentDbContext>>();
        await using var content = await contentFactory.CreateDbContextAsync(ct);

        string[] areas =
        [
            "Vieux-Québec – Cap-Blanc", "Saint-Jean-Baptiste", "Montcalm", "Saint-Roch", "Saint-Sauveur", "Limoilou",
            "Vanier", "Charlesbourg", "Orsainville", "Beauport", "Sainte-Foy", "Sillery", "Cap-Rouge", "L'Ancienne-Lorette",
            "Loretteville", "Val-Bélair", "La Haute-Saint-Charles", "Saint-Émile", "Neufchâtel", "Duberger", "Les Saules",
            "Lebourgneuf", "Lac-Saint-Charles", "Wendake",
        ];
        content.Areas.AddRange(areas.Select((name, i) => new ServiceArea
        {
            Name = name, Slug = Hub.SharedKernel.Text.Slug.From(name), SortOrder = i,
        }));

        content.Blocks.AddRange(
            new ContentBlock
            {
                Key = "home.hero", Eyebrow = "Services professionnels à Québec",
                Title = "Entretien ménager et aide à domicile à Québec",
                Body = "Des espaces propres, sécuritaires et agréables au quotidien : nettoyage résidentiel, entretien d'immeubles et soutien aux aînés.",
            },
            new ContentBlock
            {
                Key = "home.about", Eyebrow = "À propos", Title = "Qui sommes-nous ?",
                Body = """
                    ScintiNet est une entreprise d'entretien et de services de proximité de Québec. Notre mission : offrir des services de qualité qui contribuent au bien-être de nos clients, dans des espaces de vie propres, sains et accueillants.

                    Nous nous situons entre les travailleurs informels à bas prix et les grandes entreprises : un service fiable, professionnel et personnalisé, à un prix juste.
                    """,
            },
            new ContentBlock { Key = "home.advantages", Eyebrow = "Nos engagements", Title = "Pourquoi choisir ScintiNet ?" },
            new ContentBlock { Key = "home.steps", Eyebrow = "Comment ça marche", Title = "Du premier appel au travail terminé" });

        var h = 0;
        HighlightItem H(string group, string icon, string title, string text) =>
            new() { Group = group, Icon = icon, Title = title, Text = text, SortOrder = ++h };

        content.Highlights.AddRange(
            H(HighlightGroups.Values, "star", "Qualité du service", "Un travail soigné et rigoureux à chaque intervention."),
            H(HighlightGroups.Values, "clock", "Fiabilité et ponctualité", "À l'heure, et prévenu à l'avance en cas d'imprévu."),
            H(HighlightGroups.Values, "shield", "Confiance et transparence", "Des prix clairs et une communication ouverte."),
            H(HighlightGroups.Values, "heart", "Bien-être des clients", "Votre confort passe avant tout."),
            H(HighlightGroups.Values, "users", "Proximité et écoute", "Une équipe locale, un service humain et personnalisé."),
            H(HighlightGroups.Advantages, "users", "Équipe locale", "Nous connaissons les réalités des résidents de Québec : vous faites affaire avec des gens de votre communauté."),
            H(HighlightGroups.Advantages, "shield", "Personnel formé et assuré", "Nos employés sont formés à nos standards et couverts par une assurance responsabilité civile."),
            H(HighlightGroups.Advantages, "leaf", "Produits écologiques", "Des produits sécuritaires pour votre famille, vos enfants et vos animaux."),
            H(HighlightGroups.Advantages, "clock", "Ponctualité", "Nous respectons vos horaires. En cas d'imprévu, vous êtes prévenu et une solution vous est proposée."),
            H(HighlightGroups.Advantages, "thumbs-up", "Service sur mesure", "Un plan d'entretien adapté à vos besoins et à votre budget."),
            H(HighlightGroups.Advantages, "award", "Satisfaction garantie", "Pas satisfait du résultat ? Nous revenons corriger, sans frais."),
            H(HighlightGroups.Steps, "phone-call", "Vous nous contactez", "Par téléphone, WhatsApp ou par le formulaire du site."),
            H(HighlightGroups.Steps, "users", "Nous vous rappelons", "Sous un jour ouvrable, pour comprendre votre besoin et évaluer le travail."),
            H(HighlightGroups.Steps, "file-signature", "Devis et entente", "Un devis clair, sans surprise. Vous validez, nous fixons la date."),
            H(HighlightGroups.Steps, "wrench", "Intervention", "Notre équipe arrive à l'heure avec tout le matériel et vous remet des lieux impeccables."));

        var f = 0;
        FaqItem Q(string q, string a) => new() { Question = q, Answer = a, SortOrder = ++f };
        content.Faq.AddRange(
            Q("Avez-vous un horaire fixe ?", "Non, nous nous adaptons à votre emploi du temps. Nous intervenons du lundi au samedi, et convenons ensemble de l'horaire qui vous convient lors du premier contact."),
            Q("Apportez-vous votre propre matériel ?", "Oui, nos équipes se déplacent avec tout le matériel et les produits nécessaires. Vous n'avez rien à fournir."),
            Q("Êtes-vous assurés ?", "Oui. ScintiNet détient une assurance responsabilité civile qui couvre les dommages pouvant survenir lors de nos interventions."),
            Q("Vos produits sont-ils sécuritaires pour les enfants et les animaux ?", "Oui, nous utilisons des produits écologiques et biodégradables, sans danger pour les enfants, les animaux et les personnes sensibles."),
            Q("Puis-je annuler ou reporter un rendez-vous ?", "Oui, sans frais jusqu'à 24 heures avant l'intervention. Au-delà, des frais peuvent s'appliquer selon votre entente."),
            Q("Comment se passe la facturation ?", "Vous recevez une facture détaillée après chaque intervention. Nous acceptons le virement bancaire, le chèque et le paiement en ligne. Tout est précisé dans le devis."),
            Q("Offrez-vous des contrats d'entretien réguliers ?", "Oui : chaque semaine, aux deux semaines ou chaque mois, à tarif préférentiel. Un contrat régulier vous donne aussi la priorité dans notre calendrier."),
            Q("Intervenez-vous dans les copropriétés et les immeubles à logements ?", "Oui, nous entretenons les aires communes : entrées, corridors, escaliers et stationnements."),
            Q("Y a-t-il des frais supplémentaires ?", "Non, le devis est complet. Un écart n'est possible que si le travail diffère nettement de ce qui a été convenu (état très dégradé, superficie différente) et il vous est toujours annoncé avant."),
            Q("Combien de temps dure une intervention ?", "Un nettoyage résidentiel standard prend de 2 à 4 heures, le lavage de vitres de 1 à 3 heures, l'entretien d'aires communes de 1 à 2 heures. La durée est précisée dans le devis."),
            Q("Acceptez-vous les urgences ?", "Nous acceptons les urgences mineures la fin de semaine. Appelez-nous ou écrivez-nous sur WhatsApp pour évaluer la situation."),
            Q("Comment fonctionne l'aide à domicile pour les aînés ?", "Nos préposés aux bénéficiaires interviennent à l'heure ou par quart de travail. Nous commençons par un échange avec vous et votre proche pour bâtir un plan d'accompagnement adapté."));

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var spring = today.Month <= 5 ? today.Year : today.Year + 1;
        content.Promotions.AddRange(
            new Promotion
            {
                Badge = "Offre de la semaine", Title = "Réservez le vendredi,", Highlight = "économisez 10 %",
                Text = "10 % de réduction sur les interventions commandées le vendredi. Une façon simple de garder votre espace impeccable pour la fin de semaine.",
                ImageUrl = "/images/site/promo-vendredi.webp", PrimaryLabel = "Profiter de l'offre", PrimaryUrl = "/devis",
                SecondaryLabel = "Demander un rendez-vous", SecondaryUrl = "/rendez-vous", SortOrder = 1,
            },
            new Promotion
            {
                Badge = "Aînés et soutien", Title = "Un proche a besoin", Highlight = "d'aide à domicile ?",
                Text = "Nos préposés aux bénéficiaires l'accompagnent à son rythme, à l'heure ou par quart de travail.",
                ImageUrl = "/images/site/aide-a-domicile.webp", PrimaryLabel = "Découvrir le service", PrimaryUrl = "/aines/aide-a-domicile",
                SecondaryLabel = "Nous appeler", SecondaryUrl = "tel:+14189533284", SortOrder = 2,
            },
            new Promotion
            {
                Badge = "Grand ménage du printemps", Title = "Fini l'hiver.", Highlight = "Place à la fraîcheur.",
                Text = "Un grand nettoyage en profondeur pour redonner à chaque pièce sa brillance, et des vitres sans traces de calcium.",
                ImageUrl = "/images/site/promo-grand-menage.webp", PrimaryLabel = "Obtenir mon devis", PrimaryUrl = "/devis?service=nettoyage-residentiel",
                StartsOn = new DateOnly(spring, 3, 15), EndsOn = new DateOnly(spring, 6, 15), SortOrder = 3,
            },
            new Promotion
            {
                Badge = "Lavage de voiture à domicile", Title = "Votre voiture,", Highlight = "comme au premier jour",
                Text = "Nous nous déplaçons chez vous pour un lavage complet, intérieur et extérieur.",
                ImageUrl = "/images/site/promo-vehicule.webp", PrimaryLabel = "Demander un rendez-vous", PrimaryUrl = "/rendez-vous?service=lavage-auto-a-domicile",
                StartsOn = new DateOnly(spring, 4, 15), EndsOn = new DateOnly(spring, 10, 31), SortOrder = 4,
            });

        content.Pages.AddRange(
            new Page
            {
                Slug = "a-propos", Title = "À propos de ScintiNet", Lead = "L'exigence du propre et net, au service des résidents de Québec.",
                Body = """
                    ScintiNet est une entreprise de Québec spécialisée dans l'entretien résidentiel, le nettoyage spécialisé et les services de proximité, dont le soutien à domicile pour les aînés.

                    ## Notre mission
                    Offrir des services de qualité qui contribuent au bien-être et au confort de nos clients, dans des espaces de vie propres, sains et accueillants.

                    ## Notre place
                    Nous nous situons entre les travailleurs informels à bas prix et les grandes entreprises généralistes : un service fiable, professionnel et personnalisé, à un prix juste.

                    ## Nos valeurs
                    - **Qualité** : un travail soigné à chaque intervention
                    - **Fiabilité** : à l'heure, toujours
                    - **Transparence** : des prix clairs, sans surprise
                    - **Bien-être** : votre confort avant tout
                    - **Proximité** : une équipe locale, à l'écoute
                    """,
            },
            new Page
            {
                Slug = "confidentialite", Title = "Politique de confidentialité",
                Lead = "Comment {{Nom}} recueille, utilise et protège vos renseignements personnels.",
                SeoDescription = "Politique de confidentialité et de protection des renseignements personnels (Loi 25).",
                Body = """
                    ## Responsable de la protection des renseignements personnels
                    {{Responsable}} est responsable de la protection des renseignements personnels chez {{Nom}}. Pour toute question ou demande : {{CourrielResponsable}}.

                    ## Renseignements recueillis
                    Lorsque vous remplissez un formulaire, nous recueillons seulement ce qui est nécessaire pour vous répondre :
                    - nom, courriel et numéro de téléphone ;
                    - adresse et secteur de l'intervention, si vous les indiquez ;
                    - description de votre besoin (services, type de propriété, date souhaitée, message).

                    ## Utilisation
                    Ces renseignements servent uniquement à répondre à votre demande, à préparer un devis, à planifier et confirmer une intervention, et à assurer le suivi. Ils ne sont jamais vendus ni loués.

                    ## Fournisseurs
                    Le site est hébergé sur Microsoft Azure (Canada). Les courriels de confirmation sont envoyés par le service Resend. Ces fournisseurs n'utilisent pas vos renseignements à leurs propres fins.

                    ## Témoins (cookies) et mesure d'audience
                    - Un témoin de session retient la provenance de votre visite (par exemple, une publicité Facebook), sans vous identifier.
                    - Un identifiant aléatoire de visite n'est déposé que si vous acceptez la mesure d'audience. Vous pouvez retirer votre accord en tout temps avec le lien « Témoins et mesure » au bas de chaque page.
                    - Aucun témoin publicitaire n'est utilisé.

                    ## Conservation
                    Les demandes sont conservées le temps nécessaire au suivi et à nos obligations légales, puis supprimées.

                    ## Vos droits
                    Vous pouvez demander l'accès à vos renseignements, leur rectification ou leur suppression, et retirer votre consentement, en écrivant à {{CourrielResponsable}}.

                    ## Nous joindre
                    {{Nom}} · {{Telephone}} · {{Courriel}} · NEQ {{NEQ}}
                    """,
            });
        await content.SaveChangesAsync(ct);

        // Canaux
        var channelsFactory = services.GetRequiredService<IDbContextFactory<ChannelsDbContext>>();
        await using var channels = await channelsFactory.CreateDbContextAsync(ct);
        if (!await channels.Channels.AnyAsync(ct))
        {
            channels.Channels.AddRange(
                new Channel { Type = ChannelType.Website, Status = ChannelStatus.Active, Url = "https://www.scintinet.com", Phase = 1 },
                new Channel { Type = ChannelType.GoogleBusiness, Status = ChannelStatus.Planned, Phase = 2, Notes = "Créer ou réclamer la fiche pour recueillir de vrais avis." },
                new Channel { Type = ChannelType.Facebook, Status = ChannelStatus.Planned, Url = tenant.FacebookUrl, Phase = 3 },
                new Channel { Type = ChannelType.Instagram, Status = ChannelStatus.Planned, Phase = 3 },
                new Channel { Type = ChannelType.YouTube, Status = ChannelStatus.Planned, Phase = 4 },
                new Channel { Type = ChannelType.TikTok, Status = ChannelStatus.Planned, Phase = 4 });
            await channels.SaveChangesAsync(ct);
        }

        services.GetRequiredService<Hub.SharedKernel.Caching.ContentCache>().Invalidate();
        logger.LogInformation("Données de départ prêtes.");
    }
}
