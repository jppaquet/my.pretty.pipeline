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

## 8. Create the `session-signing-key` KV secret (one-shot)

The iOS app trades its short-lived Apple identity token (~10 min) at
`POST /v1/auth/session` for a Notify session JWT (30 days). That session
JWT is HS256-signed with `session-signing-key`. Same out-of-band pattern as
the pepper:

```sh
KV=$(az keyvault list -g "$RG" --query "[0].name" -o tsv)
SECRET=$(openssl rand -base64 48 | tr -d '\n')
az keyvault secret set --vault-name "$KV" --name session-signing-key --value "$SECRET" >/dev/null
echo "session signing key stored — rotating it logs every user out"
```

The issuer rejects keys shorter than 32 bytes; `openssl rand -base64 48`
yields ~64 chars / 384 bits, well above that floor.

### Optional: enable the admin plane (web app at `<swa>.azurestaticapps.net`)

The admin web app lives in [`admin/`](../admin/). It's a vanilla-JS SPA hosted on
Azure Static Web Apps; it signs you in with Entra ID + MFA and lets you
manage the producer-key list and the tester allowlist from a browser.
Skip this section if you only need the iOS-side flow.

**The bring-up is two-phase** because the SPA redirect URI has to match
the SWA's hostname, and that hostname doesn't exist until bicep provisions
the SWA. Order:

1. **First cd-deploy** (done in §5 above) — provisions the SWA, then the
   `publish-admin-swa` job _skips_ because `ADMIN_AAD_AUDIENCE` is unset.
   `/v1/admin/*` returns `503 admin plane not configured` if anyone asks.
2. **Entra bootstrap** (this section) — creates the app reg, mints the
   role + scope, hooks the SWA hostname in as a redirect URI, assigns you
   the Admin role, and sets `ADMIN_AAD_AUDIENCE`.
3. **Second cd-deploy** (`gh workflow run cd-deploy.yml`) — picks up
   `ADMIN_AAD_AUDIENCE`, sets the Function App's `Admin__*` env vars,
   renders `admin/config.template.js` → `config.js`, uploads `admin/` to
   the SWA, and adds the SWA origin to the Function App's CORS allow-list.

If the SWA from step 1 doesn't exist yet, run cd-deploy first, then come
back here.

```sh
# ── Phase 2: Entra bootstrap ────────────────────────────────────────

# 0. Sanity-check the SWA from phase 1 exists.
SWA_HOST=$(az staticwebapp list -g "$RG" --query "[0].defaultHostname" -o tsv)
test -n "$SWA_HOST" || { echo "no SWA yet — run cd-deploy.yml first"; exit 1; }
echo "SWA host: $SWA_HOST"

# 1. Create the app registration. Single-tenant; consumed by both the
#    SPA (client) and the Function App (resource) — same appId on both
#    sides, so requesting `.default` would fail (AADSTS90009).
ADMIN_APP=$(az ad app create --display-name "my.pretty.pipeline-admin" \
  --sign-in-audience AzureADMyOrg \
  --query appId -o tsv)
echo "appId: $ADMIN_APP"

# Brief wait — Graph propagation can lag the create.
sleep 5

OBJECT_ID=$(az ad app show --id "$ADMIN_APP" --query id -o tsv)
ROLE_ID=$(uuidgen | tr 'A-Z' 'a-z')
SCOPE_ID=$(uuidgen | tr 'A-Z' 'a-z')

# 2. Single Graph PATCH to set the identifier URI, mint the Admin app
#    role, expose the `access_as_user` API scope, and pin the SPA
#    redirect URI to the SWA hostname. Doing all four in one PATCH
#    avoids a sequence of `az ad app update --set "..."` calls that
#    each have finicky JSON-escaping behavior.
PATCH_BODY=$(jq -nc \
  --arg appUri "api://$ADMIN_APP" \
  --arg spa "https://$SWA_HOST" \
  --arg roleId "$ROLE_ID" \
  --arg scopeId "$SCOPE_ID" \
  '{
     identifierUris: [$appUri],
     appRoles: [{
       id: $roleId,
       displayName: "Admin",
       description: "Manage producer keys and approve testers",
       value: "Admin",
       isEnabled: true,
       allowedMemberTypes: ["User"]
     }],
     api: {
       requestedAccessTokenVersion: 2,
       oauth2PermissionScopes: [{
         id: $scopeId,
         value: "access_as_user",
         type: "User",
         isEnabled: true,
         adminConsentDescription: "Manage the admin plane",
         adminConsentDisplayName: "Manage the admin plane"
       }]
     },
     spa: { redirectUris: [$spa] }
   }')

az rest --method PATCH \
  --uri "https://graph.microsoft.com/v1.0/applications/$OBJECT_ID" \
  --headers "content-type=application/json" \
  --body "$PATCH_BODY" >/dev/null

# 3. Service principal so the role assignment in step 4 has a
#    resourceId to point at.
SP_ID=$(az ad sp create --id "$ADMIN_APP" --query id -o tsv 2>/dev/null \
        || az ad sp show --id "$ADMIN_APP" --query id -o tsv)

# 4. Assign yourself the Admin role on the new app's SP.
ME=$(az ad signed-in-user show --query id -o tsv)
az rest --method POST \
  --uri "https://graph.microsoft.com/v1.0/users/$ME/appRoleAssignments" \
  --headers "content-type=application/json" \
  --body "$(jq -nc --arg p "$ME" --arg r "$SP_ID" --arg ar "$ROLE_ID" \
           '{principalId:$p, resourceId:$r, appRoleId:$ar}')" >/dev/null

# 5. Wire the audience into cd-deploy via a repo variable. tenantId is
#    already in `AZURE_TENANT_ID` from §4.
gh variable set ADMIN_AAD_AUDIENCE --body "$ADMIN_APP"
echo "ADMIN_AAD_AUDIENCE = $ADMIN_APP"
```

```sh
# ── Phase 3: second cd-deploy uploads the SPA ───────────────────────
gh workflow run cd-deploy.yml --ref main
gh run watch
```

This run sees `vars.ADMIN_AAD_AUDIENCE != ''` and:
1. Sets `Admin__EntraTenantId` / `Admin__EntraAudience` on the Function App.
2. Adds `https://$SWA_HOST` to the Function App's CORS allowed-origins.
3. Renders `admin/config.template.js` → `config.js` (substituting your tenant
   + appId + Function-App hostname), then uploads the `admin/` folder via
   the SWA deploy action.

MFA is enforced via Entra **Security Defaults** (Free tier; on by default
for new tenants since 2019). Verify at portal → Entra ID → Properties →
"Security defaults" = Enabled. No P1 license needed.

```sh
# ── Phase 4: sign in ────────────────────────────────────────────────
echo "https://$SWA_HOST"   # open in browser
# Sign in → MFA → land on the testers tab.
```

You'll see three tabs:
- **testers** — list `allowedUsers` rows, approve/revoke (see "Approving
  testers" below).
- **projects** — mint a new producer (`npk_…`) key, list active +
  revoked, revoke. **Key is shown exactly once on mint** — store it before
  dismissing the dialog.
- **send test** — paste an `npk_` key and POST through the Ingest API to
  exercise the full pipeline (Ingest → EventGrid → Archive + Push → APNs
  → device).

### Smoke-test the admin API without the SPA

If you want to verify backend wiring before (or instead of) the SPA, mint
a Bearer token from the CLI and hit the endpoint directly. Note the route
prefix is **`/v1/admin/`**, not bare `/admin/` — the Functions host
reserves `/admin/*` for its own runtime management API.

```sh
TOKEN=$(az account get-access-token \
  --resource "api://$ADMIN_APP" --query accessToken -o tsv)
FN_HOST=$(az functionapp list -g "$RG" --query "[0].defaultHostName" -o tsv)
curl -s -H "Authorization: Bearer $TOKEN" \
  "https://$FN_HOST/v1/admin/allowlist" | jq .
```

You'll get `{"items":[…]}` if the token carries the Admin role and the
Function App has the admin plane configured. If your CLI session isn't
already scoped to this app, run `az login --scope api://$ADMIN_APP/.default`
first — the role claim only appears on tokens minted against this scope.

### Admin-plane troubleshooting

| Symptom | Cause | Fix |
|---|---|---|
| `503 admin plane not configured` on any `/v1/admin/*` | `Admin__EntraTenantId` or `Admin__EntraAudience` empty | Phase 3 hasn't run since you set `ADMIN_AAD_AUDIENCE` — `gh workflow run cd-deploy.yml --ref main` |
| `404` on `/v1/admin/*` (no JSON body) | You hit `/admin/...` instead of `/v1/admin/...` | Add the `/v1/` prefix — the host reserves bare `/admin/*` |
| `401 invalid bearer token` on `/v1/admin/*` | Token is valid Entra but doesn't carry `roles: ["Admin"]` | Re-run step 4 (`appRoleAssignments` POST). Verify in portal → Enterprise Apps → my.pretty.pipeline-admin → Users and groups |
| `AADSTS90009` in browser on sign-in | SPA requested `.default` scope (same-app trap) | Use the explicit scope; already done in `admin/app.js`. If you forked and customized, check the scope is `api://<appId>/access_as_user` |
| `Can't find variable: msalBrowser` in console | UMD global name is `msal`, not `msalBrowser` | already wired correctly in shipped code — confirm `admin/app.js` references `new msal.PublicClientApplication(...)` |
| Sign-in 200s but browser shows `404` on testers/projects tabs | SPA points at `/admin/...` instead of `/v1/admin/...` | Already fixed; if you forked, grep `admin/app.js` for `"/admin/"` and replace |
| `publish-admin-swa` job _skipped_ | `vars.ADMIN_AAD_AUDIENCE` not set | Re-run Phase 2 step 5 + `gh workflow run cd-deploy.yml` |
| SPA shows "config.js missing or unrendered" | cd-deploy uploaded the template literally without substituting; check the `Render runtime config` step in the publish-admin-swa job for the sed invocation |

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

curl -sf -H "x-api-key: $NOTIFY_KEY" -H "Content-Type: application/cloudevents+json" \
  "$NOTIFY_URL/v1/notifications" -d '{
    "specversion":"1.0","type":"notify.created.v1",
    "source":"my-cron","id":"'"$(uuidgen)"'",
    "data":{"title":"Hello","body":"first notification"}
  }'
```

Within a few seconds the notification lands in the iOS app (push if the
APNs key was uploaded in step 6; inbox-only if not).

The wire format is **CloudEvents 1.0**; structured, binary, and batch modes
are all accepted. See [SCHEMA.md](SCHEMA.md) for the full attribute table
and curl recipes for each mode.

## 11. Email ingestion (optional — Google Alerts / forwarding)

Lets producers that can only emit email (e.g. Google Alerts, Gmail
filter-forwards) land messages in the pipeline. The transport is a
Cloudflare Worker — inbound emails to `alerts@<your-domain>` are
classified, transformed to `notify.created.v1` CloudEvents, and POSTed
to `/v1/notifications` like any other producer.

Pre-reqs that go beyond the base setup:

- A custom domain on Cloudflare DNS (Cloudflare Registrar is the easiest
  path — same nameservers as the registrar, nothing to migrate).
- Three GitHub repo variables: `NOTIFY_DOMAIN`, `NOTIFY_EMAIL_DESTINATION`.
- Two GitHub repo secrets: `CLOUDFLARE_API_TOKEN`, `CLOUDFLARE_ACCOUNT_ID`.

### 11a. Buy the domain + mint a CF API token

1. Cloudflare dashboard → Domain Registration → Register Domains. Buy
   the name. Nameservers are auto-configured on CF.
2. Dashboard → My Profile → API Tokens → Create Custom Token. Permissions:
   - Account scope: **Email Routing Addresses:Edit**, **Workers Scripts:Edit**
   - Zone scope (your zone): **Zone:Read**, **DNS:Edit**, **Email Routing:Edit**
3. Copy the token + your Account ID (right panel of the zone dashboard).
4. Push them:
   ```sh
   gh secret set CLOUDFLARE_API_TOKEN
   gh secret set CLOUDFLARE_ACCOUNT_ID
   gh variable set NOTIFY_DOMAIN --body '<your-domain>'
   gh variable set NOTIFY_EMAIL_DESTINATION --body '<your-verified-destination>'
   ```

### 11b. Bootstrap order (one shot, then everything is idempotent)

The first deploy has a strict ordering because the Email Routing rule
references the Worker by name, the Worker needs the Function App URL,
and the Bicep custom-domain cert needs the DNS records live first.
After bootstrap, each workflow runs independently on its own paths.

1. **cd-deploy** (already wired in step 5) — re-run to provision the
   `tfstate` container in the storage account if you bootstrapped your
   RG before this section landed.
2. **cd-worker** — `gh workflow run cd-worker.yml`. Deploys the Worker
   to Cloudflare so the next step's routing rule has something to bind to.
3. **cd-cloudflare** — `gh workflow run cd-cloudflare.yml`. Applies the
   Terraform stack: DNS records, MX + SPF for Email Routing, the routing
   rule (alerts@<domain> → Worker), and registers your destination
   address. Cloudflare emails the destination a one-click verification
   link — click it before the next step.

   > **Manual one-time:** if the first apply fails with something like
   > "email routing is not enabled for this zone," click **Email →
   > Email Routing → Get Started** once in the CF dashboard. The
   > Terraform provider v5 marks the `enabled` toggle as read-only, so
   > the initial flip is dashboard-only. Re-run the workflow after.
4. **cd-deploy** — `gh workflow run cd-deploy.yml`. The `bind-custom-domain`
   job picks up `NOTIFY_DOMAIN` automatically and runs two idempotent
   `az` calls: one to add the hostname binding (validates ownership via
   the `asuid` TXT record cd-cloudflare just placed), one to issue +
   bind the App Service Managed Cert (HTTP-01 challenge, ~5 min). If
   DNS hasn't propagated yet, the job soft-skips with a warning — just
   re-run cd-deploy a minute later.

### 11c. Producer key + Google Alert

1. Register a producer project named `google-alerts` via the admin SPA
   or the admin API (see step 9). Mint a key.
2. Store the key as a Worker secret (the Worker code reads
   `env.INGEST_API_KEY`):
   ```sh
   cd workers/email-ingest
   npx wrangler secret put INGEST_API_KEY
   # Paste the npk_* value when prompted; it never appears in shell history.
   ```
3. Create the Google Alert at <https://www.google.com/alerts>. Set the
   "Deliver to" address to `alerts@<your-domain>`. Google sends a
   one-click verification email → the Worker detects it as a
   verification (subject prefix `Google Alerts:`) and forwards it to
   `NOTIFY_EMAIL_DESTINATION`. Click the link from your real inbox.
4. From then on, every Google Alerts data email (subject prefix
   `Google Alert -`) becomes a notification on your iOS device.

## Where things live

```
infra/                      → IaC (bicep + terraform)
  main.bicep                → cd-deploy entry point
  modules/github-oidc.bicep → bootstrap-only (step 3)
  cloudflare/               → Terraform — DNS + Email Routing (step 11)
src/                        → backend (.NET 10 Azure Functions)
app/                        → iOS app (Xcode project, source of truth)
workers/email-ingest/       → Cloudflare Worker — inbound email adapter (step 11)
.github/workflows/          → CI + cd-deploy + cd-cloudflare + cd-worker
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
