import Foundation
import CoreServices

final class SearchIndexWatcher {
    struct Batch {
        var paths: Set<String> = []
        var shallowPaths: Set<String> = []
        var rescanPaths: Set<String> = []
        var eventID: UInt64 = 0
        var requiresScan = false
        var historyDone = false

        mutating func append(path: String, flags: FSEventStreamEventFlags, id: UInt64) {
            eventID = max(eventID, id)
            if flags & FSEventStreamEventFlags(kFSEventStreamEventFlagHistoryDone) != 0 { historyDone = true; return }
            let global = FSEventStreamEventFlags(kFSEventStreamEventFlagEventIdsWrapped | kFSEventStreamEventFlagRootChanged)
            let dropped = FSEventStreamEventFlags(kFSEventStreamEventFlagMustScanSubDirs | kFSEventStreamEventFlagUserDropped | kFSEventStreamEventFlagKernelDropped)
            if flags & global != 0 { requiresScan = true }
            if flags & dropped != 0 { rescanPaths.insert(path) }
            if flags & (global | dropped) != 0 { return }
            let attributes = FSEventStreamEventFlags(kFSEventStreamEventFlagItemInodeMetaMod | kFSEventStreamEventFlagItemFinderInfoMod | kFSEventStreamEventFlagItemChangeOwner | kFSEventStreamEventFlagItemXattrMod)
            let structural = FSEventStreamEventFlags(kFSEventStreamEventFlagItemCreated | kFSEventStreamEventFlagItemRemoved | kFSEventStreamEventFlagItemRenamed | kFSEventStreamEventFlagItemModified)
            if flags & attributes != 0 && flags & (structural | dropped | global) == 0 {
                if !paths.contains(path) { shallowPaths.insert(path) }
            } else { shallowPaths.remove(path); paths.insert(path) }
        }
    }
    private var streams: [FSEventStreamRef] = []
    private let receive: (Batch) -> Void
    private let rootMappings: [(physical: String, logical: String)]
    private let deliveryQueue: DispatchQueue

    static func exclusionPaths(_ exclusions: [String], root: String) -> [String] {
        // The API accepts at most eight paths. Prioritize the high-traffic/privacy areas.
        let priority = ["/private", "/System", "/Library", "~/Library", "~/.Trash", "~/.dropbox", "/Volumes", "/usr"].map(SearchText.path)
        var selected: [String] = []
        for path in priority + exclusions where exclusions.contains(path) && SearchText.within(path, root) && !SearchText.within(root, path) && !selected.contains(path) {
            selected.append(path)
            if selected.count == 8 { break }
        }
        return selected
    }

    init?(roots: [String], exclusions: [String] = [], since: UInt64, queue: DispatchQueue, receive: @escaping (Batch) -> Void) {
        self.receive = receive
        deliveryQueue = queue
        rootMappings = roots.sorted { $0.count > $1.count }.map { root in
            let physical = SearchFilesystem.perform { () -> String in
                guard let value = realpath(root, nil) else { return root }
                defer { free(value) }
                return PathText.nfc(String(cString: value))
            } ?? root
            return (physical, root)
        }
        // Separate streams allow ~/Library to be excluded from / while cloud roots still receive events.
        for root in roots {
            var context = FSEventStreamContext(version: 0, info: Unmanaged.passUnretained(self).toOpaque(), retain: nil, release: nil, copyDescription: nil)
            let flags = FSEventStreamCreateFlags(kFSEventStreamCreateFlagFileEvents | kFSEventStreamCreateFlagUseCFTypes | kFSEventStreamCreateFlagWatchRoot | kFSEventStreamCreateFlagNoDefer)
            guard let stream = FSEventStreamCreate(kCFAllocatorDefault, { _, context, count, paths, flags, ids in
                guard let context else { return }
                let watcher = Unmanaged<SearchIndexWatcher>.fromOpaque(context).takeUnretainedValue()
                let values = unsafeBitCast(paths, to: NSArray.self)
                var batch = Batch()
                for index in 0..<count {
                    guard let value = values[index] as? String else { continue }
                    let path = SearchText.path(value)
                    let logical: String
                    if let root = watcher.rootMappings.first(where: { SearchText.within(path, $0.physical) }) {
                        let suffix = path == root.physical ? "" : String(path.dropFirst(root.physical == "/" ? 0 : root.physical.count))
                        logical = root.logical == "/" ? path : root.logical + suffix
                    } else { logical = path }
                    batch.append(path: logical, flags: flags[index], id: ids[index])
                }
                watcher.receive(batch)
            }, &context, [root] as CFArray, since, 1.0, flags) else { stop(); return nil }
            streams.append(stream)
            let physical = rootMappings.first(where: { $0.logical == root })?.physical ?? root
            let excluded = Self.exclusionPaths(exclusions.filter { !SearchText.within(root, $0) }, root: physical)
            if !excluded.isEmpty, !FSEventStreamSetExclusionPaths(stream, excluded as CFArray) { stop(); return nil }
            FSEventStreamSetDispatchQueue(stream, queue)
            guard FSEventStreamStart(stream) else { stop(); return nil }
        }
    }

    func stop() {
        guard !streams.isEmpty else { return }
        // Dispose on the delivery queue: callbacks may be in flight on it, unlike the worker.
        deliveryQueue.sync {
            for stream in streams { FSEventStreamStop(stream); FSEventStreamInvalidate(stream); FSEventStreamRelease(stream) }
            streams.removeAll()
        }
    }

    deinit { stop() }
}
