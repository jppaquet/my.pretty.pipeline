import { describe, expect, it } from "vitest";
import type { Email } from "postal-mime";
import {
  BODY_SUMMARY_MAX,
  TITLE_MAX,
  buildPayload,
  classify,
  extractInboxMarkup,
  isAuthenticated,
  renderFromInboxMarkup,
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
  it("joins from the first substantive paragraph onward", () => {
    const longFirst = "This first paragraph has well over fifty characters of content all on its own.";
    const input = `${longFirst}\n\nsecond paragraph`;
    // Substantive starts at index 0 — join everything from there.
    expect(summarize(input, 200)).toBe(`${longFirst} second paragraph`);
  });

  it("skips leading short paragraphs (logos / topic headers) and joins from the first substantive one", () => {
    // Mirrors the shape of a real Google Alerts body after
    // stripMarkdownToPlain: a logo alt-text + topic header + the
    // actual snippet. The user-facing summary should jump past the
    // first two short paragraphs and start at the real content.
    const input = [
      "Google",                                                    // alt-text from a logo, 6 chars
      "Microsoft",                                                 // topic name, 9 chars
      "Microsoft announced today a new partnership with " +        // real snippet, > 50 chars
        "Anthropic to bring Claude models to enterprise customers.",
      "Read more at example.com",                                  // tail, < 50 chars but reachable via join
    ].join("\n\n");
    const out = summarize(input, 500);
    expect(out.startsWith("Microsoft announced today")).toBe(true);
    expect(out).toContain("partnership with Anthropic");
    expect(out).toContain("Read more at example.com");
    expect(out).not.toMatch(/^Google\b/);
  });

  it("falls back to the whole text when no paragraph clears the substantive threshold", () => {
    const input = "short\n\nbits\n\nonly";
    expect(summarize(input, 100)).toBe("short bits only");
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

  it("prefers Google's inbox-markup JSON over the surrounding HTML when both are present", () => {
    // Mirrors the real Google Alerts shape: the `<script data-scope="inboxmarkup">`
    // block carries clean structured data; the surrounding HTML repeats the
    // same info wrapped in layout-table noise (social-share buttons, "Flag
    // as irrelevant" lines, "Full Coverage" links). We want the JSON
    // path to win — it's higher fidelity and skips the noise.
    const inboxMarkup = {
      api_version: "1.0",
      entity: { title: "Google Alert - Anthropic" },
      cards: [{
        widgets: [
          {
            type: "LINK",
            title: "Anthropic rents Colossus 1 for $1.25B/month",
            description: "Anthropic has just signed a $1.25 billion per month contract until May 2029.",
            url: "https://www.google.com/url?rct=j&url=https://example.com/article-1",
          },
          {
            type: "LINK",
            title: "Anthropic dropped Claude Skills",
            description: "A walkthrough of the 31 Skills released this week.",
            url: "https://www.google.com/url?rct=j&url=https://example.com/article-2",
          },
        ],
      }],
    };
    const html = `<html><body>
      <script data-scope="inboxmarkup" type="application/json">${JSON.stringify(inboxMarkup)}</script>
      <table><tr><td>
        <h3><a href="https://example.com/decorative">Flag as irrelevant</a></h3>
        <p>Share on Facebook | Twitter | RSS feed of this alert</p>
      </td></tr></table>
    </body></html>`;
    const out = buildPayload("Anthropic", mkEmail({ text: "", html }));

    expect(out.metadata.fullBody).toMatch(/\[Anthropic rents Colossus 1 for \$1\.25B\/month\]/);
    expect(out.metadata.fullBody).toContain("$1.25 billion per month contract until May 2029");
    expect(out.metadata.fullBody).toMatch(/\[Anthropic dropped Claude Skills\]/);
    expect(out.metadata.fullBody).toContain("31 Skills released this week");
    // None of the decorative HTML leaks through.
    expect(out.metadata.fullBody).not.toContain("Flag as irrelevant");
    expect(out.metadata.fullBody).not.toContain("Share on Facebook");
    // Body summary is plain — no markdown syntax — and contains the
    // real snippet content from the first widget.
    expect(out.body).not.toMatch(/[\[\]()*_`#>]/);
    expect(out.body).toContain("Anthropic rents Colossus 1");
    expect(out.body).toContain("$1.25 billion per month");
  });

  it("falls back to NHM HTML→markdown when the inbox-markup script is malformed", () => {
    // JSON.parse throws → extractInboxMarkup returns null → buildPayload
    // walks down to the HTML path. Smoke test that we don't blow up on
    // bad JSON inside the script tag.
    const html = `<html><body>
      <script data-scope="inboxmarkup" type="application/json">{not valid json</script>
      <p>Visible HTML body.</p>
    </body></html>`;
    const out = buildPayload("topic", mkEmail({ text: "", html }));
    expect(out.metadata.fullBody).toContain("Visible HTML body.");
  });
});

describe("extractInboxMarkup", () => {
  it("returns null when the script tag is absent", () => {
    expect(extractInboxMarkup("<html><body><p>nothing here</p></body></html>")).toBeNull();
  });

  it("returns null when the script tag is present but JSON is malformed", () => {
    const html = `<script data-scope="inboxmarkup" type="application/json">{nope}</script>`;
    expect(extractInboxMarkup(html)).toBeNull();
  });

  it("parses the JSON when the script tag is present", () => {
    const html = `<script data-scope="inboxmarkup" type="application/json">{"entity":{"title":"x"}}</script>`;
    expect(extractInboxMarkup(html)?.entity?.title).toBe("x");
  });

  it("tolerates single-quoted attributes and attribute reordering", () => {
    const html = `<script type='application/json' data-scope='inboxmarkup'>{"entity":{"title":"x"}}</script>`;
    expect(extractInboxMarkup(html)?.entity?.title).toBe("x");
  });

  it("only matches scripts scoped to inboxmarkup (ignores other JSON scripts)", () => {
    const html =
      `<script type="application/json" data-scope="something-else">{"a":1}</script>` +
      `<script data-scope="inboxmarkup" type="application/json">{"entity":{"title":"yes"}}</script>`;
    expect(extractInboxMarkup(html)?.entity?.title).toBe("yes");
  });
});

describe("renderFromInboxMarkup", () => {
  it("emits a bold-linked title plus description per LINK widget", () => {
    const out = renderFromInboxMarkup({
      cards: [{
        widgets: [
          { type: "LINK", title: "First", description: "First snippet.", url: "https://example.com/1" },
          { type: "LINK", title: "Second", description: "Second snippet.", url: "https://example.com/2" },
        ],
      }],
    });
    expect(out).toContain("**[First](https://example.com/1)**\nFirst snippet.");
    expect(out).toContain("**[Second](https://example.com/2)**\nSecond snippet.");
    // Widgets separated by a blank line.
    expect(out).toMatch(/First snippet\.\n\n\*\*\[Second\]/);
  });

  it("skips widgets whose type isn't LINK and widgets missing both title and description", () => {
    const out = renderFromInboxMarkup({
      cards: [{
        widgets: [
          { type: "IMAGE", title: "Decorative" },
          { type: "LINK", title: "", description: "", url: "https://example.com/empty" },
          { type: "LINK", title: "Kept", description: "Kept snippet.", url: "https://example.com/kept" },
        ],
      }],
    });
    expect(out).toContain("Kept");
    expect(out).not.toContain("Decorative");
    expect(out).not.toContain("empty");
  });

  it("falls back to updates.snippets as bullet list when no widgets are present", () => {
    const out = renderFromInboxMarkup({
      updates: { snippets: [{ message: "snippet one" }, { message: "snippet two" }] },
    });
    expect(out).toBe("- snippet one\n\n- snippet two");
  });

  it("returns an empty string for an empty markup payload", () => {
    expect(renderFromInboxMarkup({})).toBe("");
    expect(renderFromInboxMarkup({ cards: [] })).toBe("");
    expect(renderFromInboxMarkup({ cards: [{ widgets: [] }] })).toBe("");
  });

  it("handles widgets with title but no url (emits **title** without a link)", () => {
    const out = renderFromInboxMarkup({
      cards: [{ widgets: [{ type: "LINK", title: "No URL here", description: "Snippet." }] }],
    });
    expect(out).toContain("**No URL here**\nSnippet.");
    expect(out).not.toContain("[No URL here]");
  });
});
