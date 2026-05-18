import SwiftUI

// Minimal block-level markdown renderer. Built because
// `Text(AttributedString(markdown:options: .init(interpretedSyntax: .full)))`
// drops paragraph structure: the parser puts block intent into the
// `presentationIntent` attribute but SwiftUI's `Text` ignores it, so
// `\n\n` collapses into a single continuous flow.
//
// Strategy: split the body on `\n\n`, classify each block, render each
// as the right SwiftUI view. Inline styling within a block is delegated
// back to `AttributedString(markdown:)` — for single-block input there's
// no multi-paragraph collapse to fight.
//
// Supported blocks:
//   - ATX headings: `#` … `######`
//   - Unordered lists: `- ` or `* ` prefix on every line
//   - Ordered lists:  `1. ` style prefix on every line
//   - Blockquotes: `> ` prefix on every line
//   - Fenced code blocks: ``` … ``` (language tag ignored, mono-rendered)
//   - Horizontal rules: `---`, `***`, `___`
//   - Plain paragraphs (everything else)
//
// Not supported (renders as raw text): pipe tables, nested lists,
// HTML pass-through, indented (4-space) code blocks. Acceptable until
// a real markdown SPM dep is worth adopting.
struct MarkdownView: View {
    let markdown: String

    var body: some View {
        VStack(alignment: .leading, spacing: 12) {
            ForEach(Array(blocks().enumerated()), id: \.offset) { _, block in
                renderBlock(block)
            }
        }
    }

    // ── classification ─────────────────────────────────────────────────

    private enum Block {
        case heading(level: Int, text: String)
        case paragraph(text: String)
        case unorderedList(items: [String])
        case orderedList(items: [String])
        case blockquote(text: String)
        case codeFence(text: String)
        case horizontalRule
    }

    private func blocks() -> [Block] {
        markdown
            .components(separatedBy: "\n\n")
            .compactMap(Self.classify)
    }

    private static func classify(_ raw: String) -> Block? {
        let trimmed = raw.trimmingCharacters(in: .whitespacesAndNewlines)
        if trimmed.isEmpty { return nil }
        let lines = trimmed.components(separatedBy: "\n")

        if Self.horizontalRulePattern.contains(trimmed) {
            return .horizontalRule
        }

        if let first = lines.first, first.hasPrefix("```") {
            var inner = lines
            inner.removeFirst()
            if inner.last?.hasPrefix("```") == true { inner.removeLast() }
            return .codeFence(text: inner.joined(separator: "\n"))
        }

        if let (level, text) = Self.heading(lines[0]), lines.count == 1 {
            return .heading(level: level, text: text)
        }

        if lines.allSatisfy({ $0.hasPrefix("- ") || $0.hasPrefix("* ") }) {
            let items = lines.map { String($0.dropFirst(2)) }
            return .unorderedList(items: items)
        }

        if lines.allSatisfy({ Self.orderedPrefixLength($0) != nil }) {
            let items = lines.map { Self.stripOrderedPrefix($0) }
            return .orderedList(items: items)
        }

        if lines.allSatisfy({ $0.hasPrefix("> ") || $0 == ">" }) {
            let inner = lines
                .map { $0.hasPrefix("> ") ? String($0.dropFirst(2)) : "" }
                .joined(separator: "\n")
            return .blockquote(text: inner)
        }

        return .paragraph(text: trimmed)
    }

    private static let horizontalRulePattern: Set<String> = ["---", "***", "___"]

    // Hand-rolled `^\d+\.\s` matcher in lieu of NSRegularExpression — the
    // regex variant either forced `try!` (SwiftLint rejects) or required
    // a do/catch wrapper for a pattern that's a compile-time constant.
    // Returns the byte length of the matched prefix, or nil if the line
    // doesn't start with `<digits>. ` (one or more digits, a period, a
    // space).
    private static func orderedPrefixLength(_ line: String) -> Int? {
        var idx = line.startIndex
        var digits = 0
        while idx < line.endIndex, line[idx].isNumber {
            digits += 1
            idx = line.index(after: idx)
        }
        guard digits > 0, idx < line.endIndex, line[idx] == "." else { return nil }
        idx = line.index(after: idx)
        guard idx < line.endIndex, line[idx] == " " else { return nil }
        return line.distance(from: line.startIndex, to: line.index(after: idx))
    }

    private static func stripOrderedPrefix(_ line: String) -> String {
        guard let len = orderedPrefixLength(line) else { return line }
        return String(line.dropFirst(len))
    }

    private static func heading(_ line: String) -> (Int, String)? {
        var level = 0
        for ch in line.prefix(6) where ch == "#" { level += 1 }
        guard level > 0, level < line.count,
              line[line.index(line.startIndex, offsetBy: level)] == " "
        else { return nil }
        let text = String(line.dropFirst(level + 1))
        return (level, text)
    }

    // ── rendering ─────────────────────────────────────────────────────

    @ViewBuilder
    private func renderBlock(_ block: Block) -> some View {
        switch block {
        case .heading(let level, let text):
            Text(Self.inline(text))
                .font(Self.headingFont(level: level))
                .bold()

        case .paragraph(let text):
            Text(Self.inline(text))

        case .unorderedList(let items):
            VStack(alignment: .leading, spacing: 4) {
                ForEach(Array(items.enumerated()), id: \.offset) { _, item in
                    HStack(alignment: .firstTextBaseline, spacing: 8) {
                        Text("•").foregroundStyle(.secondary).frame(minWidth: 12, alignment: .leading)
                        Text(Self.inline(item))
                    }
                }
            }

        case .orderedList(let items):
            VStack(alignment: .leading, spacing: 4) {
                ForEach(Array(items.enumerated()), id: \.offset) { idx, item in
                    HStack(alignment: .firstTextBaseline, spacing: 8) {
                        Text("\(idx + 1).").foregroundStyle(.secondary).frame(minWidth: 20, alignment: .trailing)
                        Text(Self.inline(item))
                    }
                }
            }

        case .blockquote(let text):
            HStack(alignment: .top, spacing: 10) {
                RoundedRectangle(cornerRadius: 1.5)
                    .fill(Color.accentColor.opacity(0.6))
                    .frame(width: 3)
                Text(Self.inline(text))
                    .italic()
                    .foregroundStyle(.secondary)
            }

        case .codeFence(let text):
            Text(text)
                .font(.system(.callout, design: .monospaced))
                .padding(10)
                .frame(maxWidth: .infinity, alignment: .leading)
                .background(Color.secondary.opacity(0.12), in: RoundedRectangle(cornerRadius: 6))
                .textSelection(.enabled)

        case .horizontalRule:
            Divider()
        }
    }

    private static func headingFont(level: Int) -> Font {
        switch level {
        case 1: return .title
        case 2: return .title2
        case 3: return .title3
        default: return .headline
        }
    }

    // Inline-only parsing: bold, italic, strike, inline code, links. No
    // block intent to drop because each block is rendered separately by
    // the caller. Falls back to plain text on parse error.
    private static func inline(_ text: String) -> AttributedString {
        (try? AttributedString(
            markdown: text,
            options: AttributedString.MarkdownParsingOptions(
                interpretedSyntax: .inlineOnlyPreservingWhitespace,
                failurePolicy: .returnPartiallyParsedIfPossible
            )
        )) ?? AttributedString(text)
    }
}

#Preview("Markdown kitchen sink") {
    ScrollView {
        MarkdownView(markdown: """
        # Heading 1
        ## Heading 2
        ### Heading 3

        Plain **bold**, *italic*, ~~strike~~, `code`, and a [link](https://example.com).

        Bullet list:
        - First item
        - Second with **bold**
        - Third 🎉

        Numbered list:
        1. Step one
        2. Step two
        3. Step three

        > A blockquote with **emphasis** and emoji 🚀
        > line two of the quote

        ```swift
        let x = 42
        print(x)
        ```

        ---

        Final paragraph with emojis 🔥 ⚡ ✅
        """)
        .padding()
    }
}
