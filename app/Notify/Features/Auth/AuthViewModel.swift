import AuthenticationServices
import Foundation
import Observation

// State machine for the SiwA identity layer. Owns the keychain handle so
// signed-in state survives app relaunches. SignInView passes the
// `Result<ASAuthorization, Error>` from the SignInWithAppleButton's
// completion straight into `handleSignIn(_:)`.
//
// The identity token (JWT) is short-lived (~10 min per Apple's docs). PR-B
// does NOT silently refresh: if the backend starts returning 401 because the
// token expired, the user signs in again. PR-C will wire silent re-auth via
// `ASAuthorizationAppleIDProvider.getCredentialState(forUserID:)`.
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

    init(keychain: KeychainStoring) {
        self.keychain = keychain
    }

    func bootstrap() {
        if keychain.load(forKey: KeychainKey.appleIdentityToken) != nil {
            // We don't decode the JWT here — the backend rejects expired
            // tokens; until that happens we treat the user as signed in.
            state = .signedIn(displayName: nil)
        } else {
            state = .signedOut
        }
    }

    // Called from SignInView's button-tap path before the system sheet appears.
    func prepareSignIn() { state = .signingIn }

    // Wired to SignInWithAppleButton.onCompletion. Extracts the credential,
    // persists it to the keychain, and flips state. Tests can call the
    // `handleSignIn(_:)` overload directly with a synthetic Credential.
    func handleSignIn(_ result: Result<ASAuthorization, Error>) {
        switch result {
        case .success(let authorization):
            guard let apple = authorization.credential as? ASAuthorizationAppleIDCredential,
                  let tokenData = apple.identityToken,
                  let token = String(data: tokenData, encoding: .utf8) else {
                state = .failed(message: "Missing identity token in Apple credential.")
                return
            }
            let name = apple.fullName.flatMap { Self.format($0) }
            handleSignIn(Credential(userIdentifier: apple.user, identityToken: token, fullName: name))

        case .failure(let error):
            let asError = error as? ASAuthorizationError
            if asError?.code == .canceled {
                state = .signedOut
            } else {
                state = .failed(message: error.localizedDescription)
            }
        }
    }

    func handleSignIn(_ credential: Credential) {
        do {
            try keychain.save(credential.identityToken, forKey: KeychainKey.appleIdentityToken)
            try keychain.save(credential.userIdentifier, forKey: KeychainKey.appleUserIdentifier)
            state = .signedIn(displayName: credential.fullName)
        } catch {
            state = .failed(message: "Keychain write failed: \(error.localizedDescription)")
        }
    }

    func signOut() {
        try? keychain.delete(forKey: KeychainKey.appleIdentityToken)
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
