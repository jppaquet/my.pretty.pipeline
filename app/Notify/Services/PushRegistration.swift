import Foundation
import UIKit
import UserNotifications

@MainActor
final class PushRegistration {
    private let api: NotifyAPI
    private let keychain: KeychainStoring
    private let center: UNUserNotificationCenter

    init(api: NotifyAPI,
         keychain: KeychainStoring,
         center: UNUserNotificationCenter = .current()) {
        self.api = api
        self.keychain = keychain
        self.center = center
    }

    // 1. Ask the user. Permission once, then `application` registers for
    //    remote notifications which fires `didRegisterForRemoteNotifications…`
    //    on the AppDelegate — that's where `register(apnsToken:)` is called.
    @discardableResult
    func requestAuthorization() async throws -> Bool {
        let granted = try await center.requestAuthorization(options: [.alert, .badge, .sound])
        if granted {
            UIApplication.shared.registerForRemoteNotifications()
        }
        return granted
    }

    // 2. Hand the raw APNs token off to the backend. Idempotent — the server
    //    derives the installationId from the token, so re-registration is a
    //    no-op on NH.
    func register(apnsToken: Data, tags: [String]? = ["global"]) async throws {
        let hex = Self.hex(apnsToken)
        try keychain.save(hex, forKey: KeychainKey.apnsToken)
        let registration = DeviceRegistration(apnsToken: hex, tags: tags)
        _ = try await api.registerDevice(registration)
    }

    static func hex(_ data: Data) -> String {
        data.map { String(format: "%02x", $0) }.joined()
    }
}

// Decoder for the APNs `userInfo` payload built by ApnsPayload.cs on the
// server. The user-visible bits live in `aps.alert.{title,body}`; the keys
// the app cares about (`id`, `source`, `type`, `deeplink`) are siblings of
// `aps`. Modelled as a struct so PushPayloadParserTests can pin the contract.
struct PushPayload: Equatable {
    let id: String
    let source: String
    let type: String
    let title: String
    let body: String
    let deeplink: URL?

    static func parse(_ userInfo: [AnyHashable: Any]) -> PushPayload? {
        guard
            let id = userInfo["id"] as? String,
            let source = userInfo["source"] as? String,
            let aps = userInfo["aps"] as? [String: Any],
            let alert = aps["alert"] as? [String: Any],
            let title = alert["title"] as? String,
            let body = alert["body"] as? String
        else { return nil }

        let type = (userInfo["type"] as? String) ?? "info"
        let deeplink = (userInfo["deeplink"] as? String).flatMap(URL.init(string:))

        return PushPayload(id: id, source: source, type: type, title: title, body: body, deeplink: deeplink)
    }
}
