# Fork setup — stand up your own copy from zero

This walks through every step needed to fork the repo, bring up the Azure
backend, sign the iOS app under your own Apple ID, and send your first
notification end-to-end. It's the union of:

- one-time external-account setup (Azure + Apple Developer)
- one-time per-fork bootstrap (resource group + GitHub repo vars + secrets)
- `cd-deploy.yml` taking over for day-2

Target time: ~90 minutes of clicking + waiting, ~30 minutes if you've done
similar Azure + Apple setups before. `cd-deploy.yml` runs unattended after
that. Day-2 ops live in [DEPLOY.md](DEPLOY.md).

Cost target: under USD 5/month at solo scale (Cosmos Free + NH Free +
Function App Flex's free grant + a few storage cents). The Apple Developer
account is USD 99/year and is the dominant cost.

## 0. Prerequisites

- macOS (you need Xcode for the iOS build)
- `az` CLI, `gh` CLI, `git` — both logged into the right accounts
- Azure subscription — free tier is fine; you'll burn no credit at solo scale
- Apple Developer Program membership (USD 99/year). Required for: APNs
  certificates, Sign in with Apple capability, TestFlight distribution.
  *No way around this — APNs needs a real Apple-issued team.*
- A GitHub account that owns your fork. The repo can be public (this one
  is) or private.

## 1. Apple Developer setup

This is the longest step because Apple's portal has the most clicking.

### 1a. Pick a bundle identifier
You will live with this. Apple's recommendation is reverse-DNS: e.g.
`com.yourname.notify` or `me.yourname.notify`. Anything that doesn't collide
with an existing App ID across Apple's whole platform works.

This bundle id appears in three places:

| Where | What |
|---|---|
| `app/Notify.xcodeproj` Debug + Release build configs | `PRODUCT_BUNDLE_IDENTIFIER` |
| `app/Notify/Services/KeychainStore.swift` | `KeychainStore(service:)` default |
| `infra/main.bicep` `appleAudience` param (default `my.pretty.pipeline`) | Backend audience claim |

You can `grep -rn "my.pretty.pipeline" app infra` to find every literal.

### 1b. Register the App ID
[Apple Developer → Identifiers → Add](https://developer.apple.com/account/resources/identifiers/list).
Pick **App IDs → App** → bundle id from 1a → enable these capabilities:

- **Push Notifications** (for APNs)
- **Sign in with Apple** (for the JWT auth we use on Inbox/RegisterDevice)

### 1c. Generate the APNs auth key (`.p8`)
[Keys → Add](https://developer.apple.com/account/resources/authkeys/list) →
enable **Apple Push Notifications service (APNs)** → download the `.p8`
**exactly once** (Apple won't let you re-download). Record:

- `APNS_KEY_ID` — 10-char id shown on the keys list
- `APNS_TEAM_ID` — your team id (top-right of the developer portal)
- `apns_auth_key.p8` — the file contents

### 1d. Generate the App Store Connect API key (TestFlight uploads)
[App Store Connect → Users and Access → Keys](https://appstoreconnect.apple.com/access/api)
→ Generate API Key → role **App Manager** → download the `.p8`. Record:

- `APP_STORE_CONNECT_API_KEY_ID` — 10 chars
- `APP_STORE_CONNECT_API_ISSUER_ID` — UUID at the top of the page
- `AuthKey_<id>.p8` — file contents

You only need this if you'll cut TestFlight builds via `cd-testflight.yml`.
Skip if you'll only sideload via Xcode in debug.

## 2. Fork + clone

```sh
gh repo fork jppaquet/my.pretty.pipeline --clone --remote
cd my.pretty.pipeline
```

If you're going to change the bundle id (step 1a), do it now and commit on
your fork's `main` so the bootstrap deploy reads the right value:

```sh
# Edit:
#   app/Notify.xcodeproj/project.pbxproj  (PRODUCT_BUNDLE_IDENTIFIER lines)
#   app/Notify/Services/KeychainStore.swift  (KeychainStore service:)
#   infra/main.bicep  (appleAudience param default)
git commit -am "fork: my bundle id"
git push
```

## 3. Azure bootstrap (one shot)

```sh
az login
az account set --subscription <your-subscription-id>

# Register Microsoft.EventGrid (only RP that doesn't auto-register).
az provider register --namespace Microsoft.EventGrid --wait

RG=rg-notify-dev                  # or whatever you want to call it
LOCATION=canadacentral            # or your nearest region with Flex
GH_OWNER=<your-github-handle>
GH_REPO=my.pretty.pipeline        # name of your fork

az group create -n "$RG" -l "$LOCATION"

# Mints the user-assigned MI, federated credentials, and RG-scoped role
# assignments cd-deploy needs. Re-run any time the RG is wiped.
az deployment group create \
  -g "$RG" \
  --template-file infra/modules/github-oidc.bicep \
  --parameters location="$LOCATION" namePrefix=notify env=dev \
               githubOwner="$GH_OWNER" githubRepo="$GH_REPO" tags='{}'
```

## 4. GitHub repo vars + secrets

Non-secret identifiers (repo **variables**):

```sh
gh variable set AZURE_CLIENT_ID       --body "$(az identity show -g "$RG" -n mi-notify-dev --query clientId -o tsv)"
gh variable set AZURE_TENANT_ID       --body "$(az account show --query tenantId -o tsv)"
gh variable set AZURE_SUBSCRIPTION_ID --body "$(az account show --query id -o tsv)"
gh variable set AZURE_RG_DEV          --body "$RG"
```

Sensitive material (repo **secrets**, only needed if you cut TestFlight
builds):

```sh
gh secret set APP_STORE_CONNECT_API_KEY_ID       --body "<from 1d>"
gh secret set APP_STORE_CONNECT_API_ISSUER_ID    --body "<from 1d>"
gh secret set APP_STORE_CONNECT_API_KEY_P8       < AuthKey_<id>.p8
```

There is intentionally **no** GitHub secret for backend credentials. The
iOS app no longer ships a function key; it authenticates with Sign in with
Apple end-to-end (see [SCHEMA.md](SCHEMA.md)).

## 5. First cd-deploy

```sh
# Make sure your fork's `main` has your bundle-id edits + the upstream code.
git push origin main
```

That triggers `cd-deploy.yml`. Two-pass bicep + Functions zip publish; takes
~10 minutes. Watch in real time:

```sh
gh run watch
```

**First-run quirk:** Azure RBAC role propagation against fresh resources
sometimes lags. If pass-1 fails with `AuthorizationFailed` on a Key Vault
write or storage operation, re-run the workflow — propagation completes in
<60 s and the second attempt goes through.

After cd-deploy succeeds you have a live Function App. Capture its hostname:

```sh
gh variable set NOTIFY_HOSTNAME --body "$(az functionapp list -g "$RG" --query '[0].defaultHostName' -o tsv)"
```

## 6. Upload the APNs `.p8` to Notification Hubs

Bicep doesn't accept the `.p8` contents directly through the management
API. The repo ships `.github/workflows/bootstrap-apple.yml` to wire it via
the NH data-plane (Atom over SAS):

```sh
gh secret set APNS_AUTH_KEY_P8 < /path/to/AuthKey_<APNS_KEY_ID>.p8

gh workflow run bootstrap-apple.yml \
  -f apns_key_id="<APNS_KEY_ID from 1c>" \
  -f apple_team_id="<APNS_TEAM_ID from 1c>" \
  -f bundle_id="<your bundle id>" \
  -f apns_environment=sandbox     # production for TestFlight/App Store
```

Idempotent — re-run on key rotation or environment flip.

Or via portal: NH namespace → Notification Hubs → `nh-notify-dev` → Apple
(APNS) → Authentication Mode = Token → paste Key ID / Team ID /
Application Mode = Sandbox / Bundle ID = your value / Token = the `.p8`
contents.

## 7. Create the `api-key-pepper` KV secret (one-shot)

Producer API keys (`npk_*`) are HMAC'd with a per-deploy pepper that lives
in Key Vault as `api-key-pepper`. Generate a fresh one:

```sh
KV=$(az keyvault list -g "$RG" --query "[0].name" -o tsv)
PEPPER=$(openssl rand -base64 64 | tr -d '\n')
az keyvault secret set --vault-name "$KV" --name api-key-pepper --value "$PEPPER" >/dev/null
echo "pepper stored — never log this value"
```

Function App resolves this via `@Microsoft.KeyVault(...)`; it'll pick up
the new secret on its next cold start (or restart it: `az functionapp
restart -g "$RG" -n <function-app-name>`).

### Optional: enable the admin plane

The `/admin/*` routes (used by the admin web app — see [admin/](../admin/))
default to **disabled** — `AdminAuthMiddleware` returns `503 admin plane
not configured` until you create an Entra app registration and feed the
two ids back to bicep. Skip this section if you only need the iOS path.

```sh
TENANT=$(az account show --query tenantId -o tsv)

# 1. Create the app registration. Single-tenant; SPA redirect for MSAL.js.
ADMIN_APP=$(az ad app create --display-name "my.pretty.pipeline-admin" \
  --sign-in-audience AzureADMyOrg \
  --query appId -o tsv)

# 2. Mint the Admin app role and patch it onto the registration. The role
#    `value` is what lands in the JWT `roles` claim — EntraJwtValidator
#    requires exactly "Admin" (configurable via Admin__RequiredRole).
ROLE_JSON=$(jq -nc --arg id "$(uuidgen)" '
  [{ id: $id, displayName: "Admin", description: "Manage producer keys and approve testers",
     value: "Admin", isEnabled: true, allowedMemberTypes: ["User"] }]')
az ad app update --id "$ADMIN_APP" --set "appRoles=$ROLE_JSON"

# 3. Expose an API scope so MSAL.js can request a Bearer token bound to
#    this app's audience. The scope id is arbitrary; `access_as_user` is
#    the conventional name. Identifier URI must be `api://<clientId>`.
SCOPE_ID=$(uuidgen)
IDENTIFIER_URI="api://$ADMIN_APP"
SCOPE_JSON=$(jq -nc --arg id "$SCOPE_ID" --arg uri "$IDENTIFIER_URI" '{
  requestedAccessTokenVersion: 2,
  oauth2PermissionScopes: [{
    id: $id, adminConsentDescription: "Manage the admin plane",
    adminConsentDisplayName: "Manage the admin plane",
    isEnabled: true, type: "User", value: "access_as_user"
  }]
}')
az ad app update --id "$ADMIN_APP" \
  --identifier-uris "$IDENTIFIER_URI" \
  --set "api=$SCOPE_JSON"

# 4. Once cd-deploy has provisioned the Static Web App, pull its hostname
#    and add it as the SPA redirect URI so MSAL.js can complete the login
#    redirect back to the SPA. Re-run this command if you ever recreate
#    the SWA (the hostname is salted with a per-RG hash).
SWA_HOST=$(az staticwebapp list -g "$RG" --query "[0].defaultHostname" -o tsv)
az ad app update --id "$ADMIN_APP" \
  --set "spa.redirectUris=['https://$SWA_HOST']"

# 5. Assign yourself the Admin role.
ME=$(az ad signed-in-user show --query id -o tsv)
SP_ID=$(az ad sp create --id "$ADMIN_APP" --query id -o tsv 2>/dev/null \
        || az ad sp show --id "$ADMIN_APP" --query id -o tsv)
APP_ROLE_ID=$(echo "$ROLE_JSON" | jq -r '.[0].id')
az rest --method POST \
  --uri "https://graph.microsoft.com/v1.0/users/$ME/appRoleAssignments" \
  --body "{\"principalId\":\"$ME\",\"resourceId\":\"$SP_ID\",\"appRoleId\":\"$APP_ROLE_ID\"}"

# 6. Wire the audience into cd-deploy via a repo variable. tenantId is
#    already in `AZURE_TENANT_ID`.
gh variable set ADMIN_AAD_AUDIENCE --body "$ADMIN_APP"
```

Next `cd-deploy` run picks up `ADMIN_AAD_AUDIENCE` + `AZURE_TENANT_ID` and:
1. Sets `Admin__EntraTenantId` / `Admin__EntraAudience` on the Function App.
2. Adds the SWA origin to the Function App's CORS allowed-origins.
3. Renders `admin/config.template.js` → `admin/config.js` with your tenant +
   audience + Function-App hostname, then uploads the `admin/` folder to
   the Static Web App.

MFA is enforced through Entra **Security Defaults** (Free tier; on by
default for new tenants since 2019). Verify in portal → Entra ID →
Properties → "Security defaults" = Enabled.

After cd-deploy completes, open the SWA hostname in a browser
(`echo "https://$SWA_HOST"`), click **Sign in**, do MFA, and you'll land
on the allowlist table. Approve / revoke buttons hit the Function App
directly via Bearer auth.

To smoke-test the backend alone (no SPA):
```sh
TOKEN=$(az account get-access-token \
  --resource "api://$ADMIN_APP" --query accessToken -o tsv)
FN_HOST=$(az functionapp list -g "$RG" --query "[0].defaultHostName" -o tsv)
curl -s -H "Authorization: Bearer $TOKEN" \
  "https://$FN_HOST/admin/allowlist" | jq .
```

You'll get `{"items":[…]}` once you can mint a token that carries the
Admin role (CLI tokens require [`az login --scope api://$ADMIN_APP/.default`](https://learn.microsoft.com/en-us/cli/azure/account#az-account-get-access-token)
or going through the SPA redirect flow).

## 8. Build + run the iOS app

Open `app/Notify.xcodeproj` in Xcode. Pick your signing team in the
Notify target's **Signing & Capabilities** tab. Build to a simulator or a
physical device.

On first launch you'll see the Sign-in-with-Apple sheet. Sign in. The app
caches the JWT in the Keychain, registers its APNs token with the backend
(creating a `DeviceDocument` keyed by your Apple `sub`), and shows the
empty inbox.

For TestFlight: tag a release (`git tag v0.1.0 && git push --tags`) →
`cd-testflight.yml` archives + uploads.

### Approving testers (the `allowedUsers` allowlist)

Every authenticated request gates on the `allowedUsers` Cosmos container.
Your first sign-in (and every tester's) self-registers a row with
`approved: false`; you flip it to `true` in Cosmos Data Explorer and the
user is in. There is no log-scraping, no Function App restart, no deploy.

Bootstrap your own access:

1. Sign in on the iOS app. The first request to `/v1/inbox` or
   `/v1/devices` returns `403 user awaiting approval` — that's expected.
2. Open the Azure Portal → your Cosmos account → **Data Explorer** →
   `notify` → `allowedUsers` → **Items**.
3. Open your row, flip `"approved": false` to `"approved": true`, stamp
   `"approvedAt"` with a UTC timestamp (optional, informational only),
   click **Update**.
4. Pull-to-refresh the iOS app. Inbox loads.

Approved results are cached in-memory for 60 seconds, so a revocation
(flipping back to `false`) can take up to a minute to bite. For instant
effect, restart the Function App (`az functionapp restart …`).

To make the allowlist completely permissive (any valid SiwA JWT
accepted, pre-allowlist behavior), set
`Auth__CosmosAllowedUsersContainer=` (empty string) on the Function App —
the DI binds `AlwaysApproveAllowlistRepository` and skips the Cosmos
read.

## 9. Onboard a producing project

Producers authenticate with per-project `npk_*` API keys, separate from the
iOS user identity. See [PROJECT-ONBOARDING.md](PROJECT-ONBOARDING.md) for
the mint-a-key recipe.

## 10. Send your first notification

```sh
NOTIFY_URL="https://$(gh variable get NOTIFY_HOSTNAME)"
NOTIFY_KEY="<the npk_… from step 9>"

curl -sf -H "x-api-key: $NOTIFY_KEY" -H "content-type: application/json" \
  "$NOTIFY_URL/v1/notifications" \
  -d '{"source":"my-cron","title":"Hello","body":"first notification"}'
```

Within a few seconds the notification lands in the iOS app (push if the
APNs key was uploaded in step 6; inbox-only if not).

## Where things live

```
infra/                      → IaC (bicep)
  main.bicep                → cd-deploy entry point
  modules/github-oidc.bicep → bootstrap-only (step 3)
src/                        → backend (.NET 10 Azure Functions)
app/                        → iOS app (Xcode project, source of truth)
.github/workflows/          → CI + cd-deploy
docs/
  FORK-SETUP.md             → this doc
  DEPLOY.md                 → day-2 ops
  SCHEMA.md                 → producer + inbox API contract
  PROJECT-ONBOARDING.md     → mint a producer key
```

## Troubleshooting

- **iOS sign-in fails immediately** — your App ID doesn't have Sign in
  with Apple capability enabled. Revisit step 1b.
- **Inbox returns 401 with a valid token** — `Auth__AppleAudience` on the
  Function App doesn't match your iOS bundle id. Check
  `az functionapp config appsettings list -g "$RG" -n <func> --query
  "[?name=='Auth__AppleAudience']"` and `appleAudience` on the bicep
  deploy.
- **Inbox returns 403 "user awaiting approval"** — your sub is in
  `allowedUsers` with `approved: false`. Open Cosmos Data Explorer → flip
  to `true`, then pull-to-refresh in the app. See the "Approving testers"
  section above.
- **Empty inbox after sending notifications** — Archive logs "zero
  registered users" when no `DeviceDocument` exists. Sign in on the iOS
  app first; that creates the device entry; then send the notification.
- **First cd-deploy fails on `AuthorizationFailed`** — RBAC propagation
  lag, see step 5.
- **`bicep` is missing locally** — `az bicep install` (the CLI ships with
  Azure CLI).
