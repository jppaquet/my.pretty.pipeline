import SwiftUI
import UserNotifications

@main
struct NotifyApp: App {
    @UIApplicationDelegateAdaptor(AppDelegate.self) private var appDelegate

    var body: some Scene {
        WindowGroup {
            RootView(container: AppContainer.shared)
        }
    }
}

final class AppDelegate: NSObject, UIApplicationDelegate, UNUserNotificationCenterDelegate {
    func application(
        _ application: UIApplication,
        didFinishLaunchingWithOptions launchOptions: [UIApplication.LaunchOptionsKey: Any]? = nil
    ) -> Bool {
        UNUserNotificationCenter.current().delegate = self
        // Ask the user once on first launch. iOS remembers the answer; subsequent
        // launches return the cached decision without re-prompting. If granted,
        // ask APNs for a device token — that fires
        // `didRegisterForRemoteNotificationsWithDeviceToken` which POSTs to
        // /v1/devices and creates an Installation in Notification Hubs.
        Task { @MainActor in
            do {
                let granted = try await UNUserNotificationCenter.current()
                    .requestAuthorization(options: [.alert, .badge, .sound])
                if granted {
                    application.registerForRemoteNotifications()
                } else {
                    NSLog("User declined notification permission — push delivery disabled")
                }
            } catch {
                NSLog("requestAuthorization failed: %@", String(describing: error))
            }
        }
        return true
    }

    func application(
        _ application: UIApplication,
        didRegisterForRemoteNotificationsWithDeviceToken deviceToken: Data
    ) {
        Task { @MainActor in
            do {
                try await AppContainer.shared.push.register(apnsToken: deviceToken)
            } catch {
                NSLog("PushRegistration.register failed: %@", String(describing: error))
            }
        }
    }

    func application(
        _ application: UIApplication,
        didFailToRegisterForRemoteNotificationsWithError error: Error
    ) {
        NSLog("APNs registration failed: %@", error.localizedDescription)
    }

    // Foreground delivery — show the system banner so the user can see it.
    func userNotificationCenter(
        _ center: UNUserNotificationCenter,
        willPresent notification: UNNotification,
        withCompletionHandler completionHandler: @escaping (UNNotificationPresentationOptions) -> Void
    ) {
        completionHandler([.banner, .sound, .badge])
    }
}
