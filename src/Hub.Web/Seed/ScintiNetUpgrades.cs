using Hub.Modules.Catalog.Domain;
using Hub.Modules.Catalog.Infrastructure;
using Hub.Modules.Content.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Hub.Web.Seed;

/// <summary>
/// Mises à niveau des données de ScintiNet, exécutées à chaque démarrage.
/// Chaque étape n'ajoute que ce qui manque : rien de ce qui a été saisi
/// dans le back-office n'est écrasé.
/// </summary>
public static class ScintiNetUpgrades
{
    /// <summary>Grille de l'analyse des prix (phase 2), non publiée : à afficher depuis le back-office.</summary>
    public static async Task SeedPricingAsync(IServiceProvider services, CancellationToken ct)
    {
        var factory = services.GetRequiredService<IDbContextFactory<CatalogDbContext>>();
        await using var db = await factory.CreateDbContextAsync(ct);
        if (await db.PricingModels.AnyAsync(ct)) return;

        var serviceIds = await db.Services.ToDictionaryAsync(s => s.Slug, s => s.Id, ct);
        var models = new List<PricingModel>();

        void Add(string slug, PricingModel model)
        {
            if (!serviceIds.TryGetValue(slug, out var id)) return;
            model.ServiceId = id;
            models.Add(model);
        }

        const string cleaningNote = "Analyse des prix (oct. 2026) : plancher environ 31 $/h avec un salaire estimé de 18 $/h. Ménage ponctuel 42 $/h ; grand ménage 45 $/h ou forfait.";
        const string seniorNote = "Analyse des prix (oct. 2026) : plancher environ 35 $/h avec un salaire de préposé estimé à 22 $/h. Agences privées : 40 à 55 $/h.";
        const string seniorPublic = "Minimum 3 heures par visite. Fins de semaine et jours fériés : +15 %. Taxes en sus, s'il y a lieu.";

        Add("nettoyage-residentiel", new PricingModel
        {
            Kind = PricingKind.Hourly, MinAmount = 37m, MinimumHours = 3m, Notes = cleaningNote,
            PublicNote = "Entretien régulier, chaque semaine ou aux deux semaines. Minimum 3 heures par visite. Ménage ponctuel : 42 $/h. Taxes en sus.",
        });
        Add("entretien-immeubles", new PricingModel
        {
            Kind = PricingKind.OnAssessment,
            Notes = "Hypothèse à valider par soumissions : 38 $/h, en forfait mensuel (heures estimées × 38 $), minimum 2 h par visite.",
        });
        Add("lavage-de-vitres", new PricingModel
        {
            Kind = PricingKind.OnAssessment,
            Notes = "Hypothèse à valider par soumissions : 42 $/h, minimum 150 $.",
        });
        Add("lavage-auto-a-domicile", new PricingModel
        {
            Kind = PricingKind.Flat, MinAmount = 120m, MaxAmount = 180m,
            Notes = "Hypothèse à valider : berline 120 $, VUS 180 $, intérieur et extérieur. Coût estimé environ 90 $ par véhicule.",
            PublicNote = "Selon la taille du véhicule, intérieur et extérieur. Taxes en sus.",
        });

        foreach (var slug in new[] { "aide-a-domicile", "accompagnement-des-aines", "assistance-personnes-agees", "soutien-quotidien", "maintien-a-domicile", "aide-domestique", "perte-d-autonomie" })
        {
            Add(slug, new PricingModel
            {
                Kind = PricingKind.Hourly, MinAmount = 44m, MinimumHours = 3m, Notes = seniorNote, PublicNote = seniorPublic,
            });
        }

        Add("aide-a-domicile", new PricingModel
        {
            Kind = PricingKind.PerShift, MinAmount = 40m, ShiftHours = 8m,
            Notes = "Quart de 8 h à 40 $/h, soit 320 $. Plancher environ 33 $/h (moins de déplacements). Aucun tarif de quart publié par la concurrence.",
            PublicNote = "Quart de 8 heures. Taxes en sus, s'il y a lieu.",
        });

        db.PricingModels.AddRange(models);
        await db.SaveChangesAsync(ct);
    }

    /// <summary>Texte propre à chaque secteur, seulement là où aucun texte n'a encore été écrit.</summary>
    public static async Task FillAreaTextsAsync(IServiceProvider services, CancellationToken ct)
    {
        var factory = services.GetRequiredService<IDbContextFactory<ContentDbContext>>();
        await using var db = await factory.CreateDbContextAsync(ct);
        var areas = await db.Areas.Where(a => a.Intro == null || a.Intro == "").ToListAsync(ct);
        var changed = false;
        foreach (var area in areas)
        {
            if (AreaTexts.BySlug.TryGetValue(area.Slug, out var text))
            {
                area.Intro = text;
                changed = true;
            }
        }
        if (changed) await db.SaveChangesAsync(ct);
    }
}
