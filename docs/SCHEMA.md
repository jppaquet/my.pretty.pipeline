# Notification message schema

Every producing project sends the same shape to `POST /v1/notifications`. Phase 1+
will own the canonical schema as an `Notify.Shared` C# record; this doc is for
non-.NET producers (cron scripts, shell hooks, Python, JS, etc.).

## Headers

| Header | Required | Notes |
|---|---|---|
| `x-api-key` | yes | per-project key, prefix `npk_` |
| `content-type` | yes | `application/json` |

## Body

```jsonc
{
  "source": "home-pipeline",          // required, project id (must match the API key's project)
  "type": "alert",                    // info | warning | alert | <custom>
  "title": "Backup failed",           // required, ≤120 chars
  "body": "rsync exited 12 on host pi-01",  // required, ≤2000 chars
  "priority": "high",                 // low | normal | high (default: normal)
  "tags": ["pi-01", "backup"],        // optional
  "deeplink": "https://...",          // optional
  "metadata": { "host": "pi-01" },    // optional, free-form, ≤4 KB serialized
  "deduplicationKey": "backup-2026-04-28",  // optional; same key within TTL = idempotent
  "timestamp": "2026-04-28T14:00:00Z" // optional, server fills if missing
}
```

## Response

`202 Accepted` with `{ "id": "<uuid-v7>" }`. The server discards the request body's
`source` and overrides it with the project locked to the API key.

## curl recipe

```sh
curl -sf -H "x-api-key: $NOTIFY_KEY" -H "content-type: application/json" \
  "$NOTIFY_URL/v1/notifications" \
  -d '{"source":"my-cron","title":"Done","body":"weekly cleanup ok","priority":"low"}'
```

---

# Inbox read API (`GET /v1/inbox`)

Reads the archived notification history. Used by the iOS app for the inbox
list and pull-to-refresh; a future web client will use the same endpoint.
Authenticated with the Function App's per-function key (not a project
`npk_*` key) because the inbox is user-owned, not project-scoped.

## Headers

| Header | Required | Notes |
|---|---|---|
| `x-functions-key` | one of these two | Function App per-function key |
| `?code=<key>` (query) | one of these two | same key, in the URL |

## Query parameters

| Param | Type | Default | Notes |
|---|---|---|---|
| `source` | string | none | If supplied, scopes the query to one Cosmos partition (cheap point query). Omit for cross-partition. Max 64 chars. |
| `limit` | int | 50 | Page size; max 200. |
| `continuationToken` | string | none | Opaque token returned by the previous page; pass it back to fetch the next page. |

## Response — `200 OK`

```jsonc
{
  "items": [
    {
      "id": "…",                  // dedup-derived hash or envelope id
      "source": "home-pipeline",
      "title": "Backup failed",
      "body": "rsync exited 12 on host pi-01",
      "type": "alert",
      "priority": "high",
      "tags": ["pi-01"],
      "deeplink": null,
      "metadata": null,
      "deduplicationKey": "backup-2026-04-28",
      "timestamp": "2026-04-28T14:00:00Z",
      "envelopeId": "…"
    }
    // … newest first
  ],
  "continuationToken": "…or null when no more pages"
}
```

Same `NotificationDocument` shape that Archive writes — see
`src/Notify.Shared/Cosmos/NotificationDocument.cs`.

## Errors

- `400` — `limit` outside `[1, 200]` or `source` over 64 chars. Response:
  `{ "errors": [ { "field": "limit", "message": "…" } ] }`.
- `401` — missing/invalid function key (Functions runtime).

## curl recipe

```sh
KEY=$(az functionapp keys list -g rg-notify-dev -n func-notify-<suffix> \
  --query functionKeys.default -o tsv)
curl -sf "$NOTIFY_URL/v1/inbox?source=home-pipeline&limit=20&code=$KEY" | jq
```
