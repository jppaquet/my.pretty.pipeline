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
  "tags": ["pi-01", "backup"],        // optional, ≤10, each ≤64 chars, [A-Za-z0-9._-]; "global" reserved
  "deeplink": "https://...",          // optional, scheme ∈ {https, notify}, ≤2048 chars
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
list and pull-to-refresh. The inbox is user-scoped: the response only
contains notifications archived for the authenticated user's `sub`.

## Headers

| Header | Required | Notes |
|---|---|---|
| `Authorization: Bearer <jwt>` | yes | Sign-in-with-Apple identity token. Validated server-side against Apple's JWKS and the configured `Auth__AppleAudience` (the iOS bundle id). The `sub` claim becomes the inbox partition. |

`x-functions-key` was retired in PR-C — the IPA no longer carries a
backend credential.

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
      "id": "…:<userId>",         // baseId + ':' + Apple sub (per-user fan-out)
      "source": "home-pipeline",
      "userId": "001234.abcdef",  // Apple sub of the recipient
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
- `401` — missing or invalid `Authorization: Bearer …` (JwtAuthMiddleware
  rejects tokens with bad signature, wrong issuer/audience, or expired).

# Device registration API (`POST /v1/devices`)

iOS app registers its APNs token + the authenticated user binding. Same
`Authorization: Bearer …` requirement as `/v1/inbox`. Tags are
**server-derived** from the JWT `sub` — the client's `tags` field is
ignored so a token holder can't subscribe their device to another user's
audience.

```jsonc
{
  "deviceToken": "<64 hex chars APNs token>",
  "platform": "apns"
}
```

Response: `202 Accepted` with `{ "installationId": "<sha256(deviceToken)>" }`.
