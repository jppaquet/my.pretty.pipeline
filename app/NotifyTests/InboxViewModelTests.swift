import XCTest
@testable import Notify

@MainActor
final class InboxViewModelTests: XCTestCase {
    private func sample(_ source: String, offsetSeconds: Int) -> InboxNotification {
        InboxNotification(
            id: UUID().uuidString,
            source: source,
            title: "title-\(source)",
            body: "body",
            type: "info",
            priority: .normal,
            tags: nil,
            deeplink: nil,
            deduplicationKey: nil,
            timestamp: Date(timeIntervalSince1970: 1_700_000_000 + TimeInterval(offsetSeconds)),
            envelopeId: UUID().uuidString,
            metadata: nil
        )
    }

    func testInitialStateIsIdle() {
        let vm = InboxViewModel(api: MockNotifyAPI())
        guard case .idle = vm.state else {
            return XCTFail("Expected .idle, got \(vm.state)")
        }
    }

    func testLoadTransitionsLoadingThenLoaded() async {
        let api = MockNotifyAPI()
        let item = sample("a", offsetSeconds: 0)
        api.pages = [InboxPage(items: [item], continuationToken: nil)]

        let vm = InboxViewModel(api: api)
        await vm.load()

        guard case .loaded(let items, let token) = vm.state else {
            return XCTFail("Expected .loaded, got \(vm.state)")
        }
        XCTAssertEqual(items, [item])
        XCTAssertNil(token)
        XCTAssertEqual(api.inboxCalls.count, 1)
    }

    func testLoadFailureSurfacesMessage() async {
        let api = MockNotifyAPI()
        api.inboxError = NotifyAPIError.http(status: 500, body: "boom")

        let vm = InboxViewModel(api: api)
        await vm.load()

        guard case .failed(let message) = vm.state else {
            return XCTFail("Expected .failed, got \(vm.state)")
        }
        XCTAssertEqual(message, "Server returned 500")
    }

    func testLoadMoreAppendsAndCarriesContinuationToken() async {
        let api = MockNotifyAPI()
        let firstItem = sample("a", offsetSeconds: 0)
        let secondItem = sample("b", offsetSeconds: 1)
        api.pages = [
            InboxPage(items: [firstItem], continuationToken: "next"),
            InboxPage(items: [secondItem], continuationToken: nil),
        ]

        let vm = InboxViewModel(api: api)
        await vm.load()
        await vm.loadMore()

        guard case .loaded(let items, let token) = vm.state else {
            return XCTFail("Expected .loaded, got \(vm.state)")
        }
        XCTAssertEqual(items, [firstItem, secondItem])
        XCTAssertNil(token)
        XCTAssertEqual(api.inboxCalls.count, 2)
        XCTAssertEqual(api.inboxCalls[1].token, "next")
    }

    func testLoadMoreNoOpsWithoutContinuationToken() async {
        let api = MockNotifyAPI()
        let item = sample("a", offsetSeconds: 0)
        api.pages = [InboxPage(items: [item], continuationToken: nil)]

        let vm = InboxViewModel(api: api)
        await vm.load()
        await vm.loadMore()

        XCTAssertEqual(api.inboxCalls.count, 1)
    }

    func testRefreshReplacesItems() async {
        let api = MockNotifyAPI()
        let firstItem = sample("a", offsetSeconds: 0)
        let secondItem = sample("b", offsetSeconds: 1)
        api.pages = [
            InboxPage(items: [firstItem], continuationToken: nil),
            InboxPage(items: [secondItem], continuationToken: nil),
        ]

        let vm = InboxViewModel(api: api)
        await vm.load()
        await vm.refresh()

        guard case .loaded(let items, _) = vm.state else {
            return XCTFail("Expected .loaded, got \(vm.state)")
        }
        XCTAssertEqual(items, [secondItem])
    }

    func testSourceFilterIsForwarded() async {
        let api = MockNotifyAPI()
        api.pages = [InboxPage(items: [], continuationToken: nil)]

        let vm = InboxViewModel(api: api, source: "alerts")
        await vm.load()

        XCTAssertEqual(api.inboxCalls.first?.source, "alerts")
    }

    // ── markRead / delete ──────────────────────────────────────────

    func testMarkReadFlipsLocalAndCallsAPI() async {
        let api = MockNotifyAPI()
        let item = sample("home", offsetSeconds: 0)
        api.pages = [InboxPage(items: [item], continuationToken: nil)]

        let vm = InboxViewModel(api: api)
        await vm.load()
        await vm.markRead(id: item.id)

        guard case .loaded(let items, _) = vm.state else {
            return XCTFail("Expected .loaded, got \(vm.state)")
        }
        XCTAssertEqual(items.first?.isRead, true)
        XCTAssertEqual(api.markReadCalls, [InboxItemCall(id: item.id, source: "home")])
    }

    func testMarkReadIsIdempotentWhenAlreadyRead() async {
        let api = MockNotifyAPI()
        var item = sample("home", offsetSeconds: 0)
        item.isRead = true
        api.pages = [InboxPage(items: [item], continuationToken: nil)]

        let vm = InboxViewModel(api: api)
        await vm.load()
        await vm.markRead(id: item.id)

        XCTAssertEqual(api.markReadCalls.count, 0, "should not POST when row is already read")
    }

    func testMarkReadIgnoresUnknownId() async {
        let api = MockNotifyAPI()
        api.pages = [InboxPage(items: [sample("home", offsetSeconds: 0)], continuationToken: nil)]

        let vm = InboxViewModel(api: api)
        await vm.load()
        await vm.markRead(id: "no-such-id")

        XCTAssertEqual(api.markReadCalls.count, 0)
    }

    func testMarkAllReadFlipsEveryUnreadInTheBatchAndPostsOnePerUnread() async {
        let api = MockNotifyAPI()
        let first = sample("home", offsetSeconds: 0)
        let second = sample("home", offsetSeconds: 1)
        var alreadyRead = sample("home", offsetSeconds: 2)
        alreadyRead.isRead = true // already-read row in the batch — should not POST
        api.pages = [InboxPage(items: [first, second, alreadyRead], continuationToken: nil)]

        let vm = InboxViewModel(api: api)
        await vm.load()
        await vm.markAllRead(ids: [first.id, second.id, alreadyRead.id])

        guard case .loaded(let items, _) = vm.state else {
            return XCTFail("Expected .loaded, got \(vm.state)")
        }
        XCTAssertTrue(items.allSatisfy { $0.isRead == true })
        XCTAssertEqual(api.markReadCalls.count, 2, "should only POST for the two previously-unread rows")
        XCTAssertEqual(Set(api.markReadCalls.map { $0.id }), Set([first.id, second.id]))
    }

    func testMarkAllReadNoopWhenEveryItemAlreadyRead() async {
        let api = MockNotifyAPI()
        var first = sample("home", offsetSeconds: 0)
        var second = sample("home", offsetSeconds: 1)
        first.isRead = true
        second.isRead = true
        api.pages = [InboxPage(items: [first, second], continuationToken: nil)]

        let vm = InboxViewModel(api: api)
        await vm.load()
        await vm.markAllRead(ids: [first.id, second.id])

        XCTAssertEqual(api.markReadCalls.count, 0)
    }

    func testUnreadCountReflectsLoadedItems() async {
        let api = MockNotifyAPI()
        var read = sample("home", offsetSeconds: 0)
        read.isRead = true
        let unread1 = sample("home", offsetSeconds: 1)
        let unread2 = sample("home", offsetSeconds: 2)
        api.pages = [InboxPage(items: [read, unread1, unread2], continuationToken: nil)]

        let vm = InboxViewModel(api: api)
        XCTAssertEqual(vm.unreadCount, 0, "idle state has no unread")
        await vm.load()
        XCTAssertEqual(vm.unreadCount, 2)

        await vm.markRead(id: unread1.id)
        XCTAssertEqual(vm.unreadCount, 1)

        await vm.markAllRead(ids: [unread2.id])
        XCTAssertEqual(vm.unreadCount, 0)
    }

    func testDeleteRemovesLocalAndCallsAPI() async {
        let api = MockNotifyAPI()
        let target = sample("home", offsetSeconds: 0)
        let other = sample("other", offsetSeconds: 1)
        api.pages = [InboxPage(items: [target, other], continuationToken: nil)]

        let vm = InboxViewModel(api: api)
        await vm.load()
        await vm.delete(id: target.id)

        guard case .loaded(let items, _) = vm.state else {
            return XCTFail("Expected .loaded, got \(vm.state)")
        }
        XCTAssertEqual(items.map(\.id), [other.id])
        XCTAssertEqual(api.deleteCalls, [InboxItemCall(id: target.id, source: "home")])
    }

    func testDeleteFailureStillRemovesLocally() async {
        let api = MockNotifyAPI()
        let item = sample("home", offsetSeconds: 0)
        api.pages = [InboxPage(items: [item], continuationToken: nil)]
        api.mutationError = NotifyAPIError.http(status: 500, body: "boom")

        let vm = InboxViewModel(api: api)
        await vm.load()
        await vm.delete(id: item.id)

        // Best-effort: optimistic remove sticks even on API failure; the next
        // refresh will reconcile against the server's soft-delete filter.
        guard case .loaded(let items, _) = vm.state else {
            return XCTFail("Expected .loaded, got \(vm.state)")
        }
        XCTAssertTrue(items.isEmpty)
    }
}
