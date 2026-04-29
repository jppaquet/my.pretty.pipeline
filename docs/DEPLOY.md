# Deploy

## One-time bootstrap (do this once per Azure subscription)

The OIDC managed identity has to exist *before* GitHub Actions can authenticate.
Bootstrap it manually with `az`, then everything else flows through CI.

```sh
# 1. Sign in.
az login
az account set --subscription <subscription-id>

# 2. Create the resource group.
RG=rg-notify-dev
az group create -n "$RG" -l canadacentral

# 3. Deploy *only* the OIDC module (skips everything else by passing --what-if first to confirm).
az deployment group create \
  -g "$RG" \
  --template-file infra/main.bicep \
  --parameters env=dev githubOwner=jppaquet githubRepo=my.pretty.pipeline \
  --query "properties.outputs"

# 4. Read the MI client id from the output above; set it as a *repo variable*
#    (NOT a secret — these are non-sensitive identifiers).
gh variable set AZURE_CLIENT_ID      --body "<clientId from output>"
gh variable set AZURE_TENANT_ID      --body "$(az account show --query tenantId -o tsv)"
gh variable set AZURE_SUBSCRIPTION_ID --body "$(az account show --query id -o tsv)"
gh variable set AZURE_RG_DEV         --body "$RG"
```

After this, every `git push` to `main` runs `cd-deploy.yml` which redeploys infra
idempotently and ships the Functions to the staging slot.

## TestFlight (Phase 3)

Set these as **secrets** (sensitive) in the repo:

| Name | What |
|---|---|
| `APP_STORE_CONNECT_API_KEY_ID` | App Store Connect API key id (10 chars) |
| `APP_STORE_CONNECT_API_ISSUER_ID` | Issuer id (UUID) |
| `APP_STORE_CONNECT_API_KEY_P8` | Full `.p8` file contents |

Then tag a release: `git tag v0.1.0 && git push --tags` → `cd-testflight.yml` archives + uploads.
