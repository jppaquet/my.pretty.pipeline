import XCTest
@testable import Notify

@MainActor
final class AuthViewModelTests: XCTestCase {
    private var keychain: InMemoryKeychainStore!
    private var viewModel: AuthViewModel!

    override func setUp() {
        super.setUp()
        keychain = InMemoryKeychainStore()
        viewModel = AuthViewModel(keychain: keychain)
    }

    func testBootstrap_signedOutWhenKeychainEmpty() {
        viewModel.bootstrap()
        XCTAssertEqual(viewModel.state, .signedOut)
    }

    func testBootstrap_signedInWhenJwtPresent() throws {
        try keychain.save("stub.jwt", forKey: KeychainKey.appleIdentityToken)
        viewModel.bootstrap()
        XCTAssertEqual(viewModel.state, .signedIn(displayName: nil))
    }

    func testHandleSignIn_credentialPersistsAndFlipsState() {
        let cred = AuthViewModel.Credential(
            userIdentifier: "001234.abcdef",
            identityToken: "header.payload.sig",
            fullName: "Test User"
        )
        viewModel.handleSignIn(cred)

        XCTAssertEqual(viewModel.state, .signedIn(displayName: "Test User"))
        XCTAssertEqual(keychain.load(forKey: KeychainKey.appleIdentityToken), "header.payload.sig")
        XCTAssertEqual(keychain.load(forKey: KeychainKey.appleUserIdentifier), "001234.abcdef")
    }

    func testSignOut_clearsKeychainAndFlipsState() throws {
        try keychain.save("jwt", forKey: KeychainKey.appleIdentityToken)
        try keychain.save("user", forKey: KeychainKey.appleUserIdentifier)
        viewModel.bootstrap()
        XCTAssertEqual(viewModel.state, .signedIn(displayName: nil))

        viewModel.signOut()

        XCTAssertEqual(viewModel.state, .signedOut)
        XCTAssertNil(keychain.load(forKey: KeychainKey.appleIdentityToken))
        XCTAssertNil(keychain.load(forKey: KeychainKey.appleUserIdentifier))
    }

    func testPrepareSignIn_flipsToSigningIn() {
        viewModel.prepareSignIn()
        XCTAssertEqual(viewModel.state, .signingIn)
    }
}
