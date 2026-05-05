# my.pretty.pipeline

> A push-notification service for an audience of one.
> Because the cron job that finished at 3 a.m. deserves to make a sound, and the one that didn't deserves a louder one.

A single HTTP endpoint, a single event broker, a single inbox on every device I own. Anything that can `curl` becomes a producer; everything I missed shows up in a SwiftUI feed I can scroll through later.

```
                                          ┌──► PushDelivery ─► Notification Hubs ─► APNs ─► iOS app
producer ──► IngestionApi ──► Event Grid ─┤
                                          └──► Archive ───────► Cosmos DB ◄────────────── InboxApi
```

## What it's for

Concrete things in the queue (or already wired):

- The home backup that quietly stops working three Saturdays in a row
- A long build finishing while I'm in another room
- The smoke detector with a flat battery, the dishwasher with a clean cycle done
- GitHub webhooks I don't want to live in Slack
- Any one-line shell script that ends in `&&` and currently dies in a cron log

Producing is whatever speaks JSON over HTTPS:

```sh
curl -sf -H "x-api-key: $NOTIFY_KEY" -H "content-type: application/json" \
  https://notify.example.com/v1/notifications \
  -d '{"source":"home-pipeline","title":"Backup OK","body":"480 GB in 41 min","priority":"low"}'
```

There is no SDK to learn and there will not be one.

## Stack

| | |
|---|---|
| Backend | .NET 10 isolated-worker Azure Functions, Consumption Plan |
| Broker | Event Grid Custom Topic, CloudEvents 1.0 |
| Storage | Cosmos DB NoSQL — Free tier (1000 RU/s + 25 GB), 90-day TTL on the inbox |
| Push | Notification Hubs Free tier → APNs |
| Mobile | SwiftUI universal app, iOS 17+, bundle id `my.pretty.pipeline` |
| IaC | Bicep, region `canadacentral` |
| Auth | OIDC end-to-end — no long-lived Azure secrets, anywhere |

The whole thing is built to live inside the free tiers. Target monthly bill: under a coffee.

## Engineering hygiene, for a personal project

Everything that sits in the way of a push being noisy is gated:

- `scripts/pre-push` runs **gitleaks** + `dotnet build -warnaserror` + unit tests + `bicep build` + (when iOS files change) `swiftlint --strict` + `xcodebuild test`. CI minutes are free for public repos but my time isn't.
- Branch protection on `main`: required status checks, no force-push, no deletion, linear history.
- **Dependabot** runs daily — version updates grouped by vendor, plus automated security fixes for transitive vulnerabilities.
- Cloud CI (GitHub Actions) is the second line of defense: `ci-functions`, `ci-infra`, `ci-app`. OIDC into Azure for the deploy pipeline; no `AZURE_CREDENTIALS` JSON anywhere.

Documentation lives in `docs/` — the message contract (`SCHEMA.md`), the deployment runbook (`DEPLOY.md`), and how to mint API keys for new producers (`PROJECT-ONBOARDING.md`).

## Status

| Phase | What | State |
|---|---|---|
| 0 | Scaffold + CI/CD + dev RG provisioned | done |
| 1 | Shared contract → IngestionApi → Archive → Cosmos | done |
| 2 | Push delivery via Notification Hubs + iOS subscribe flow | next |
| 3 | TestFlight + signed builds | queued |
| 4 | Admin endpoint for project onboarding | queued |

## Local dev

```sh
bash scripts/install-hooks.sh   # one-time per clone — installs the pre-push gate
docker compose up -d            # Azurite + Cosmos emulator + Event Grid stub
cd src && dotnet test           # unit + integration
open app/Notify.xcodeproj       # iOS app — Xcode 16+
```

## Personal-use disclaimer

This is one person's home notification rig, kept public so the CI runs on free macOS/Linux minutes and so the OIDC story doesn't have to live in screenshots. No support, no roadmap promises, no contributions expected. MIT-licensed if anything turns out to be useful to you.
