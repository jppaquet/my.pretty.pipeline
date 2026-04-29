import XCTest

final class InboxFlowTests: XCTestCase {
    func testAppLaunches() throws {
        // Phase 0 smoke: app launches on the destination simulator (iPhone or iPad).
        // The CI matrix runs this on iPhone 15 *and* iPad Pro 13" so any future
        // NavigationSplitView regression breaks the build on whichever idiom regressed.
        let app = XCUIApplication()
        app.launch()
        XCTAssertTrue(app.wait(for: .runningForeground, timeout: 10))
    }
}
