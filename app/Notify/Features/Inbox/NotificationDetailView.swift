import SwiftUI

struct InboxNotificationDetailView: View {
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

                Text(notification.body)
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
