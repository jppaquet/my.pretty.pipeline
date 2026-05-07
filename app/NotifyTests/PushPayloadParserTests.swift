import XCTest
@testable import Notify

// Pins the APNs userInfo shape produced by ApnsPayload.cs on the .NET side.
// Update both sides together when the contract changes.
final class PushPayloadParserTests: XCTestCase {
    private func userInfo(extra: [String: Any] = [:]) -> [AnyHashable: Any] {
        var info: [AnyHashable: Any] = [
            "id": "abc",
            "source": "home-pipeline",
            "type": "alert",
            "aps": [
                "alert": [
                    "title": "Backup failed",
                    "body": "rsync exited 12",
                ],
                "sound": "default",
            ],
        ]
        for (key, value) in extra { info[key] = value }
        return info
    }

    func testParsesMinimalPayload() throws {
        let parsed = try XCTUnwrap(PushPayload.parse(userInfo()))
        XCTAssertEqual(parsed.id, "abc")
        XCTAssertEqual(parsed.source, "home-pipeline")
        XCTAssertEqual(parsed.type, "alert")
        XCTAssertEqual(parsed.title, "Backup failed")
        XCTAssertEqual(parsed.body, "rsync exited 12")
        XCTAssertNil(parsed.deeplink)
    }

    func testParsesDeeplinkWhenPresent() throws {
        let parsed = try XCTUnwrap(PushPayload.parse(
            userInfo(extra: ["deeplink": "https://example.com/runs/42"])
        ))
        XCTAssertEqual(parsed.deeplink?.absoluteString, "https://example.com/runs/42")
    }

    func testReturnsNilWhenIdMissing() {
        var info = userInfo()
        info.removeValue(forKey: "id")
        XCTAssertNil(PushPayload.parse(info))
    }

    func testReturnsNilWhenAlertMissing() {
        let info: [AnyHashable: Any] = [
            "id": "abc",
            "source": "x",
            "aps": ["sound": "default"],
        ]
        XCTAssertNil(PushPayload.parse(info))
    }

    func testDefaultTypeIsInfoWhenAbsent() throws {
        var info = userInfo()
        info.removeValue(forKey: "type")
        let parsed = try XCTUnwrap(PushPayload.parse(info))
        XCTAssertEqual(parsed.type, "info")
    }
}
