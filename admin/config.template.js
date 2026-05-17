// Runtime config. cd-deploy.yml renders this template into `config.js`
// before uploading to the Static Web App, substituting the three tokens
// below with values from repo variables. The bundle itself stays generic
// in the repo, fork-specific only at deploy time.
//
// Token  | Source repo var       | Origin
// -------|-----------------------|------------------------------------
// TENANT | vars.AZURE_TENANT_ID  | bootstrap (FORK-SETUP.md §3)
// CLIENT | vars.ADMIN_AAD_AUDIENCE | bootstrap (FORK-SETUP.md §7)
// API    | function app hostname | cd-deploy bicep output
window.__ADMIN_CONFIG__ = {
  tenantId: "__TENANT_ID__",
  clientId: "__CLIENT_ID__",
  apiBase:  "__API_BASE__",
};
