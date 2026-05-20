import SwiftUI

struct NotificationDetailView: View {
    let notification: InboxNotification

    var body: some View {
        ScrollView {
            VStack(alignment: .leading, spacing: 16) {
                Text(notification.title)
                    .font(.title2.bold())

                HStack(spacing: 8) {
                    Label(notification.source, systemImage: "tag")
                    Label(notification.timestamp.formatted(date: .abbreviated, time: .shortened),
                          systemImage: "clock")
                }
                .font(.caption)
                .foregroundStyle(.secondary)

                // MarkdownView splits the body on paragraph breaks and
                // renders each block (heading / list / blockquote /
                // code fence / paragraph / hr) as the right SwiftUI
                // view. Foundation's `Text(AttributedString)` with
                // `.full` markdown collapses paragraph structure into
                // the `presentationIntent` attribute, which Text
                // ignores — every `\n\n` disappeared visually. The
                // block-level renderer is what restores layout.
                // Prefer the producer-supplied long-form content
                // (`metadata.fullBody`, no 2000-char cap) when present;
                // otherwise fall back to the standard `body`. The push
                // banner + inbox row preview still use `body` — they
                // don't have room for a digest anyway.
                MarkdownView(markdown: notification.renderedBody)
                    .font(.body)

                if let deeplink = notification.deeplink {
                    Link(destination: deeplink) {
                        Label("Open link", systemImage: "arrow.up.right.square")
                    }
                }

                if let tags = notification.tags, !tags.isEmpty {
                    HStack {
                        ForEach(tags, id: \.self) { tag in
                            Text(tag)
                                .font(.caption2)
                                .padding(.horizontal, 8)
                                .padding(.vertical, 3)
                                .background(Color.secondary.opacity(0.15), in: Capsule())
                        }
                    }
                }
            }
            .frame(maxWidth: .infinity, alignment: .leading)
            .padding()
        }
        .navigationTitle(notification.title)
        .navigationBarTitleDisplayMode(.inline)
        .accessibilityIdentifier("notification.detail")
    }
}

#Preview("Detail — High Priority") {
    NotificationDetailView(notification: InboxNotification.mock(
        id: "p1", source: "deploy", title: "Production rollout failed",
        body: "Rollback initiated automatically. **v2.4.1** exceeded error budget.",
        priority: .high, tags: ["sre", "rollback"], timestamp: Date()
    ))
}

#Preview("Detail — Heavy Markdown") {
    NotificationDetailView(notification: InboxNotification.mock(
        id: "p9", source: "discord-bot", title: "Rich formatting stress test",
        body: "This is **bold**, *italic*, ~~strikethrough~~, `inline code`, "
            + "and a [link](https://example.com).\n\n"
            + "Blockquote:\n> Important note here\n\n"
            + "Bullet list:\n- First item\n- Second item\n"
            + "- Third item with **bold**\n\n"
            + "Numbered list:\n1. Step one\n2. Step two\n3. Step three\n\n"
            + "Mixed: ***bold italic***, `code with [brackets]`, and emojis 🚀 🔥 🎉",
        priority: .normal, tags: ["formatting", "test"], timestamp: Date()
    ))
}
