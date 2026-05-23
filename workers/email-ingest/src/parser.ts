// Pure functions for classifying + transforming Google Alerts emails.
// Kept side-effect free so tests can exercise them without standing up the
// Worker runtime.

import type { Email } from "postal-mime";
import { NodeHtmlMarkdown } from "node-html-markdown";

export type Classification =
  | { kind: "data"; topic: string }
  | { kind: "verification" }
  | { kind: "drop"; reason: string };

// The validator caps `body` at 2000 chars. Anything longer goes into
// `metadata.fullBody` (see docs/SCHEMA.md). 1800 leaves headroom for a
// rendered ellipsis without bumping into the cap.
export const BODY_SUMMARY_MAX = 1800;
export const TITLE_MAX = 120;

// Subject conventions:
//   - Data:         "Google Alert - <query>"
//   - Verification: "Google Alerts: Confirm your subscription"
// Both originate from googlealerts-noreply@google.com. The classifier is
// strict-prefix-based: if the subject doesn't match either, we drop the
// email rather than guess, so unknown formats surface as drops in the
// Worker logs rather than as garbled inbox rows.
export function classify(subject: string): Classification {
  const trimmed = subject.trim();
  const dataMatch = trimmed.match(/^Google Alert\s*[-–]\s*(.+)$/i);
  if (dataMatch) {
    return { kind: "data", topic: dataMatch[1].trim() };
  }
  if (/^Google Alerts:/i.test(trimmed) || /confirm.*google alert/i.test(trimmed)) {
    return { kind: "verification" };
  }
  return { kind: "drop", reason: `unrecognized Google Alerts subject: "${trimmed}"` };
}

// CF Email Routing does NOT stamp a standard `Authentication-Results`
// header on inbound mail (verified empirically by dumping
// `headers.keys()` on a rejected message — see the `auth fail` log
// from the bootstrap debugging session). Instead it folds DKIM, SPF,
// DMARC, and a handful of other signals into a single internal score
// surfaced as `x-cf-spamh-score`. Score 0 = no spam indicators
// triggered; higher = more concerning.
//
// We accept anything ≤ MAX_SPAM_SCORE, which leaves room for legit
// emails with mild ambiguity (e.g. minor SPF misalignment on a
// noreply sub-domain) without opening the door to obvious spam. The
// From: whitelist downstream is the strong identity gate; this
// function is the spam-filter layer in front of it.
export const MAX_SPAM_SCORE = 5;

export function isAuthenticated(spamScore: string | null): boolean {
  // `Number("")` returns 0, so reject empty/whitespace explicitly before
  // letting it fall through as "score 0 → accept."
  if (spamScore === null || spamScore.trim() === "") return false;
  const score = Number(spamScore);
  if (!Number.isFinite(score)) return false;
  return score <= MAX_SPAM_SCORE;
}

// Builds the NotifyCreatedV1 `data` payload from a parsed Google Alert
// email. Title from subject (truncated to TITLE_MAX); body from a
// summary of the text part; metadata.fullBody carries the untruncated
// content so the iOS detail view can show it via the `renderedBody`
// fallback added in feat/ios-fullbody-fallback.
export interface NotifyData {
  title: string;
  body: string;
  type: string;
  priority: "low" | "normal" | "high";
  tags: string[];
  metadata: { fullBody: string };
}

// `node-html-markdown` (NHM) replaced turndown after turndown crashed
// in the CF Workers runtime with `ReferenceError: document is not
// defined`. Turndown's HTML parser leans on a real DOM (resolved via
// `@mixmark-io/domino` in Node, the global `document` in browsers).
// CF Workers' V8 isolate exposes neither, so the parser blew up at
// first invocation. NHM parses HTML via `node-html-parser`, a pure
// JS tokenizer with no DOM dependency — works everywhere.
//
// Bullet/heading options match what iOS MarkdownView's classifier
// recognizes (single-space `- ` markers, atx-style headings).
const htmlToMarkdown = new NodeHtmlMarkdown({
  bulletMarker: "-",
  codeBlockStyle: "fenced",
  emDelimiter: "*",
  strongDelimiter: "**",
});

// Google Alerts embeds a Schema.org "inbox markup" JSON block inside
// the HTML — the same payload Gmail uses to render its in-thread
// action card. Shape (only the fields we read):
//
//   <script data-scope="inboxmarkup" type="application/json">
//   {
//     "entity":  { "title": "Google Alert - Anthropic", "subtitle": "Latest: …" },
//     "updates": { "snippets": [ { "message": "…" }, … ] },
//     "cards":   [ { "widgets": [
//        { "type": "LINK", "title": "…", "description": "…", "url": "…" },
//        …
//     ] } ]
//   }
//   </script>
//
// This is dramatically cleaner than scraping the layout-table HTML: each
// widget is already title/description/url with no decorative noise (no
// "Flag as irrelevant" / "Full Coverage" / share-button alt text / etc.).
// preprocessHtml() strips <script> blocks before NHM sees them, so
// extraction must happen before that path runs.
interface InboxMarkupWidget {
  type?: string;
  title?: string;
  description?: string;
  url?: string;
}
interface InboxMarkup {
  entity?: { title?: string; subtitle?: string };
  updates?: { snippets?: { message?: string }[] };
  cards?: { widgets?: InboxMarkupWidget[] }[];
}

export function extractInboxMarkup(html: string): InboxMarkup | null {
  // Match the script block by its `data-scope="inboxmarkup"` attribute,
  // tolerant of attribute order and single/double quotes.
  const re =
    /<script\b[^>]*data-scope\s*=\s*["']inboxmarkup["'][^>]*>([\s\S]*?)<\/script>/i;
  const match = html.match(re);
  if (!match) return null;
  try {
    return JSON.parse(match[1]) as InboxMarkup;
  } catch {
    return null;
  }
}

// Renders the inbox-markup JSON as the same flavor of markdown NHM
// produces, so the downstream summarize / fullBody path doesn't need
// to know which source built the string. Each LINK widget becomes:
//
//   **[Title](url)**
//   description
//
// separated by blank lines. When `cards[].widgets[]` is empty but
// `updates.snippets[]` has items (terse alert variants), each snippet
// becomes a `- message` bullet line.
export function renderFromInboxMarkup(markup: InboxMarkup): string {
  const parts: string[] = [];
  const widgets = (markup.cards ?? [])
    .flatMap((c) => c.widgets ?? [])
    .filter((w) => w.type === "LINK" && (w.title || w.description));

  for (const w of widgets) {
    const title = (w.title ?? "").trim();
    const desc = (w.description ?? "").trim();
    const url = (w.url ?? "").trim();
    const lines: string[] = [];
    if (title && url) lines.push(`**[${title}](${url})**`);
    else if (title) lines.push(`**${title}**`);
    if (desc) lines.push(desc);
    if (lines.length > 0) parts.push(lines.join("\n"));
  }

  if (parts.length === 0) {
    const snippets = markup.updates?.snippets ?? [];
    for (const s of snippets) {
      const m = (s.message ?? "").trim();
      if (m) parts.push(`- ${m}`);
    }
  }

  return parts.join("\n\n").trim();
}

// Pre-strip layout wrappers + script/style blocks before NHM parses
// the HTML. Google Alerts wraps each result in `<table>` for visual
// layout; NHM's default would emit a pipe-style markdown table which
// reads terribly when the cells are full paragraphs (and iOS doesn't
// render markdown tables anyway). `<style>` and `<script>` need to
// be removed wholesale, not just stripped of their tags, so we delete
// the entire block.
function preprocessHtml(html: string): string {
  return html
    .replace(/<style\b[\s\S]*?<\/style>/gi, "")
    .replace(/<script\b[\s\S]*?<\/script>/gi, "")
    .replace(/<\/?(table|thead|tbody|tfoot|tr|td|th)\b[^>]*>/gi, " ");
}

// Strips inline markdown syntax + decodes common HTML entities so a
// summary is safe to show in an APNs push banner (which renders text
// literally, with no markdown awareness). The detail view's
// `metadata.fullBody` keeps the markdown intact for rich rendering.
//
// Coverage:
//   - `[text](url)` and `![alt](url)`  → text / alt
//   - `**bold**`, `__bold__`           → bold
//   - `*italic*`, `_italic_`           → italic
//   - `~~strike~~`                     → strike
//   - `` `code` ``                     → code
//   - Leading `#…` headings            → drop the `#`s + space
//   - Leading `- ` / `* ` / `1. ` list markers → drop the marker
//   - Horizontal rules (`---`, `***`)  → drop the line
//   - Common HTML entities             → decoded
//   - Successive blank lines           → single newline
export function stripMarkdownToPlain(md: string): string {
  let s = md;

  // Images first (so the ! in `![alt](url)` doesn't leak), then links.
  s = s.replace(/!\[([^\]]*)\]\([^)]*\)/g, "$1");
  s = s.replace(/\[([^\]]+)\]\([^)]*\)/g, "$1");

  // Emphasis pairs. Process the doubles before the singles so `**foo**`
  // doesn't get half-stripped by the `*…*` rule.
  s = s.replace(/\*\*([^*]+)\*\*/g, "$1");
  s = s.replace(/__([^_]+)__/g, "$1");
  s = s.replace(/\*([^*\n]+)\*/g, "$1");
  s = s.replace(/(^|[^\w])_([^_\n]+)_($|[^\w])/g, "$1$2$3");
  s = s.replace(/~~([^~]+)~~/g, "$1");
  s = s.replace(/`([^`\n]+)`/g, "$1");

  // Line-leading markers (heading hashes, list bullets, blockquote `>`).
  // The `^` + multiline flag matches each line individually.
  s = s.replace(/^\s{0,3}#{1,6}\s+/gm, "");
  s = s.replace(/^\s{0,3}[-*+]\s+/gm, "");
  s = s.replace(/^\s{0,3}\d+\.\s+/gm, "");
  s = s.replace(/^\s{0,3}>\s?/gm, "");

  // Horizontal rules — drop entire matching lines.
  s = s.replace(/^\s*([-*_]\s*){3,}\s*$/gm, "");

  // HTML entities most likely to leak through node-html-markdown when
  // the source had layout we stripped via regex (preprocessHtml).
  s = s.replace(/&amp;/g, "&")
       .replace(/&lt;/g, "<")
       .replace(/&gt;/g, ">")
       .replace(/&quot;/g, "\"")
       .replace(/&#39;/g, "'")
       .replace(/&apos;/g, "'")
       .replace(/&nbsp;/g, " ")
       .replace(/&#(\d+);/g, (_, n) => String.fromCharCode(Number(n)))
       .replace(/&#x([0-9a-f]+);/gi, (_, h) => String.fromCharCode(parseInt(h, 16)));

  // Collapse extra whitespace from the cleanup pass.
  s = s.replace(/\n{3,}/g, "\n\n");
  return s.trim();
}

export function buildPayload(topic: string, parsed: Email): NotifyData {
  const text = (parsed.text ?? "").trim();
  const html = (parsed.html ?? "").trim();
  // Three-tier source preference for the body:
  //   1. The Schema.org inbox-markup JSON Google embeds in the HTML
  //      — pre-cleaned title/description/url per result, no layout
  //      noise. This is the high-fidelity path.
  //   2. HTML → NHM markdown — fallback when the JSON block is
  //      absent (other producers, or if Google ever changes the
  //      schema). Carries the full digest at the cost of some
  //      residual layout artifacts.
  //   3. text/plain — last-resort fallback. Google Alerts' text part
  //      is decorative-sparse (`=== … ===` separators + raw URLs)
  //      and lacks the snippets, so we only use it when HTML is
  //      absent entirely.
  const markup = html ? extractInboxMarkup(html) : null;
  const fromMarkup = markup ? renderFromInboxMarkup(markup) : "";
  const fromHtml = !fromMarkup && html
    ? htmlToMarkdown.translate(preprocessHtml(html)).trim()
    : "";
  const full = fromMarkup || fromHtml || text;
  // The push banner renders body as plain text — markdown syntax
  // (`[link](url)`, `**bold**`, leading `#`/`-`) shows up literally on
  // the lock-screen otherwise. Strip syntax before summarize so the
  // banner reads naturally. metadata.fullBody keeps the full markdown
  // for the iOS detail view, which renders via MarkdownView.
  const summary = summarize(stripMarkdownToPlain(full), BODY_SUMMARY_MAX);

  return {
    title: truncate(`Google Alert - ${topic}`, TITLE_MAX),
    body: summary,
    type: "info",
    priority: "normal",
    tags: ["google-alerts"],
    metadata: { fullBody: full },
  };
}

// Single-pass truncation that breaks on a word boundary when possible
// and appends an ellipsis if it actually cut anything. Result is at most
// `limit` chars long (counting "…" as 1) so the validator's `<= limit`
// check passes.
export function truncate(s: string, limit: number): string {
  if (s.length <= limit) return s;
  const window = s.slice(0, limit - 1);
  const lastSpace = window.lastIndexOf(" ");
  // If the last space is too early in the window (< 70%), don't bother —
  // hard-cut rather than emit a 2-word summary of a 1800-char window.
  const cutoff = lastSpace > limit * 0.7 ? lastSpace : window.length;
  return window.slice(0, cutoff) + "…";
}

// Smallest paragraph length that we consider "substantive" — anything
// shorter is treated as a header / logo alt-text / link line and
// skipped over while looking for real content. Tuned against the
// shape of Google Alerts emails:
//   `Google Microsoft` (logo alt + topic, ~16 chars)
//   `Microsoft news`   (section header, ~15 chars)
//   `Result snippet…`  (the real content, easily 80+ chars)
const SUMMARY_MIN_PARA = 50;

// Body summary for the push banner + inbox row preview. Splits the
// post-markdown-strip text into paragraphs, skips leading short ones
// (logos, headers, alt-text lines that survive stripMarkdownToPlain
// as tiny fragments), then joins everything from the first
// substantive paragraph onward into a single flowing string and
// truncates. Falls back to the whole text when no paragraph clears
// the threshold (short alerts, plain-text producers).
export function summarize(text: string, limit: number): string {
  const paragraphs = text.split(/\n\s*\n/);
  const normalize = (s: string) => s.replace(/\s+/g, " ").trim();

  let firstSubstantive = paragraphs.findIndex(
    (p) => normalize(p).length >= SUMMARY_MIN_PARA,
  );
  if (firstSubstantive < 0) firstSubstantive = 0;

  const joined = normalize(paragraphs.slice(firstSubstantive).join(" "));
  return truncate(joined, limit);
}
