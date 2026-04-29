import XCTest
@testable import Notify

final class SmokeTests: XCTestCase {
    func testAppCompilesAndTargetIsLinked() {
        // Phase 0 sanity: ensures the test target links the app target.
        // Phase 1+ replaces this with real ViewModel / Service tests.
        XCTAssertTrue(true)
    }
}
