// Event Grid custom topic with two initial subscriptions (push, archive).
// Adding a new consumer = add a `subscription` resource here, no producer change.

@description('Azure region.')
param location string

@description('Lowercase name prefix.')
param namePrefix string

@description('Environment (dev | prod).')
param env string

@description('Tags.')
param tags object

@description('Function App name. Used to build webhook URLs for the subscriptions.')
param functionAppName string

resource topic 'Microsoft.EventGrid/topics@2024-06-01-preview' = {
  name: 'egt-${namePrefix}-${env}'
  location: location
  tags: tags
  properties: {
    inputSchema: 'CloudEventSchemaV1_0'
    publicNetworkAccess: 'Enabled'
  }
}

// Phase 1+ activates these by setting `endpointUrl` to a real Function URL.
// We declare them now so what-if shows the structure but they don't fire until
// the underlying Functions exist.
resource subArchive 'Microsoft.EventGrid/topics/eventSubscriptions@2024-06-01-preview' = {
  parent: topic
  name: 'sub-archive'
  properties: {
    deliveryWithResourceIdentity: {
      identity: { type: 'SystemAssigned' }
      destination: {
        endpointType: 'AzureFunction'
        properties: {
          resourceId: resourceId('Microsoft.Web/sites/functions', functionAppName, 'archive')
          maxEventsPerBatch: 1
          preferredBatchSizeInKilobytes: 64
        }
      }
    }
    filter: { includedEventTypes: [ 'notify.created.v1' ] }
    eventDeliverySchema: 'CloudEventSchemaV1_0'
    retryPolicy: { maxDeliveryAttempts: 30, eventTimeToLiveInMinutes: 1440 }
  }
}

resource subPush 'Microsoft.EventGrid/topics/eventSubscriptions@2024-06-01-preview' = {
  parent: topic
  name: 'sub-push'
  properties: {
    deliveryWithResourceIdentity: {
      identity: { type: 'SystemAssigned' }
      destination: {
        endpointType: 'AzureFunction'
        properties: {
          resourceId: resourceId('Microsoft.Web/sites/functions', functionAppName, 'push')
          maxEventsPerBatch: 1
          preferredBatchSizeInKilobytes: 64
        }
      }
    }
    filter: { includedEventTypes: [ 'notify.created.v1' ] }
    eventDeliverySchema: 'CloudEventSchemaV1_0'
    retryPolicy: { maxDeliveryAttempts: 30, eventTimeToLiveInMinutes: 1440 }
  }
}

output topicName string = topic.name
output topicEndpoint string = topic.properties.endpoint
