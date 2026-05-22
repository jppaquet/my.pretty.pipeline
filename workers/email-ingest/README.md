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
  parser.test.ts   # vitest, exercises classify/auth/buildPayload
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
  --var ALLOWED_SENDERS:googlealerts-noreply@google.com,forwarding-noreply@google.com \
  --var PRODUCER_ID:google-alerts
# Set the producer key once (CI also pushes from the INGEST_API_KEY
# GH secret on every deploy, so this is only needed for ad-hoc dev):
npx wrangler secret put INGEST_API_KEY
```

## Supply chain

This is a public repo and the Worker has elevated privileges (it can
POST authenticated requests to Ingest). Three guardrails:

1. **Pinned exact versions** in `package.json` — no `^` ranges. A
   malicious `2.7.5` of `postal-mime` won't auto-pull on the next
   install.
2. **Pin to a non-fresh release** — every dep here is at a version
   published at least ~2 months ago (per user-level CLAUDE.md). New
   releases are the most common vector for compromised-maintainer
   incidents; trailing edge gives the ecosystem time to notice.
3. **Committed `package-lock.json`** + CI uses `npm ci`, which fails
   when the lockfile and `package.json` disagree. Anyone reviewing a
   PR sees the full resolved dep tree diff.

To bump a dep: edit the exact version in `package.json`, delete
`package-lock.json`, run `npm install`, commit both files together so
reviewers can audit the tree change.

## Why each runtime dep

- **`postal-mime`** — parses the raw MIME stream CF hands the Worker.
  Hand-rolling multipart + base64/quoted-printable decoding is a
  footgun. By Andris Reinman (nodemailer / mailparser author),
  zero runtime deps, Workers-compatible.
- **`node-html-markdown`** — converts the HTML body of Google Alerts
  (which arrives HTML-rich; the matching `text/plain` is decorative-
  sparse, just `=== title ===` separators) into Markdown so iOS
  `MarkdownView` renders links + bullets natively instead of literal
  `<a>` / `<li>` tags. Pure-JS HTML tokenizer (via `node-html-parser`),
  **no DOM dependency** — works in CF Workers' V8 isolate where the
  earlier `turndown` choice crashed with `ReferenceError: document
  is not defined` on its first invocation. Single direct dep
  (`node-html-parser`, ~3-years mature). We pre-strip layout tables
  + `<style>`/`<script>` blocks before the conversion: Google Alerts
  wraps each result in `<table>` for visual layout, which would
  otherwise render as a pipe-style markdown table that reads terribly.
