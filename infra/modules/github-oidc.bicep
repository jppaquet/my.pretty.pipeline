// User-Assigned Managed Identity + federated credentials for GitHub Actions OIDC.
//
// This is the *only* Phase 0 resource. Bootstrap once with `az` (the bootstrap
// script in DEPLOY.md) so the MI exists before any GitHub Actions deploy can
// authenticate. After that, this module is idempotent — re-running cd-deploy
// with no changes is a no-op.

@description('Azure region.')
param location string

@description('Lowercase name prefix for resources.')
param namePrefix string

@description('Environment name (dev | prod).')
param env string

@description('GitHub owner (org or user).')
param githubOwner string

@description('GitHub repository name.')
param githubRepo string

@description('Tags applied to all resources.')
param tags object

resource mi 'Microsoft.ManagedIdentity/userAssignedIdentities@2023-01-31' = {
  name: 'mi-${namePrefix}-${env}'
  location: location
  tags: tags
}

// Allow GitHub Actions on `main` to deploy. This is the *only* federated subject
// — there is intentionally no `:pull_request` FIC. PR-context jobs that mint a
// token for this MI would inherit Contributor + User Access Administrator on
// the RG (see role assignments below); since the repo is public, that
// previously meant any PR run could exfiltrate Cosmos data, KV secrets, or
// re-grant roles to attacker principals. PR validation is limited to
// `bicep build` (no Azure auth required) in ci-infra.yml.
resource fedMain 'Microsoft.ManagedIdentity/userAssignedIdentities/federatedIdentityCredentials@2023-01-31' = {
  parent: mi
  name: 'github-main'
  properties: {
    issuer: 'https://token.actions.githubusercontent.com'
    audiences: [ 'api://AzureADTokenExchange' ]
    subject: 'repo:${githubOwner}/${githubRepo}:ref:refs/heads/main'
  }
}

// Contributor on the current resource group (scoped, not subscription-wide).
@description('Built-in role definition: Contributor.')
var contributorRoleId = subscriptionResourceId('Microsoft.Authorization/roleDefinitions', 'b24988ac-6180-42a0-ab88-20f7382dd24c')

resource contributorAssignment 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(resourceGroup().id, mi.id, 'contributor')
  properties: {
    principalId: mi.properties.principalId
    principalType: 'ServicePrincipal'
    roleDefinitionId: contributorRoleId
  }
}

// User Access Administrator on the same RG. Contributor cannot
// `Microsoft.Authorization/roleAssignments/write`, so without this the MI
// can't apply role assignments declared inside main.bicep — e.g. the KV
// "Key Vault Secrets User" grant the Function App needs at runtime, or the
// idempotent re-apply of this module's own Contributor assignment.
@description('Built-in role definition: User Access Administrator.')
var userAccessAdminRoleId = subscriptionResourceId('Microsoft.Authorization/roleDefinitions', '18d7d88d-d35e-4fb5-a5c3-7773c20a72d9')

resource userAccessAdminAssignment 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(resourceGroup().id, mi.id, 'user-access-admin')
  properties: {
    principalId: mi.properties.principalId
    principalType: 'ServicePrincipal'
    roleDefinitionId: userAccessAdminRoleId
  }
}

output managedIdentityClientId string = mi.properties.clientId
output managedIdentityPrincipalId string = mi.properties.principalId
output managedIdentityResourceId string = mi.id
