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

    private(set) var state: State = .idle

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

    func refresh() async { await load() }

    func loadMore() async {
        guard case .loaded(let items, let token?) = state else { return }
        do {
            let page = try await api.inbox(source: source, limit: pageSize, continuationToken: token)
            state = .loaded(items: items + page.items, continuationToken: page.continuationToken)
        } catch {
            state = .failed(message: Self.message(for: error))
        }
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
