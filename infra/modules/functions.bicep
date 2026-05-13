// Function App on Flex Consumption Plan, .NET 10 isolated worker.
// Hosts every Notify.* Function project. Linux Consumption (Y1) does NOT
// support .NET 10 — only Flex Consumption (FC1) does. Flex doesn't support
// deployment slots, so cd-deploy.yml publishes straight to production.
// Reference: https://learn.microsoft.com/en-us/azure/azure-functions/dotnet-isolated-process-guide

@description('Azure region.')
param location string

@description('Lowercase name prefix.')
param namePrefix string

@description('Environment (dev | prod).')
param env string

@description('Tags.')
param tags object

@description('Cosmos DB account endpoint URL (e.g. https://<acct>.documents.azure.com:443/). Bound into every *Options.CosmosAccountEndpoint at startup.')
param cosmosAccountEndpoint string

@description('Key Vault name (for @Microsoft.KeyVault references).')
param keyVaultName string

@description('Notification Hub full SAS connection string (DeviceApi + PushDelivery).')
@secure()
param notificationHubConnectionString string

@description('Notification Hub name (DeviceApi + PushDelivery).')
param notificationHubName string

@description('Resource ID of the user-assigned managed identity the Function App uses at runtime. The same identity gets Cosmos data-plane access in cosmos.bicep; DefaultAzureCredential picks it up automatically when it is the only MI attached.')
param userAssignedIdentityResourceId string

@description('clientId of the user-assigned managed identity. Exposed to the worker as AZURE_CLIENT_ID so DefaultAzureCredential.ManagedIdentityCredential mints a token for the right identity. Without it, IMDS returns 400 "Identity not found" because no system MI exists to fall back on.')
param userAssignedIdentityClientId string

var storageName = toLower('st${namePrefix}${env}${uniqueString(resourceGroup().id)}')
var planName = 'plan-${namePrefix}-${env}'
var appInsightsName = 'appi-${namePrefix}-${env}'
var workspaceName = 'log-${namePrefix}-${env}'
// Function App names are globally unique (DNS for *.azurewebsites.net). Salted.
var functionAppName = 'func-${namePrefix}-${env}-${take(uniqueString(resourceGroup().id), 6)}'
// Flex Consumption requires a blob container that the platform pulls deployment
// packages from. Created on the same storage account; the Function App's
// SystemAssigned identity reads from it via AzureWebJobsStorage.
var deploymentContainerName = 'app-package'

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

  resource blobServices 'blobServices' = {
    name: 'default'

    resource deploymentContainer 'containers' = {
      name: deploymentContainerName
      properties: { publicAccess: 'None' }
    }
  }
}

resource plan 'Microsoft.Web/serverfarms@2023-12-01' = {
  name: planName
  location: location
  tags: tags
  sku: { name: 'FC1', tier: 'FlexConsumption' }
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
  // UserAssigned-only so role assignments (Cosmos data plane in cosmos.bicep)
  // can be computed at deploy start — system-assigned principalIds are only
  // known post-creation, which causes BCP120 in role-assignment resources.
  // AzureWebJobsStorage uses the storage account key, not MI, so dropping
  // SystemAssigned doesn't affect the blob-deploy path.
  identity: {
    type: 'UserAssigned'
    userAssignedIdentities: {
      '${userAssignedIdentityResourceId}': {}
    }
  }
  properties: {
    serverFarmId: plan.id
    httpsOnly: true
    functionAppConfig: {
      deployment: {
        storage: {
          type: 'blobContainer'
          value: '${storage.properties.primaryEndpoints.blob}${deploymentContainerName}'
          authentication: {
            type: 'StorageAccountConnectionString'
            storageAccountConnectionStringName: 'AzureWebJobsStorage'
          }
        }
      }
      runtime: {
        name: 'dotnet-isolated'
        version: '10.0'
      }
      scaleAndConcurrency: {
        maximumInstanceCount: 100
        instanceMemoryMB: 2048
      }
    }
    siteConfig: {
      ftpsState: 'Disabled'
      minTlsVersion: '1.2'
      appSettings: [
        { name: 'AzureWebJobsStorage', value: 'DefaultEndpointsProtocol=https;AccountName=${storage.name};EndpointSuffix=${environment().suffixes.storage};AccountKey=${storage.listKeys().keys[0].value}' }
        { name: 'APPLICATIONINSIGHTS_CONNECTION_STRING', value: appInsights.properties.ConnectionString }
        { name: 'AZURE_CLIENT_ID', value: userAssignedIdentityClientId }
        { name: 'CosmosAccountEndpoint', value: cosmosAccountEndpoint }
        { name: 'KEY_VAULT_NAME', value: keyVaultName }
        // DeviceApi (Notify.DeviceApi/DeviceApiOptions.cs) reads these via
        // ConfigureFunctionsWorkerDefaults binding. PushDelivery will consume
        // the same pair when it lands.
        { name: 'NotificationHubConnectionString', value: notificationHubConnectionString }
        { name: 'NotificationHubName', value: notificationHubName }
      ]
    }
  }
  dependsOn: [
    storage::blobServices::deploymentContainer
  ]
}

output functionAppName string = functionApp.name
output defaultHostname string = functionApp.properties.defaultHostName
