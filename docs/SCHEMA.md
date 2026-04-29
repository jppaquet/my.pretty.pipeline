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
