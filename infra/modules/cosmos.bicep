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

resource account 'Microsoft.DocumentDB/databaseAccounts@2024-05-15' = {
  name: 'cosmos-${namePrefix}-${env}'
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

output accountName string = account.name
output endpoint string = account.properties.documentEndpoint
output databaseName string = db.name
