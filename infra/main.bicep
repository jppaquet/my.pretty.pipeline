// my.pipeline — root deployment. Wires every module.
//
// Phase 0 ships only the OIDC managed identity (so GitHub Actions can deploy
// future phases without long-lived secrets). Subsequent phases enable each
// `module` block by replacing its `// PHASE-N:` guard with a real instantiation.

targetScope = 'resourceGroup'

@description('Environment name — used to suffix resource names (e.g. dev, prod).')
@allowed([ 'dev', 'prod' ])
param env string = 'dev'

@description('Azure region. Defaults to the resource group location.')
param location string = resourceGroup().location

@description('GitHub org/user that owns the repo (used to scope the OIDC federated credential).')
param githubOwner string = 'jppaquet'

@description('GitHub repository name.')
param githubRepo string = 'my.pretty.pipeline'

var namePrefix = 'notify'
var tags = {
  project: 'my.pipeline'
  env: env
  managedBy: 'bicep'
}

// ── PHASE 0 ─────────────────────────────────────────────────────────
// User-Assigned Managed Identity for GitHub Actions OIDC.
module githubOidc 'modules/github-oidc.bicep' = {
  name: 'github-oidc-${env}'
  params: {
    location: location
    namePrefix: namePrefix
    env: env
    githubOwner: githubOwner
    githubRepo: githubRepo
    tags: tags
  }
}

// ── PHASE 1 ─────────────────────────────────────────────────────────
// Cosmos DB Free tier (one per Azure account; first deployment wins).
module cosmos 'modules/cosmos.bicep' = {
  name: 'cosmos-${env}'
  params: {
    location: location
    namePrefix: namePrefix
    env: env
    tags: tags
  }
}

// Key Vault — secrets store (APNs .p8, NH conn str, API-key HMAC pepper).
module keyvault 'modules/keyvault.bicep' = {
  name: 'keyvault-${env}'
  params: {
    location: location
    namePrefix: namePrefix
    env: env
    tags: tags
    accessReaderPrincipalIds: [
      githubOidc.outputs.managedIdentityPrincipalId
    ]
  }
}

// Function App + Storage + App Insights (all five Functions live here).
module functions 'modules/functions.bicep' = {
  name: 'functions-${env}'
  params: {
    location: location
    namePrefix: namePrefix
    env: env
    tags: tags
    cosmosAccountName: cosmos.outputs.accountName
    keyVaultName: keyvault.outputs.vaultName
  }
}

// Event Grid custom topic + initial subscriptions (push, archive).
module eventgrid 'modules/eventgrid.bicep' = {
  name: 'eventgrid-${env}'
  params: {
    location: location
    namePrefix: namePrefix
    env: env
    tags: tags
    functionAppName: functions.outputs.functionAppName
  }
}

// ── PHASE 2 ─────────────────────────────────────────────────────────
// Notification Hubs Free tier — namespace + hub.
module notificationHub 'modules/notificationhub.bicep' = {
  name: 'notification-hub-${env}'
  params: {
    location: location
    namePrefix: namePrefix
    env: env
    tags: tags
  }
}

// ── outputs consumed by GitHub Actions cd-deploy.yml ────────────────
output functionAppName string = functions.outputs.functionAppName
output functionAppHostname string = functions.outputs.defaultHostname
