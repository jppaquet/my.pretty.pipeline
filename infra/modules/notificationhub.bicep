// Azure Notification Hubs — Free tier (500 devices, 1M pushes/mo).
// Activated in Phase 2; APNs credential uploaded out-of-band via az CLI / portal
// because Bicep doesn't accept the .p8 contents directly without additional plumbing.

@description('Azure region.')
param location string

@description('Lowercase name prefix.')
param namePrefix string

@description('Environment (dev | prod).')
param env string

@description('Tags.')
param tags object

// NH namespace names are globally unique (DNS). Salted.
var namespaceName = 'nhns-${namePrefix}-${env}-${take(uniqueString(resourceGroup().id), 6)}'

resource namespace 'Microsoft.NotificationHubs/namespaces@2023-09-01' = {
  name: namespaceName
  location: location
  tags: tags
  sku: { name: 'Free' }
  properties: { namespaceType: 'NotificationHub' }
}

resource hub 'Microsoft.NotificationHubs/namespaces/notificationHubs@2023-09-01' = {
  parent: namespace
  name: 'nh-${namePrefix}-${env}'
  location: location
  tags: tags
  properties: {}
}

// Default authorization rule with Manage permissions — created automatically
// alongside every NH. We listKeys against it to surface the connection string
// to consumers (DeviceApi for CreateOrUpdateInstallation, PushDelivery for
// SendNotification). Treat the output as sensitive — only flow it to other
// modules' app settings, never log it.
resource defaultRule 'Microsoft.NotificationHubs/namespaces/notificationHubs/AuthorizationRules@2023-09-01' existing = {
  parent: hub
  name: 'DefaultFullSharedAccessSignature'
}

output namespaceName string = namespace.name
output hubName string = hub.name
@secure()
output hubConnectionString string = defaultRule.listKeys().primaryConnectionString
