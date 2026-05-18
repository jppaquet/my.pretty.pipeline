import SwiftUI

struct InboxView: View {
    @Bindable var viewModel: InboxViewModel
    @Binding var selection: InboxNotification.ID?

    // Section ids that are currently collapsed. Default-empty = every
    // section starts expanded. Keying by section.id (the header string)
    // means stale ids for sections that no longer exist (after a grouping
    // change) are harmless dormant entries — no need to clear on switch.
    @State private var collapsedSections: Set<String> = []

    var body: some View {
        Group {
            switch viewModel.state {
            case .idle, .loading:
                ProgressView().controlSize(.large)
            case .loaded(let items, let continuation):
                List(selection: $selection) {
                    if viewModel.grouping == .none {
                        ForEach(items) { item in
                            InboxRow(notification: item)
                                .tag(item.id)
                                // Combine into a single accessibility element so
                                // `inbox.row.<id>` lands on the cell itself, not a
                                // descendant — XCUITest needs it on the cell.
                                .accessibilityElement(children: .combine)
                                .accessibilityIdentifier("inbox.row.\(item.id)")
                        }
                    } else {
                        let sections = groupedSections(items: items, by: viewModel.grouping)
                        ForEach(sections) { section in
                            Section {
                                if !collapsedSections.contains(section.id) {
                                    ForEach(section.items) { item in
                                        InboxRow(notification: item)
                                            .tag(item.id)
                                            .accessibilityElement(children: .combine)
                                            .accessibilityIdentifier("inbox.row.\(item.id)")
                                    }
                                }
                            } header: {
                                CollapsibleHeader(
                                    title: section.header,
                                    count: section.items.count,
                                    isCollapsed: collapsedSections.contains(section.id),
                                    toggle: {
                                        withAnimation(.easeInOut(duration: 0.18)) {
                                            if collapsedSections.contains(section.id) {
                                                collapsedSections.remove(section.id)
                                            } else {
                                                collapsedSections.insert(section.id)
                                            }
                                        }
                                    }
                                )
                                .accessibilityIdentifier("inbox.section.\(section.id)")
                            }
                        }
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
        .toolbar {
            ToolbarItem(placement: .topBarTrailing) {
                Menu {
                    Picker("Group by", selection: $viewModel.grouping) {
                        ForEach(InboxViewModel.Grouping.allCases, id: \.self) { mode in
                            Text(mode.rawValue).tag(mode)
                        }
                    }
                } label: {
                    Label("Group by", systemImage: "line.3.horizontal.decrease.circle")
                }
            }
        }
        .task { if case .idle = viewModel.state { await viewModel.load() } }
    }
}

private struct InboxRow: View {
    let notification: InboxNotification

    private var attributedBody: AttributedString {
        (try? AttributedString(
            markdown: notification.body,
            options: AttributedString.MarkdownParsingOptions(
                interpretedSyntax: .inlineOnlyPreservingWhitespace,
                failurePolicy: .returnPartiallyParsedIfPossible
            )
        )) ?? AttributedString(notification.body)
    }

    var body: some View {
        VStack(alignment: .leading, spacing: 4) {
            HStack {
                Text(notification.source)
                    .font(.caption)
                    .foregroundStyle(.secondary)
                Spacer()
                Text(notification.timestamp.formatted(date: .abbreviated, time: .shortened))
                    .font(.caption2)
                    .foregroundStyle(.secondary)
            }
            Text(notification.title)
                .font(.headline)
            Text(attributedBody)
                .font(.subheadline)
                .foregroundStyle(.secondary)
                .lineLimit(2)
        }
        .padding(.vertical, 2)
    }
}

private struct SectionData: Identifiable {
    let header: String
    let items: [InboxNotification]
    var id: String { header }
}

// Tappable section header for the grouped-list views. Renders the title +
// row count on the left and a chevron on the right that rotates when the
// section is collapsed. Button(.plain) on the whole row gives a sane
// touch target while keeping the visual identical to a default Section
// header on iOS.
private struct CollapsibleHeader: View {
    let title: String
    let count: Int
    let isCollapsed: Bool
    let toggle: () -> Void

    var body: some View {
        Button(action: toggle) {
            HStack {
                Text(title)
                Text("\(count)")
                    .font(.caption)
                    .foregroundStyle(.secondary)
                Spacer()
                Image(systemName: "chevron.down")
                    .font(.caption.weight(.semibold))
                    .foregroundStyle(.secondary)
                    .rotationEffect(.degrees(isCollapsed ? -90 : 0))
            }
            .contentShape(Rectangle())
        }
        .buttonStyle(.plain)
    }
}

private func groupedSections(items: [InboxNotification], by grouping: InboxViewModel.Grouping) -> [SectionData] {
    switch grouping {
    case .none:
        return [SectionData(header: "", items: items)]
    case .day:
        let calendar = Calendar.current
        let grouped = Dictionary(grouping: items) { item in
            calendar.startOfDay(for: item.timestamp)
        }
        return grouped.keys.sorted(by: >).compactMap { date in
            grouped[date].map { SectionData(header: dayHeader(date), items: $0) }
        }
    case .project:
        let grouped = Dictionary(grouping: items) { $0.source }
        return grouped.keys.sorted().compactMap { source in
            grouped[source].map { SectionData(header: source, items: $0) }
        }
    }
}

#Preview("Inbox — Preview Data") {
    let vm = InboxViewModel(api: MockNotifyAPI.previewSeeded())
    vm.state = .loaded(items: MockNotifyAPI.previewSeeded().pages[0].items, continuationToken: nil)
    return InboxView(viewModel: vm, selection: .constant(nil))
}

private func dayHeader(_ date: Date) -> String {
    let calendar = Calendar.current
    if calendar.isDateInToday(date) { return "Today" }
    if calendar.isDateInYesterday(date) { return "Yesterday" }
    return date.formatted(date: .long, time: .omitted)
}
