import XCTest

// Same test, two destinations — the CI matrix runs this on iPhone 16 *and*
// iPad Pro 13" so a NavigationSplitView regression breaks the build on
// whichever idiom regressed.
final class InboxFlowTests: XCTestCase {
    func testAppLaunchesAndShowsInbox() throws {
        let app = XCUIApplication()
        // The launch arg flips AppContainer to its mock-API path so the UI test
        // is hermetic — see AppContainer.makeDefault for the override hook
        // (`-NotifyUITestMockBackend`). When that hook lands the smoke test
        // becomes a real flow test; for now it exercises launch + sidebar.
        app.launchArguments.append("-NotifyUITestMockBackend")
        app.launch()

        XCTAssertTrue(app.wait(for: .runningForeground, timeout: 10))
        // Sidebar list always present in both compact (iPhone) and regular (iPad)
        // size classes — `inbox.list` is the accessibilityIdentifier on InboxView.
        let inbox = app.otherElements["inbox.list"]
        XCTAssertTrue(inbox.waitForExistence(timeout: 5))
    }
}
