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

@description('Sign-in-with-Apple audience claim. iOS bundle identifier of the client app. Forks: override this on the cd-deploy parameters line (`appleAudience=<your-bundle-id>`) to match the bundle id in your Xcode project.')
param appleAudience string = 'my.pretty.pipeline'

@description('Entra ID tenant GUID for the admin app. Empty = admin plane disabled (AdminAuthMiddleware returns 503 on /admin/*). Set this *and* `adminEntraAudience` after running the Entra bootstrap (FORK-SETUP.md). Forks usually use the same tenant their Azure subscription lives in; passes through to the functions module.')
param adminEntraTenantId string = ''

@description('Entra ID app registration `appId` (client id) for the admin app. Audience the admin JWT middleware enforces. Empty = admin plane disabled. Pair with `adminEntraTenantId`.')
param adminEntraAudience string = ''

var namePrefix = 'notify'
var tags = {
  project: 'my.pipeline'
  env: env
  managedBy: 'bicep'
}

// Event Grid topic endpoint follows a fixed Azure-Public-Cloud pattern:
// https://<topicName>.<region>-1.eventgrid.azure.net/api/events
// Computed here instead of referencing eventgrid.outputs.topicEndpoint so the
// functions module (which consumes this for IngestionOptions.EventGridTopicEndpoint)
// doesn't depend on eventgrid — avoids reordering the module graph and the
// chicken-egg with eventgrid needing functionAppName for its subscriptions.
var eventGridTopicEndpoint = 'https://egt-${namePrefix}-${env}.${location}-1.eventgrid.azure.net/api/events'

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
    // Grant the Function App runtime identity Data Contributor on the data
    // plane. Same MI as cd-deploy uses, attached to the function app via
    // functions.bicep — DefaultAzureCredential picks it up automatically.
    dataContributorPrincipalIds: [
      mi.properties.principalId
    ]
  }
}

// Key Vault — secrets store (APNs .p8, NH conn str, API-key HMAC pepper).
// The MI is both the runtime reader (`accessReaderPrincipalIds`) and the
// deploy-time writer (`secretsOfficerPrincipalIds`) so this template can
// declaratively write deploy-time-derived secrets (NH conn string) without a
// separate post-deploy `az keyvault secret set` step.
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
    secretsOfficerPrincipalIds: [
      mi.properties.principalId
    ]
    deploySecrets: {
      // NH connection string is derived from the hub at deploy time but the
      // value carries Manage rights. Keep it in KV so it isn't readable via
      // `Microsoft.Web/sites/config/list` against the Function App.
      'notification-hub-connection-string': notificationHub.outputs.hubConnectionString
    }
  }
}

var notificationHubConnectionStringSecretName = 'notification-hub-connection-string'

// Notification Hubs Free tier — namespace + hub. The connection string is
// consumed by the Function App as an app setting (DeviceApi for installation
// upserts; Phase-2+ PushDelivery for sends). APNs .p8 upload remains manual
// — Bicep doesn't accept the file contents directly.
module notificationHub 'modules/notificationhub.bicep' = {
  name: 'notification-hub-${env}'
  params: {
    location: location
    namePrefix: namePrefix
    env: env
    tags: tags
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
    cosmosAccountEndpoint: cosmos.outputs.endpoint
    keyVaultName: keyvault.outputs.vaultName
    eventGridTopicEndpoint: eventGridTopicEndpoint
    notificationHubConnectionStringSecretName: notificationHubConnectionStringSecretName
    notificationHubName: notificationHub.outputs.hubName
    userAssignedIdentityResourceId: mi.id
    userAssignedIdentityClientId: mi.properties.clientId
    userAssignedIdentityPrincipalId: mi.properties.principalId
    appleAudience: appleAudience
    adminEntraTenantId: adminEntraTenantId
    adminEntraAudience: adminEntraAudience
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
    // Grant the Function App runtime identity EventGrid Data Sender on this
    // topic. The Ingest function publishes via `new EventGridPublisherClient(
    // uri, new DefaultAzureCredential())`, which token-auths against the topic.
    dataSenderPrincipalIds: [
      mi.properties.principalId
    ]
  }
}

// ── outputs consumed by GitHub Actions cd-deploy.yml ────────────────
output functionAppName string = functions.outputs.functionAppName
output functionAppHostname string = functions.outputs.defaultHostname
