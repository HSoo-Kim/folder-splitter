import Foundation
import Darwin

/// Consent prompts and unavailable providers cannot hold the index worker indefinitely. At most
/// four OS calls may remain blocked; saturated readers skip folders for a later denied-path retry.
enum SearchFilesystem {
    private static let queue = DispatchQueue(label: "FolderSplitter.Search.read", qos: .utility, attributes: .concurrent)
    private static let slots = DispatchSemaphore(value: 4)
    private static let gate = NSLock()
    private static var saturated = false

    static func perform<T>(within seconds: TimeInterval = 1, _ operation: @escaping () -> T) -> T? {
        let deadline = DispatchTime.now() + max(0, seconds)
        gate.lock()
        let grace = saturated ? 0 : max(0, seconds)
        gate.unlock()
        let acquired = slots.wait(timeout: .now() + grace) == .success
        gate.lock(); saturated = !acquired; gate.unlock()
        guard acquired else { return nil }
        let result = SearchReadResult<T>()
        let done = DispatchSemaphore(value: 0)
        queue.async {
            let previous = getiopolicy_np(IOPOL_TYPE_VFS_MATERIALIZE_DATALESS_FILES, IOPOL_SCOPE_THREAD)
            setiopolicy_np(IOPOL_TYPE_VFS_MATERIALIZE_DATALESS_FILES, IOPOL_SCOPE_THREAD, IOPOL_MATERIALIZE_DATALESS_FILES_OFF)
            defer {
                if previous >= 0 { setiopolicy_np(IOPOL_TYPE_VFS_MATERIALIZE_DATALESS_FILES, IOPOL_SCOPE_THREAD, previous) }
                slots.signal()
                done.signal()
            }
            result.set(operation())
        }
        guard done.wait(timeout: deadline) == .success else { return nil }
        return result.get()
    }
}

private final class SearchReadResult<T>: @unchecked Sendable {
    private let lock = NSLock()
    private var value: T?
    func set(_ value: T) { lock.lock(); self.value = value; lock.unlock() }
    func get() -> T? { lock.lock(); defer { lock.unlock() }; return value }
}
