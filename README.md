# my.pretty.pipeline

> A push-notification service for an audience of one.
> Because the cron job that finished at 3 a.m. deserves to make a sound, and the one that didn't deserves a louder one.

A single HTTP endpoint, a single event broker, a single inbox on every device I own.

```
                                          ┌──► PushDelivery ─► Notification Hubs ─► APNs ─► iOS app
producer ──► IngestionApi ──► Event Grid ─┤
                                          └──► Archive ───────► Cosmos DB ◄────────────── InboxApi
```

## Auth model

Two independent boundaries:

- **Producers** authenticate to `/v1/notifications` with per-project API keys (`npk_…`, Argon2id-hashed). Producers are scripts/cron/CI.
- **iOS app** authenticates to `/v1/inbox` and `/v1/devices` with **Sign in with Apple** end-to-end. The IPA carries no backend credential; the user's Apple identity token is the only client auth header. Each user's inbox is partitioned by their Apple `sub`.

## Producing

Anything that speaks **CloudEvents 1.0** over HTTPS (structured, binary, or batch — see [SCHEMA.md](docs/SCHEMA.md)):

```sh
curl -sf -H "x-api-key: $NOTIFY_KEY" -H "Content-Type: application/cloudevents+json" \
  https://notify.example.com/v1/notifications -d '{
    "specversion":"1.0","type":"notify.created.v1",
    "source":"home-pipeline","id":"'"$(uuidgen)"'",
    "data":{"title":"Backup OK","body":"480 GB in 41 min"}
  }'
```

No SDK, by design — any CloudEvents emitter (Event Grid, Knative, custom) interoperates out of the box.

## Stack

| | |
|---|---|
| Backend | .NET 10 isolated-worker Azure Functions, Flex Consumption |
| Broker | Event Grid Custom Topic, CloudEvents 1.0 |
| Storage | Cosmos DB NoSQL, Free tier, 90-day TTL |
| Push | Notification Hubs Free tier → APNs |
| Mobile | SwiftUI universal app, iOS 17+ |
| IaC | Bicep, region `canadacentral` |

Fits inside free tiers. Target monthly bill: under a coffee.

## Status

Phases 0–3 done end-to-end on the dev RG: ingestion, archive, push fan-out, per-user inbox, and iOS Sign-in-with-Apple auth. Backend MI-only for Storage/Cosmos/EventGrid (shared keys disabled); KV-resolved secrets; OIDC federation locked to `:ref:refs/heads/main`.

## Local dev

```sh
bash scripts/install-hooks.sh                              # one-time per clone
cd src && dotnet test --filter Category!=Integration       # pure unit suite (~few s, no Docker)
docker compose up -d                                       # boots Cosmos emulator only
cd src && dotnet test --filter Category=Integration        # Archive + Inbox + Auth suites
open app/Notify.xcodeproj
```

Apple Silicon: the Cosmos image is amd64; Docker Desktop runs it under Rosetta. First boot is slow (60–120 s health-check); subsequent test runs are fast.

## Forking?

See [docs/FORK-SETUP.md](docs/FORK-SETUP.md) — a step-by-step walkthrough that takes you from `gh repo fork` to a working notification, including the Apple Developer + Azure + GitHub + iOS-signing pieces.

- [docs/DEPLOY.md](docs/DEPLOY.md) — day-2 ops
- [docs/SCHEMA.md](docs/SCHEMA.md) — producer + inbox API contract
- [docs/PROJECT-ONBOARDING.md](docs/PROJECT-ONBOARDING.md) — mint a producer key

---

One person's home rig, public for free CI minutes. No support, no contributions expected. MIT.
