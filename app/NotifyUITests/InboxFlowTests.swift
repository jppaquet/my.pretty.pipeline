import XCTest

// Same test, two destinations — the CI matrix runs this on iPhone 16 *and*
// iPad Pro 13" so a NavigationSplitView regression breaks the build on
// whichever idiom regressed. Compact pushes the detail; regular reveals it
// in the right pane. Either way `notification.detail` becomes hittable.
final class InboxFlowTests: XCTestCase {
    func testTappingRowShowsDetail() throws {
        let app = XCUIApplication()
        app.launchArguments.append("-NotifyUITestMockBackend")
        app.launch()

        XCTAssertTrue(app.wait(for: .runningForeground, timeout: 10))

        let inbox = app.collectionViews["inbox.list"]
        XCTAssertTrue(inbox.waitForExistence(timeout: 5))

        // SwiftUI List on iOS 18+ doesn't promote `accessibilityIdentifier`
        // onto the cell itself — the identifier lands on a descendant, so we
        // match any descendant carrying it and tap that.
        let row = app.descendants(matching: .any)
            .matching(identifier: "inbox.row.ui-test-1")
            .firstMatch
        XCTAssertTrue(row.waitForExistence(timeout: 5), "Seeded row not found")
        row.tap()

        let detail = app.scrollViews["notification.detail"]
        XCTAssertTrue(detail.waitForExistence(timeout: 5), "Detail not shown after tap")
    }
}
