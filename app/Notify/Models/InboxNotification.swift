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

    // Per-recipient mutation state. Optional so docs written before these
    // fields existed still decode (nil treated as false / unread / not
    // hidden). Optionals are implicitly initialized to nil so existing
    // call sites of the memberwise initializer keep working without passing
    // them. Mutated locally by the view-model after POST /v1/inbox/{id}/read
    // or DELETE /v1/inbox/{id}.
    var isRead: Bool?
    var isHidden: Bool?
}

enum Priority: String, Codable, Hashable {
    case low, normal, high
}

extension InboxNotification {
    static func mock(
        id: String,
        source: String,
        title: String,
        body: String,
        type: String = "info",
        priority: Priority = .normal,
        tags: [String]? = nil,
        deeplink: URL? = nil,
        deduplicationKey: String? = nil,
        timestamp: Date,
        envelopeId: String? = nil,
        isRead: Bool? = nil,
        isHidden: Bool? = nil
    ) -> InboxNotification {
        InboxNotification(
            id: id, source: source, title: title, body: body,
            type: type, priority: priority, tags: tags,
            deeplink: deeplink, deduplicationKey: deduplicationKey,
            timestamp: timestamp, envelopeId: envelopeId ?? "env-\(id)",
            isRead: isRead, isHidden: isHidden
        )
    }
}

// Inbox page response from `GET /v1/inbox`.
struct InboxPage: Decodable {
    let items: [InboxNotification]
    let continuationToken: String?
}
