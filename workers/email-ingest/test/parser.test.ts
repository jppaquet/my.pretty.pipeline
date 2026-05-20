import { describe, expect, it } from "vitest";
import type { Email } from "postal-mime";
import {
  BODY_SUMMARY_MAX,
  TITLE_MAX,
  buildPayload,
  classify,
  isAuthenticated,
  summarize,
  truncate,
} from "../src/parser";

describe("classify", () => {
  it("recognizes a Google Alert data email by subject prefix", () => {
    expect(classify("Google Alert - claude code")).toEqual({
      kind: "data",
      topic: "claude code",
    });
  });

  it("tolerates en-dash + extra whitespace in the subject", () => {
    expect(classify("Google Alert  – Claude Code  ")).toEqual({
      kind: "data",
      topic: "Claude Code",
    });
  });

  it("recognizes a verification email by 'Google Alerts:' prefix", () => {
    expect(classify("Google Alerts: Confirm your subscription")).toEqual({
      kind: "verification",
    });
  });

  it("recognizes a verification email by 'confirm google alert' phrasing", () => {
    expect(classify("Please confirm your Google Alert")).toEqual({
      kind: "verification",
    });
  });

  it("drops unrecognized subjects with a reason", () => {
    const result = classify("Random Google Search Update");
    expect(result.kind).toBe("drop");
    if (result.kind === "drop") expect(result.reason).toContain("unrecognized");
  });

  it("drops empty subjects", () => {
    expect(classify("").kind).toBe("drop");
    expect(classify("   ").kind).toBe("drop");
  });
});

describe("isAuthenticated", () => {
  it("passes when both dkim and spf land at pass", () => {
    expect(isAuthenticated("mx.cloudflare.com; spf=pass; dkim=pass; dmarc=pass")).toBe(true);
  });

  it("fails when dkim is not pass", () => {
    expect(isAuthenticated("mx.cloudflare.com; spf=pass; dkim=fail")).toBe(false);
  });

  it("fails when spf is not pass", () => {
    expect(isAuthenticated("mx.cloudflare.com; spf=neutral; dkim=pass")).toBe(false);
  });

  it("fails on a missing header", () => {
    expect(isAuthenticated(null)).toBe(false);
    expect(isAuthenticated("")).toBe(false);
  });

  it("fails when one of the two is absent", () => {
    // Some senders won't emit a dkim= component; we treat that as a failure
    // rather than a pass to avoid widening the trust boundary by accident.
    expect(isAuthenticated("mx.cloudflare.com; spf=pass")).toBe(false);
  });
});

describe("truncate", () => {
  it("returns input unchanged when within the limit", () => {
    expect(truncate("hello", 10)).toBe("hello");
    expect(truncate("hello", 5)).toBe("hello");
  });

  it("cuts on a word boundary near the end of the window", () => {
    const out = truncate("one two three four five", 12);
    expect(out.length).toBeLessThanOrEqual(12);
    expect(out.endsWith("…")).toBe(true);
    expect(out.includes("two")).toBe(true);
  });

  it("hard-cuts when the only word boundary is too early in the window", () => {
    // The 12-char window contains one space at index 0 ("a "); that's <70%
    // through the window, so we hard-cut instead of returning "a…".
    const out = truncate("a verylongwordwithoutspaces", 12);
    expect(out.endsWith("…")).toBe(true);
    expect(out.length).toBe(12);
  });
});

describe("summarize", () => {
  it("returns the first paragraph when shorter than the limit", () => {
    const input = "first paragraph here\n\nsecond paragraph way longer than the first one";
    expect(summarize(input, 100)).toBe("first paragraph here");
  });

  it("truncates a wall-of-text body with no paragraph breaks", () => {
    const wall = "x".repeat(BODY_SUMMARY_MAX + 500);
    const out = summarize(wall, BODY_SUMMARY_MAX);
    expect(out.length).toBe(BODY_SUMMARY_MAX);
    expect(out.endsWith("…")).toBe(true);
  });
});

describe("buildPayload", () => {
  const mkEmail = (overrides: Partial<Email>): Email =>
    ({
      headers: [],
      from: { address: "googlealerts-noreply@google.com" },
      to: [],
      ...overrides,
    } as unknown as Email);

  it("packages a short alert with body == fullBody", () => {
    const email = mkEmail({ text: "Three new results matched your query." });
    const out = buildPayload("claude code", email);

    expect(out.title).toBe("Google Alert - claude code");
    expect(out.body).toBe("Three new results matched your query.");
    expect(out.metadata.fullBody).toBe("Three new results matched your query.");
    expect(out.tags).toEqual(["google-alerts"]);
    expect(out.priority).toBe("normal");
  });

  it("splits a long alert so body holds the summary and metadata.fullBody the whole thing", () => {
    const full = "Daily digest summary. " + "Lorem ipsum dolor sit amet. ".repeat(200);
    const email = mkEmail({ text: full });
    const out = buildPayload("daily digest", email);

    expect(out.body.length).toBeLessThanOrEqual(BODY_SUMMARY_MAX);
    expect(out.body.endsWith("…")).toBe(true);
    // fullBody is the original, untruncated text (modulo trim()).
    expect(out.metadata.fullBody).toBe(full.trim());
    expect(out.metadata.fullBody.length).toBeGreaterThan(BODY_SUMMARY_MAX);
  });

  it("falls back to the html part when text is missing", () => {
    const email = mkEmail({ text: "", html: "<p>html only body</p>" });
    const out = buildPayload("topic", email);
    expect(out.body).toBe("<p>html only body</p>");
    expect(out.metadata.fullBody).toBe("<p>html only body</p>");
  });

  it("truncates a long subject so title stays within TITLE_MAX", () => {
    const longTopic = "x".repeat(200);
    const email = mkEmail({ text: "body" });
    const out = buildPayload(longTopic, email);
    expect(out.title.length).toBeLessThanOrEqual(TITLE_MAX);
    expect(out.title.startsWith("Google Alert -")).toBe(true);
  });
});
