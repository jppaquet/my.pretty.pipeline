import Foundation

// Composition root for the app. Built once in NotifyApp; passed down via
// SwiftUI environment to RootView and consumed by AppDelegate via the
// `shared` reference (UIApplicationDelegate doesn't get clean DI).
@MainActor
final class AppContainer {
    let api: NotifyAPI
    let keychain: KeychainStoring
    let push: PushRegistration

    init(api: NotifyAPI, keychain: KeychainStoring) {
        self.api = api
        self.keychain = keychain
        self.push = PushRegistration(api: api, keychain: keychain)
    }

    static var shared: AppContainer = .makeDefault()

    // Reads `NotifyAPIBaseURL` from Info.plist (set via xcconfig per
    // environment). Auth is entirely Sign-in-with-Apple: the user signs in
    // through SignInView, the JWT lands in the Keychain, and every backend
    // request reads it back via `bearer`. PR-C retired the legacy
    // `NotifyFunctionKey` build-time plist value — the iOS bundle no longer
    // carries any backend credential.
    static func makeDefault() -> AppContainer {
        // UI tests run hermetically against a mock backend pre-seeded with
        // stable IDs so XCUITest can locate `inbox.row.ui-test-1` on both
        // iPhone (compact) and iPad (regular) destinations. Use an in-memory
        // keychain pre-seeded with stub SiwA credentials: on the simulator
        // the real KeychainStore returns errSecMissingEntitlement (-34018)
        // so a silent `try? keychain.save` would leave the SiwA gate empty
        // and SignInView would block the inbox under test.
        if ProcessInfo.processInfo.arguments.contains("-NotifyUITestMockBackend") {
            let keychain = InMemoryKeychainStore()
            try? keychain.save("ui-test-stub-jwt", forKey: KeychainKey.appleIdentityToken)
            try? keychain.save("ui-test-stub-user", forKey: KeychainKey.appleUserIdentifier)
            return AppContainer(api: MockNotifyAPI.uiTestSeeded(), keychain: keychain)
        }

        let keychain = KeychainStore()

        let plistURL = Bundle.main.object(forInfoDictionaryKey: "NotifyAPIBaseURL") as? String
        // swiftlint:disable force_unwrapping
        let baseURL = (plistURL.flatMap(URL.init(string:)))
            ?? URL(string: "https://func-notify.invalid")!  // sentinel — always valid literal
        // swiftlint:enable force_unwrapping

        // Resolves the JWT at call time so a sign-in / sign-out mid-session is
        // picked up by the next request without rebuilding the API client.
        let bearer: BearerTokenProvider = { keychain.load(forKey: KeychainKey.appleIdentityToken) }

        let api = NotifyAPIClient(baseURL: baseURL, bearer: bearer)
        return AppContainer(api: api, keychain: keychain)
    }
}
