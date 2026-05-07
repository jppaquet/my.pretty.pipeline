import SwiftUI

struct RootView: View {
    let container: AppContainer

    @State private var viewModel: InboxViewModel
    @State private var selection: InboxNotification.ID?

    init(container: AppContainer) {
        self.container = container
        _viewModel = State(initialValue: InboxViewModel(api: container.api))
    }

    var body: some View {
        NavigationSplitView {
            InboxView(viewModel: viewModel, selection: $selection)
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
            try? await container.push.requestAuthorization()
        }
    }

    private func selectedNotification(id: InboxNotification.ID) -> InboxNotification? {
        guard case .loaded(let items, _) = viewModel.state else { return nil }
        return items.first { $0.id == id }
    }
}
