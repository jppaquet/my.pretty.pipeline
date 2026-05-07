import Foundation

// Request body for `POST /v1/devices`. Mirrors src/Notify.Functions/Devices/DeviceRegistration.cs.
// Platform is fixed to "apns" for v1; Android can be a sibling case later.
struct DeviceRegistration: Codable, Equatable {
    let deviceToken: String
    let platform: String
    let tags: [String]?

    init(apnsToken: String, tags: [String]? = nil) {
        self.deviceToken = apnsToken
        self.platform = "apns"
        self.tags = tags
    }
}

// Response from `POST /v1/devices` on the 202 path.
struct DeviceRegistrationResponse: Decodable, Equatable {
    let installationId: String
}
