import XCTest
@testable import Notify

@MainActor
final class AuthViewModelTests: XCTestCase {
    private var keychain: InMemoryKeychainStore!
    private var api: MockNotifyAPI!
    private var viewModel: AuthViewModel!

    override func setUp() {
        super.setUp()
        keychain = InMemoryKeychainStore()
        api = MockNotifyAPI()
        viewModel = AuthViewModel(keychain: keychain, api: api)
    }

    func testBootstrap_signedOutWhenKeychainEmpty() {
        viewModel.bootstrap()
        XCTAssertEqual(viewModel.state, .signedOut)
    }

    func testBootstrap_signedInWhenSessionTokenPresent() throws {
        try keychain.save("stub.session.jwt", forKey: KeychainKey.sessionToken)
        viewModel.bootstrap()
        XCTAssertEqual(viewModel.state, .signedIn(displayName: nil))
    }

    func testHandleSignIn_exchangesAppleTokenForSessionAndPersists() async {
        api.sessionResponse = SessionResponse(
            sessionToken: "issued.session.jwt",
            expiresAt: Date().addingTimeInterval(30 * 24 * 3600))
        let cred = AuthViewModel.Credential(
            userIdentifier: "001234.abcdef",
            identityToken: "apple.identity.token",
            fullName: "Test User"
        )

        await viewModel.handleSignIn(cred)

        XCTAssertEqual(viewModel.state, .signedIn(displayName: "Test User"))
        XCTAssertEqual(api.createSessionCalls, ["apple.identity.token"])
        // Apple token must NOT be stored — only the backend-issued session JWT.
        XCTAssertEqual(keychain.load(forKey: KeychainKey.sessionToken), "issued.session.jwt")
        XCTAssertEqual(keychain.load(forKey: KeychainKey.appleUserIdentifier), "001234.abcdef")
    }

    func testHandleSignIn_forbiddenSurfacesAwaitingApproval() async {
        api.sessionError = NotifyAPIError.http(status: 403, body: "user awaiting approval")
        let cred = AuthViewModel.Credential(
            userIdentifier: "001234.zz",
            identityToken: "apple.identity.token",
            fullName: nil
        )

        await viewModel.handleSignIn(cred)

        guard case .failed(let message) = viewModel.state else {
            return XCTFail("expected .failed, got \(viewModel.state)")
        }
        XCTAssertTrue(message.lowercased().contains("approval"), "unexpected message: \(message)")
        XCTAssertNil(keychain.load(forKey: KeychainKey.sessionToken))
    }

    func testHandleSignIn_genericHttpFailureBailsAndLeavesKeychainEmpty() async {
        api.sessionError = NotifyAPIError.http(status: 500, body: nil)
        let cred = AuthViewModel.Credential(
            userIdentifier: "001234.cc",
            identityToken: "apple.identity.token",
            fullName: nil
        )

        await viewModel.handleSignIn(cred)

        guard case .failed = viewModel.state else {
            return XCTFail("expected .failed, got \(viewModel.state)")
        }
        XCTAssertNil(keychain.load(forKey: KeychainKey.sessionToken))
    }

    func testSignOut_clearsKeychainAndFlipsState() throws {
        try keychain.save("session.jwt", forKey: KeychainKey.sessionToken)
        try keychain.save("user", forKey: KeychainKey.appleUserIdentifier)
        viewModel.bootstrap()
        XCTAssertEqual(viewModel.state, .signedIn(displayName: nil))

        viewModel.signOut()

        XCTAssertEqual(viewModel.state, .signedOut)
        XCTAssertNil(keychain.load(forKey: KeychainKey.sessionToken))
        XCTAssertNil(keychain.load(forKey: KeychainKey.appleUserIdentifier))
    }

    func testPrepareSignIn_flipsToSigningIn() {
        viewModel.prepareSignIn()
        XCTAssertEqual(viewModel.state, .signingIn)
    }
}
