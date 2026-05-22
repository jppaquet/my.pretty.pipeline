# Ingestion API (`POST /v1/notifications`)

The pipeline accepts **CloudEvents 1.0** (CNCF spec) over HTTP, exclusively. The
legacy `application/json` body the IngestionApi used to accept was retired in
favor of an interoperable envelope so any producer that already speaks CE —
Azure Event Grid, Knative, observability tools, Kafka bridges, custom emitters —
can publish without a Notify-specific client.

Three modes are accepted (HTTP binding §3 in the CE spec):

| Mode | `Content-Type` | Body |
|---|---|---|
| Structured single | `application/cloudevents+json` | one event JSON envelope |
| Batch | `application/cloudevents-batch+json` | JSON array of envelopes |
| Binary single | `application/json` (or omitted) | event `data` only; envelope attrs in `ce-*` headers |

Anything else returns `415 Unsupported Media Type`.

## Auth

`x-api-key: npk_…` — per-project key (Argon2id-hashed server-side). All events in
a batch must share the same `source` attribute, which the server uses to look up
the project record before verifying the key.

## Required CloudEvent attributes

| Attribute | Required | Use |
|---|---|---|
| `specversion` | yes | must be `"1.0"` |
| `type` | yes | must be `"notify.created.v1"` — gates the `data` schema |
| `source` | yes | producer's project id (e.g. `home-pipeline`) |
| `id` | yes | producer-side correlation id; server mints its own UUIDv7 for internal storage |
| `time` | optional | RFC3339; used as the notification timestamp; server fills if absent |
| `datacontenttype` | optional | must be `application/json` or omitted |

## `data` payload (notification body)

```jsonc
{
  "type": "alert",                    // info | warning | alert | <custom>
  "title": "Backup failed",           // required, ≤120 chars
  "body": "rsync exited 12 on host pi-01",  // required, ≤2000 chars
  "priority": "high",                 // low | normal | high (default: normal)
  "tags": ["pi-01", "backup"],        // optional, ≤10, each ≤64 chars, [A-Za-z0-9._-]; "global" reserved
  "deeplink": "https://...",          // optional, scheme ∈ {https, notify}, ≤2048 chars
  "metadata": { "host": "pi-01" },    // optional, free-form, ≤32 KB serialized
                                       // Reserved key: `fullBody` (string) —
                                       // the iOS detail view renders this in
                                       // place of `body` when present, so
                                       // producers with long-form content
                                       // (digests, reports) put the summary
                                       // in `body` (≤2000) and the full
                                       // markdown in `metadata.fullBody`.
  "deduplicationKey": "backup-2026-04-28"   // optional; same key within TTL = idempotent
}
```

If `data` carries its own `source` / `id` / `timestamp` fields they're silently
overridden — the CloudEvent attributes are canonical on the wire and the server
locks `source` to the authenticated project regardless.

## Response

- Single (structured or binary): `202 Accepted` with `{ "id": "<uuid-v7>" }`.
- Batch: `202 Accepted` with `{ "ids": ["<uuid-v7>", ...] }`.
- Batches are atomic: any per-event validation failure rejects the whole batch
  with `400` (`{ "errors": [{ "field": "events[i].title", "message": "…" }] }`)
  and nothing is published.
- Max batch size: 100 events. Max request body: 128 KB.

## curl recipes

Structured single:

```sh
curl -sf -H "x-api-key: $NOTIFY_KEY" -H "Content-Type: application/cloudevents+json" \
  "$NOTIFY_URL/v1/notifications" -d '{
    "specversion":"1.0","type":"notify.created.v1",
    "source":"my-cron","id":"'"$(uuidgen)"'",
    "time":"'"$(date -u +%Y-%m-%dT%H:%M:%SZ)"'",
    "data":{"title":"Done","body":"weekly cleanup ok","priority":"low"}
  }'
```

Batch:

```sh
curl -sf -H "x-api-key: $NOTIFY_KEY" -H "Content-Type: application/cloudevents-batch+json" \
  "$NOTIFY_URL/v1/notifications" -d '[
    {"specversion":"1.0","type":"notify.created.v1","source":"my-cron","id":"a",
     "data":{"title":"a","body":"a"}},
    {"specversion":"1.0","type":"notify.created.v1","source":"my-cron","id":"b",
     "data":{"title":"b","body":"b"}}
  ]'
```

Binary single (envelope attrs in headers, body is `data` only):

```sh
curl -sf -H "x-api-key: $NOTIFY_KEY" \
  -H "ce-specversion: 1.0" -H "ce-type: notify.created.v1" \
  -H "ce-source: my-cron" -H "ce-id: $(uuidgen)" \
  -H "Content-Type: application/json" \
  "$NOTIFY_URL/v1/notifications" \
  -d '{"title":"Done","body":"weekly cleanup ok","priority":"low"}'
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
      "envelopeId": "…",
      "isRead": false,            // optional; absent on rows written before the field existed
      "isHidden": false           // optional; rows with isHidden=true are filtered out of this response
    }
    // … newest first
  ],
  "continuationToken": "…or null when no more pages"
}
```

Same `NotificationDocument` shape that Archive writes — see
`src/Notify.Shared/Cosmos/NotificationDocument.cs`. `isHidden=true` rows are
filtered server-side; absent `isRead` / `isHidden` (pre-migration rows) is
treated as `false`.

## Errors

- `400` — `limit` outside `[1, 200]` or `source` over 64 chars. Response:
  `{ "errors": [ { "field": "limit", "message": "…" } ] }`.
- `401` — missing or invalid `Authorization: Bearer …` (JwtAuthMiddleware
  rejects tokens with bad signature, wrong issuer/audience, or expired).

---

# Inbox mutation API

Per-recipient mutations on a single archived notification row. Same Bearer-JWT
auth as `GET /v1/inbox`. Each endpoint touches one document, partitioned by
`source` (sent as a query parameter). The `id` in the path is the **full**
document id (`{baseId}:{userId}`) the client received in the inbox response;
the server asserts the trailing `:{userId}` matches the JWT `sub` before
mutating so a token holder can only flip their own rows.

## Mark as read — `POST /v1/inbox/{id}/read?source=<source>`

```sh
curl -sf -X POST -H "Authorization: Bearer $JWT" \
  "$NOTIFY_URL/v1/inbox/55555555-…:001234.abc/read?source=home-pipeline"
```

- `204 No Content` — `isRead` set to `true`. Idempotent (re-call is harmless).
- `400` — missing `source` query param, or `id` not in `{baseId}:{userId}` form.
- `401` — missing/invalid Bearer.
- `403` — `id` userId suffix doesn't match the JWT `sub`.
- `404` — no document at that `(source, id)` partition (Archive hasn't fanned
  out to this user, or the row already expired via TTL).

## Soft-delete — `DELETE /v1/inbox/{id}?source=<source>`

```sh
curl -sf -X DELETE -H "Authorization: Bearer $JWT" \
  "$NOTIFY_URL/v1/inbox/55555555-…:001234.abc?source=home-pipeline"
```

Sets `isHidden=true` on the document. The row is filtered out of subsequent
`GET /v1/inbox` responses, but the document stays in Cosmos until the 90-day
TTL expires. Same response semantics as the read endpoint (`204`, `400`,
`401`, `403`, `404`). Idempotent.

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
