// my.pipeline — root deployment. Wires every module that cd-deploy.yml is
// responsible for. Excludes `github-oidc.bicep`: that module is bootstrap-only
// (mints the MI, federated credentials, and the RG-scoped role assignments
// that cd-deploy itself relies on). Re-running it from cd-deploy hits role-
// assignment idempotency edge cases and breaks the pipeline.
//
// To recreate the MI: deploy `infra/modules/github-oidc.bicep` directly via
// `az` once per RG (see docs/DEPLOY.md / infra/bootstrap.sh).

targetScope = 'resourceGroup'

@description('Environment name — used to suffix resource names (e.g. dev, prod).')
@allowed([ 'dev', 'prod' ])
param env string = 'dev'

@description('Azure region. Defaults to the resource group location.')
param location string = resourceGroup().location

// EG endpoint validation requires the target Function to exist on the production
// slot before the subscription can be created. cd-deploy.yml deploys this template
// twice: pass 1 with both flags false (creates the Function App), then publishes
// + slot-swaps the Function code, then pass 2 with the matching flag true to
// create the EG subscription against an already-registered function endpoint.
@description('Phase 1+: provision the EG archive subscription. Set true only after Notify.Archive code is on the production slot.')
param enableArchiveSubscription bool = false

@description('Phase 2+: provision the EG push subscription. Set true only after Notify.PushDelivery code is on the production slot.')
param enablePushSubscription bool = false

var namePrefix = 'notify'
var tags = {
  project: 'my.pipeline'
  env: env
  managedBy: 'bicep'
}

// Existing managed identity (created by github-oidc.bicep during bootstrap).
// We need its principalId to grant Key Vault Secrets User. We only *reference*
// it — never re-create — so cd-deploy never tries to write to it.
resource mi 'Microsoft.ManagedIdentity/userAssignedIdentities@2023-01-31' existing = {
  name: 'mi-${namePrefix}-${env}'
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
      mi.properties.principalId
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
    enableArchiveSubscription: enableArchiveSubscription
    enablePushSubscription: enablePushSubscription
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
