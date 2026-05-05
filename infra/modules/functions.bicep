// Function App on Consumption Plan, .NET 10 isolated worker.
// Hosts every Notify.* Function project. Production traffic ↔ `production` slot;
// cd-deploy.yml swaps `staging` → `production` only after e2e is green.

@description('Azure region.')
param location string

@description('Lowercase name prefix.')
param namePrefix string

@description('Environment (dev | prod).')
param env string

@description('Tags.')
param tags object

@description('Cosmos DB account name (for app setting wiring).')
param cosmosAccountName string

@description('Key Vault name (for @Microsoft.KeyVault references).')
param keyVaultName string

var storageName = toLower('st${namePrefix}${env}${uniqueString(resourceGroup().id)}')
var planName = 'plan-${namePrefix}-${env}'
var appInsightsName = 'appi-${namePrefix}-${env}'
var workspaceName = 'log-${namePrefix}-${env}'
// Function App names are globally unique (DNS for *.azurewebsites.net). Salted.
var functionAppName = 'func-${namePrefix}-${env}-${take(uniqueString(resourceGroup().id), 6)}'

resource storage 'Microsoft.Storage/storageAccounts@2023-05-01' = {
  name: storageName
  location: location
  tags: tags
  sku: { name: 'Standard_LRS' }
  kind: 'StorageV2'
  properties: {
    minimumTlsVersion: 'TLS1_2'
    allowBlobPublicAccess: false
    supportsHttpsTrafficOnly: true
  }
}

resource plan 'Microsoft.Web/serverfarms@2023-12-01' = {
  name: planName
  location: location
  tags: tags
  sku: { name: 'Y1', tier: 'Dynamic' }
  kind: 'functionapp,linux'
  properties: { reserved: true }
}

resource workspace 'Microsoft.OperationalInsights/workspaces@2023-09-01' = {
  name: workspaceName
  location: location
  tags: tags
  properties: { sku: { name: 'PerGB2018' }, retentionInDays: 30 }
}

resource appInsights 'Microsoft.Insights/components@2020-02-02' = {
  name: appInsightsName
  location: location
  tags: tags
  kind: 'web'
  properties: {
    Application_Type: 'web'
    WorkspaceResourceId: workspace.id
  }
}

resource functionApp 'Microsoft.Web/sites@2023-12-01' = {
  name: functionAppName
  location: location
  tags: tags
  kind: 'functionapp,linux'
  identity: { type: 'SystemAssigned' }
  properties: {
    serverFarmId: plan.id
    httpsOnly: true
    siteConfig: {
      linuxFxVersion: 'DOTNET-ISOLATED|10'
      ftpsState: 'Disabled'
      minTlsVersion: '1.2'
      appSettings: [
        { name: 'AzureWebJobsStorage', value: 'DefaultEndpointsProtocol=https;AccountName=${storage.name};EndpointSuffix=${environment().suffixes.storage};AccountKey=${storage.listKeys().keys[0].value}' }
        { name: 'FUNCTIONS_EXTENSION_VERSION', value: '~4' }
        { name: 'FUNCTIONS_WORKER_RUNTIME', value: 'dotnet-isolated' }
        { name: 'APPLICATIONINSIGHTS_CONNECTION_STRING', value: appInsights.properties.ConnectionString }
        { name: 'COSMOS_ACCOUNT_NAME', value: cosmosAccountName }
        { name: 'KEY_VAULT_NAME', value: keyVaultName }
      ]
    }
  }
}

resource stagingSlot 'Microsoft.Web/sites/slots@2023-12-01' = {
  parent: functionApp
  name: 'staging'
  location: location
  tags: tags
  kind: 'functionapp,linux'
  identity: { type: 'SystemAssigned' }
  properties: {
    serverFarmId: plan.id
    httpsOnly: true
    siteConfig: {
      linuxFxVersion: 'DOTNET-ISOLATED|10'
      ftpsState: 'Disabled'
      minTlsVersion: '1.2'
      appSettings: [
        { name: 'AzureWebJobsStorage', value: 'DefaultEndpointsProtocol=https;AccountName=${storage.name};EndpointSuffix=${environment().suffixes.storage};AccountKey=${storage.listKeys().keys[0].value}' }
        { name: 'FUNCTIONS_EXTENSION_VERSION', value: '~4' }
        { name: 'FUNCTIONS_WORKER_RUNTIME', value: 'dotnet-isolated' }
        { name: 'APPLICATIONINSIGHTS_CONNECTION_STRING', value: appInsights.properties.ConnectionString }
        { name: 'COSMOS_ACCOUNT_NAME', value: cosmosAccountName }
        { name: 'KEY_VAULT_NAME', value: keyVaultName }
      ]
    }
  }
}

output functionAppName string = functionApp.name
output defaultHostname string = functionApp.properties.defaultHostName
output principalIdProduction string = functionApp.identity.principalId
output principalIdStaging string = stagingSlot.identity.principalId
