// Pure functions for classifying + transforming Google Alerts emails.
// Kept side-effect free so tests can exercise them without standing up the
// Worker runtime.

import type { Email } from "postal-mime";
import TurndownService from "turndown";

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

// Reused across calls — TurndownService is stateless once configured.
// `bulletListMarker: "-"` matches what iOS MarkdownView's unordered-list
// classifier already recognizes (and what producers writing markdown by
// hand tend to use). `codeBlockStyle: "fenced"` matches the iOS renderer's
// ```-fence expectation.
const turndown = new TurndownService({
  bulletListMarker: "-",
  codeBlockStyle: "fenced",
  emDelimiter: "*",
  headingStyle: "atx",
});
// Google Alerts wraps each result in `<table>` for layout; iOS doesn't
// render markdown tables, and a table-stripped flow reads better anyway.
// Drop `<table>` / `<thead>` / `<tbody>` / `<tr>` while keeping `<td>`
// content; turndown's default is to emit pipe-tables which look terrible
// when the cells are paragraphs of body text.
turndown.remove(["style", "script"]);
turndown.addRule("flatten-table", {
  filter: ["table", "thead", "tbody", "tr", "td", "th"],
  replacement: (content) => `${content}\n`,
});

// Turndown's default list output pads markers with 2-3 spaces ("- " +
// "  item") for internal column alignment. iOS MarkdownView (see
// `app/Notify/Features/Inbox/MarkdownView.swift`) strict-strips exactly
// `- ` (2 chars) or `\d+\. ` (single trailing space) when classifying
// list items, so the extra padding spills into the rendered item text.
// Collapse to single-space markers so iOS sees what it expects.
function normalizeListMarkers(md: string): string {
  return md
    .replace(/^(\s*)-\s+/gm, "$1- ")
    .replace(/^(\s*\d+\.)\s+/gm, "$1 ");
}

export function buildPayload(topic: string, parsed: Email): NotifyData {
  const text = (parsed.text ?? "").trim();
  const html = (parsed.html ?? "").trim();
  // Prefer the HTML-converted markdown when present. Google Alerts
  // emails include BOTH parts in the MIME multipart: the text/plain
  // version is decorative-sparse (just titles wrapped in `=== … ===`
  // separators + URLs, ~10× shorter than the HTML), and using it
  // leaves the iOS detail view looking truncated. HTML → turndown
  // markdown yields the full digest with links, snippets, and
  // emphasis intact. Fall back to text/plain only when HTML is absent.
  const fromHtml = html ? normalizeListMarkers(turndown.turndown(html)).trim() : "";
  const full = fromHtml || text;
  const summary = summarize(full, BODY_SUMMARY_MAX);

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

// Body summary = first paragraph (or first ~1800 chars if it's all
// one wall). Truncate is the final guard.
export function summarize(text: string, limit: number): string {
  const firstPara = text.split(/\n\s*\n/, 1)[0] ?? text;
  return truncate(firstPara.replace(/\s+/g, " ").trim(), limit);
}
