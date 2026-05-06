# Deploy

## One-time bootstrap (per Azure subscription)

The OIDC managed identity has to exist *before* GitHub Actions can authenticate.
Bootstrap it manually with `az`, then everything else flows through CI.

```sh
# 1. Sign in.
az login
az account set --subscription <subscription-id>

# 2. Register Microsoft.EventGrid (most RPs auto-register on first use; EG doesn't).
az provider register --namespace Microsoft.EventGrid --wait

# 3. Create the resource group.
RG=rg-notify-dev
az group create -n "$RG" -l canadacentral

# 4. Deploy ONLY the OIDC module — mints the UAMI + federated credentials +
#    Contributor + User Access Administrator role assignments.
#    main.bicep references this MI as `existing` and never tries to write to it.
az deployment group create \
  -g "$RG" \
  --template-file infra/modules/github-oidc.bicep \
  --parameters githubOwner=jppaquet githubRepo=my.pretty.pipeline env=dev namePrefix=notify

# 5. Set the four GitHub Actions repo variables (non-secret identifiers).
gh variable set AZURE_CLIENT_ID       --body "$(az identity show -g "$RG" -n mi-notify-dev --query clientId -o tsv)"
gh variable set AZURE_TENANT_ID       --body "$(az account show --query tenantId -o tsv)"
gh variable set AZURE_SUBSCRIPTION_ID --body "$(az account show --query id -o tsv)"
gh variable set AZURE_RG_DEV          --body "$RG"
```

Re-run steps 3-5 any time `rg-notify-dev` is wiped. Step 2 stays sticky on the
subscription.

## Day-2 deploys (CI)

Every `git push` to `main` runs `cd-deploy.yml`. It's a five-stage pipeline:

1. **deploy-infra (pass 1)** — `infra/main.bicep` with `enableArchiveSubscription=false`. Creates the Function App + storage + KV + Cosmos + EG topic. EG subscriptions are *not* created yet because ARM validates each subscription's destination function exists before allowing the deployment, and the function code only ships in step 2.
2. **publish-functions** — builds and deploys each `Notify.*` Function project via `Azure/functions-action@v1`. Skips projects that don't exist yet (Phase 0/1).
3. **enable-eventgrid-subs (pass 2)** — re-runs `main.bicep` with the matching EG-sub flags true. ARM now sees the function endpoints and the subscriptions provision cleanly. Skipped when no Phase-1+ project (`Notify.Archive`, `Notify.PushDelivery`) is in the tree.
4. **e2e** — runs `tests/Notify.E2E` against the deployed app.

Flex Consumption has **no deployment slots**, so there's no staging/production
swap. Publish lands directly on production. For solo/dev that's fine; if you
ever add real users, gate writes behind a feature flag and validate with e2e
before flipping the flag.

## Hard-won lessons

- **Linux Consumption (Y1) cannot run .NET 10.** The `linuxFxVersion` string is
  silently accepted but the platform image doesn't exist; the host returns 503
  on the main site, the Kudu site, and `syncfunctiontriggers`, with zero
  traces in App Insights. .NET 9 is the last version supported on Y1.
  Use `FC1` / Flex Consumption. See
  [Microsoft docs](https://learn.microsoft.com/en-us/azure/azure-functions/dotnet-isolated-process-guide).
- **Don't `az functionapp deployment source config-zip`.** That command routes
  through Kudu which is unreliable on Linux Consumption / Flex. It returns a
  bare `Bad Request` with no detail. Use `Azure/functions-action@v1` (or
  Run-From-Package via storage SAS) instead.
- **Don't run `infra/main.bicep` manually.** The two-pass EG-sub flow is in
  `cd-deploy.yml`; running the template once with the default flags will leave
  the subs unprovisioned, and running with the flags `true` from a clean RG
  will fail at EG endpoint validation. Either deploy through CI, or replicate
  the two-pass dance locally with `enableArchiveSubscription=false` first.
- **SKU changes between Dynamic and FlexConsumption are not supported.** ARM
  rejects the in-place update; you must delete the existing Function App and
  App Service Plan before redeploying with the new SKU.

## TestFlight (Phase 3)

Set these as **secrets** (sensitive) in the repo:

| Name | What |
|---|---|
| `APP_STORE_CONNECT_API_KEY_ID` | App Store Connect API key id (10 chars) |
| `APP_STORE_CONNECT_API_ISSUER_ID` | Issuer id (UUID) |
| `APP_STORE_CONNECT_API_KEY_P8` | Full `.p8` file contents |

Then tag a release: `git tag v0.1.0 && git push --tags` → `cd-testflight.yml`
archives + uploads.
