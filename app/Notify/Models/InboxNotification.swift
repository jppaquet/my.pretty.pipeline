import Foundation

// Mirror of `InboxNotificationDocument` (see src/Notify.Shared/Cosmos/InboxNotificationDocument.cs).
// Server JSON is camelCase via NotifyJson.Options, so the property names match
// 1:1 with no `CodingKeys` overrides.
struct InboxNotification: Identifiable, Codable, Hashable {
    let id: String
    let source: String
    let title: String
    let body: String
    let type: String
    let priority: Priority
    let tags: [String]?
    let deeplink: URL?
    let deduplicationKey: String?
    let timestamp: Date
    let envelopeId: String
}

enum Priority: String, Codable, Hashable {
    case low, normal, high
}

// Inbox page response from `GET /v1/inbox`.
struct InboxPage: Decodable {
    let items: [InboxNotification]
    let continuationToken: String?
}
