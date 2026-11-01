import Foundation
import CoreServices

final class SearchIndexWatcher {
    struct Batch {
        var paths: Set<String> = []
        var eventID: UInt64 = 0
        var requiresScan = false
        var historyDone = false
    }
    private var stream: FSEventStreamRef?
    private let receive: (Batch) -> Void
    private let rootMappings: [(physical: String, logical: String)]

    init?(roots: [String], since: UInt64, queue: DispatchQueue, receive: @escaping (Batch) -> Void) {
        self.receive = receive
        rootMappings = roots.map { root in
            guard let physical = realpath(root, nil) else { return (root, root) }
            defer { free(physical) }
            return (PathText.nfc(String(cString: physical)), root)
        }
        var context = FSEventStreamContext(version: 0, info: Unmanaged.passUnretained(self).toOpaque(), retain: nil, release: nil, copyDescription: nil)
        let flags = FSEventStreamCreateFlags(kFSEventStreamCreateFlagFileEvents | kFSEventStreamCreateFlagUseCFTypes | kFSEventStreamCreateFlagWatchRoot | kFSEventStreamCreateFlagNoDefer)
        guard let stream = FSEventStreamCreate(kCFAllocatorDefault, { _, context, count, paths, flags, ids in
            guard let context else { return }
            let watcher = Unmanaged<SearchIndexWatcher>.fromOpaque(context).takeUnretainedValue()
            let values = unsafeBitCast(paths, to: NSArray.self)
            var batch = Batch()
            let invalid = FSEventStreamEventFlags(kFSEventStreamEventFlagMustScanSubDirs | kFSEventStreamEventFlagEventIdsWrapped | kFSEventStreamEventFlagRootChanged | kFSEventStreamEventFlagUserDropped | kFSEventStreamEventFlagKernelDropped)
            for index in 0..<count {
                batch.eventID = max(batch.eventID, ids[index])
                if flags[index] & invalid != 0 { batch.requiresScan = true }
                if flags[index] & FSEventStreamEventFlags(kFSEventStreamEventFlagHistoryDone) != 0 { batch.historyDone = true; continue }
                if let value = values[index] as? String {
                    let path = SearchText.path(value)
                    if let root = watcher.rootMappings.first(where: { SearchText.within(path, $0.physical) }) {
                        let suffix = path == root.physical ? "" : String(path.dropFirst(root.physical == "/" ? 0 : root.physical.count))
                        batch.paths.insert(root.logical == "/" ? path : root.logical + suffix)
                    } else { batch.paths.insert(path) }
                }
            }
            watcher.receive(batch)
        }, &context, roots as CFArray, since, 1.0, flags) else { return nil }
        self.stream = stream
        FSEventStreamSetDispatchQueue(stream, queue)
        guard FSEventStreamStart(stream) else {
            FSEventStreamInvalidate(stream); FSEventStreamRelease(stream); self.stream = nil
            return nil
        }
    }

    func stop() {
        guard let stream else { return }
        FSEventStreamStop(stream); FSEventStreamInvalidate(stream); FSEventStreamRelease(stream)
        self.stream = nil
    }

    deinit { stop() }
}
