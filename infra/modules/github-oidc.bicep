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

// Allow GitHub Actions on `main` to deploy.
resource fedMain 'Microsoft.ManagedIdentity/userAssignedIdentities/federatedIdentityCredentials@2023-01-31' = {
  parent: mi
  name: 'github-main'
  properties: {
    issuer: 'https://token.actions.githubusercontent.com'
    audiences: [ 'api://AzureADTokenExchange' ]
    subject: 'repo:${githubOwner}/${githubRepo}:ref:refs/heads/main'
  }
}

// Allow GitHub Actions in PR context to run `bicep what-if` (read-only against the RG).
// Azure rejects concurrent FIC writes under the same MI ("ConcurrentFederatedIdentityCredentialsWritesForSingleManagedIdentity"),
// so serialize via dependsOn on the previous FIC.
resource fedPullRequest 'Microsoft.ManagedIdentity/userAssignedIdentities/federatedIdentityCredentials@2023-01-31' = {
  parent: mi
  name: 'github-pull-request'
  dependsOn: [ fedMain ]
  properties: {
    issuer: 'https://token.actions.githubusercontent.com'
    audiences: [ 'api://AzureADTokenExchange' ]
    subject: 'repo:${githubOwner}/${githubRepo}:pull_request'
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

output managedIdentityClientId string = mi.properties.clientId
output managedIdentityPrincipalId string = mi.properties.principalId
output managedIdentityResourceId string = mi.id
