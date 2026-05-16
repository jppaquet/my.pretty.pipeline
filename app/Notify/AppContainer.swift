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

    // Reads `NotifyAPIBaseURL` and `NotifyFunctionKey` from Info.plist (set
    // via xcconfig per environment). Falls back to the keychain for the
    // function key — useful so a user can paste a key on first launch via a
    // settings sheet instead of baking it into the build.
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

        let plistKey = Bundle.main.object(forInfoDictionaryKey: "NotifyFunctionKey") as? String
        let functionKey = keychain.load(forKey: KeychainKey.functionKey) ?? plistKey ?? ""

        // Resolves the JWT at call time so a sign-in / sign-out mid-session is
        // picked up by the next request without rebuilding the API client.
        let bearer: BearerTokenProvider = { keychain.load(forKey: KeychainKey.appleIdentityToken) }

        let api = NotifyAPIClient(baseURL: baseURL, functionKey: functionKey, bearer: bearer)
        return AppContainer(api: api, keychain: keychain)
    }
}
