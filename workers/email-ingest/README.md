# `workers/email-ingest/` — Cloudflare Worker for inbound Google Alerts

Triggered by inbound emails to `alerts@<domain>` via Cloudflare Email
Routing. Classifies, transforms, and forwards or POSTs each message to
the Notify Ingest API. Bound to the routing rule declared in
[`infra/cloudflare/email-routing.tf`](../../infra/cloudflare/email-routing.tf).

## Layout

```
src/
  parser.ts        # pure logic — classify, isAuthenticated, buildPayload
  index.ts         # Worker entry (email() handler), uses parser
test/
  parser.test.ts   # vitest, 20 cases, exercise the parser only
wrangler.toml      # name + main + observability; vars/secrets injected at deploy
package.json       # pinned-version deps (see "Supply chain" below)
package-lock.json  # committed — npm ci uses this in CI
```

## Local dev

```sh
npm ci          # deterministic install from the committed lockfile
npm test        # vitest
npx tsc --noEmit
```

To deploy from your laptop (rarely needed — CI handles it):

```sh
export CLOUDFLARE_API_TOKEN=...
export CLOUDFLARE_ACCOUNT_ID=...
npm run deploy -- \
  --var INGEST_URL:https://func.<domain>/v1/notifications \
  --var FORWARD_VERIFICATION_TO:<your-verified-destination> \
  --var ALLOWED_SENDERS:googlealerts-noreply@google.com
# Then set the producer key once:
npx wrangler secret put INGEST_API_KEY
```

## Supply chain

This is a public repo and the Worker has elevated privileges (it can
POST authenticated requests to Ingest). Two guardrails:

1. **Pinned exact versions** in `package.json` — no `^` ranges. A
   malicious `2.7.5` of `postal-mime` won't auto-pull on the next
   install.
2. **Committed `package-lock.json`** + CI uses `npm ci`, which fails
   when the lockfile and `package.json` disagree. Anyone reviewing a
   PR sees the full resolved dep tree diff.

To bump a dep: edit the exact version in `package.json`, delete
`package-lock.json`, run `npm install`, commit both files together so
reviewers can audit the tree change.

## Why `postal-mime`

The Worker receives the raw MIME message stream from CF. Hand-rolling
multipart parsing + base64/quoted-printable decoding is a footgun.
`postal-mime` is by Andris Reinman (nodemailer/mailparser author),
zero runtime deps, Workers-compatible, widely audited.
