# my.pretty.pipeline

> A push-notification service for an audience of one.
> Because the cron job that finished at 3 a.m. deserves to make a sound, and the one that didn't deserves a louder one.

A single HTTP endpoint, a single event broker, a single inbox on every device I own.

```
                                          ┌──► PushDelivery ─► Notification Hubs ─► APNs ─► iOS app
producer ──► IngestionApi ──► Event Grid ─┤
                                          └──► Archive ───────► Cosmos DB ◄────────────── InboxApi
```

## Producing

Anything that speaks JSON over HTTPS:

```sh
curl -sf -H "x-api-key: $NOTIFY_KEY" -H "content-type: application/json" \
  https://notify.example.com/v1/notifications \
  -d '{"source":"home-pipeline","title":"Backup OK","body":"480 GB in 41 min"}'
```

No SDK, by design.

## Stack

| | |
|---|---|
| Backend | .NET 10 isolated-worker Azure Functions, Consumption Plan |
| Broker | Event Grid Custom Topic, CloudEvents 1.0 |
| Storage | Cosmos DB NoSQL, Free tier, 90-day TTL |
| Push | Notification Hubs Free tier → APNs |
| Mobile | SwiftUI universal app, iOS 17+ |
| IaC | Bicep, region `canadacentral` |

Fits inside free tiers. Target monthly bill: under a coffee.

## Status

Phase 1 done (ingestion + archive). Phase 2 next (push delivery + iOS subscribe).

## Local dev

```sh
bash scripts/install-hooks.sh   # one-time per clone
docker compose up -d            # Azurite + Cosmos emulator + EG stub
cd src && dotnet test
open app/Notify.xcodeproj
```

---

One person's home rig, public for free CI minutes. No support, no contributions expected. MIT.
