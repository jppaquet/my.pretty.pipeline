import XCTest
@testable import Notify

// Pins the wire format with a captured server JSON fixture so changes to
// NotificationDocument on the .NET side surface as a test failure here, not
// as a runtime decode error in the inbox.
final class NotifyAPICodableTests: XCTestCase {
    private let inboxResponseJSON = #"""
    {
      "items": [
        {
          "id": "abc123",
          "source": "home-pipeline",
          "title": "Backup failed",
          "body": "rsync exited 12",
          "type": "alert",
          "priority": "high",
          "tags": ["pi-01"],
          "deeplink": "https://example.com/run/42",
          "deduplicationKey": "backup-2026-04-28",
          "timestamp": "2026-04-28T14:00:00.1234567Z",
          "envelopeId": "env-1"
        },
        {
          "id": "def456",
          "source": "cron",
          "title": "Done",
          "body": "weekly cleanup ok",
          "type": "info",
          "priority": "normal",
          "tags": null,
          "deeplink": null,
          "deduplicationKey": null,
          "timestamp": "2026-04-28T15:00:00Z",
          "envelopeId": "env-2"
        }
      ],
      "continuationToken": "page2"
    }
    """#

    func testInboxPageDecodesWithFractionalSecondsAndNulls() throws {
        let data = Data(inboxResponseJSON.utf8)
        let page = try NotifyAPIClient.decoder.decode(InboxPage.self, from: data)

        XCTAssertEqual(page.items.count, 2)
        XCTAssertEqual(page.continuationToken, "page2")

        let first = page.items[0]
        XCTAssertEqual(first.id, "abc123")
        XCTAssertEqual(first.source, "home-pipeline")
        XCTAssertEqual(first.title, "Backup failed")
        XCTAssertEqual(first.priority, .high)
        XCTAssertEqual(first.tags, ["pi-01"])
        XCTAssertEqual(first.deeplink?.absoluteString, "https://example.com/run/42")
        XCTAssertEqual(first.deduplicationKey, "backup-2026-04-28")
        XCTAssertEqual(first.envelopeId, "env-1")

        let second = page.items[1]
        XCTAssertNil(second.tags)
        XCTAssertNil(second.deeplink)
        XCTAssertNil(second.deduplicationKey)
        XCTAssertEqual(second.priority, .normal)
    }

    func testDeviceRegistrationRoundTripsThroughEncoder() throws {
        let registration = DeviceRegistration(
            apnsToken: "deadbeef",
            tags: ["global", "source:home"]
        )

        let data = try NotifyAPIClient.encoder.encode(registration)
        let decoded = try NotifyAPIClient.decoder.decode(DeviceRegistration.self, from: data)

        XCTAssertEqual(decoded, registration)
        // Wire-shape sanity: server is checking for camelCase deviceToken and a literal "apns".
        let json = try XCTUnwrap(String(data: data, encoding: .utf8))
        XCTAssertTrue(json.contains("\"deviceToken\":\"deadbeef\""))
        XCTAssertTrue(json.contains("\"platform\":\"apns\""))
    }

    func testDeviceRegistrationResponseDecodes() throws {
        let json = #"{ "installationId": "inst-1" }"#
        let response = try NotifyAPIClient.decoder.decode(
            DeviceRegistrationResponse.self,
            from: Data(json.utf8)
        )
        XCTAssertEqual(response.installationId, "inst-1")
    }
}
