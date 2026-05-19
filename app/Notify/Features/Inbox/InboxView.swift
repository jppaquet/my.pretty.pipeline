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
                                .listRowBackground(item.priority.rowBackground)
                                .swipeActions(edge: .trailing, allowsFullSwipe: true) {
                                    Button(role: .destructive) {
                                        Task { await viewModel.delete(id: item.id) }
                                    } label: {
                                        Label("Delete", systemImage: "trash")
                                    }
                                    .accessibilityIdentifier("inbox.row.\(item.id).delete")
                                }
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
                                            .listRowBackground(item.priority.rowBackground)
                                            .swipeActions(edge: .trailing, allowsFullSwipe: true) {
                                                Button(role: .destructive) {
                                                    Task { await viewModel.delete(id: item.id) }
                                                } label: {
                                                    Label("Delete", systemImage: "trash")
                                                }
                                                .accessibilityIdentifier("inbox.row.\(item.id).delete")
                                            }
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

    // Unread = no `isRead` field OR `isRead == false`. The title gets a
    // heavier weight so the row stands out from already-opened siblings;
    // the prefix dot reinforces it for users on Smart Invert / increased-
    // contrast settings where font-weight alone is hard to read.
    private var isUnread: Bool { notification.isRead != true }

    var body: some View {
        VStack(alignment: .leading, spacing: 4) {
            HStack(spacing: 6) {
                Image(systemName: TypeStyling.symbol(for: notification.type))
                    .font(.caption.weight(.semibold))
                    .foregroundStyle(TypeStyling.tint(for: notification.type))
                    .accessibilityHidden(true)
                Text(notification.source)
                    .font(.caption)
                    .foregroundStyle(.secondary)
                Spacer()
                Text(notification.timestamp.formatted(date: .abbreviated, time: .shortened))
                    .font(.caption2)
                    .foregroundStyle(.secondary)
            }
            HStack(alignment: .firstTextBaseline, spacing: 6) {
                if isUnread {
                    Circle()
                        .fill(Color.accentColor)
                        .frame(width: 8, height: 8)
                        .accessibilityHidden(true)
                }
                Text(notification.title)
                    .font(.headline)
                    .fontWeight(isUnread ? .bold : .regular)
            }
            Text(attributedBody)
                .font(.subheadline)
                .foregroundStyle(.secondary)
                .lineLimit(2)
        }
        .padding(.vertical, 2)
        .accessibilityLabel(Text(isUnread ? "Unread. \(notification.title)" : notification.title))
    }
}

// Type styling. Severity ladder is info → warning → alert; anything else is
// treated as a custom category (neutral tag icon). Symbol + tint are
// foreground-only — background tint is driven by Priority on the row.
private enum TypeStyling {
    static func symbol(for type: String) -> String {
        switch type {
        case "info":    return "info.circle"
        case "warning": return "exclamationmark.triangle.fill"
        case "alert":   return "exclamationmark.octagon.fill"
        default:        return "tag"
        }
    }

    static func tint(for type: String) -> Color {
        switch type {
        case "info":    return .blue
        case "warning": return .orange
        case "alert":   return .red
        default:        return .secondary
        }
    }
}

// Priority → list-row background. Subtle enough to read at a glance
// without overwhelming the row content; values picked to hold up in both
// light and dark mode. `nil` defers to the system default background.
extension Priority {
    var rowBackground: Color? {
        switch self {
        case .high:   return Color.red.opacity(0.12)
        case .normal: return nil
        case .low:    return Color.gray.opacity(0.06)
        }
    }

    // Sort order from most-urgent to least-urgent. Used by the `By Priority`
    // grouping so high sections land at the top regardless of how Dictionary
    // happens to iterate the keys.
    var sortOrder: Int {
        switch self {
        case .high:   return 0
        case .normal: return 1
        case .low:    return 2
        }
    }

    var displayName: String {
        switch self {
        case .high:   return "High"
        case .normal: return "Normal"
        case .low:    return "Low"
        }
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
    case .priority:
        let grouped = Dictionary(grouping: items) { $0.priority }
        return grouped.keys.sorted { $0.sortOrder < $1.sortOrder }.compactMap { priority in
            grouped[priority].map { SectionData(header: priority.displayName, items: $0) }
        }
    case .type:
        // Severity-ordered ladder for known types, then any custom types
        // sorted alphabetically below. Backend allows arbitrary `type`
        // strings (schema: "info | warning | alert | <custom>"), so we
        // can't enumerate — fall back to lex order for the long tail.
        let known: [String: Int] = ["alert": 0, "warning": 1, "info": 2]
        let grouped = Dictionary(grouping: items) { $0.type }
        return grouped.keys.sorted { lhs, rhs in
            let lhsRank = known[lhs] ?? Int.max
            let rhsRank = known[rhs] ?? Int.max
            if lhsRank != rhsRank { return lhsRank < rhsRank }
            return lhs < rhs
        }.compactMap { type in
            grouped[type].map { SectionData(header: type.capitalized, items: $0) }
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
