# my.pretty.pipeline — context for Claude

Generic notification pipeline: producer → IngestionApi → Event Grid → {PushDelivery → NH → APNs → iOS, Archive → Cosmos ← InboxApi}. Architecture lives in `~/.claude/plans/that-the-begining-of-validated-lecun.md`.

## Stack
- Backend: .NET 10 isolated-worker Azure Functions on **Flex Consumption** (FC1). Linux Y1/Consumption can't run .NET 10 — locked to Flex per [MS docs](https://learn.microsoft.com/en-us/azure/azure-functions/dotnet-isolated-process-guide). Solution: `src/Notify.slnx` (not `.sln`).
- Mobile: SwiftUI universal (iPhone + iPad), iOS 17+, bundle id `my.pretty.pipeline`. Source of truth: `app/Notify.xcodeproj` (committed). **No xcodegen, no `project.yml`.**
- IaC: Bicep. Region `canadacentral`. CI/CD: GitHub Actions, OIDC.

## Layout
```
src/Notify.Shared/                shared contract, validation, hashing
src/Notify.Functions/             single Function App project — every trigger lives here
  Ingestion/                      Ingest HTTP function + handler + project lookup
  Archive/                        archive EG-trigger + Cosmos sink
  Devices/                        RegisterDevice HTTP function + NH installation
  Push/                           push EG-trigger + NH sender + tag/payload helpers
src/tests/Notify.*.Tests/         xUnit; Trait("Category","Integration") for integration
src/tests/Notify.E2E/             post-deploy black-box tests, Trait("Category","E2E")
infra/main.bicep                  cd-deploy template
infra/modules/                    one bicep per Azure resource
infra/modules/github-oidc.bicep   bootstrap-only — NOT in main.bicep
app/Notify.xcodeproj/             Xcode project (open directly)
docs/                             SCHEMA, DEPLOY, PROJECT-ONBOARDING
```

## Commands
- Tests: `cd src && dotnet test` (or `dotnet test --filter Category=Integration` once Phase 1 lands).
- Strict build: `dotnet build src/Notify.slnx -c Release -warnaserror`.
- Local emulators: `docker compose up -d` (Azurite + Cosmos emulator + EG stub).
- iOS: `open app/Notify.xcodeproj`.

## Conventions
- C#: file-scoped namespaces, `<Nullable>enable</Nullable>`, `TreatWarningsAsErrors` on `Notify.Shared`.
- Tests: xUnit. Integration tests use `[Trait("Category","Integration")]`.
- JSON: always go through `Notify.Shared.Json.NotifyJson.Options` (camelCase, lowercase enum, omit-null) so client/server/tests round-trip identically.
- Branches: feature branch → PR → squash-merge. No direct push to `main`. Branch protection is paywalled on private repos (GitHub Pro), so it's solo-discipline.

## Azure (dev)
- RG: `rg-notify-dev` in `canadacentral`. Subscription + tenant GUIDs are not committed; they live in GitHub Actions repo variables (`AZURE_SUBSCRIPTION_ID`, `AZURE_TENANT_ID`) for CI and in the maintainer's local memory for `az` work.
- MI: `mi-notify-dev`. Has `Contributor` + `User Access Administrator` on the RG.
- All globally-unique resource names are salted with `take(uniqueString(rg.id), 6)` (current suffix: `nrajdy`). **Never hardcode them in scripts** — look up via `az <kind> list -g $RG --query "[0].name" -o tsv`.
- GitHub repo vars (set, non-secret): `AZURE_CLIENT_ID`, `AZURE_TENANT_ID`, `AZURE_SUBSCRIPTION_ID`, `AZURE_RG_DEV`.
- `Microsoft.EventGrid` RP is registered (only one that needed manual `az provider register`).

## Deploy model
- **Bootstrap (manual, once per RG):** `az deployment group create --template-file infra/modules/github-oidc.bicep …`. Mints MI + federated credentials + role assignments. Re-run any time `rg-notify-dev` is wiped.
- **Day-2 (CI):** push to `main` → `cd-deploy.yml` runs over OIDC. Two-pass infra deploy: pass 1 with EG subs disabled, publish functions via `Azure/functions-action@v1`, pass 2 with EG subs enabled (subs reference function endpoints that don't exist until publish runs). E2E runs last against the deployed app. Flex doesn't support deployment slots — publish lands on production directly.
- **Don't run main.bicep manually** — keep cd-deploy as the only path. Don't `az functionapp deployment source config-zip` either; it routes through Kudu which 503s on Flex.

## Hard rules
- Don't reintroduce xcodegen / `project.yml`. Edit through Xcode UI.
- Don't put role assignments at RG scope inside `main.bicep` — they belong in `github-oidc.bicep` (bootstrap-only).
- Don't hardcode salted resource names in docs / scripts / Function code — look them up.
- Don't add `AZURE_CREDENTIALS` JSON or any long-lived Azure secret. The pipeline is OIDC-only by design.
- Don't switch the Function App back to Y1/Consumption — .NET 10 Linux only runs on FC1/Flex.
- Don't add a deployment-slot resource to `functions.bicep` — Flex Consumption rejects slot creation.
- Don't split `Notify.Functions` back into per-feature projects — one Function App = one zip = one project. The matrix that overwrote itself is gone for a reason (PR #46).

## Phase status
Phase 0/1/2 backend done end-to-end on dev RG. Live triggers: `Ingest`, `archive`, `push`, `RegisterDevice`. EG subs: `sub-archive`, `sub-push` both Succeeded. **Phase 2 manual gap:** APNs `.p8` upload (`az notification-hub credential apns update …`) — required for actual on-device delivery, not for the test loop. **Phase 3 next:** mobile app (SwiftUI), per `~/.claude/plans/that-the-begining-of-validated-lecun.md` line 341.
