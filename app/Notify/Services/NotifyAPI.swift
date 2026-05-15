import Foundation

protocol NotifyAPI {
    func registerDevice(_ registration: DeviceRegistration) async throws -> DeviceRegistrationResponse
    func inbox(source: String?, limit: Int, continuationToken: String?) async throws -> InboxPage
}

enum NotifyAPIError: Error, Equatable {
    case http(status: Int, body: String?)
    case decoding(String)
    case transport(String)
}

struct NotifyAPIClient: NotifyAPI {
    let baseURL: URL
    let functionKey: String
    let session: URLSession

    init(baseURL: URL, functionKey: String, session: URLSession = .shared) {
        self.baseURL = baseURL
        self.functionKey = functionKey
        self.session = session
    }

    func registerDevice(_ registration: DeviceRegistration) async throws -> DeviceRegistrationResponse {
        var request = URLRequest(url: baseURL.appendingPathComponent("v1/devices"))
        request.httpMethod = "POST"
        request.setValue("application/json", forHTTPHeaderField: "content-type")
        request.setValue(functionKey, forHTTPHeaderField: "x-functions-key")
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
        request.setValue(functionKey, forHTTPHeaderField: "x-functions-key")

        return try await send(request)
    }

    private func send<T: Decodable>(_ request: URLRequest) async throws -> T {
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

        do {
            return try Self.decoder.decode(T.self, from: data)
        } catch {
            throw NotifyAPIError.decoding(String(describing: error))
        }
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
