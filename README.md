# Hub — système central de présence en ligne

Plateforme générique qui pilote la présence en ligne d'une entreprise. Premier client : **ScintiNet** (entretien ménager et aide à domicile, Québec). Le site public est le premier canal ; les réseaux sociaux et l'agent IA de prix suivront.

Document de vision et d'architecture : https://claude.ai/artifact/YaLTFj4usemoNH3FPfvthg

## Architecture

Monolithe modulaire .NET 10, Blazor (rendu statique côté serveur pour le site public, interactif pour le back-office), EF Core + SQL Server / Azure SQL.

```text
src/
  Hub.SharedKernel/        entité de base (TenantId), filtre par entreprise, cache, événements, courriel
  Hub.Web/                 hôte : site public, back-office (/admin), sitemap, mesure
  Modules/
    Platform/              entreprises (tenants), administrateurs (Identity)
    Catalog/               catégories, services, modèles de prix
    Content/               secteurs, pages, offres, FAQ, avis, textes
    Leads/ (+ Contracts)   demandes de devis, rendez-vous, contact ; courriels
    Analytics/             pages vues, clics, conversions (écritures par lots)
    Channels/              canaux (site, Facebook…) — point d'accroche des phases suivantes
tests/
  Hub.UnitTests/           règles métier
  Hub.ArchitectureTests/   les modules ne se parlent que par leurs Contracts
```

Chaque module a son schéma SQL (`platform`, `catalog`, `content`, `leads`, `analytics`, `channels`) et son propre historique de migrations.

## Démarrer en local (Windows)

Une seule commande, depuis le dossier du dépôt cloné :

```powershell
git clone https://github.com/phoenixSil/Hub.git
cd Hub
powershell -ExecutionPolicy Bypass -File .\scripts\installer-local.ps1
```

Le script vérifie ou installe le SDK .NET 10, démarre SQL Server LocalDB, approuve le certificat HTTPS de développement,
demande le courriel et le mot de passe du premier administrateur (stockés dans les secrets utilisateur, jamais dans Git),
compile, lance les tests puis démarre le site.

| | |
| --- | --- |
| Site public | https://localhost:7180 |
| Back-office | https://localhost:7180/admin |
| Courriels | `src/Hub.Web/App_Data/courriels/*.html` : chaque courriel est un fichier à ouvrir dans le navigateur, rien n'est envoyé |
| Base | `(localdb)\MSSQLLocalDB`, base `Hub` (créée et remplie au premier démarrage) |

Options : `-Reinitialiser` (repartir d'une base vide), `-SansTests`, `-SansLancer`.

Dans Visual Studio : ouvrir `Hub.slnx`, choisir le profil **https** de `Hub.Web` et lancer (F5). Il faut avoir exécuté le script une fois pour l'administrateur et le certificat.

## Configuration (variables d'environnement sur Azure)

| Clé | Rôle |
| --- | --- |
| `ConnectionStrings__Hub` | Chaîne de connexion Azure SQL |
| `Hub__AdminEmail`, `Hub__AdminPassword` | Premier administrateur (créé une seule fois) |
| `Resend__ApiKey`, `Resend__FromEmail` | Envoi des courriels |
| `Media__BlobConnectionString`, `Media__Container` | Stockage des images (sinon `wwwroot/uploads`) |
| `Turnstile__SiteKey`, `Turnstile__SecretKey` | Anti-pourriel Cloudflare (facultatif) |

## Migrations

Le workflow **Générer une migration EF Core** (onglet Actions, lancement manuel) crée une migration pour chaque module et la dépose en artefact. En local :

```bash
dotnet ef migrations add NomDeLaMigration --project src/Modules/Leads --startup-project src/Hub.Web --context LeadsDbContext --output-dir Infrastructure/Migrations
```

## Déploiement

Guide complet : [docs/deploiement-azure.md](docs/deploiement-azure.md). En résumé :

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\azure-deployer.ps1
```

crée l'infrastructure décrite dans `infra/main.bicep` (App Service B1, Azure SQL Basic, stockage des images) et déploie le site. Ensuite, le workflow **Déployer sur Azure** (manuel) met le site à jour depuis GitHub (secrets `AZURE_WEBAPP_NAME`, `AZURE_WEBAPP_PUBLISH_PROFILE`).
