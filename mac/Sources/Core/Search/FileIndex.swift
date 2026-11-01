import Foundation

struct SearchDirectory: Sendable {
    let path: String
    // Each entry: type byte, NFC name, NUL, case-folded NFC name, NUL.
    let packed: Data
    let count: Int
    let prefix: String
    let foldedPrefix: [UInt8]

    init(path: String, packed: Data, count: Int) {
        self.path = path; self.packed = packed; self.count = count
        prefix = path == "/" ? "/" : path + "/"
        foldedPrefix = Array(SearchText.fold(prefix).utf8)
    }

    static func pack(_ entries: [(String, Bool)]) -> Data {
        var data = Data()
        for (name, directory) in entries {
            data.append(directory ? 1 : 2)
            data.append(contentsOf: name.utf8); data.append(0)
            data.append(contentsOf: SearchText.fold(name).utf8); data.append(0)
        }
        return data
    }

    static func valid(_ data: Data, count: Int) -> Bool {
        var offset = 0
        var actual = 0
        while offset < data.count {
            guard data[offset] == 1 || data[offset] == 2 else { return false }
            offset += 1
            for _ in 0..<2 {
                let start = offset
                while offset < data.count && data[offset] != 0 { offset += 1 }
                guard offset < data.count, offset > start,
                      let text = String(data: data[start..<offset], encoding: .utf8),
                      !text.contains("/") else { return false }
                offset += 1
            }
            actual += 1
        }
        return actual == count
    }
}

struct SearchSnapshot: Sendable {
    var roots: [SearchHit] = []
    var directories: [SearchDirectory] = []
    var denied: [String] = []
    var eventID: UInt64 = 0
    var volumeUUIDs: [String] = []
    var count: Int { roots.count + directories.reduce(0) { $0 + $1.count } }

    func search(_ query: SearchQuery, priorityRoots: [String], limit: Int,
                cancelled: @escaping @Sendable () -> Bool) -> [SearchHit] {
        guard limit > 0, !query.isEmpty, !cancelled() else { return [] }
        let roots = priorityRoots.map { SearchText.fold(SearchText.path($0)) }
        let workers = count >= 200_000 ? min(4, ProcessInfo.processInfo.activeProcessorCount) : 1
        let results = SearchPartitions(workers)
        DispatchQueue.concurrentPerform(iterations: workers) { worker in
            var top = SearchTopMatches(limit: limit, roots: roots, query: query)
            if worker == 0 {
                for root in self.roots where query.matches(root.path) { top.add(root) }
            }
            for number in (directories.count * worker / workers)..<(directories.count * (worker + 1) / workers) {
                if cancelled() { return }
                let directory = directories[number]
                let prefix = directory.prefix
                let foldedPrefix = directory.foldedPrefix
                let foldedPrefixString = String(decoding: foldedPrefix, as: UTF8.self)
                directory.packed.withUnsafeBytes { raw in
                    let bytes = raw.bindMemory(to: UInt8.self)
                    var offset = 0
                    var checked = 0
                    while offset < bytes.count {
                        if checked & 255 == 0 && cancelled() { return }
                        checked += 1
                        let isDirectory = bytes[offset] == 1
                        let nameStart = offset + 1
                        var nameEnd = nameStart
                        while bytes[nameEnd] != 0 { nameEnd += 1 }
                        let foldedStart = nameEnd + 1
                        var foldedEnd = foldedStart
                        while bytes[foldedEnd] != 0 { foldedEnd += 1 }
                        offset = foldedEnd + 1
                        let name = UnsafeBufferPointer(rebasing: bytes[foldedStart..<foldedEnd])
                        if !query.nameTerms.allSatisfy({ searchContains(name, $0) }) { continue }
                        // As in the reference, path-only searches return anchors, not every
                        // descendant of an already matching folder.
                        if query.nameTerms.isEmpty && !query.pathTerms.isEmpty {
                            let anchor = query.pathTails.contains { $0.isEmpty || searchContains(name, $0) }
                            if !anchor { continue }
                        }
                        if !query.pathTerms.isEmpty {
                            var path = foldedPrefix
                            path.append(contentsOf: name)
                            let matches = path.withUnsafeBufferPointer { full in
                                query.pathTerms.allSatisfy { searchContains(full, $0) }
                            }
                            if !matches { continue }
                            // A path-only term must actually cross into the item's own name.
                            if query.nameTerms.isEmpty {
                                let leafStart = foldedPrefix.count
                                let anchored = path.withUnsafeBufferPointer { full in
                                    query.pathTerms.contains { term in
                                        let start = max(0, leafStart - term.count + 1)
                                        return searchContains(UnsafeBufferPointer(rebasing: full[start...]), term)
                                    }
                                }
                                if !anchored { continue }
                            }
                        }
                        let original = String(decoding: bytes[nameStart..<nameEnd], as: UTF8.self)
                        let folded = foldedPrefixString + String(decoding: name, as: UTF8.self)
                        top.add(SearchHit(path: prefix + original, isDirectory: isDirectory), foldedPath: folded)
                    }
                }
            }
            results.set(top.sorted(), at: worker)
        }
        if cancelled() { return [] }
        var combined = SearchTopMatches(limit: limit, roots: roots, query: query)
        for result in results.values { for hit in result { combined.add(hit) } }
        return combined.sorted()
    }
}

private final class SearchPartitions: @unchecked Sendable {
    private let lock = NSLock()
    private var storage: [[SearchHit]]
    init(_ count: Int) { storage = Array(repeating: [], count: count) }
    func set(_ hits: [SearchHit], at index: Int) { lock.lock(); storage[index] = hits; lock.unlock() }
    var values: [[SearchHit]] { lock.lock(); defer { lock.unlock() }; return storage }
}

private struct RankedSearchHit {
    let hit: SearchHit
    let folded: String
    let scope: Int
    let penalty: Int

    func precedes(_ other: Self) -> Bool {
        if scope != other.scope { return scope < other.scope }
        if penalty != other.penalty { return penalty < other.penalty }
        if hit.isDirectory != other.hit.isDirectory { return hit.isDirectory }
        if folded != other.folded { return folded < other.folded }
        return hit.path < other.hit.path
    }
}

private struct SearchTopMatches {
    let limit: Int
    let roots: [String]
    let query: SearchQuery
    private var heap: [RankedSearchHit] = [] // worst first

    init(limit: Int, roots: [String], query: SearchQuery) {
        self.limit = limit; self.roots = roots; self.query = query
    }

    mutating func add(_ hit: SearchHit, foldedPath: String? = nil) {
        let folded = foldedPath ?? SearchText.fold(hit.path)
        let scope = roots.firstIndex { SearchText.within(folded, $0) } ?? roots.count
        let penalty = query.terms.filter { term in
            guard term.contains("/"), !term.hasPrefix("/") else { return false }
            return !folded.hasPrefix(term) && !folded.contains("/" + term)
        }.count
        let value = RankedSearchHit(hit: hit, folded: folded, scope: scope, penalty: penalty)
        if heap.count == limit {
            guard value.precedes(heap[0]) else { return }
            heap[0] = value
            var index = 0
            while index * 2 + 1 < heap.count {
                var child = index * 2 + 1
                if child + 1 < heap.count && heap[child].precedes(heap[child + 1]) { child += 1 }
                if !heap[index].precedes(heap[child]) { break }
                heap.swapAt(index, child); index = child
            }
        } else {
            heap.append(value)
            var index = heap.count - 1
            while index > 0 {
                let parent = (index - 1) / 2
                if !heap[parent].precedes(heap[index]) { break }
                heap.swapAt(parent, index); index = parent
            }
        }
    }

    func sorted() -> [SearchHit] { heap.sorted { $0.precedes($1) }.map(\.hit) }
}
