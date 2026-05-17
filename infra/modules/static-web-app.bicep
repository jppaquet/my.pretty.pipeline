// Azure Static Web App that hosts the admin SPA (`admin/`). Free tier:
// global CDN, no managed Functions (we BYO Function App and call it cross-
// origin), 100 GB bandwidth/mo, 0.5 GB max app size. No custom domain
// supported on Free without an Azure subscription credit; the auto-issued
// `<name>-<hash>.<region>.azurestaticapps.net` URL is fine for a personal
// admin app.
//
// Region pinning: SWA is global but resource lives in a specific region.
// `centralus` is currently the only Canada-adjacent location that lets you
// create Free SWA — `canadacentral` rejects Free tier. The proxy is still
// global; latency from Canada is negligible.

@description('Lowercase name prefix.')
param namePrefix string

@description('Environment (dev | prod).')
param env string

@description('Tags.')
param tags object

@description('Resource location. SWA Free supports a fixed set; centralus works from Canada.')
param location string = 'centralus'

// SWA names are globally unique (DNS). Salted with a per-RG hash like the
// other globally-unique resources.
var siteName = 'swa-${namePrefix}-${env}-${take(uniqueString(resourceGroup().id), 6)}'

resource site 'Microsoft.Web/staticSites@2023-12-01' = {
  name: siteName
  location: location
  tags: tags
  sku: { name: 'Free', tier: 'Free' }
  properties: {
    // Manual deploy mode — cd-deploy.yml fetches the deploy token via
    // `az staticwebapp secrets list` and uploads the `admin/` contents
    // with azure/static-web-apps-deploy@v1. NOT integrated with the GitHub
    // repo (no repositoryUrl), so SWA doesn't try to clone + build on its
    // own, which is what we want — the build is trivial (static files
    // only, vanilla JS).
    allowConfigFileUpdates: true
    stagingEnvironmentPolicy: 'Disabled'
  }
}

output name string = site.name
output hostname string = site.properties.defaultHostname
output origin string = 'https://${site.properties.defaultHostname}'
