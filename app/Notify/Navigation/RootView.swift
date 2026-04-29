import SwiftUI

struct RootView: View {
    var body: some View {
        NavigationSplitView {
            List {
                Text("Sources")
                    .font(.headline)
                Text("(populated in Phase 3)")
                    .foregroundStyle(.secondary)
            }
            .navigationTitle("Notify")
        } detail: {
            ContentUnavailableView(
                "No notification selected",
                systemImage: "bell",
                description: Text("Select a notification from the inbox.")
            )
        }
    }
}

#Preview {
    RootView()
}
