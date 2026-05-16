import SwiftUI

struct RootView: View {
    let container: AppContainer

    @State private var inboxViewModel: InboxViewModel
    @State private var authViewModel: AuthViewModel
    @State private var selection: InboxNotification.ID?

    init(container: AppContainer) {
        self.container = container
        _inboxViewModel = State(initialValue: InboxViewModel(api: container.api))
        _authViewModel = State(initialValue: AuthViewModel(keychain: container.keychain))
    }

    var body: some View {
        Group {
            switch authViewModel.state {
            case .unknown:
                ProgressView()
                    .controlSize(.large)
                    .frame(maxWidth: .infinity, maxHeight: .infinity)

            case .signedOut, .signingIn, .failed:
                SignInView(viewModel: authViewModel)

            case .signedIn:
                inboxRoot
            }
        }
        .task {
            authViewModel.bootstrap()
        }
    }

    private var inboxRoot: some View {
        NavigationSplitView {
            InboxView(viewModel: inboxViewModel, selection: $selection)
                .toolbar {
                    ToolbarItem(placement: .topBarLeading) {
                        Button("Sign out") { authViewModel.signOut() }
                            .accessibilityIdentifier("auth.signout.button")
                    }
                }
        } detail: {
            if let selection, let notification = selectedNotification(id: selection) {
                NotificationDetailView(notification: notification)
            } else {
                ContentUnavailableView(
                    "No notification selected",
                    systemImage: "bell",
                    description: Text("Select a notification from the inbox.")
                )
            }
        }
        .task {
            // Ask for push permission once. Token POST happens later when
            // AppDelegate gets `didRegisterForRemoteNotificationsWithDeviceToken`.
            _ = try? await container.push.requestAuthorization()
        }
    }

    private func selectedNotification(id: InboxNotification.ID) -> InboxNotification? {
        guard case .loaded(let items, _) = inboxViewModel.state else { return nil }
        return items.first { $0.id == id }
    }
}
