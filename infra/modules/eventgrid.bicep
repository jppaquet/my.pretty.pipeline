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

@description('Principal IDs to grant EventGrid Data Sender on this topic so they can publish CloudEvents via DefaultAzureCredential. Required for the Ingest function — without it, publish fails with Azure.RequestFailedException: The principal does not have permission to send data.')
param dataSenderPrincipalIds array = []

resource topic 'Microsoft.EventGrid/topics@2024-06-01-preview' = {
  name: 'egt-${namePrefix}-${env}'
  location: location
  tags: tags
  properties: {
    inputSchema: 'CloudEventSchemaV1_0'
    publicNetworkAccess: 'Enabled'
    // Force AAD-only publishing. The Ingest function uses
    // `new EventGridPublisherClient(uri, new DefaultAzureCredential())` and is
    // granted EventGrid Data Sender on this topic via `dataSenderPrincipalIds`
    // below. Topic SAS keys aren't used anywhere; disabling local auth removes
    // them as an attack surface (a Contributor-scoped pivot can't
    // `az eventgrid topic key list` and publish past the Ingest gate).
    disableLocalAuth: true
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

// EventGrid Data Sender (built-in role id d5a91429-5739-47e2-a06b-3470a27159e7) —
// scoped to the topic so the principal can only publish to this specific topic.
resource dataSender 'Microsoft.Authorization/roleAssignments@2022-04-01' = [for principalId in dataSenderPrincipalIds: {
  scope: topic
  name: guid(topic.id, principalId, 'd5a91429-5739-47e2-a06b-3470a27159e7')
  properties: {
    principalId: principalId
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', 'd5a91429-5739-47e2-a06b-3470a27159e7')
    principalType: 'ServicePrincipal'
  }
}]

output topicName string = topic.name
output topicEndpoint string = topic.properties.endpoint
