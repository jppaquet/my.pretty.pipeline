// Function App on Flex Consumption Plan, .NET 10 isolated worker.
// Hosts every Notify.* Function project. Linux Consumption (Y1) does NOT
// support .NET 10 — only Flex Consumption (FC1) does. Flex doesn't support
// deployment slots, so cd-deploy.yml publishes straight to production.
// Reference: https://learn.microsoft.com/en-us/azure/azure-functions/dotnet-isolated-process-guide

@description('Azure region.')
param location string

@description('Lowercase name prefix.')
param namePrefix string

@description('Environment (dev | prod).')
param env string

@description('Tags.')
param tags object

@description('Cosmos DB account endpoint URL (e.g. https://<acct>.documents.azure.com:443/). Bound into every *Options.CosmosAccountEndpoint at startup.')
param cosmosAccountEndpoint string

@description('Key Vault name (for @Microsoft.KeyVault references).')
param keyVaultName string

@description('Event Grid custom-topic endpoint URL. Bound into IngestionOptions.EventGridTopicEndpoint so the Ingest function publishes CloudEvents.')
param eventGridTopicEndpoint string

@description('Name of the Key Vault secret holding the Notification Hub Listen+Send connection string. Written by main.bicep through keyvault.bicep `deploySecrets`. The Function App resolves it via `@Microsoft.KeyVault(...)` reference at runtime so the bare conn string never appears in `Microsoft.Web/sites/config/list` output.')
param notificationHubConnectionStringSecretName string

@description('Notification Hub name (DeviceApi + PushDelivery).')
param notificationHubName string

@description('Sign-in-with-Apple audience claim. iOS bundle identifier of the client app (e.g. `my.pretty.pipeline`). The JWT middleware (Notify.Functions/Auth) rejects tokens whose `aud` does not match. Forks set this to their own bundle id.')
param appleAudience string

@description('Entra ID tenant GUID for the admin app. Empty = admin plane disabled (AdminAuthMiddleware returns 503 on /admin/*). Picks the JWKS endpoint + the issuer claim. Same tenant as Azure subscription unless the fork wants a separate identity store.')
param adminEntraTenantId string = ''

@description('Entra ID app registration `appId` (client id) for the admin app. The audience the JWT middleware enforces. Empty = admin plane disabled.')
param adminEntraAudience string = ''

@description('Origin of the admin Static Web App (e.g. `https://swa-notify-dev-xxxxx.centralus.azurestaticapps.net`). Added to the Function App siteConfig.cors.allowedOrigins so the admin SPA can call /admin/* from a different origin. Empty = no CORS rule added.')
param adminAllowedOrigin string = ''

@description('Resource ID of the user-assigned managed identity the Function App uses at runtime. The same identity gets Cosmos data-plane access in cosmos.bicep; DefaultAzureCredential picks it up automatically when it is the only MI attached.')
param userAssignedIdentityResourceId string

@description('clientId of the user-assigned managed identity. Exposed to the worker as AZURE_CLIENT_ID so DefaultAzureCredential.ManagedIdentityCredential mints a token for the right identity. Without it, IMDS returns 400 "Identity not found" because no system MI exists to fall back on.')
param userAssignedIdentityClientId string

@description('principalId of the user-assigned managed identity. Used here to grant Storage Blob Data Owner on the storage account so the runtime + cd-deploy can use MI auth (storage `allowSharedKeyAccess: false`).')
param userAssignedIdentityPrincipalId string

var storageName = toLower('st${namePrefix}${env}${uniqueString(resourceGroup().id)}')
var planName = 'plan-${namePrefix}-${env}'
var appInsightsName = 'appi-${namePrefix}-${env}'
var workspaceName = 'log-${namePrefix}-${env}'
// Function App names are globally unique (DNS for *.azurewebsites.net). Salted.
var functionAppName = 'func-${namePrefix}-${env}-${take(uniqueString(resourceGroup().id), 6)}'
// Flex Consumption requires a blob container that the platform pulls deployment
// packages from. Created on the same storage account; the Function App's
// SystemAssigned identity reads from it via AzureWebJobsStorage.
var deploymentContainerName = 'app-package'
// Terraform state container — used by `cd-cloudflare.yml` for the AzureRM
// backend. Shared storage account keeps the resource graph tight (one
// storage account per env). The MI already has Storage Blob Data Owner at
// the account scope (see below), so no extra role grant needed for CI.
var tfStateContainerName = 'tfstate'

resource storage 'Microsoft.Storage/storageAccounts@2023-05-01' = {
  name: storageName
  location: location
  tags: tags
  sku: { name: 'Standard_LRS' }
  kind: 'StorageV2'
  properties: {
    minimumTlsVersion: 'TLS1_2'
    allowBlobPublicAccess: false
    supportsHttpsTrafficOnly: true
    // Force AAD-only data-plane auth. AzureWebJobsStorage uses the MI form
    // below (see appSettings); the Flex deployment container reads via MI
    // (see `functionAppConfig.deployment.storage.authentication`). Storage
    // account keys aren't used anywhere; disabling shared-key access removes
    // the "Contributor → listKeys → overwrite app-package blob → arbitrary
    // code execution on cold start" pivot.
    allowSharedKeyAccess: false
  }

  resource blobServices 'blobServices' = {
    name: 'default'

    resource deploymentContainer 'containers' = {
      name: deploymentContainerName
      properties: { publicAccess: 'None' }
    }

    resource tfStateContainer 'containers' = {
      name: tfStateContainerName
      properties: { publicAccess: 'None' }
    }
  }
}

// Storage Blob Data Owner on the storage account so:
// - The Function App runtime can read/write blobs via AzureWebJobsStorage (MI form)
// - The deployment container can be written by cd-deploy's publish step (same MI)
// Data Owner > Data Contributor here because the deploy needs to create/delete
// containers, and the runtime emits state blobs the host expects to manage.
@description('Built-in role: Storage Blob Data Owner.')
var storageBlobDataOwnerRoleId = subscriptionResourceId('Microsoft.Authorization/roleDefinitions', 'b7e6dc6d-f1e8-4753-8033-0f276bb0955b')

resource storageBlobDataOwner 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  scope: storage
  name: guid(storage.id, userAssignedIdentityPrincipalId, 'storage-blob-data-owner')
  properties: {
    principalId: userAssignedIdentityPrincipalId
    principalType: 'ServicePrincipal'
    roleDefinitionId: storageBlobDataOwnerRoleId
  }
}

resource plan 'Microsoft.Web/serverfarms@2023-12-01' = {
  name: planName
  location: location
  tags: tags
  sku: { name: 'FC1', tier: 'FlexConsumption' }
  kind: 'functionapp,linux'
  properties: { reserved: true }
}

resource workspace 'Microsoft.OperationalInsights/workspaces@2023-09-01' = {
  name: workspaceName
  location: location
  tags: tags
  properties: { sku: { name: 'PerGB2018' }, retentionInDays: 30 }
}

resource appInsights 'Microsoft.Insights/components@2020-02-02' = {
  name: appInsightsName
  location: location
  tags: tags
  kind: 'web'
  properties: {
    Application_Type: 'web'
    WorkspaceResourceId: workspace.id
  }
}

resource functionApp 'Microsoft.Web/sites@2023-12-01' = {
  name: functionAppName
  location: location
  tags: tags
  kind: 'functionapp,linux'
  // UserAssigned-only so role assignments (Cosmos data plane in cosmos.bicep)
  // can be computed at deploy start — system-assigned principalIds are only
  // known post-creation, which causes BCP120 in role-assignment resources.
  // AzureWebJobsStorage uses the storage account key, not MI, so dropping
  // SystemAssigned doesn't affect the blob-deploy path.
  identity: {
    type: 'UserAssigned'
    userAssignedIdentities: {
      '${userAssignedIdentityResourceId}': {}
    }
  }
  properties: {
    serverFarmId: plan.id
    httpsOnly: true
    // KV references default to the System-assigned MI; we removed that, so
    // point KV-reference resolution at the same user-assigned MI used for
    // Cosmos / EG / Storage / IMDS auth. keyvault.bicep already granted it
    // Secrets User.
    keyVaultReferenceIdentity: userAssignedIdentityResourceId
    functionAppConfig: {
      deployment: {
        storage: {
          type: 'blobContainer'
          value: '${storage.properties.primaryEndpoints.blob}${deploymentContainerName}'
          // MI auth on the deployment container — `allowSharedKeyAccess:
          // false` on the storage account rejects the connection-string
          // path. The same UA-MI has Storage Blob Data Owner via the
          // role assignment above.
          authentication: {
            type: 'UserAssignedIdentity'
            userAssignedIdentityResourceId: userAssignedIdentityResourceId
          }
        }
      }
      runtime: {
        name: 'dotnet-isolated'
        version: '10.0'
      }
      scaleAndConcurrency: {
        maximumInstanceCount: 100
        instanceMemoryMB: 2048
      }
    }
    siteConfig: {
      ftpsState: 'Disabled'
      minTlsVersion: '1.2'
      // CORS allowed-origins. iOS app calls /v1/* same-origin via the
      // Function App hostname (no CORS needed). The admin SPA, however,
      // is served from a `*.azurestaticapps.net` host and calls /admin/*
      // cross-origin — without this entry, browsers reject the response
      // before AdminAuthMiddleware ever sees the request.
      cors: empty(adminAllowedOrigin) ? null : {
        allowedOrigins: [ adminAllowedOrigin ]
        supportCredentials: false
      }
      appSettings: [
        // AzureWebJobsStorage in MI form. Three settings instead of one
        // connection string — the host probes for `__accountName`, infers
        // the credential from `__credential`, and uses `__clientId` to
        // pick the right UA-MI (without it, IMDS returns 400 because no
        // system MI exists to fall back on). Storage shared-key auth is
        // disabled at the account level, so this is the only path that works.
        { name: 'AzureWebJobsStorage__accountName', value: storage.name }
        { name: 'AzureWebJobsStorage__credential',  value: 'managedidentity' }
        { name: 'AzureWebJobsStorage__clientId',    value: userAssignedIdentityClientId }
        { name: 'APPLICATIONINSIGHTS_CONNECTION_STRING', value: appInsights.properties.ConnectionString }
        { name: 'AZURE_CLIENT_ID', value: userAssignedIdentityClientId }
        { name: 'CosmosAccountEndpoint', value: cosmosAccountEndpoint }
        { name: 'EventGridTopicEndpoint', value: eventGridTopicEndpoint }
        // Per-deploy KV reference: out-of-band-managed pepper used by ApiKeyHasher
        // to verify project keys. The Function App's KV-ref resolver uses
        // mi-notify-dev (see keyVaultReferenceIdentity above) which has Secrets User.
        { name: 'ApiKeyPepperBase64', value: '@Microsoft.KeyVault(VaultName=${keyVaultName};SecretName=api-key-pepper)' }
        { name: 'KEY_VAULT_NAME', value: keyVaultName }
        // NH connection string via KV reference. The bare value used to live
        // here in plaintext, readable by anyone with `Microsoft.Web/sites/
        // config/list/action` (Reader+) on the Function App. main.bicep writes
        // the secret value into KV through keyvault.bicep `deploySecrets`;
        // the runtime resolves the reference via the user-assigned MI.
        { name: 'NotificationHubConnectionString', value: '@Microsoft.KeyVault(VaultName=${keyVaultName};SecretName=${notificationHubConnectionStringSecretName})' }
        { name: 'NotificationHubName', value: notificationHubName }
        // Sign-in-with-Apple audience. Double-underscore syntax binds to
        // AuthOptions.AppleAudience via configuration sectioning.
        { name: 'Auth__AppleAudience', value: appleAudience }
        // Session JWT signing key (HS256). KV-backed; same out-of-band model
        // as api-key-pepper — written once after bootstrap with
        //   az keyvault secret set --vault-name $KV --name session-signing-key \
        //     --value $(openssl rand -base64 48)
        // Rotating this value invalidates every active session (no per-session
        // revocation table); users sign back in with Apple. Treat the value as
        // ≥ 32 bytes of entropy — the issuer rejects shorter keys at startup.
        { name: 'Auth__SessionSigningKey', value: '@Microsoft.KeyVault(VaultName=${keyVaultName};SecretName=session-signing-key)' }
        // Cosmos pointers for the user allowlist. JwtAuthMiddleware does a
        // point-read against `allowedUsers/<sub>` on every authenticated
        // request to gate access; first sign-in self-registers a
        // `approved:false` row, which an admin flips in Cosmos Data Explorer
        // to enroll the tester. Leaving CosmosAllowedUsersContainer empty
        // binds AlwaysApproveAllowlistRepository (pre-allowlist behavior).
        { name: 'Auth__CosmosDatabase', value: 'notify' }
        { name: 'Auth__CosmosAllowedUsersContainer', value: 'allowedUsers' }
        // Admin plane — Entra ID validation for /admin/*. Empty values
        // (default) disable the admin plane: AdminAuthMiddleware returns 503
        // until the maintainer runs the Entra bootstrap and feeds the values
        // back via these params.
        { name: 'Admin__EntraTenantId', value: adminEntraTenantId }
        { name: 'Admin__EntraAudience', value: adminEntraAudience }
      ]
    }
  }
  dependsOn: [
    storage::blobServices::deploymentContainer
    storageBlobDataOwner
  ]
}

// Custom domain binding + managed cert are intentionally NOT in Bicep.
// The Microsoft.Web/sites/hostNameBindings + Microsoft.Web/certificates
// pair has a circular dependency (binding needs cert thumbprint; cert
// needs a live binding for the HTTP-01 challenge) which can only be
// broken with a two-pass deploy. cd-deploy.yml handles it with two
// idempotent `az` calls — see the `bind-custom-domain` job there.
// `customDomainVerificationId` below is exposed so cd-cloudflare can
// stamp the `asuid.<host>` TXT record that Azure validates against.

output functionAppName string = functionApp.name
output defaultHostname string = functionApp.properties.defaultHostName
output customDomainVerificationId string = functionApp.properties.customDomainVerificationId
output storageAccountName string = storage.name
output tfStateContainerName string = tfStateContainerName
