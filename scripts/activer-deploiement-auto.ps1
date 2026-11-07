#Requires -Version 5.1
<#
.SYNOPSIS
    Active le déploiement automatique : chaque changement validé sur « main » est mis en ligne tout seul.

.DESCRIPTION
    À lancer une seule fois, depuis le dossier du projet, après « az login ».
    1. Crée une identité Azure dédiée au déploiement (gratuite), sans mot de passe :
       GitHub prouve son identité à chaque déploiement (OpenID Connect, environnement « production »).
    2. Ne lui donne qu'un seul droit : déployer le site (rôle « Website Contributor » sur l'application web).
    3. Enregistre les identifiants (non secrets) dans les variables du dépôt GitHub si l'outil « gh » est installé,
       sinon les affiche pour les saisir dans GitHub.

    Coût : 0 $. GitHub Actions est gratuit pour un dépôt public et l'identité Azure ne se paie pas.

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File .\scripts\activer-deploiement-auto.ps1
#>
param(
    [string]$GroupeRessources = 'rg-scintinet',
    [string]$NomSite = '',
    [string]$Depot = 'phoenixSil/Hub',
    [string]$AdresseSite = 'https://www.scintinet.com'
)

$ErrorActionPreference = 'Stop'
function Etape([string]$texte) { Write-Host ''; Write-Host "==> $texte" -ForegroundColor Cyan }
function Ok([string]$texte) { Write-Host "    OK  $texte" -ForegroundColor Green }
function Az { $ErrorActionPreference = 'Continue'; $sortie = & az @args 2>$null; if ($LASTEXITCODE -ne 0) { throw "Commande az en échec : az $($args -join ' ')" }; return $sortie }

Etape 'Connexion à Azure'
$compte = Az account show -o json | ConvertFrom-Json
Ok "Abonnement : $($compte.name)"

Etape 'Application web'
if (-not $NomSite) {
    $NomSite = Az webapp list -g $GroupeRessources --query '[0].name' -o tsv
    if (-not $NomSite) { throw "Aucune application web trouvée dans $GroupeRessources." }
}
$idSite = Az webapp show -g $GroupeRessources -n $NomSite --query id -o tsv
Ok $NomSite

Etape 'Identité de déploiement (sans mot de passe)'
$nomIdentite = 'hub-deploiement-github'
$appId = Az ad app list --display-name $nomIdentite --query '[0].appId' -o tsv
if (-not $appId) {
    $appId = Az ad app create --display-name $nomIdentite --query appId -o tsv
    Ok "Identité créée ($appId)"
} else { Ok "Identité existante ($appId)" }

$sp = Az ad sp list --filter "appId eq '$appId'" --query '[0].id' -o tsv
if (-not $sp) { [void](Az ad sp create --id $appId); Ok 'Principal de service créé' }

$sujet = "repo:${Depot}:environment:production"
$existants = Az ad app federated-credential list --id $appId --query '[].subject' -o tsv
if ($existants -notcontains $sujet) {
    $fichier = Join-Path $env:TEMP 'hub-federation.json'
    @{ name = 'github-production'; issuer = 'https://token.actions.githubusercontent.com'; subject = $sujet; audiences = @('api://AzureADTokenExchange') } |
        ConvertTo-Json | Set-Content -Path $fichier -Encoding ASCII
    [void](Az ad app federated-credential create --id $appId --parameters "@$fichier")
    Remove-Item $fichier -ErrorAction SilentlyContinue
}
Ok "GitHub ($sujet) peut se connecter sans secret"

Etape 'Droit de déploiement (sur ce site seulement)'
$deja = Az role assignment list --assignee $appId --scope $idSite --query "[?roleDefinitionName=='Website Contributor'] | length(@)" -o tsv
if ($deja -eq '0' -or -not $deja) {
    # La propagation de la nouvelle identité peut prendre quelques secondes.
    for ($i = 1; $i -le 6; $i++) {
        try { [void](Az role assignment create --assignee $appId --role 'Website Contributor' --scope $idSite); break }
        catch { if ($i -eq 6) { throw }; Start-Sleep -Seconds 10 }
    }
}
Ok 'Rôle « Website Contributor » attribué'

$variables = [ordered]@{
    AZURE_CLIENT_ID       = $appId
    AZURE_TENANT_ID       = $compte.tenantId
    AZURE_SUBSCRIPTION_ID = $compte.id
    AZURE_WEBAPP_NAME     = $NomSite
    SITE_URL              = $AdresseSite
}

Etape 'Variables du dépôt GitHub'
if (Get-Command gh -ErrorAction SilentlyContinue) {
    foreach ($v in $variables.GetEnumerator()) { & gh variable set $v.Key --body $v.Value --repo $Depot | Out-Null }
    Ok 'Variables enregistrées dans GitHub'
} else {
    Write-Host '    L''outil « gh » n''est pas installé : ajoutez ces 5 variables dans GitHub' -ForegroundColor Yellow
    Write-Host "    (https://github.com/$Depot/settings/variables/actions > New repository variable) :" -ForegroundColor Yellow
    foreach ($v in $variables.GetEnumerator()) { Write-Host ("      {0,-22} {1}" -f $v.Key, $v.Value) }
    Start-Process "https://github.com/$Depot/settings/variables/actions"
}

Write-Host ''
Write-Host 'Déploiement automatique activé.' -ForegroundColor Green
Write-Host '    Chaque changement sur « main » est mis en ligne après les tests (onglet Actions > Déploiement).'
Write-Host '    Ces identifiants ne sont pas des secrets : sans GitHub, ils ne permettent rien.'
