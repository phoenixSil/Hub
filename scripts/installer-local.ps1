#Requires -Version 5.1
<#
.SYNOPSIS
    Prépare et lance l'environnement local de Hub (ScintiNet) sur Windows.

.DESCRIPTION
    1. Vérifie (et installe au besoin) le SDK .NET 10
    2. Vérifie SQL Server LocalDB et démarre l'instance MSSQLLocalDB
    3. Approuve le certificat HTTPS de développement
    4. Crée le compte administrateur du back-office (secrets utilisateur, jamais dans Git)
    5. Compile la solution et lance les tests
    6. Démarre le site

    Au premier démarrage, l'application crée elle-même la base « Hub », les tables
    et les données de départ de ScintiNet.

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File .\scripts\installer-local.ps1

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File .\scripts\installer-local.ps1 -Reinitialiser
    Supprime la base locale et repart de zéro (données de départ recréées).
#>
param(
    [switch]$SansTests,
    [switch]$SansLancer,
    [switch]$Reinitialiser
)

$ErrorActionPreference = 'Stop'
$racine = Split-Path -Parent $PSScriptRoot
$web = Join-Path $racine 'src\Hub.Web'
$solution = Join-Path $racine 'Hub.slnx'

function Etape([string]$texte) { Write-Host ''; Write-Host "==> $texte" -ForegroundColor Cyan }
function Ok([string]$texte) { Write-Host "    OK  $texte" -ForegroundColor Green }
function Attention([string]$texte) { Write-Host "    !!  $texte" -ForegroundColor Yellow }
function Existe([string]$commande) { return [bool](Get-Command $commande -ErrorAction SilentlyContinue) }
function RafraichirPath {
    $env:PATH = [Environment]::GetEnvironmentVariable('PATH', 'Machine') + ';' + [Environment]::GetEnvironmentVariable('PATH', 'User')
}

Write-Host 'Hub — installation de l''environnement local' -ForegroundColor White

# 1. SDK .NET 10 ------------------------------------------------------------
Etape 'SDK .NET 10'
$sdk10 = $null
if (Existe 'dotnet') { $sdk10 = & dotnet --list-sdks | Where-Object { $_ -match '^10\.' } | Select-Object -Last 1 }
if (-not $sdk10) {
    if (Existe 'winget') {
        Attention 'SDK .NET 10 absent : installation avec winget (quelques minutes)...'
        winget install --id Microsoft.DotNet.SDK.10 --exact --accept-source-agreements --accept-package-agreements
        RafraichirPath
        $sdk10 = & dotnet --list-sdks | Where-Object { $_ -match '^10\.' } | Select-Object -Last 1
    }
    if (-not $sdk10) {
        throw 'Installez le SDK .NET 10 depuis https://dotnet.microsoft.com/download/dotnet/10.0 puis relancez ce script.'
    }
}
Ok $sdk10

# 2. SQL Server LocalDB -----------------------------------------------------
Etape 'SQL Server LocalDB'
if (-not (Existe 'sqllocaldb')) {
    Attention 'LocalDB est introuvable.'
    Write-Host '    Deux façons de l''installer :'
    Write-Host '      - Visual Studio Installer > Modifier > charge de travail « Développement ASP.NET et web »'
    Write-Host '        (LocalDB est coché par défaut dans les composants) ;'
    Write-Host '      - ou SQL Server Express : https://www.microsoft.com/fr-ca/sql-server/sql-server-downloads'
    Write-Host '        > Express > « Télécharger le média » > LocalDB.'
    throw 'Installez LocalDB puis relancez ce script.'
}
$instances = & sqllocaldb info
if ($instances -notcontains 'MSSQLLocalDB') {
    & sqllocaldb create MSSQLLocalDB | Out-Null
}
& sqllocaldb start MSSQLLocalDB | Out-Null
Ok 'Instance (localdb)\MSSQLLocalDB démarrée'

if ($Reinitialiser) {
    Etape 'Réinitialisation de la base locale « Hub »'
    try {
        $cn = New-Object System.Data.SqlClient.SqlConnection 'Server=(localdb)\MSSQLLocalDB;Database=master;Integrated Security=true'
        $cn.Open()
        $cmd = $cn.CreateCommand()
        $cmd.CommandText = "IF DB_ID('Hub') IS NOT NULL BEGIN ALTER DATABASE [Hub] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [Hub]; END"
        [void]$cmd.ExecuteNonQuery()
        $cn.Close()
        Ok 'Base supprimée : elle sera recréée au démarrage'
    }
    catch {
        Attention "Suppression impossible ($($_.Exception.Message))."
        Attention 'Supprimez la base « Hub » dans l''Explorateur d''objets SQL Server de Visual Studio, puis relancez.'
    }
}

# 3. Certificat HTTPS de développement --------------------------------------
Etape 'Certificat HTTPS de développement'
& dotnet dev-certs https --check --trust *> $null
if ($LASTEXITCODE -ne 0) {
    Attention 'Windows va demander de faire confiance au certificat local : répondez Oui.'
    & dotnet dev-certs https --trust
}
Ok 'Certificat approuvé'

# 4. Compte administrateur --------------------------------------------------
Etape 'Compte administrateur du back-office'
$secrets = & dotnet user-secrets list --project $web 2>$null
if (-not ($secrets -match '^Hub:AdminEmail')) {
    $courriel = Read-Host '    Courriel de l''administrateur'
    do {
        $securise = Read-Host '    Mot de passe (12 caractères minimum, avec majuscule, minuscule et chiffre)' -AsSecureString
        $motDePasse = [Runtime.InteropServices.Marshal]::PtrToStringAuto([Runtime.InteropServices.Marshal]::SecureStringToBSTR($securise))
        $valide = $motDePasse.Length -ge 12 -and $motDePasse -cmatch '[A-Z]' -and $motDePasse -cmatch '[a-z]' -and $motDePasse -match '\d'
        if (-not $valide) { Attention 'Mot de passe trop faible, recommencez.' }
    } while (-not $valide)
    & dotnet user-secrets set 'Hub:AdminEmail' $courriel --project $web | Out-Null
    & dotnet user-secrets set 'Hub:AdminPassword' $motDePasse --project $web | Out-Null
    Ok "Administrateur : $courriel (créé au premier démarrage)"
}
else {
    $ligne = $secrets | Where-Object { $_ -match '^Hub:AdminEmail' } | Select-Object -First 1
    Ok "Déjà configuré ($ligne)"
}

# 5. Compilation et tests ---------------------------------------------------
Etape 'Compilation'
& dotnet build $solution -c Debug --nologo -v quiet
if ($LASTEXITCODE -ne 0) { throw 'La compilation a échoué : voir les erreurs ci-dessus.' }
Ok 'Solution compilée'

if (-not $SansTests) {
    Etape 'Tests'
    & dotnet test $solution -c Debug --no-build --nologo -v quiet
    if ($LASTEXITCODE -ne 0) { throw 'Des tests échouent : voir ci-dessus.' }
    Ok 'Tous les tests passent'
}

# 6. Lancement --------------------------------------------------------------
$courrielsLocaux = Join-Path $web 'App_Data\courriels'
Write-Host ''
Write-Host 'Environnement prêt.' -ForegroundColor Green
Write-Host '    Site public  : https://localhost:7180'
Write-Host '    Back-office  : https://localhost:7180/admin'
Write-Host "    Courriels    : $courrielsLocaux (fichiers .html, rien n'est envoyé)"
Write-Host '    Base         : (localdb)\MSSQLLocalDB, base « Hub »'

if (-not $SansLancer) {
    Write-Host ''
    Write-Host 'Démarrage du site (Ctrl+C pour arrêter)...' -ForegroundColor Cyan
    & dotnet run --project $web --launch-profile https --no-build
}
