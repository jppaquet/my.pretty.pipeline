import AuthenticationServices
import Foundation
import Observation

// State machine for the SiwA identity layer. Owns the keychain handle so
// signed-in state survives app relaunches. SignInView passes the
// `Result<ASAuthorization, Error>` from the SignInWithAppleButton's
// completion straight into `handleSignIn(_:)`.
//
// Token lifecycle: Apple's identity token is short-lived (~10 min) and the
// app never stores it. After a successful Apple sign-in we POST it to
// `/v1/auth/session` and the backend returns a Notify session JWT (HS256,
// default 30-day lifetime) which lives in the Keychain and is sent as the
// Bearer on every subsequent call. When the session expires the backend
// 401s and SignInView is shown again.
@MainActor
@Observable
final class AuthViewModel {
    enum State: Equatable {
        case unknown                       // bootstrap: checking keychain
        case signedOut
        case signingIn
        case signedIn(displayName: String?) // displayName for the UI header
        case failed(message: String)
    }

    struct Credential: Equatable {
        let userIdentifier: String   // Apple `sub` — stable per-app per-user
        let identityToken: String    // JWT, valid for ~10 min
        let fullName: String?        // present only on the very first sign-in
    }

    private(set) var state: State = .unknown

    private let keychain: KeychainStoring
    private let api: NotifyAPI

    init(keychain: KeychainStoring, api: NotifyAPI) {
        self.keychain = keychain
        self.api = api
    }

    func bootstrap() {
        if keychain.load(forKey: KeychainKey.sessionToken) != nil {
            // We don't decode the session JWT here — the backend rejects
            // expired tokens; until that happens we treat the user as
            // signed in.
            state = .signedIn(displayName: nil)
        } else {
            state = .signedOut
        }
    }

    // Called from SignInView's button-tap path before the system sheet appears.
    func prepareSignIn() { state = .signingIn }

    // Wired to SignInWithAppleButton.onCompletion. Extracts the Apple
    // identity token, exchanges it for a Notify session JWT, persists the
    // session JWT to the Keychain, and flips state. Tests can call the
    // `handleSignIn(_:)` overload directly with a synthetic Credential.
    func handleSignIn(_ result: Result<ASAuthorization, Error>) async {
        switch result {
        case .success(let authorization):
            guard let apple = authorization.credential as? ASAuthorizationAppleIDCredential,
                  let tokenData = apple.identityToken,
                  let token = String(data: tokenData, encoding: .utf8) else {
                state = .failed(message: "Missing identity token in Apple credential.")
                return
            }
            let name = apple.fullName.flatMap { Self.format($0) }
            await handleSignIn(Credential(userIdentifier: apple.user, identityToken: token, fullName: name))

        case .failure(let error):
            let asError = error as? ASAuthorizationError
            if asError?.code == .canceled {
                state = .signedOut
            } else {
                state = .failed(message: error.localizedDescription)
            }
        }
    }

    func handleSignIn(_ credential: Credential) async {
        do {
            let session = try await api.createSession(appleIdentityToken: credential.identityToken)
            try keychain.save(session.sessionToken, forKey: KeychainKey.sessionToken)
            try keychain.save(credential.userIdentifier, forKey: KeychainKey.appleUserIdentifier)
            state = .signedIn(displayName: credential.fullName)
        } catch let NotifyAPIError.http(status, _) where status == 403 {
            state = .failed(message: "Your account is awaiting approval.")
        } catch let NotifyAPIError.http(status, _) {
            state = .failed(message: "Sign-in failed (HTTP \(status)).")
        } catch {
            state = .failed(message: "Sign-in failed: \(error.localizedDescription)")
        }
    }

    func signOut() {
        try? keychain.delete(forKey: KeychainKey.sessionToken)
        try? keychain.delete(forKey: KeychainKey.appleUserIdentifier)
        state = .signedOut
    }

    private static func format(_ name: PersonNameComponents) -> String? {
        let formatter = PersonNameComponentsFormatter()
        formatter.style = .default
        let formatted = formatter.string(from: name)
        return formatted.isEmpty ? nil : formatted
    }
}
