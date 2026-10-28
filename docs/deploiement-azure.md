# Mise en ligne sur Azure

Ce guide remplace le site OVH par le nouveau site, sans coupure. Compter environ une heure, plus le temps de propagation DNS.

## Ce qui est créé

| Ressource | Offre | Coût mensuel approximatif |
| --- | --- | --- |
| App Service Linux (site + back-office) | B1 (1 cœur, 1,75 Go) | environ 13 $ US |
| Azure SQL | Basic (2 Go, prix fixe) | environ 5 $ US |
| Stockage des images | Standard LRS | moins de 1 $ US |
| Certificat HTTPS | Certificat géré par App Service | gratuit |

Pourquoi Azure SQL **Basic** plutôt que l'offre gratuite « serverless » : la mesure des visites écrit dans la base toutes les 2 minutes dès qu'il y a du trafic. La base ne se mettrait donc presque jamais en pause et dépasserait la limite gratuite en quelques jours. Elle serait alors coupée, ou facturée bien plus cher que Basic.

Tout est décrit dans `infra/main.bicep` et vérifié à chaque compilation sur GitHub.

## 1. Créer l'infrastructure et déployer (script)

Prérequis : un abonnement Azure actif.

```powershell
cd Hub
git pull
powershell -ExecutionPolicy Bypass -File .\scripts\azure-deployer.ps1
```

Le script installe Azure CLI au besoin, ouvre la connexion à Azure, puis demande :

- le courriel et le mot de passe de l'administrateur du back-office en ligne ;
- la clé API Resend : appuyer sur Entrée pour la saisir plus tard.

Il crée ensuite le groupe de ressources `rg-scintinet` et les ressources, publie le site et le déploie. À la fin, il affiche :

- l'adresse provisoire du site, du type `https://scintinet-web-xxxx.azurewebsites.net` ;
- un mot de passe SQL généré, à garder dans un gestionnaire de mots de passe.

Pour redéployer seulement le code plus tard :

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\azure-deployer.ps1 -CodeSeulement
```

## 2. Vérifier sur l'adresse provisoire

Avant de toucher au domaine, sur l'adresse `…azurewebsites.net` :

- [ ] Le site s'affiche, avec images et menus
- [ ] Une demande de devis de test arrive dans le back-office (`/admin`)
- [ ] Le téléversement d'une image fonctionne (back-office → Images)
- [ ] Back-office → Entreprise : régler « Adresse du site » à `https://www.scintinet.com`
- [ ] Back-office → Sécurité du compte : activer la vérification en deux étapes

## 3. Brancher le domaine scintinet.com

Le domaine est géré chez OVH. On ajoute les enregistrements DNS **avant** de retirer l'ancien site.

1. Récupérer le code de vérification et l'adresse IP du site :

   ```powershell
   az webapp show -g rg-scintinet -n <nom-du-site> --query "{verification:customDomainVerificationId, ip:inboundIpAddress}"
   ```

2. Dans l'espace client OVH, zone DNS de `scintinet.com`, ajouter ou modifier :

   | Type | Sous-domaine | Valeur |
   | --- | --- | --- |
   | TXT | `asuid.www` | le code de vérification |
   | CNAME | `www` | `<nom-du-site>.azurewebsites.net.` |
   | TXT | `asuid` | le code de vérification |
   | A | *(vide, domaine nu)* | l'adresse IP du site |

   Supprimer les anciens enregistrements A et CNAME qui pointaient vers le VPS OVH, pour `www` et pour le domaine nu.

3. Une fois les DNS propagés (de quelques minutes à quelques heures) :

   ```powershell
   az webapp config hostname add -g rg-scintinet --webapp-name <nom-du-site> --hostname www.scintinet.com
   az webapp config hostname add -g rg-scintinet --webapp-name <nom-du-site> --hostname scintinet.com
   az webapp config ssl create -g rg-scintinet -n <nom-du-site> --hostname www.scintinet.com
   az webapp config ssl create -g rg-scintinet -n <nom-du-site> --hostname scintinet.com
   ```

   Puis lier chaque certificat (portail Azure → le site → Domaines personnalisés → « Ajouter une liaison », type SNI SSL).

`scintinet.com` est automatiquement redirigé vers `www.scintinet.com` (réglage `Hub__CanonicalHost`), pour que Google ne voie qu'une seule adresse.

## 4. Courriels (Resend)

1. Dans Resend → Domains, ajouter `scintinet.com`. Resend affiche 3 enregistrements DNS (DKIM, SPF, MX de retour) à ajouter dans la zone OVH.
2. Attendre « Verified », puis renseigner la clé :

   ```powershell
   az webapp config appsettings set -g rg-scintinet -n <nom-du-site> --settings Resend__ApiKey=<clé>
   ```

3. Envoyer une demande de test depuis le site et vérifier les deux courriels : l'accusé de réception au client et l'avis à l'administrateur.

Les enregistrements MX de la messagerie `contact@scintinet.com` ne doivent pas être modifiés.

## 5. Déploiement automatique depuis GitHub

1. Récupérer le profil de publication :

   ```powershell
   az webapp deployment list-publishing-profiles -g rg-scintinet -n <nom-du-site> --xml
   ```

2. Dans GitHub → `phoenixSil/Hub` → Settings → Secrets and variables → Actions, créer :
   - `AZURE_WEBAPP_NAME` : le nom du site ;
   - `AZURE_WEBAPP_PUBLISH_PROFILE` : tout le XML obtenu.

3. Onglet Actions → **Déployer sur Azure** → Run workflow. Les tests passent d'abord, puis le site est mis à jour.

## 6. Retirer l'ancien site OVH

Garder le VPS une à deux semaines après la bascule, le temps de vérifier les demandes reçues et le référencement, puis le résilier.

## En cas de problème

- Journal en direct : `az webapp log tail -g rg-scintinet -n <nom-du-site>`
- Le site ne démarre pas après un changement de base : vérifier la chaîne de connexion dans Azure (le site → Configuration → Chaînes de connexion → `Hub`).
- Mot de passe administrateur perdu : l'administrateur n'est créé qu'au premier démarrage. Ajouter un nouvel administrateur en changeant `Hub__AdminEmail` et `Hub__AdminPassword` dans la configuration, puis redémarrer le site.
