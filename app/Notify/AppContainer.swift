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
        let keychain = KeychainStore()

        let plistURL = Bundle.main.object(forInfoDictionaryKey: "NotifyAPIBaseURL") as? String
        let baseURL = (plistURL.flatMap(URL.init(string:)))
            ?? URL(string: "https://func-notify.invalid")!  // sentinel — surfaces in failed-load state

        let plistKey = Bundle.main.object(forInfoDictionaryKey: "NotifyFunctionKey") as? String
        let functionKey = keychain.load(forKey: KeychainKey.functionKey) ?? plistKey ?? ""

        let api = NotifyAPIClient(baseURL: baseURL, functionKey: functionKey)
        return AppContainer(api: api, keychain: keychain)
    }
}
