import AuthenticationServices
import SwiftUI

// Initial-launch sign-in screen. Single Sign in with Apple button; the rest
// of the app stays hidden until AuthViewModel flips to `signedIn`. Failure
// states render inline so the user has a single screen to retry from.
struct SignInView: View {
    let viewModel: AuthViewModel

    var body: some View {
        VStack(spacing: 24) {
            Spacer()

            VStack(spacing: 12) {
                Image(systemName: "bell.badge.fill")
                    .font(.system(size: 56))
                    .foregroundStyle(.tint)
                Text("Notify")
                    .font(.largeTitle.bold())
                Text("Sign in to receive notifications.")
                    .font(.subheadline)
                    .foregroundStyle(.secondary)
            }

            Spacer()

            if case .failed(let message) = viewModel.state {
                Text(message)
                    .font(.footnote)
                    .foregroundStyle(.secondary)
                    .multilineTextAlignment(.center)
                    .padding(.bottom, 8)
            }

            SignInWithAppleButton(.signIn) { request in
                request.requestedScopes = [.fullName]
                viewModel.prepareSignIn()
            } onCompletion: { result in
                Task { await viewModel.handleSignIn(result) }
            }
            .frame(maxWidth: .infinity, minHeight: 50)
            .signInWithAppleButtonStyle(.black)
            .disabled(viewModel.state == .signingIn)
            .accessibilityIdentifier("auth.signin.button")

            if case .signingIn = viewModel.state {
                ProgressView().padding(.top, 8)
            }

            Spacer()
        }
        .padding(.horizontal, 32)
        .padding(.vertical, 48)
    }
}

#Preview {
    SignInView(viewModel: AuthViewModel(keychain: InMemoryKeychainStore(), api: MockNotifyAPI()))
}
