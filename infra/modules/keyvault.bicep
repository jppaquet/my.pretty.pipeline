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

@description('Principal IDs that should receive Key Vault Secrets User access (read-only). Runtime identities go here.')
param accessReaderPrincipalIds array = []

@description('Principal IDs that should receive Key Vault Secrets Officer access (read + write + delete). cd-deploy goes here so this template can write deploy-time-derived secrets (e.g. resource connection strings) declaratively rather than as a post-deploy `az keyvault secret set` step.')
param secretsOfficerPrincipalIds array = []

@description('Deploy-time secrets to write into the vault. Object keys are secret names, values are the secret values. Use for derived secrets (resource connection strings). Out-of-band-managed secrets (APNs `.p8`, `api-key-pepper`) live outside this template — write them once via `az keyvault secret set` after bootstrap.')
@secure()
param deploySecrets object = {}

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

@description('Built-in role: Key Vault Secrets Officer.')
var secretsOfficerRoleId = subscriptionResourceId('Microsoft.Authorization/roleDefinitions', 'b86a8fe4-44ce-4948-aee5-eccb2c155cd7')

resource secretsOfficerAssignments 'Microsoft.Authorization/roleAssignments@2022-04-01' = [for principalId in secretsOfficerPrincipalIds: {
  name: guid(vault.id, principalId, 'kv-secrets-officer')
  scope: vault
  properties: {
    principalId: principalId
    principalType: 'ServicePrincipal'
    roleDefinitionId: secretsOfficerRoleId
  }
}]

// Deploy-time secrets. RBAC role propagation for the Secrets Officer grant
// above can lag a few seconds; on a fresh RG bootstrap the first deploy may
// race the role-propagation. Re-running cd-deploy in that case resolves it.
resource managedSecrets 'Microsoft.KeyVault/vaults/secrets@2023-07-01' = [for secret in items(deploySecrets): {
  parent: vault
  name: secret.key
  properties: { value: secret.value }
  dependsOn: [ secretsOfficerAssignments ]
}]

output vaultName string = vault.name
output vaultUri string = vault.properties.vaultUri
