// Infrastructure Azure de Hub (site ScintiNet) — région Canada Central.
//   App Service Linux B1 (.NET 10)  ·  Azure SQL Basic  ·  Stockage Blob pour les images
// Déploiement : scripts/azure-deployer.ps1 (ou az deployment group create).

targetScope = 'resourceGroup'

@description('Préfixe des noms de ressources (minuscules, sans espaces).')
@minLength(3)
@maxLength(12)
param prefix string = 'scintinet'

@description('Région Azure.')
param location string = 'canadacentral'

@description('Identifiant administrateur du serveur SQL.')
param sqlAdminLogin string = 'hubadmin'

@secure()
@description('Mot de passe administrateur du serveur SQL (12 caractères ou plus, majuscules, minuscules, chiffres).')
param sqlAdminPassword string

@description('Courriel du premier administrateur du back-office.')
param adminEmail string

@secure()
@description('Mot de passe du premier administrateur du back-office.')
param adminPassword string

@secure()
@description('Clé API Resend pour l\'envoi des courriels (vide = courriels journalisés seulement).')
param resendApiKey string = ''

@description('Adresse d\'expédition des courriels (domaine vérifié dans Resend).')
param fromEmail string = 'noreply@scintinet.com'

@description('Adresse principale du site : l\'autre variante y est redirigée (vide = aucune redirection).')
param canonicalHost string = 'www.scintinet.com'

@description('Domaines servis par le site, séparés par des virgules.')
param tenantDomains string = 'scintinet.com,www.scintinet.com'

var suffix = uniqueString(resourceGroup().id)
var webAppName = '${prefix}-web-${suffix}'
var sqlServerName = '${prefix}-sql-${suffix}'
var storageName = '${toLower(take(replace(prefix, '-', ''), 9))}st${suffix}'

resource plan 'Microsoft.Web/serverfarms@2023-12-01' = {
  name: '${prefix}-plan'
  location: location
  kind: 'linux'
  sku: {
    name: 'B1'
    tier: 'Basic'
  }
  properties: {
    reserved: true
  }
}

resource sqlServer 'Microsoft.Sql/servers@2021-11-01' = {
  name: sqlServerName
  location: location
  properties: {
    administratorLogin: sqlAdminLogin
    administratorLoginPassword: sqlAdminPassword
    minimalTlsVersion: '1.2'
    publicNetworkAccess: 'Enabled'
  }
}

// Autorise les services Azure (dont App Service) à joindre la base.
resource sqlAllowAzure 'Microsoft.Sql/servers/firewallRules@2021-11-01' = {
  parent: sqlServer
  name: 'AllowAllWindowsAzureIps'
  properties: {
    startIpAddress: '0.0.0.0'
    endIpAddress: '0.0.0.0'
  }
}

// Basic : prix fixe (environ 5 $ US/mois), 2 Go, toujours disponible.
resource database 'Microsoft.Sql/servers/databases@2021-11-01' = {
  parent: sqlServer
  name: 'hub'
  location: location
  sku: {
    name: 'Basic'
    tier: 'Basic'
  }
  properties: {
    maxSizeBytes: 2147483648
    requestedBackupStorageRedundancy: 'Local'
  }
}

resource storage 'Microsoft.Storage/storageAccounts@2023-05-01' = {
  name: storageName
  location: location
  kind: 'StorageV2'
  sku: {
    name: 'Standard_LRS'
  }
  properties: {
    minimumTlsVersion: 'TLS1_2'
    allowBlobPublicAccess: true
    supportsHttpsTrafficOnly: true
  }
}

resource blobService 'Microsoft.Storage/storageAccounts/blobServices@2023-05-01' = {
  parent: storage
  name: 'default'
}

// Images du site : lecture publique, écriture seulement par l'application.
resource mediaContainer 'Microsoft.Storage/storageAccounts/blobServices/containers@2023-05-01' = {
  parent: blobService
  name: 'media'
  properties: {
    publicAccess: 'Blob'
  }
}

var sqlConnection = 'Server=tcp:${sqlServer.properties.fullyQualifiedDomainName},1433;Initial Catalog=${database.name};User ID=${sqlAdminLogin};Password=${sqlAdminPassword};Encrypt=True;TrustServerCertificate=False;Connection Timeout=60;'
var storageConnection = 'DefaultEndpointsProtocol=https;AccountName=${storage.name};AccountKey=${storage.listKeys().keys[0].value};EndpointSuffix=${environment().suffixes.storage}'

resource webApp 'Microsoft.Web/sites@2023-12-01' = {
  name: webAppName
  location: location
  kind: 'app,linux'
  properties: {
    serverFarmId: plan.id
    httpsOnly: true
    siteConfig: {
      linuxFxVersion: 'DOTNETCORE|10.0'
      alwaysOn: true
      http20Enabled: true
      minTlsVersion: '1.2'
      ftpsState: 'Disabled'
      healthCheckPath: '/robots.txt'
      appSettings: [
        { name: 'ASPNETCORE_ENVIRONMENT', value: 'Production' }
        { name: 'Hub__DefaultTenantDomains', value: tenantDomains }
        { name: 'Hub__CanonicalHost', value: canonicalHost }
        { name: 'Hub__AdminEmail', value: adminEmail }
        { name: 'Hub__AdminPassword', value: adminPassword }
        { name: 'Resend__ApiKey', value: resendApiKey }
        { name: 'Resend__FromEmail', value: fromEmail }
        { name: 'Media__BlobConnectionString', value: storageConnection }
        { name: 'Media__Container', value: mediaContainer.name }
      ]
      connectionStrings: [
        { name: 'Hub', connectionString: sqlConnection, type: 'SQLAzure' }
      ]
    }
  }
}

output webAppName string = webApp.name
output siteUrl string = 'https://${webApp.properties.defaultHostName}'
output sqlServer string = sqlServer.properties.fullyQualifiedDomainName
output storageAccount string = storage.name

// Autorise le déploiement par profil de publication (GitHub Actions « Déployer sur Azure »).
resource scmPublishing 'Microsoft.Web/sites/basicPublishingCredentialsPolicies@2023-12-01' = {
  parent: webApp
  name: 'scm'
  properties: {
    allow: true
  }
}
