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
    // Free-form producer-supplied JSON. We only surface the keys iOS knows
    // about (`fullBody`) — anything else is ignored at decode time.
    let metadata: InboxMetadata?

    // Per-recipient mutation state. Optional so docs written before these
    // fields existed still decode (nil treated as false / unread / not
    // hidden). Optionals are implicitly initialized to nil so existing
    // call sites of the memberwise initializer keep working without passing
    // them. Mutated locally by the view-model after POST /v1/inbox/{id}/read
    // or DELETE /v1/inbox/{id}.
    var isRead: Bool?
    var isHidden: Bool?

    // The detail view's preferred rendering source. Producers whose body
    // exceeds the 2000-char ingestion cap put the long-form content in
    // `metadata.fullBody`; everything else falls back to the regular `body`
    // (which still drives the push banner + inbox row preview).
    var renderedBody: String { metadata?.fullBody ?? body }

    // Producer-supplied grouping label (e.g. "email", "news"). Surfaced as a
    // badge in the inbox row + used for filtering. Nil for producers that
    // don't set one.
    var category: String? { metadata?.category }
}

// Lean projection of the server-side `metadata` object — only the keys the
// iOS app understands. Unknown keys are dropped silently; a typed value
// mismatch (e.g. a producer sends a number for `fullBody`) also degrades to
// nil instead of failing the whole inbox decode.
struct InboxMetadata: Codable, Hashable {
    let fullBody: String?
    let category: String?

    init(fullBody: String? = nil, category: String? = nil) {
        self.fullBody = fullBody
        self.category = category
    }

    init(from decoder: Decoder) throws {
        let container = try decoder.container(keyedBy: CodingKeys.self)
        self.fullBody = try? container.decodeIfPresent(String.self, forKey: .fullBody)
        self.category = try? container.decodeIfPresent(String.self, forKey: .category)
    }

    private enum CodingKeys: String, CodingKey {
        case fullBody, category
    }
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
        metadata: InboxMetadata? = nil,
        isRead: Bool? = nil,
        isHidden: Bool? = nil
    ) -> InboxNotification {
        InboxNotification(
            id: id, source: source, title: title, body: body,
            type: type, priority: priority, tags: tags,
            deeplink: deeplink, deduplicationKey: deduplicationKey,
            timestamp: timestamp, envelopeId: envelopeId ?? "env-\(id)",
            metadata: metadata,
            isRead: isRead, isHidden: isHidden
        )
    }
}

// Inbox page response from `GET /v1/inbox`.
struct InboxPage: Decodable {
    let items: [InboxNotification]
    let continuationToken: String?
}
