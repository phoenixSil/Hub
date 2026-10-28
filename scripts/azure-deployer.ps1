#Requires -Version 5.1
<#
.SYNOPSIS
    Crée l'infrastructure Azure de Hub et y déploie le site.

.DESCRIPTION
    1. Vérifie Azure CLI et la connexion à Azure
    2. Crée le groupe de ressources et l'infrastructure (infra/main.bicep) :
       App Service Linux B1, Azure SQL Basic, stockage des images
    3. Publie le site et le déploie
    4. Affiche l'adresse provisoire et les étapes suivantes (domaine, courriels, GitHub)

    Relancer le script est sans danger : Azure met à jour ce qui existe déjà.

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File .\scripts\azure-deployer.ps1

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File .\scripts\azure-deployer.ps1 -CodeSeulement
    Redéploie seulement le code, sans toucher à l'infrastructure.
#>
param(
    [string]$GroupeRessources = 'rg-scintinet',
    [string]$Region = 'canadacentral',
    [switch]$CodeSeulement
)

$ErrorActionPreference = 'Stop'
$racine = Split-Path -Parent $PSScriptRoot
$web = Join-Path $racine 'src\Hub.Web'

function Etape([string]$texte) { Write-Host ''; Write-Host "==> $texte" -ForegroundColor Cyan }
function Ok([string]$texte) { Write-Host "    OK  $texte" -ForegroundColor Green }
function Attention([string]$texte) { Write-Host "    !!  $texte" -ForegroundColor Yellow }
function Existe([string]$commande) { return [bool](Get-Command $commande -ErrorAction SilentlyContinue) }
function EnClair([Security.SecureString]$s) {
    return [Runtime.InteropServices.Marshal]::PtrToStringAuto([Runtime.InteropServices.Marshal]::SecureStringToBSTR($s))
}
function MotDePasseAleatoire {
    $jeu = 'ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnopqrstuvwxyz23456789'
    $octets = New-Object byte[] 24
    [Security.Cryptography.RandomNumberGenerator]::Create().GetBytes($octets)
    $corps = -join ($octets | ForEach-Object { $jeu[$_ % $jeu.Length] })
    return "Hub-$corps-9a"
}

Write-Host 'Hub — mise en ligne sur Azure' -ForegroundColor White

# 1. Azure CLI et connexion ---------------------------------------------------
Etape 'Azure CLI'
if (-not (Existe 'az')) {
    if (Existe 'winget') {
        Attention 'Azure CLI absent : installation avec winget...'
        winget install --id Microsoft.AzureCLI --exact --accept-source-agreements --accept-package-agreements
        $env:PATH = [Environment]::GetEnvironmentVariable('PATH', 'Machine') + ';' + [Environment]::GetEnvironmentVariable('PATH', 'User')
    }
    if (-not (Existe 'az')) { throw 'Installez Azure CLI (https://aka.ms/installazurecliwindows) puis relancez.' }
}
Ok 'Azure CLI présent'

& az account show --only-show-errors *> $null
if ($LASTEXITCODE -ne 0) {
    Attention 'Connexion à Azure : une fenêtre du navigateur va s''ouvrir.'
    & az login --only-show-errors | Out-Null
}
$compte = & az account show --query '{nom:name, id:id, utilisateur:user.name}' -o json | ConvertFrom-Json
Ok "Abonnement : $($compte.nom) ($($compte.utilisateur))"
$reponse = Read-Host '    Utiliser cet abonnement ? (O/n)'
if ($reponse -match '^[nN]') {
    & az account list --query '[].{Nom:name, Id:id}' -o table
    $id = Read-Host '    Id de l''abonnement à utiliser'
    & az account set --subscription $id
}

# 2. Infrastructure ---------------------------------------------------------
if (-not $CodeSeulement) {
    Etape "Groupe de ressources $GroupeRessources ($Region)"
    & az group create --name $GroupeRessources --location $Region --only-show-errors -o none
    Ok 'Prêt'

    Etape 'Paramètres'
    $courriel = Read-Host '    Courriel de l''administrateur du back-office'
    do {
        $mdp = EnClair (Read-Host '    Mot de passe de l''administrateur (12+ caractères, majuscule, minuscule, chiffre)' -AsSecureString)
        $valide = $mdp.Length -ge 12 -and $mdp -cmatch '[A-Z]' -and $mdp -cmatch '[a-z]' -and $mdp -match '\d'
        if (-not $valide) { Attention 'Mot de passe trop faible, recommencez.' }
    } while (-not $valide)
    $resend = EnClair (Read-Host '    Clé API Resend (Entrée pour plus tard)' -AsSecureString)
    $mdpSql = MotDePasseAleatoire

    Etape 'Création de l''infrastructure (5 à 10 minutes)'
    $parametres = @(
        "sqlAdminPassword=$mdpSql",
        "adminEmail=$courriel",
        "adminPassword=$mdp",
        "resendApiKey=$resend"
    )
    $sortie = & az deployment group create --resource-group $GroupeRessources `
        --template-file (Join-Path $racine 'infra\main.bicep') `
        --parameters $parametres --query 'properties.outputs' -o json --only-show-errors
    if ($LASTEXITCODE -ne 0) { throw 'La création de l''infrastructure a échoué : voir le message ci-dessus.' }
    $sorties = $sortie | ConvertFrom-Json
    $nomSite = $sorties.webAppName.value
    $url = $sorties.siteUrl.value
    Ok "Site Azure : $nomSite"
    Attention "Mot de passe SQL généré (à garder dans votre gestionnaire de mots de passe) : $mdpSql"
}
else {
    $nomSite = & az webapp list --resource-group $GroupeRessources --query '[0].name' -o tsv
    if (-not $nomSite) { throw "Aucun site trouvé dans $GroupeRessources : lancez d'abord le script sans -CodeSeulement." }
    $url = 'https://' + (& az webapp show --resource-group $GroupeRessources --name $nomSite --query defaultHostName -o tsv)
}

# 3. Publication et déploiement ----------------------------------------------
Etape 'Publication du site'
$publication = Join-Path $env:TEMP 'hub-publish'
$archive = Join-Path $env:TEMP 'hub-publish.zip'
if (Test-Path $publication) { Remove-Item $publication -Recurse -Force }
if (Test-Path $archive) { Remove-Item $archive -Force }
& dotnet publish $web -c Release -o $publication --nologo -v quiet
if ($LASTEXITCODE -ne 0) { throw 'La publication a échoué.' }
Compress-Archive -Path (Join-Path $publication '*') -DestinationPath $archive
Ok 'Archive prête'

Etape 'Déploiement sur Azure (2 à 4 minutes)'
& az webapp deploy --resource-group $GroupeRessources --name $nomSite --src-path $archive --type zip --only-show-errors -o none
if ($LASTEXITCODE -ne 0) { throw 'Le déploiement a échoué.' }
Ok 'Code déployé'

Etape 'Premier démarrage (création de la base et des données de départ)'
$pret = $false
for ($i = 0; $i -lt 30 -and -not $pret; $i++) {
    try {
        $r = Invoke-WebRequest -Uri $url -UseBasicParsing -TimeoutSec 30
        $pret = $r.StatusCode -eq 200
    }
    catch { Start-Sleep -Seconds 10 }
}
if ($pret) { Ok "En ligne : $url" } else { Attention "Le site ne répond pas encore : réessayez $url dans quelques minutes." }

# 4. Étapes suivantes --------------------------------------------------------
Write-Host ''
Write-Host 'Étapes suivantes (voir docs\deploiement-azure.md) :' -ForegroundColor White
Write-Host "  1. Vérifier le site sur $url et le back-office sur $url/admin"
Write-Host '  2. Brancher le domaine scintinet.com (enregistrements DNS chez OVH)'
Write-Host '  3. Vérifier le domaine dans Resend pour l''envoi des courriels'
Write-Host '  4. Activer le déploiement automatique depuis GitHub (profil de publication)'
