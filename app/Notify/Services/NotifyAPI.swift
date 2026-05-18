// swiftlint:disable file_length
//
// This file deliberately bundles three concerns — the NotifyAPI protocol, the
// real NotifyAPIClient, and the MockNotifyAPI fixture used by tests + the
// LOCAL_UI_PREVIEW build — to keep the .xcodeproj as the source of truth
// for the file list (no xcodegen / project.yml in this project). Splitting
// MockNotifyAPI to its own file would require an Xcode-UI edit and a
// .pbxproj change; deferred to a future cleanup pass.

import Foundation

protocol NotifyAPI {
    func registerDevice(_ registration: DeviceRegistration) async throws -> DeviceRegistrationResponse
    func inbox(source: String?, limit: Int, continuationToken: String?) async throws -> InboxPage
    func markInboxRead(id: String, source: String) async throws
    func deleteInboxItem(id: String, source: String) async throws
}

enum NotifyAPIError: Error, Equatable {
    case http(status: Int, body: String?)
    case decoding(String)
    case transport(String)
}

// Resolves the Bearer token at call time so the API client picks up the latest
// JWT from the Keychain on every request. Returning nil means the user is
// signed out; the request still goes out without an Authorization header and
// the backend will respond 401 — letting the view-model surface the sign-in
// gate to the user. The function key was retired in PR-C.
typealias BearerTokenProvider = @Sendable () -> String?

final class NotifyAPIClient: NotifyAPI {
    let baseURL: URL
    let session: URLSession
    private let bearer: BearerTokenProvider

    init(baseURL: URL, bearer: @escaping BearerTokenProvider, session: URLSession = .shared) {
        self.baseURL = baseURL
        self.bearer = bearer
        self.session = session
    }

    func registerDevice(_ registration: DeviceRegistration) async throws -> DeviceRegistrationResponse {
        var request = URLRequest(url: baseURL.appendingPathComponent("v1/devices"))
        request.httpMethod = "POST"
        request.setValue("application/json", forHTTPHeaderField: "content-type")
        applyAuth(to: &request)
        request.httpBody = try Self.encoder.encode(registration)

        return try await send(request)
    }

    func inbox(source: String?, limit: Int, continuationToken: String?) async throws -> InboxPage {
        var components = URLComponents(url: baseURL.appendingPathComponent("v1/inbox"), resolvingAgainstBaseURL: false)
        var query: [URLQueryItem] = [URLQueryItem(name: "limit", value: String(limit))]
        if let source { query.append(URLQueryItem(name: "source", value: source)) }
        if let continuationToken { query.append(URLQueryItem(name: "continuationToken", value: continuationToken)) }
        components?.queryItems = query

        guard let url = components?.url else {
            throw NotifyAPIError.transport("Could not build inbox URL")
        }

        var request = URLRequest(url: url)
        applyAuth(to: &request)

        return try await send(request)
    }

    func markInboxRead(id: String, source: String) async throws {
        try await mutateInboxItem(id: id, source: source, method: "POST", suffix: "/read")
    }

    func deleteInboxItem(id: String, source: String) async throws {
        try await mutateInboxItem(id: id, source: source, method: "DELETE", suffix: nil)
    }

    // Shared shape for `v1/inbox/{id}[/suffix]?source=…`. `id` is a full
    // Cosmos doc id (`{baseId}:{userId}`); `:` is allowed in path segments
    // per RFC 3986 §3.3 so the segment is passed through unencoded.
    private func mutateInboxItem(id: String, source: String, method: String, suffix: String?) async throws {
        let path = "v1/inbox/\(id)\(suffix ?? "")"
        var components = URLComponents(url: baseURL.appendingPathComponent(path), resolvingAgainstBaseURL: false)
        components?.queryItems = [URLQueryItem(name: "source", value: source)]
        guard let url = components?.url else {
            throw NotifyAPIError.transport("Could not build inbox-item URL")
        }
        var request = URLRequest(url: url)
        request.httpMethod = method
        applyAuth(to: &request)
        try await sendNoBody(request)
    }

    private func applyAuth(to request: inout URLRequest) {
        if let token = bearer(), !token.isEmpty {
            request.setValue("Bearer \(token)", forHTTPHeaderField: "Authorization")
        }
    }

    private func send<T: Decodable>(_ request: URLRequest) async throws -> T {
        let data = try await performAndCheck(request)
        do {
            return try Self.decoder.decode(T.self, from: data)
        } catch {
            throw NotifyAPIError.decoding(String(describing: error))
        }
    }

    private func sendNoBody(_ request: URLRequest) async throws {
        _ = try await performAndCheck(request)
    }

    private func performAndCheck(_ request: URLRequest) async throws -> Data {
        let data: Data
        let response: URLResponse
        do {
            (data, response) = try await session.data(for: request)
        } catch {
            throw NotifyAPIError.transport(error.localizedDescription)
        }
        guard let http = response as? HTTPURLResponse else {
            throw NotifyAPIError.transport("Non-HTTP response")
        }
        guard (200..<300).contains(http.statusCode) else {
            throw NotifyAPIError.http(status: http.statusCode, body: String(data: data, encoding: .utf8))
        }
        return data
    }

    static let decoder: JSONDecoder = {
        let decoder = JSONDecoder()
        decoder.dateDecodingStrategy = .iso8601WithFractionalSeconds
        return decoder
    }()

    static let encoder: JSONEncoder = {
        let encoder = JSONEncoder()
        encoder.dateEncodingStrategy = .iso8601WithFractionalSeconds
        return encoder
    }()
}

// .NET's NotifyJson.Options writes timestamps with fractional seconds (e.g.
// "2026-04-28T14:00:00.1234567Z") — Foundation's `.iso8601` rejects those, so
// we install an ISO8601 formatter that accepts the fraction.
private extension JSONDecoder.DateDecodingStrategy {
    static var iso8601WithFractionalSeconds: JSONDecoder.DateDecodingStrategy {
        .custom { decoder in
            let container = try decoder.singleValueContainer()
            let raw = try container.decode(String.self)
            if let date = ISO8601Formatters.withFraction.date(from: raw)
                ?? ISO8601Formatters.withoutFraction.date(from: raw) {
                return date
            }
            throw DecodingError.dataCorruptedError(in: container, debugDescription: "Unparseable timestamp \(raw)")
        }
    }
}

private extension JSONEncoder.DateEncodingStrategy {
    static var iso8601WithFractionalSeconds: JSONEncoder.DateEncodingStrategy {
        .custom { date, encoder in
            var container = encoder.singleValueContainer()
            try container.encode(ISO8601Formatters.withFraction.string(from: date))
        }
    }
}

private enum ISO8601Formatters {
    static let withFraction: ISO8601DateFormatter = {
        let formatter = ISO8601DateFormatter()
        formatter.formatOptions = [.withInternetDateTime, .withFractionalSeconds]
        return formatter
    }()
    static let withoutFraction: ISO8601DateFormatter = {
        let formatter = ISO8601DateFormatter()
        formatter.formatOptions = [.withInternetDateTime]
        return formatter
    }()
}

// In-memory mock for tests + SwiftUI previews. Drives the VM without a network.
final class MockNotifyAPI: NotifyAPI {
    var pages: [InboxPage] = []
    var registerResponse: DeviceRegistrationResponse?
    var registerError: Error?
    var inboxError: Error?
    private(set) var registerCalls: [DeviceRegistration] = []
    private(set) var inboxCalls: [InboxCall] = []
    private var pageCursor = 0

    func registerDevice(_ registration: DeviceRegistration) async throws -> DeviceRegistrationResponse {
        registerCalls.append(registration)
        if let registerError { throw registerError }
        return registerResponse ?? DeviceRegistrationResponse(installationId: "mock-installation")
    }

    func inbox(source: String?, limit: Int, continuationToken: String?) async throws -> InboxPage {
        inboxCalls.append(InboxCall(source: source, limit: limit, token: continuationToken))
        if let inboxError { throw inboxError }
        defer { pageCursor = min(pageCursor + 1, pages.count) }
        return pageCursor < pages.count ? pages[pageCursor] : InboxPage(items: [], continuationToken: nil)
    }

    private(set) var markReadCalls: [InboxItemCall] = []
    private(set) var deleteCalls: [InboxItemCall] = []
    var mutationError: Error?

    func markInboxRead(id: String, source: String) async throws {
        markReadCalls.append(InboxItemCall(id: id, source: source))
        if let mutationError { throw mutationError }
    }

    func deleteInboxItem(id: String, source: String) async throws {
        deleteCalls.append(InboxItemCall(id: id, source: source))
        if let mutationError { throw mutationError }
    }

    // Fixture used by AppContainer for LOCAL_UI_PREVIEW builds. Richer than
    // the UI-test fixture: multiple sources, days, priorities, tags, and
    // deeplinks so grouping / detail layouts can be iterated without a backend.
    static func previewSeeded() -> MockNotifyAPI {
        let mock = MockNotifyAPI()
        mock.pages = [InboxPage(items: Self.previewItems(), continuationToken: nil)]
        return mock
    }

    private static let previewStressBody = "This is **bold**, *italic*, ~~strikethrough~~, "
        + "`inline code`, and a [link](https://example.com).\n\n"
        + "Blockquote:\n> Important note here\n\n"
        + "Bullet list:\n- First item\n- Second item\n"
        + "- Third item with **bold**\n\n"
        + "Numbered list:\n1. Step one\n2. Step two\n3. Step three\n\n"
        + "Mixed: ***bold italic***, `code with [brackets]`, and emojis 🚀 🔥 🎉"

    private static let previewChangelogBody = "**Full changelog:**\n\n"
        + "### Added\n- New ~~dark~~ light theme toggle\n"
        + "- Support for *markdown* in notifications\n\n"
        + "### Fixed\n- Memory leak in `ImageCache`\n"
        + "- Race condition on `AuthViewModel`\n\n"
        + "### Removed\n- Legacy `NotifyFunctionKey` (see PR-C)\n\n"
        + "Code block:\n```swift\nlet container = AppContainer.makeDefault()\n```\n\n"
        + "> ⚠️ **Breaking:** Requires iOS 17+"

    // Kitchen-sink markdown body. Exercises every formatting axis the
    // detail view's `.full` markdown renderer is expected to handle, plus
    // emoji and long paragraphs so layout/scroll behavior is visible.
    // Paired with `priority: .high` + `type: "alert"` so the row carries
    // the red row tint + red `exclamationmark.octagon.fill` icon for the
    // color axis the renderer itself doesn't support.
    private static let previewKitchenSinkBody = """
    🚨 **Critical incident — please read carefully** 🚨

    Production cluster `api-west` lost quorum at **02:47 UTC** after a \
    rolling restart picked up a *corrupted* configuration. Auto-rollback \
    fired and the cluster is back to the last known-good revision.

    ## What we know

    - **Blast radius:** ~12% of inbound traffic between 02:47–02:51 UTC
    - **Root cause:** `etcd` leader election timeout misconfigured (3 s → 30 s)
    - **Detection:** Synthetic probe pager fired within 18 s 🔥
    - **Mitigation:** Auto-rollback completed at 02:51 UTC ✅

    > ⚠️ **Heads-up:** The same misconfiguration may exist in `api-east` \
    > and `api-eu` — a sweep is queued for the next maintenance window. \
    > Block any deploys that touch `etcd-config.yaml` until then.

    ### Action items

    1. Audit `etcd-config.yaml` across all clusters — see [runbook](https://example.com/runbook/etcd).
    2. Add a CI check that rejects values outside the safe band (1–10 s).
    3. Backfill a postmortem ticket — owner: **@you**, due **Friday**.
    4. ~~Rotate the on-call schedule~~ already handled by the bot.

    ### Things that *worked* well 🎉

    - Synthetic probe pager fired in `< 20 s` (target: ≤ 60 s)
    - Auto-rollback executed cleanly with `0` manual interventions
    - Status page updated in `< 90 s` — Customer Success had a heads-up before \
      the first ticket came in

    ```yaml
    # Safe etcd config — pin this in CI
    election-timeout: 5s    # was 30s
    heartbeat-interval: 500ms
    ```

    ---

    **Severity matrix** (legend: 🔴 critical · 🟠 high · 🟡 medium · 🟢 low):

    | Service     | Before | After |
    | ----------- | ------ | ----- |
    | api-west    | 🔴     | 🟢    |
    | api-east    | 🟠     | 🟠    |
    | api-eu      | 🟠     | 🟠    |

    *Reply with* `ack` *to acknowledge, or* `pager` *to escalate to the on-call lead.*

    Long-tail context: this is the third incident this quarter traced to a \
    config-drift between clusters. We've been carrying a tech-debt item to \
    canonicalize cluster config under a single source of truth since \
    **2026-Q1**; the postmortem from this incident will be the forcing \
    function to actually schedule that work into a sprint. The team lead \
    will pair with infra-platform to draft an RFC by end of next week — \
    expect a follow-up notification when the doc is ready for review. 📄
    """

    private static func previewItems() -> [InboxNotification] {
        let now = Date()
        let cal = Calendar.current
        let yesterday = cal.date(byAdding: .day, value: -1, to: now) ?? now
        let twoDaysAgo = cal.date(byAdding: .day, value: -2, to: now) ?? now
        let lastWeek = cal.date(byAdding: .day, value: -5, to: now) ?? now

        return [
            .mock(id: "p1", source: "deploy", title: "Production rollout failed",
                  body: "Rollback initiated automatically. **v2.4.1** exceeded error budget.",
                  priority: .high, tags: ["sre", "rollback"],
                  timestamp: cal.date(byAdding: .minute, value: -5, to: now) ?? now),
            .mock(id: "p2", source: "ci", title: "PR #149 checks passing",
                  body: "All **12** workflows green on `feat/admin-app-pr2`.",
                  priority: .normal, tags: ["github", "ci"],
                  deeplink: URL(string: "https://github.com/jpp/my.pretty.pipeline/pull/149"),
                  timestamp: cal.date(byAdding: .hour, value: -1, to: now) ?? now),
            .mock(id: "p3", source: "deploy", title: "Staging deployment succeeded",
                  body: "> Rollout completed in **42 s**\n- 3 pods updated\n- 0 restarts",
                  priority: .normal, tags: ["deploy", "staging"],
                  timestamp: cal.date(byAdding: .hour, value: -3, to: now) ?? now),
            .mock(id: "p4", source: "home-pipeline", title: "NAS disk space warning",
                  body: "NAS `/volume1` is **87 %** full. Consider running cleanup.",
                  priority: .high, tags: ["nas", "storage"],
                  timestamp: cal.date(byAdding: .hour, value: -6, to: now) ?? now),
            .mock(id: "p5", source: "cron", title: "Weekly backup completed",
                  body: "200 files archived to `s3://backups/weekly`.",
                  priority: .low, timestamp: yesterday),
            .mock(id: "p6", source: "ci", title: "Nightly tests passed",
                  body: "All **847** tests green on `main`.",
                  priority: .low, tags: ["ci"],
                  timestamp: cal.date(byAdding: .hour, value: -3, to: yesterday) ?? yesterday),
            .mock(id: "p7", source: "monitoring", title: "CPU spike on api-03",
                  body: "Load average **12.4** — investigate before on-call shift ends.",
                  priority: .high, tags: ["alert", "cpu"],
                  timestamp: twoDaysAgo),
            .mock(id: "p8", source: "security", title: "New advisory: CVE-2026-1234",
                  body: "Patch available for `libfoo`. Severity: **critical**.",
                  priority: .high, tags: ["cve", "security"],
                  deeplink: URL(string: "https://nvd.nist.gov/vuln/detail/CVE-2026-1234"),
                  timestamp: lastWeek),
            .mock(id: "p9", source: "discord-bot", title: "Rich formatting stress test",
                  body: previewStressBody, priority: .normal, tags: ["formatting", "test"],
                  timestamp: cal.date(byAdding: .minute, value: -10, to: now) ?? now),
            .mock(id: "p10", source: "deploy", title: "Deployment changelog v2.5.0",
                  body: previewChangelogBody, priority: .high, tags: ["changelog", "deploy"],
                  timestamp: cal.date(byAdding: .minute, value: -15, to: now) ?? now),
            previewKitchenSinkMock(timestamp: cal.date(byAdding: .minute, value: -2, to: now) ?? now),
        ]
    }

    // Markdown kitchen sink — everything the detail view's `.full`
    // renderer should handle, plus heavy emoji and a long tail of
    // body text for scroll testing. Pinned `priority: .high` +
    // `type: "alert"` so the row exercises the priority background +
    // type icon styling alongside the markdown body.
    // Hoisted out of `previewItems()` to keep that function's body
    // under SwiftLint's 50-line ceiling.
    private static func previewKitchenSinkMock(timestamp: Date) -> InboxNotification {
        .mock(
            id: "p11",
            source: "incident-bot",
            title: "🚨 Incident: api-west quorum loss",
            body: previewKitchenSinkBody,
            type: "alert",
            priority: .high,
            tags: ["incident", "critical", "etcd"],
            deeplink: URL(string: "https://example.com/incident/2026-05-18-api-west"),
            timestamp: timestamp
        )
    }

    // Fixture used by AppContainer when the app is launched with
    // -NotifyUITestMockBackend. IDs are stable so InboxFlowTests can locate
    // `inbox.row.ui-test-1` on both iPhone and iPad destinations.
    static func uiTestSeeded() -> MockNotifyAPI {
        let mock = MockNotifyAPI()
        let now = Date()
        let cal = Calendar.current
        let yesterday = cal.date(byAdding: .day, value: -1, to: now) ?? now
        let twoDaysAgo = cal.date(byAdding: .day, value: -2, to: now) ?? now

        mock.pages = [InboxPage(items: [
            .mock(id: "ui-test-1", source: "home-pipeline", title: "Backup failed",
                  body: "**rsync** exited `12` on **pi-01** — check `df -h` output",
                  priority: .high, tags: ["pi-01"], timestamp: now),
            .mock(id: "today-2", source: "build-system", title: "PR #142 merged",
                  body: "Branch `feature/apns-rewrite` merged into `main` by **jpp**",
                  priority: .normal, tags: ["ci", "merged"],
                  deeplink: URL(string: "https://github.com/jpp/my.pretty.pipeline/pull/142"),
                  timestamp: cal.date(byAdding: .hour, value: -2, to: now) ?? now),
            .mock(id: "today-3", source: "staging-deploy", title: "Deployment succeeded",
                  body: "> Rollout completed in **42 s**\n- 3 pods updated\n- 0 restarts",
                  priority: .normal, tags: ["deploy", "staging"],
                  timestamp: cal.date(byAdding: .hour, value: -4, to: now) ?? now),
            .mock(id: "yesterday-1", source: "cron", title: "Weekly cleanup ok",
                  body: "200 files removed from `~/Downloads`", timestamp: yesterday),
            .mock(id: "yesterday-2", source: "build-system", title: "Nightly tests passed",
                  body: "All **847** tests green on `main`",
                  priority: .low, tags: ["ci"],
                  timestamp: cal.date(byAdding: .hour, value: -3, to: yesterday) ?? yesterday),
            .mock(id: "2days-1", source: "home-pipeline", title: "Disk space warning",
                  body: "NAS `/volume1` is **87 %** full",
                  priority: .high, tags: ["nas", "storage"], timestamp: twoDaysAgo),
        ], continuationToken: nil)]
        return mock
    }
}

struct InboxCall: Equatable {
    let source: String?
    let limit: Int
    let token: String?
}

struct InboxItemCall: Equatable {
    let id: String
    let source: String
}
