// Pure functions for classifying + transforming Google Alerts emails.
// Kept side-effect free so tests can exercise them without standing up the
// Worker runtime.

import type { Email } from "postal-mime";

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

// Reads the Authentication-Results header CF stamps on inbound mail and
// returns true only when BOTH dkim and spf land at "pass". DMARC isn't
// strictly required for Google Alerts but we check it when present.
export function isAuthenticated(authResults: string | null): boolean {
  if (!authResults) return false;
  const dkim = /\bdkim=([a-z]+)/i.exec(authResults)?.[1]?.toLowerCase();
  const spf = /\bspf=([a-z]+)/i.exec(authResults)?.[1]?.toLowerCase();
  return dkim === "pass" && spf === "pass";
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

export function buildPayload(topic: string, parsed: Email): NotifyData {
  const text = (parsed.text ?? "").trim();
  // Prefer text/plain. If only text/html is present we hand the raw HTML
  // to fullBody — the iOS markdown renderer is lenient enough that even
  // HTML tags read as escaped angle-brackets rather than crashing the row.
  const full = text || (parsed.html ?? "").trim();
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
