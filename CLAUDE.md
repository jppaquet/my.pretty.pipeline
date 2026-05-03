# my.pretty.pipeline — context for Claude

Generic notification pipeline: producer → IngestionApi → Event Grid → {PushDelivery → NH → APNs → iOS, Archive → Cosmos ← InboxApi}. Architecture lives in `~/.claude/plans/that-the-begining-of-validated-lecun.md`.

## Stack
- Backend: .NET 10 isolated-worker Azure Functions (Consumption). Solution: `src/Notify.slnx` (not `.sln`).
- Mobile: SwiftUI universal (iPhone + iPad), iOS 17+, bundle id `my.pretty.pipeline`. Source of truth: `app/Notify.xcodeproj` (committed). **No xcodegen, no `project.yml`.**
- IaC: Bicep. Region `canadacentral`. CI/CD: GitHub Actions, OIDC.

## Layout
```
src/Notify.Shared/         shared contract, validation, hashing
src/Notify.{Api,…}/        Function projects (one per endpoint, Phase 1+)
src/tests/Notify.*.Tests/  xUnit; Trait("Category","Integration") for integration
infra/main.bicep           cd-deploy template
infra/modules/             one bicep per Azure resource
infra/modules/github-oidc.bicep   bootstrap-only — NOT in main.bicep
app/Notify.xcodeproj/      Xcode project (open directly)
docs/                      SCHEMA, DEPLOY, PROJECT-ONBOARDING
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
- **`ci-app.yml` (iOS) is opt-in.** macos-15 minutes bill at 10×; the workflow only auto-runs when `app/**` or the workflow file itself changes. Force a run on any PR with "Run workflow" in the Actions tab (`workflow_dispatch`).

## Azure (dev)
- Subscription: `JPP-SUB` (`e9e3fab7-6c1d-4778-8196-b6ca90a7c438`). Tenant `0e1e585f-75cb-49a4-8a1d-c29068adf4eb`.
- RG: `rg-notify-dev`.
- MI: `mi-notify-dev`. Has `Contributor` + `User Access Administrator` on the RG.
- All globally-unique resource names are salted with `take(uniqueString(rg.id), 6)` (current suffix: `nrajdy`). **Never hardcode them in scripts** — look up via `az <kind> list -g $RG --query "[0].name" -o tsv`.
- GitHub repo vars (set, non-secret): `AZURE_CLIENT_ID`, `AZURE_TENANT_ID`, `AZURE_SUBSCRIPTION_ID`, `AZURE_RG_DEV`.
- `Microsoft.EventGrid` RP is registered (only one that needed manual `az provider register`).

## Deploy model
- **Bootstrap (manual, once per RG):** `az deployment group create --template-file infra/modules/github-oidc.bicep …`. Mints MI + federated credentials + role assignments. Re-run any time `rg-notify-dev` is wiped.
- **Day-2 (CI):** push to `main` → `cd-deploy.yml` runs `az deployment group create infra/main.bicep` over OIDC. Idempotent. **Don't run main.bicep manually** — keep cd-deploy as the only path.
- `cd-deploy.yml` triggers on `push: branches: [main]` directly; it does NOT gate on `ci-functions` / `ci-infra` (known gap, separate fix when prioritized).

## Hard rules
- Don't reintroduce xcodegen / `project.yml`. Edit through Xcode UI.
- Don't put role assignments at RG scope inside `main.bicep` — they belong in `github-oidc.bicep` (bootstrap-only).
- Don't hardcode salted resource names in docs / scripts / Function code — look them up.
- Don't add `AZURE_CREDENTIALS` JSON or any long-lived Azure secret. The pipeline is OIDC-only by design.

## Phase status
Phase 0 done (scaffold + CI/CD + dev RG provisioned). Phase 1 in progress: PR-1.A (`Notify.Shared` contract) is open as PR #8. Roadmap in `~/.claude/plans/what-s-next-keen-meadow.md`.
