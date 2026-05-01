// Key Vault — APNs .p8, NH connection string, API-key HMAC pepper.
// Phase 0 creates the vault; Phase 1+ populate secrets.

@description('Azure region.')
param location string

@description('Lowercase name prefix.')
param namePrefix string

@description('Environment (dev | prod).')
param env string

@description('Tags.')
param tags object

@description('Principal IDs that should receive Key Vault Secrets User access (read-only).')
param accessReaderPrincipalIds array = []

// KV names are globally unique (DNS); short generic names like `kv-notify-dev`
// collide with other Azure tenants. Salted with a per-RG hash.
var vaultName = 'kv-${namePrefix}-${env}-${take(uniqueString(resourceGroup().id), 6)}'

resource vault 'Microsoft.KeyVault/vaults@2023-07-01' = {
  name: vaultName
  location: location
  tags: tags
  properties: {
    sku: { family: 'A', name: 'standard' }
    tenantId: subscription().tenantId
    enableRbacAuthorization: true
    enableSoftDelete: true
    softDeleteRetentionInDays: 7
    publicNetworkAccess: 'Enabled'
  }
}

@description('Built-in role: Key Vault Secrets User.')
var secretsUserRoleId = subscriptionResourceId('Microsoft.Authorization/roleDefinitions', '4633458b-17de-408a-b874-0445c86b69e6')

resource secretsUserAssignments 'Microsoft.Authorization/roleAssignments@2022-04-01' = [for principalId in accessReaderPrincipalIds: {
  name: guid(vault.id, principalId, 'kv-secrets-user')
  scope: vault
  properties: {
    principalId: principalId
    principalType: 'ServicePrincipal'
    roleDefinitionId: secretsUserRoleId
  }
}]

output vaultName string = vault.name
output vaultUri string = vault.properties.vaultUri
