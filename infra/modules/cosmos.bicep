// Cosmos DB NoSQL — Free tier (one per Azure account; first deployment wins).
// Database `notify` with three containers sharing 1000 RU/s + 25 GB free allotment.

@description('Azure region.')
param location string

@description('Lowercase name prefix.')
param namePrefix string

@description('Environment (dev | prod).')
param env string

@description('Tags.')
param tags object

@description('Default TTL for notifications (seconds). 90 days.')
param notificationsTtlSeconds int = 60 * 60 * 24 * 90

@description('Database-level shared throughput (RU/s). Free tier covers 1000.')
@minValue(400)
@maxValue(1000)
param sharedThroughput int = 1000

@description('Principal IDs to grant Cosmos DB Built-in Data Contributor on this account (read + write across every container in `notify`). Used by the Function App runtime via DefaultAzureCredential — without this, CosmosClient calls 401 and the host returns 500.')
param dataContributorPrincipalIds array = []

// Cosmos account names are globally unique (DNS). Salted with a per-RG hash.
var accountName = 'cosmos-${namePrefix}-${env}-${take(uniqueString(resourceGroup().id), 6)}'

resource account 'Microsoft.DocumentDB/databaseAccounts@2024-05-15' = {
  name: accountName
  location: location
  tags: tags
  kind: 'GlobalDocumentDB'
  properties: {
    databaseAccountOfferType: 'Standard'
    enableFreeTier: true
    consistencyPolicy: { defaultConsistencyLevel: 'Session' }
    locations: [
      { locationName: location, failoverPriority: 0, isZoneRedundant: false }
    ]
    // Free tier requires *provisioned* throughput (not serverless) — no `capabilities` entry.
    backupPolicy: {
      type: 'Periodic'
      periodicModeProperties: {
        backupIntervalInMinutes: 240
        backupRetentionIntervalInHours: 8
        backupStorageRedundancy: 'Local'
      }
    }
    // Force AAD-only data-plane auth. The Function App already uses MI via
    // CosmosClient(endpoint, new DefaultAzureCredential()) and gets access through
    // `dataContributorPrincipalIds` below — keys aren't needed anywhere. Disabling
    // local auth removes the entire shared-key class of credentials so a
    // Contributor-scoped pivot can't `az cosmosdb keys list` and bypass RBAC.
    disableLocalAuth: true
  }
}

resource db 'Microsoft.DocumentDB/databaseAccounts/sqlDatabases@2024-05-15' = {
  parent: account
  name: 'notify'
  properties: {
    resource: { id: 'notify' }
    options: { throughput: sharedThroughput }
  }
}

resource notifications 'Microsoft.DocumentDB/databaseAccounts/sqlDatabases/containers@2024-05-15' = {
  parent: db
  name: 'notifications'
  properties: {
    resource: {
      id: 'notifications'
      partitionKey: { paths: [ '/source' ], kind: 'Hash' }
      defaultTtl: notificationsTtlSeconds
      indexingPolicy: {
        indexingMode: 'consistent'
        automatic: true
        includedPaths: [ { path: '/*' } ]
        excludedPaths: [ { path: '/metadata/*' } ]
      }
    }
  }
}

resource devices 'Microsoft.DocumentDB/databaseAccounts/sqlDatabases/containers@2024-05-15' = {
  parent: db
  name: 'devices'
  properties: {
    resource: {
      id: 'devices'
      partitionKey: { paths: [ '/deviceId' ], kind: 'Hash' }
    }
  }
}

resource projects 'Microsoft.DocumentDB/databaseAccounts/sqlDatabases/containers@2024-05-15' = {
  parent: db
  name: 'projects'
  properties: {
    resource: {
      id: 'projects'
      partitionKey: { paths: [ '/projectId' ], kind: 'Hash' }
    }
  }
}

resource dataContributor 'Microsoft.DocumentDB/databaseAccounts/sqlRoleAssignments@2024-05-15' = [for principalId in dataContributorPrincipalIds: {
  parent: account
  name: guid(account.id, principalId, '00000000-0000-0000-0000-000000000002')
  properties: {
    // Built-in role: Cosmos DB Built-in Data Contributor (data plane read + write).
    roleDefinitionId: '${account.id}/sqlRoleDefinitions/00000000-0000-0000-0000-000000000002'
    principalId: principalId
    scope: account.id
  }
}]

output accountName string = account.name
output endpoint string = account.properties.documentEndpoint
output databaseName string = db.name
