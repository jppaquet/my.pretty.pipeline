---
name: send-test-notification
description: Post a smoke-test notification through the Ingest API for the Watchtower producer. Triggers the full pipeline (Ingest → EventGrid → Archive + Push → NH → APNs → iOS). User-invocable only because it fires a real push to live devices.
disable-model-invocation: true
---

# Send Test Notification

## What this does

`POST /v1/notifications` against the live Function App in `rg-notify-dev`, using the **Watchtower** producer key. End-to-end: the notification lands in the Cosmos `notifications` container for every signed-in user *and* an APNs push fires to every registered device tag.

## Required state

- **`NOTIFY_KEY`** (env var): the producer's `npk_…` API key bound to project `Watchtower`. The skill never stores this — it's a secret, you paste it or `export` it before invoking.
- **Function App hostname**: resolved at runtime via `az functionapp list -g rg-notify-dev --query "[0].defaultHostName" -o tsv`. The salted suffix (`func-notify-dev-XXXXXX`) is not hardcoded — see `feedback_azure_deploy_traps.md` (don't hardcode salted names in scripts).

## The non-obvious bit

The Ingest handler resolves the project by the **`source` field** in the request body *first*, then verifies the API key hash against that project's salt. Passing the wrong `source` returns **401 even with a valid key** — this is the gotcha that wasted a round-trip the first time this was tried in the conversation that minted this skill. The skill always sets `source: "Watchtower"` to match the project the `NOTIFY_KEY` is bound to.

(If you're sending from a different producer, the `source` must match that producer's project id and the key must be that producer's `npk_…`.)

## Usage

Invoke with `/send-test-notification`. Provide a title + body inline or accept the default smoke-test text. Optional flags:

| Flag | Default | Notes |
|---|---|---|
| `--title <str>` | `"hello from claude"` | ≤120 chars |
| `--body <str>`  | `"smoke test"`        | ≤2000 chars |
| `--priority <low\|normal\|high>` | `normal` | passes through to NH |
| `--tags <a,b,c>` | `"smoke-test"` | ≤10 tags, each ≤64 chars, `[A-Za-z0-9._-]` |

## Bash recipe

```sh
: "${NOTIFY_KEY:?set NOTIFY_KEY=npk_… (or paste it now) — never commit this value}"

FN_HOST=$(az functionapp list -g rg-notify-dev --query "[0].defaultHostName" -o tsv)
TITLE="${TITLE:-hello from claude}"
BODY="${BODY:-smoke test}"

BODY_JSON=$(jq -nc \
  --arg t "$TITLE" --arg b "$BODY" \
  '{
    source: "Watchtower",
    type:   "info",
    title:  $t,
    body:   $b,
    priority: "normal",
    tags:   ["smoke-test"]
  }')

curl -sf -w "\nHTTP %{http_code}\n" \
  -H "x-api-key: $NOTIFY_KEY" \
  -H "content-type: application/json" \
  "https://$FN_HOST/v1/notifications" \
  -d "$BODY_JSON"
```

Successful response: `202 Accepted` with `{"id":"<uuid-v7>"}`. The id can be used to look the document up in Cosmos `notifications` after Archive fans it out.

## Failure-mode cheatsheet

| Status | Likely cause |
|---|---|
| `401` | `source` doesn't match the project the key is bound to, OR the key is wrong/inactive |
| `400` | Schema violation — title/body length, tag charset, deeplink scheme not in {https, notify} |
| `413` | Body > 8 KB (the `IngestionOptions.MaxRequestBodyBytes` limit) |
| `202` | Success — but check APNs delivery: if no signed-in iOS user is approved in `allowedUsers`, Archive logs "zero registered users" and no push fires |
