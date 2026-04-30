# my.pretty.pipeline

Generic notification pipeline for personal projects. One HTTP API, one event broker, one inbox on iPhone/iPad.

```
producer ──► IngestionApi ──► Event Grid ──┬──► PushDelivery ──► Notification Hubs ──► APNs ──► iOS app
                                            └──► Archive ──────► Cosmos DB ◄─── InboxApi ◄────┘
```

- **Backend:** .NET 10 isolated-worker Azure Functions, Consumption Plan
- **Broker:** Event Grid Custom Topic (CloudEvents 1.0)
- **Storage:** Cosmos DB NoSQL, Free tier (1000 RU/s + 25 GB)
- **Push:** Azure Notification Hubs Free tier → APNs
- **Mobile:** Native SwiftUI universal app (iPhone + iPad), iOS 17+
- **IaC:** Bicep
- **CI/CD:** GitHub Actions, OIDC auth to Azure (no long-lived secrets)
- **Region:** `canadacentral`
- **Bundle id:** `my.pretty.pipeline`

See `docs/` for the message contract, deployment guide, and onboarding new producing projects.
The architectural plan lives at `~/.claude/plans/that-the-begining-of-validated-lecun.md`.

## Quick start (local dev)

```sh
docker compose up -d                        # Azurite + Cosmos emulator + EG stub
cd src && dotnet test                       # unit + integration
open app/Notify.xcodeproj                   # iOS app (requires Xcode)
```

## Status

Phase 0 — scaffolding. No features yet.
