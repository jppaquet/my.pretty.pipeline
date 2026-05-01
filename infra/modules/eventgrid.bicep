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

@description('Function App name. Used to build webhook URLs once the subs are enabled.')
param functionAppName string

@description('Phase 1+: provision the archive subscription. Requires the `archive` function to exist in the Function App.')
param enableArchiveSubscription bool = false

@description('Phase 2+: provision the push subscription. Requires the `push` function to exist in the Function App.')
param enablePushSubscription bool = false

resource topic 'Microsoft.EventGrid/topics@2024-06-01-preview' = {
  name: 'egt-${namePrefix}-${env}'
  location: location
  tags: tags
  properties: {
    inputSchema: 'CloudEventSchemaV1_0'
    publicNetworkAccess: 'Enabled'
  }
}

// Subscriptions reference functions inside the Function App — they can't be
// declared before those functions exist, or ARM rejects with "InvalidRequest:
// This endpoint type is not supported as a destination with managed identities."
// Each subscription is gated on a Phase-* flag flipped to true once the
// matching function code is published.
resource subArchive 'Microsoft.EventGrid/topics/eventSubscriptions@2024-06-01-preview' = if (enableArchiveSubscription) {
  parent: topic
  name: 'sub-archive'
  properties: {
    destination: {
      endpointType: 'AzureFunction'
      properties: {
        resourceId: resourceId('Microsoft.Web/sites/functions', functionAppName, 'archive')
        maxEventsPerBatch: 1
        preferredBatchSizeInKilobytes: 64
      }
    }
    filter: { includedEventTypes: [ 'notify.created.v1' ] }
    eventDeliverySchema: 'CloudEventSchemaV1_0'
    retryPolicy: { maxDeliveryAttempts: 30, eventTimeToLiveInMinutes: 1440 }
  }
}

resource subPush 'Microsoft.EventGrid/topics/eventSubscriptions@2024-06-01-preview' = if (enablePushSubscription) {
  parent: topic
  name: 'sub-push'
  properties: {
    destination: {
      endpointType: 'AzureFunction'
      properties: {
        resourceId: resourceId('Microsoft.Web/sites/functions', functionAppName, 'push')
        maxEventsPerBatch: 1
        preferredBatchSizeInKilobytes: 64
      }
    }
    filter: { includedEventTypes: [ 'notify.created.v1' ] }
    eventDeliverySchema: 'CloudEventSchemaV1_0'
    retryPolicy: { maxDeliveryAttempts: 30, eventTimeToLiveInMinutes: 1440 }
  }
}

output topicName string = topic.name
output topicEndpoint string = topic.properties.endpoint
