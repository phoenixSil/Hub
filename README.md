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

## Démarrer en local

Prérequis : SDK .NET 10, SQL Server LocalDB (installé avec Visual Studio).

```bash
cd src/Hub.Web
dotnet user-secrets set "Hub:AdminEmail" "vous@exemple.com"
dotnet user-secrets set "Hub:AdminPassword" "UnMotDePasseDe12+Caracteres"
dotnet run
```

Au démarrage, l'application applique les migrations, crée l'entreprise ScintiNet, le premier administrateur et les données de départ (services, secteurs, FAQ…). Site : https://localhost:7180 — back-office : https://localhost:7180/admin.

Sans clé Resend, les courriels sont écrits dans la console au lieu d'être envoyés.

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

Workflow **Déployer sur Azure** (manuel). Secrets GitHub : `AZURE_WEBAPP_NAME`, `AZURE_WEBAPP_PUBLISH_PROFILE`.
