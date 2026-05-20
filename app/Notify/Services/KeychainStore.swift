import Foundation
import Security

// Thin wrapper around SecItem* for storing the per-function-key and the
// last-known APNs token. Protocol-fronted so InboxViewModel / PushRegistration
// tests can swap an in-memory fake.
protocol KeychainStoring {
    func save(_ value: String, forKey key: String) throws
    func load(forKey key: String) -> String?
    func delete(forKey key: String) throws
}

enum KeychainKey {
    static let apnsToken = "apnsToken"
    // Notify-issued session JWT (HS256), minted by `POST /v1/auth/session`
    // after a Sign-in-with-Apple exchange. Sent as `Authorization: Bearer …`
    // on every backend call. Apple's own identity token is short-lived
    // (~10 min) so we never store it — it lives only on the stack during
    // the exchange. Default session lifetime is 30 days; backend's
    // `JwtAuthMiddleware` verifies signature, issuer, audience, and expiry.
    static let sessionToken = "sessionToken"
    // Apple's stable per-app user identifier (the JWT `sub` claim). Captured
    // alongside the session so we have a durable display handle that
    // outlives the session token.
    static let appleUserIdentifier = "appleUserIdentifier"
}

enum KeychainError: Error, Equatable {
    case unexpectedStatus(OSStatus)
    case encodingFailed
}

final class KeychainStore: KeychainStoring {
    private let service: String

    init(service: String = "my.pretty.pipeline") {
        self.service = service
    }

    func save(_ value: String, forKey key: String) throws {
        guard let data = value.data(using: .utf8) else {
            throw KeychainError.encodingFailed
        }

        let query: [String: Any] = [
            kSecClass as String: kSecClassGenericPassword,
            kSecAttrService as String: service,
            kSecAttrAccount as String: key,
        ]

        let attributes: [String: Any] = [
            kSecValueData as String: data,
            // ThisDeviceOnly so the item is excluded from encrypted iTunes/
            // Finder backups and never migrates to a restored device — keeps
            // the session token off-device only.
            kSecAttrAccessible as String: kSecAttrAccessibleAfterFirstUnlockThisDeviceOnly,
        ]

        let status = SecItemUpdate(query as CFDictionary, attributes as CFDictionary)
        switch status {
        case errSecSuccess:
            return
        case errSecItemNotFound:
            var addQuery = query
            addQuery.merge(attributes) { _, new in new }
            let addStatus = SecItemAdd(addQuery as CFDictionary, nil)
            if addStatus != errSecSuccess {
                throw KeychainError.unexpectedStatus(addStatus)
            }
        default:
            throw KeychainError.unexpectedStatus(status)
        }
    }

    func load(forKey key: String) -> String? {
        let query: [String: Any] = [
            kSecClass as String: kSecClassGenericPassword,
            kSecAttrService as String: service,
            kSecAttrAccount as String: key,
            kSecReturnData as String: true,
            kSecMatchLimit as String: kSecMatchLimitOne,
        ]

        var item: CFTypeRef?
        let status = SecItemCopyMatching(query as CFDictionary, &item)
        guard status == errSecSuccess, let data = item as? Data else { return nil }
        return String(data: data, encoding: .utf8)
    }

    func delete(forKey key: String) throws {
        let query: [String: Any] = [
            kSecClass as String: kSecClassGenericPassword,
            kSecAttrService as String: service,
            kSecAttrAccount as String: key,
        ]
        let status = SecItemDelete(query as CFDictionary)
        if status != errSecSuccess && status != errSecItemNotFound {
            throw KeychainError.unexpectedStatus(status)
        }
    }
}

// In-memory fake — used by tests and SwiftUI previews.
final class InMemoryKeychainStore: KeychainStoring {
    private var storage: [String: String] = [:]
    private let queue = DispatchQueue(label: "InMemoryKeychainStore")

    func save(_ value: String, forKey key: String) throws {
        queue.sync { storage[key] = value }
    }

    func load(forKey key: String) -> String? {
        queue.sync { storage[key] }
    }

    func delete(forKey key: String) throws {
        queue.sync { _ = storage.removeValue(forKey: key) }
    }
}
