// Cloudflare Worker email handler. Routes Google Alerts emails into the
// Notify pipeline:
//   - Verification emails → forwarded to the maintainer's verified
//     destination (CF Email Routing) so the click-link can be actioned
//     from a real inbox.
//   - Data emails → wrapped in a CloudEvents 1.0 binary-mode envelope
//     and POSTed to /v1/notifications with the producer API key.
//   - Anything else → dropped (logged for observability).

import PostalMime from "postal-mime";
import {
  buildPayload,
  classify,
  isAuthenticated,
  type NotifyData,
} from "./parser";

export interface Env {
  // Set via `wrangler deploy --var`:
  INGEST_URL: string;
  FORWARD_VERIFICATION_TO: string;
  ALLOWED_SENDERS: string; // comma-separated list
  // Project ID registered in Cosmos `projects/<id>`. Ingest looks up the
  // project by `ce-source` ("urn:notify:<PRODUCER_ID>") before verifying
  // the key hash — a mismatch returns 401 even with a valid key.
  PRODUCER_ID: string;

  // Set via `wrangler secret put` (or pushed by cd-worker.yml from the
  // INGEST_API_KEY GH secret):
  INGEST_API_KEY: string;
}

const CE_TYPE = "notify.created.v1";

export default {
  async email(message: ForwardableEmailMessage, env: Env, ctx: ExecutionContext): Promise<void> {
    const headers = message.headers;
    const subject = headers.get("subject") ?? "";

    // 1. Spam-score gate — see parser.isAuthenticated. CF Email Routing
    //    rolls DKIM/SPF/DMARC/etc. into a single `x-cf-spamh-score`
    //    header; we accept ≤ 5 and reject anything higher. The From:
    //    whitelist below is the strong identity check.
    const spamScore = headers.get("x-cf-spamh-score");
    if (!isAuthenticated(spamScore)) {
      console.warn("spam-score reject", {
        subject,
        envelopeFrom: message.from,
        spamScore,
      });
      message.setReject("Mail rejected (CF spam score too high)");
      return;
    }

    // 2. Parse MIME first so we can check the visible `From:` header.
    //    Gmail filter-forwards rewrite the SMTP envelope (so
    //    `message.from` becomes a Gmail bounce address), but the
    //    `From:` header in the MIME body stays as the original sender.
    //    DKIM=pass on the original signing domain proves the From:
    //    header wasn't tampered with.
    const raw = new Response(message.raw);
    const arrayBuf = await raw.arrayBuffer();
    const parsed = await PostalMime.parse(new Uint8Array(arrayBuf));
    const from = (parsed.from?.address ?? "").toLowerCase();

    // 3. From whitelist. Configured via ALLOWED_SENDERS so a future
    //    forward use case can drop in without code change.
    const allowed = env.ALLOWED_SENDERS.split(",").map((s) => s.trim().toLowerCase()).filter(Boolean);
    if (!allowed.includes(from)) {
      message.setReject(`Sender ${from} not in allow-list`);
      return;
    }

    // 4. Classify by subject.
    const cls = classify(subject);
    if (cls.kind === "drop") {
      console.log("drop", { from, subject, reason: cls.reason });
      message.setReject(cls.reason);
      return;
    }
    if (cls.kind === "verification") {
      // Kept for legacy direct-delivery path; under the Gmail-forward
      // model the verification email never reaches the Worker because
      // it lands at the maintainer's Gmail directly. Harmless either way.
      console.log("forward verification", { from, subject, to: env.FORWARD_VERIFICATION_TO });
      await message.forward(env.FORWARD_VERIFICATION_TO);
      return;
    }

    // 5. Data path — build the payload from the already-parsed MIME, POST to Ingest.
    const payload = buildPayload(cls.topic, parsed);
    // The Ingest POST can take a couple seconds (KV ref lookup + EG
    // publish). Hand it to ctx.waitUntil so we can return from the
    // email handler immediately and let Cloudflare ack the SMTP
    // transaction — failures still surface in `console.error`.
    ctx.waitUntil(postToIngest(env, payload, { from, subject }));
  },
} satisfies ExportedHandler<Env>;

async function postToIngest(env: Env, data: NotifyData, ctx: { from: string; subject: string }): Promise<void> {
  // Binary-mode CloudEvents (per src/Notify.Functions/Ingestion/CloudEventsParser.cs
  // TryParseBinary): attributes go in headers, body is plain JSON of the
  // data payload. Smaller wire format than structured mode, less Worker
  // CPU to assemble.
  const id = crypto.randomUUID();
  const time = new Date().toISOString();

  const resp = await fetch(env.INGEST_URL, {
    method: "POST",
    headers: {
      "Content-Type": "application/json",
      "x-api-key": env.INGEST_API_KEY,
      "ce-specversion": "1.0",
      "ce-type": CE_TYPE,
      "ce-source": `urn:notify:${env.PRODUCER_ID}`,
      "ce-id": id,
      "ce-time": time,
    },
    body: JSON.stringify(data),
  });

  if (!resp.ok) {
    const text = await resp.text().catch(() => "<unreadable>");
    // Surface in Workers logs (observability enabled in wrangler.toml).
    // No retry: a transient Ingest 5xx will retry on the next email; a
    // 4xx is a producer bug and retrying won't help.
    console.error("ingest POST failed", {
      status: resp.status,
      body: text.slice(0, 400),
      ceId: id,
      from: ctx.from,
      subject: ctx.subject,
    });
    return;
  }

  console.log("ingest POST ok", { status: resp.status, ceId: id, from: ctx.from, subject: ctx.subject });
}
