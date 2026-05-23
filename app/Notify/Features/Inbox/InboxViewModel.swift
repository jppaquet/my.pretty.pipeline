import Foundation
import Observation

@MainActor
@Observable
final class InboxViewModel {
    enum State: Equatable {
        case idle
        case loading
        case loaded(items: [InboxNotification], continuationToken: String?)
        case failed(message: String)
    }

    enum Grouping: String, CaseIterable {
        case none = "List"
        case day = "By Day"
        case project = "By Project"
        case priority = "By Priority"
        case type = "By Type"
    }

    var grouping: Grouping = .none

    var state: State = .idle

    private let api: NotifyAPI
    private let source: String?
    private let pageSize: Int

    init(api: NotifyAPI, source: String? = nil, pageSize: Int = 50) {
        self.api = api
        self.source = source
        self.pageSize = pageSize
    }

    func load() async {
        state = .loading
        do {
            let page = try await api.inbox(source: source, limit: pageSize, continuationToken: nil)
            state = .loaded(items: page.items, continuationToken: page.continuationToken)
        } catch {
            state = .failed(message: Self.message(for: error))
        }
    }

    // Don't delegate to load() — load() sets state = .loading, which flips
    // InboxView's switch from .loaded (List) to .loading (ProgressView). The
    // List unmounts mid-refresh, taking the .refreshable Task with it; the
    // inflight URLSession.data(for:) call gets cancelled and the user sees
    // "cancelled" in the failure banner. Keep the .loaded state alive across
    // the fetch so the List stays mounted and the Task survives.
    func refresh() async {
        do {
            let page = try await api.inbox(source: source, limit: pageSize, continuationToken: nil)
            state = .loaded(items: page.items, continuationToken: page.continuationToken)
        } catch {
            state = .failed(message: Self.message(for: error))
        }
    }

    func loadMore() async {
        guard case .loaded(let items, let token?) = state else { return }
        do {
            let page = try await api.inbox(source: source, limit: pageSize, continuationToken: token)
            state = .loaded(items: items + page.items, continuationToken: page.continuationToken)
        } catch {
            state = .failed(message: Self.message(for: error))
        }
    }

    // Optimistically flip `isRead` on the row, then POST /v1/inbox/{id}/read.
    // No-op if already read. API failure is swallowed: marking read is
    // idempotent and any future open will retry — surfacing a banner for
    // this would be noisier than the value of knowing it failed.
    func markRead(id: InboxNotification.ID) async {
        guard case .loaded(var items, let token) = state else { return }
        guard let idx = items.firstIndex(where: { $0.id == id }) else { return }
        guard items[idx].isRead != true else { return }
        let source = items[idx].source
        items[idx].isRead = true
        state = .loaded(items: items, continuationToken: token)
        try? await api.markInboxRead(id: id, source: source)
    }

    // Flip every unread item in `ids` to read, in one state update so the
    // UI redraws once. POSTs go out sequentially — the earlier parallel
    // TaskGroup version raced against MockNotifyAPI's non-thread-safe
    // `markReadCalls.append` in the unit suite (CI failure on iPad Pro
    // 13-inch (M4), `testMarkAllReadFlipsEveryUnreadInTheBatchAndPostsOnePerUnread`
    // saw 1 entry instead of 2). `markInboxRead` is idempotent +
    // best-effort, and a section batch caps at ~pageSize (50), so a
    // sequential loop costs at most ~2.5 s on a deliberate user tap.
    func markAllRead(ids: [InboxNotification.ID]) async {
        guard case .loaded(var items, let token) = state else { return }
        var toPost: [(id: String, source: String)] = []
        for id in ids {
            guard let idx = items.firstIndex(where: { $0.id == id }) else { continue }
            guard items[idx].isRead != true else { continue }
            items[idx].isRead = true
            toPost.append((id: items[idx].id, source: items[idx].source))
        }
        guard !toPost.isEmpty else { return }
        state = .loaded(items: items, continuationToken: token)
        for entry in toPost {
            try? await api.markInboxRead(id: entry.id, source: entry.source)
        }
    }

    // Count of items currently in state with isRead != true. Used by
    // InboxView to drive the app icon badge via UNUserNotificationCenter.
    // Computed each access — cheap given pageSize ≤ 50 in practice.
    var unreadCount: Int {
        guard case .loaded(let items, _) = state else { return 0 }
        return items.lazy.filter { $0.isRead != true }.count
    }

    // Optimistically drop the row from the list, then DELETE /v1/inbox/{id}.
    // On API failure we leave the row out — server filter will catch up on
    // the next refresh either way (soft delete is server-side).
    func delete(id: InboxNotification.ID) async {
        guard case .loaded(var items, let token) = state else { return }
        guard let idx = items.firstIndex(where: { $0.id == id }) else { return }
        let source = items[idx].source
        items.remove(at: idx)
        state = .loaded(items: items, continuationToken: token)
        try? await api.deleteInboxItem(id: id, source: source)
    }

    private static func message(for error: Error) -> String {
        if let api = error as? NotifyAPIError {
            switch api {
            case .http(let status, _):  return "Server returned \(status)"
            case .decoding:             return "Couldn't read the inbox response"
            case .transport(let msg):   return msg
            }
        }
        return error.localizedDescription
    }
}
