import XCTest
@testable import Notify

// Hits the simulator's keychain. Each test uses a unique service id so runs
// don't pollute each other or the device's real keychain across reruns.
final class KeychainStoreTests: XCTestCase {
    private var store: KeychainStore!
    private let key = "test-key"

    override func setUp() {
        super.setUp()
        store = KeychainStore(service: "my.pretty.pipeline.tests.\(UUID().uuidString)")
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
