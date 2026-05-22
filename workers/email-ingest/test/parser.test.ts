import { describe, expect, it } from "vitest";
import type { Email } from "postal-mime";
import {
  BODY_SUMMARY_MAX,
  TITLE_MAX,
  buildPayload,
  classify,
  isAuthenticated,
  stripMarkdownToPlain,
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
  // CF's `x-cf-spamh-score`: 0 = clean, higher = more concerning.
  // Threshold is MAX_SPAM_SCORE (5) per parser.ts.

  it("accepts score 0 (the observed value for legitimate Gmail-side notifications)", () => {
    expect(isAuthenticated("0")).toBe(true);
  });

  it("accepts mid-range scores within the threshold", () => {
    expect(isAuthenticated("1")).toBe(true);
    expect(isAuthenticated("5")).toBe(true);
  });

  it("rejects scores above the threshold", () => {
    expect(isAuthenticated("6")).toBe(false);
    expect(isAuthenticated("50")).toBe(false);
    expect(isAuthenticated("100")).toBe(false);
  });

  it("rejects when the header is absent", () => {
    expect(isAuthenticated(null)).toBe(false);
  });

  it("rejects when the value is not numeric", () => {
    expect(isAuthenticated("")).toBe(false);
    expect(isAuthenticated("undefined")).toBe(false);
    expect(isAuthenticated("NaN")).toBe(false);
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

describe("stripMarkdownToPlain", () => {
  it("strips link syntax to the visible text", () => {
    expect(stripMarkdownToPlain("[Anthropic raises $$$](https://example.com)"))
      .toBe("Anthropic raises $$$");
  });

  it("strips image syntax to the alt text", () => {
    expect(stripMarkdownToPlain("![Logo](https://example.com/logo.png)")).toBe("Logo");
  });

  it("strips bold and italic markers", () => {
    expect(stripMarkdownToPlain("**bold** and *italic* and ***both***"))
      .toBe("bold and italic and both");
    expect(stripMarkdownToPlain("__bold__ and _italic_")).toBe("bold and italic");
  });

  it("strips strikethrough and inline code", () => {
    expect(stripMarkdownToPlain("~~old~~ and `code`")).toBe("old and code");
  });

  it("strips heading hashes", () => {
    expect(stripMarkdownToPlain("# Big\n## Medium\n### Small")).toBe("Big\nMedium\nSmall");
  });

  it("strips list bullets and ordered markers", () => {
    expect(stripMarkdownToPlain("- one\n- two\n1. first\n2. second"))
      .toBe("one\ntwo\nfirst\nsecond");
  });

  it("strips blockquote prefix and horizontal rules", () => {
    expect(stripMarkdownToPlain("> quoted\n---\nafter")).toBe("quoted\n\nafter");
  });

  it("decodes common HTML entities", () => {
    expect(stripMarkdownToPlain("AT&amp;T &lt;3 &quot;hi&quot; &#39;ok&#39; &nbsp; &#x41;"))
      .toBe("AT&T <3 \"hi\" 'ok'   A");
  });

  it("collapses runs of blank lines", () => {
    expect(stripMarkdownToPlain("a\n\n\n\nb")).toBe("a\n\nb");
  });

  it("produces clean lock-screen text from a typical Google Alerts excerpt", () => {
    const md = [
      "### [Anthropic raises $$$](https://example.com/a)",
      "",
      "**Anthropic** announced a new round with *strategic* partners. " +
        "See the [full release](https://example.com/release) for details.",
      "",
      "- Cited investors",
      "- Round size",
    ].join("\n");
    const out = stripMarkdownToPlain(md);
    expect(out).not.toMatch(/[\[\]()*_`#>]/);
    expect(out).toContain("Anthropic raises $$$");
    expect(out).toContain("Anthropic announced a new round");
    expect(out).toContain("Cited investors");
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

  it("converts the html part to markdown when text is missing", () => {
    // Realistic Google Alerts shape: links + bold + a list of results,
    // wrapped in `<table>` layout that we flatten via the custom rule.
    const html = `
      <html><body>
        <table><tr><td>
          <h3><a href="https://example.com/article-1">First result with <b>important</b> term</a></h3>
          <p>Snippet of the first result with more context...</p>
          <h3><a href="https://example.com/article-2">Second hit</a></h3>
          <p>Another snippet.</p>
          <ul>
            <li>See also: <a href="https://example.com/more">more</a></li>
            <li>Unsubscribe: <a href="https://example.com/unsub">here</a></li>
          </ul>
        </td></tr></table>
      </body></html>`;
    const out = buildPayload("topic", mkEmail({ text: "", html }));

    // Links survive as markdown `[text](url)`.
    expect(out.metadata.fullBody).toMatch(/\[First result with \*\*important\*\* term\]\(https:\/\/example\.com\/article-1\)/);
    expect(out.metadata.fullBody).toMatch(/\[Second hit\]\(https:\/\/example\.com\/article-2\)/);
    // No raw HTML tags leak through.
    expect(out.metadata.fullBody).not.toMatch(/<\/?(p|a|h3|ul|li|table|tr|td|b)\b/i);
    // Bullets use `-` (matches what iOS MarkdownView's unordered-list
    // classifier recognizes; configured via bulletListMarker on the
    // TurndownService instance in parser.ts).
    expect(out.metadata.fullBody).toMatch(/^- See also:/m);
    expect(out.metadata.fullBody).toMatch(/^- Unsubscribe:/m);
  });

  it("body is plain (no markdown syntax) but metadata.fullBody keeps the markdown", () => {
    const html = `
      <h3><a href="https://example.com/a">Anthropic raises <b>$$$</b></a></h3>
      <p>Snippet with <em>emphasis</em> and a <a href="https://example.com/b">link</a>.</p>
      <ul><li>investor list</li></ul>`;
    const out = buildPayload("anthropic", mkEmail({ text: "", html }));

    // Push banner: plain text only — no markdown chars survive.
    expect(out.body).not.toMatch(/[\[\]()*_`#>]/);
    expect(out.body).toContain("Anthropic raises $$$");
    // Detail view: markdown intact.
    expect(out.metadata.fullBody).toMatch(/\[Anthropic raises \*\*\$\$\$\*\*\]\(https:/);
    expect(out.metadata.fullBody).toMatch(/^- investor list/m);
  });

  it("strips style/script blocks instead of leaking their contents", () => {
    const html = `<style>.x{color:red}</style><script>alert(1)</script><p>visible body</p>`;
    const out = buildPayload("topic", mkEmail({ text: "", html }));
    expect(out.metadata.fullBody).toContain("visible body");
    expect(out.metadata.fullBody).not.toContain(".x{");
    expect(out.metadata.fullBody).not.toContain("alert(1)");
  });

  it("prefers HTML-converted markdown over text/plain when both are present", () => {
    // Google Alerts emits both parts but text/plain is decorative-sparse
    // (titles separated by `=== … ===`, no snippets), so going through
    // turndown on the HTML yields a much richer body. Plain text is the
    // fallback only when HTML is absent.
    const out = buildPayload("topic", mkEmail({
      text: "=== Title ===\nhttps://example.com",
      html: "<h3><a href='https://example.com'>Title</a></h3><p>Full snippet of content here.</p>",
    }));
    expect(out.metadata.fullBody).toContain("[Title](https://example.com)");
    expect(out.metadata.fullBody).toContain("Full snippet of content here.");
    expect(out.metadata.fullBody).not.toContain("===");
  });

  it("falls back to text/plain when html is missing", () => {
    const out = buildPayload("topic", mkEmail({ text: "plain only", html: "" }));
    expect(out.metadata.fullBody).toBe("plain only");
  });

  it("yields empty body when both text and html are missing", () => {
    const out = buildPayload("topic", mkEmail({ text: "", html: "" }));
    expect(out.body).toBe("");
    expect(out.metadata.fullBody).toBe("");
  });

  it("truncates a long subject so title stays within TITLE_MAX", () => {
    const longTopic = "x".repeat(200);
    const email = mkEmail({ text: "body" });
    const out = buildPayload(longTopic, email);
    expect(out.title.length).toBeLessThanOrEqual(TITLE_MAX);
    expect(out.title.startsWith("Google Alert -")).toBe(true);
  });
});
