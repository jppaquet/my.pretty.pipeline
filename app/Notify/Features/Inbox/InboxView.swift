import SwiftUI

struct InboxView: View {
    @Bindable var viewModel: InboxViewModel
    @Binding var selection: InboxNotification.ID?

    var body: some View {
        Group {
            switch viewModel.state {
            case .idle, .loading:
                ProgressView().controlSize(.large)
            case .loaded(let items, let continuation):
                List(selection: $selection) {
                    ForEach(items) { item in
                        InboxRow(notification: item)
                            .tag(item.id)
                            // Combine into a single accessibility element so
                            // `inbox.row.<id>` lands on the cell itself, not a
                            // descendant — XCUITest needs it on the cell.
                            .accessibilityElement(children: .combine)
                            .accessibilityIdentifier("inbox.row.\(item.id)")
                    }
                    if continuation != nil {
                        ProgressView()
                            .frame(maxWidth: .infinity)
                            .task { await viewModel.loadMore() }
                    }
                }
                .refreshable { await viewModel.refresh() }
                .accessibilityIdentifier("inbox.list")
                .overlay {
                    if items.isEmpty {
                        ContentUnavailableView(
                            "Inbox is empty",
                            systemImage: "tray",
                            description: Text("New notifications will appear here.")
                        )
                    }
                }
            case .failed(let message):
                ContentUnavailableView(
                    "Couldn't load inbox",
                    systemImage: "exclamationmark.triangle",
                    description: Text(message)
                )
                .overlay(alignment: .bottom) {
                    Button("Retry") { Task { await viewModel.load() } }
                        .padding()
                }
            }
        }
        .navigationTitle("Inbox")
        .task { if case .idle = viewModel.state { await viewModel.load() } }
    }
}

private struct InboxRow: View {
    let notification: InboxNotification

    var body: some View {
        VStack(alignment: .leading, spacing: 4) {
            HStack {
                Text(notification.source)
                    .font(.caption)
                    .foregroundStyle(.secondary)
                Spacer()
                Text(notification.timestamp, style: .relative)
                    .font(.caption2)
                    .foregroundStyle(.secondary)
            }
            Text(notification.title)
                .font(.headline)
            Text(notification.body)
                .font(.subheadline)
                .foregroundStyle(.secondary)
                .lineLimit(2)
        }
        .padding(.vertical, 2)
    }
}
