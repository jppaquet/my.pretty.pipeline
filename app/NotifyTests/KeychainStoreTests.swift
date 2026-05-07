import XCTest
@testable import Notify

// On device we hit the real keychain; on simulator SecItemAdd can return
// errSecMissingEntitlement (-34018) so we fall back to the in-memory fake.
final class KeychainStoreTests: XCTestCase {
    private var store: KeychainStoring!
    private let key = "test-key"

    override func setUp() {
        super.setUp()
        #if targetEnvironment(simulator)
        store = InMemoryKeychainStore()
        #else
        store = KeychainStore(service: "my.pretty.pipeline.tests.\(UUID().uuidString)")
        #endif
    }

    override func tearDown() {
        try? store.delete(forKey: key)
        store = nil
        super.tearDown()
    }

    func testLoadReturnsNilWhenAbsent() {
        XCTAssertNil(store.load(forKey: key))
    }

    func testSaveThenLoadRoundTrip() throws {
        try store.save("hunter2", forKey: key)
        XCTAssertEqual(store.load(forKey: key), "hunter2")
    }

    func testSaveOverwritesExistingValue() throws {
        try store.save("old", forKey: key)
        try store.save("new", forKey: key)
        XCTAssertEqual(store.load(forKey: key), "new")
    }

    func testDeleteRemovesValue() throws {
        try store.save("temp", forKey: key)
        try store.delete(forKey: key)
        XCTAssertNil(store.load(forKey: key))
    }

    func testDeleteIsIdempotent() {
        XCTAssertNoThrow(try store.delete(forKey: key))
    }

    func testInMemoryStoreImplementsContract() throws {
        let inMemory: KeychainStoring = InMemoryKeychainStore()
        try inMemory.save("v", forKey: key)
        XCTAssertEqual(inMemory.load(forKey: key), "v")
        try inMemory.delete(forKey: key)
        XCTAssertNil(inMemory.load(forKey: key))
    }
}
