import Foundation

struct SearchScope: Sendable {
    static let namedExclusions: Set<String> = [".Spotlight-V100", ".fseventsd", ".DocumentRevisions-V100", ".TemporaryItems", ".git", "node_modules"]
    let roots: [String]
    let excluded: [String]
    let storePath: String
    private let defaults: [String]
    private let custom: [String]

    static func cloudRoots(home: String = FileManager.default.homeDirectoryForCurrentUser.path) -> [String] {
        [home + "/Library/CloudStorage", home + "/Library/Mobile Documents/com~apple~CloudDocs"]
    }

    init(roots: [String], excluded: [String], storeURL: URL, home: String = FileManager.default.homeDirectoryForCurrentUser.path) {
        self.roots = roots
        defaults = PathSearchService.defaultExclusions.filter { $0.hasPrefix("/") || $0.hasPrefix("~") }
            .map { $0.hasPrefix("~/") ? home + String($0.dropFirst()) : SearchText.path($0) }
        custom = excluded.map(SearchText.path)
        self.excluded = Array(Set(defaults + custom)).sorted()
        storePath = SearchText.path(storeURL.path)
    }

    func inScope(_ path: String) -> Bool { roots.contains { SearchText.within(path, $0) } }

    func skipsContents(_ path: String) -> Bool {
        if path == "/System/Volumes/Data" { return true }
        if custom.contains(where: { SearchText.within(path, $0) }) { return true }
        // An explicit root opens only its own branch, never its excluded siblings or ancestors.
        if defaults.contains(where: { tree in
            SearchText.within(path, tree) && !roots.contains { SearchText.within($0, tree) && SearchText.within(path, $0) }
        }) { return true }
        let root = roots.filter { SearchText.within(path, $0) }.max { $0.count < $1.count } ?? "/"
        let relative = path == root ? "" : String(path.dropFirst(root == "/" ? 1 : root.count + 1))
        return relative.split(separator: "/").contains { Self.namedExclusions.contains(String($0)) }
    }

    func ignoresItem(_ path: String) -> Bool {
        if path == storePath || path.hasPrefix(storePath + ".") { return true }
        if path == "/System/Volumes/Data" || path.hasPrefix("/System/Volumes/Data/") { return true }
        if roots.contains(path) { return false }
        return skipsContents((path as NSString).deletingLastPathComponent)
    }

    var fingerprint: UInt64 {
        let parts = [roots.joined(separator: "\0"), excluded.joined(separator: "\0"),
                     custom.sorted().joined(separator: "\0"), Self.namedExclusions.sorted().joined(separator: "\0")]
        return SearchStore.digest(Data(parts.joined(separator: "\n").utf8))
    }
}

final class SearchIndexBuilder {
    let scope: SearchScope
    private var directories: [String: SearchDirectory]
    private var denied: Set<String>
    // Direct-child adjacency makes removal proportional to the removed subtree, not the disk.
    private var children: [String: Set<String>] = [:]
    private let keys: Set<URLResourceKey> = [.isDirectoryKey, .isSymbolicLinkKey, .isPackageKey]

    init(scope: SearchScope, snapshot: SearchSnapshot = SearchSnapshot()) {
        self.scope = scope
        directories = Dictionary(snapshot.directories.map { ($0.path, $0) }, uniquingKeysWith: { _, new in new })
        denied = Set(snapshot.denied)
        for path in Set(directories.keys).union(denied) { link(path) }
    }

    func build(cancelled: () -> Bool, progress: (Int) -> Void,
               partial: (SearchSnapshot) -> Void = { _ in }) -> SearchSnapshot {
        directories.removeAll(keepingCapacity: true); denied.removeAll(); children.removeAll(keepingCapacity: true)
        var count = 0
        readTrees(scope.roots, cancelled: cancelled, progress: { count += $0; progress(count) }, partial: partial)
        return snapshot()
    }

    func reconcile(_ changed: Set<String>, shallow: Set<String> = [], cancelled: () -> Bool,
                   partial: (SearchSnapshot) -> Void = { _ in }) -> SearchSnapshot {
        var trees: [String] = []
        var parents: Set<String> = []
        // Explicit cloud roots have opaque ancestors: an ancestor refresh must visit them
        // separately even though its ordinary traversal deliberately skips ~/Library.
        let explicit = scope.roots.filter { root in changed.contains { !shallow.contains($0) && SearchText.within(root, $0) } }
        for path in changed.union(explicit).sorted(by: { $0.count < $1.count }) {
            if cancelled() { break }
            guard scope.inScope(path), !scope.ignoresItem(path), !hasOpaqueAncestor(path),
                  scope.roots.contains(path) || !trees.contains(where: { SearchText.within(path, $0) }) else { continue }
            if shallow.contains(path) {
                if traversable(path), !scope.skipsContents(path) { _ = readDirectory(path) }
                continue
            }
            if directories[path] != nil || denied.contains(path) || traversable(path) {
                removeTree(path)
                readTrees([path], cancelled: cancelled, progress: { _ in }, partial: partial)
                trees.append(path)
            }
            let parent = (path as NSString).deletingLastPathComponent
            if !scope.roots.contains(path), scope.inScope(parent) { parents.insert(parent) }
        }
        for parent in parents where !cancelled() {
            if traversable(parent) && !scope.skipsContents(parent) && !hasOpaqueAncestor(parent) {
                let newChildren = readDirectory(parent).filter { directories[$0] == nil }
                readTrees(newChildren, cancelled: cancelled, progress: { _ in }, partial: partial)
            } else {
                let inaccessible = denied.contains(parent)
                removeTree(parent)
                if inaccessible { markDenied(parent) }
            }
        }
        return snapshot()
    }

    private func traversable(_ path: String) -> Bool {
        let keys = self.keys
        guard let result = SearchFilesystem.perform({ Result { try URL(fileURLWithPath: path).resourceValues(forKeys: keys) } }) else {
            markDenied(path); return false
        }
        switch result {
        case .success(let values): return values.isDirectory == true && values.isSymbolicLink != true && values.isPackage != true
        case .failure(let error):
            let failure = error as NSError
            if failure.code == NSFileReadNoPermissionError || failure.domain == NSPOSIXErrorDomain && (failure.code == 1 || failure.code == 13) { markDenied(path) }
            return false
        }
    }

    fileprivate func hasOpaqueAncestor(_ path: String) -> Bool {
        if scope.roots.contains(path) { return false }
        var parent = (path as NSString).deletingLastPathComponent
        while scope.inScope(parent) {
            if scope.skipsContents(parent) || !traversable(parent) { return true }
            if scope.roots.contains(parent) || parent == "/" { break }
            parent = (parent as NSString).deletingLastPathComponent
        }
        return false
    }

    private func link(_ path: String) {
        let parent = (path as NSString).deletingLastPathComponent
        if parent != path { children[parent, default: []].insert(path) }
    }

    private func markDenied(_ path: String) { denied.insert(path); link(path) }

    private func removeTree(_ path: String) {
        var pending = [path]
        while let next = pending.popLast() {
            pending.append(contentsOf: children.removeValue(forKey: next) ?? [])
            directories.removeValue(forKey: next); denied.remove(next)
        }
        children[(path as NSString).deletingLastPathComponent]?.remove(path)
    }

    private func readTrees(_ roots: [String], cancelled: () -> Bool, progress: (Int) -> Void,
                           partial: (SearchSnapshot) -> Void) {
        var pending = roots
        var lastPartial = Date()
        while let path = pending.popLast() {
            if cancelled() { markDenied(path); for remaining in pending { markDenied(remaining) }; return }
            if !scope.skipsContents(path), traversable(path) {
                autoreleasepool {
                    pending.append(contentsOf: readDirectory(path))
                    progress(directories[path]?.count ?? 0)
                }
            }
            if Date().timeIntervalSince(lastPartial) >= 1 {
                var value = snapshot()
                value.denied = Array(Set(value.denied).union(pending)).sorted()
                partial(value); lastPartial = Date()
            }
        }
    }

    private func readDirectory(_ path: String) -> [String] {
        let scope = self.scope
        let keys = self.keys
        let result = SearchFilesystem.perform { () -> Result<([(String, Bool)], [String]), Error> in
            Result {
                let urls = try FileManager.default.contentsOfDirectory(at: URL(fileURLWithPath: path), includingPropertiesForKeys: Array(keys), options: [])
                var entries: [(String, Bool)] = []
                var children: [String] = []
                entries.reserveCapacity(urls.count)
                for url in urls {
                    let name = PathText.nfc(url.lastPathComponent)
                    let full = path == "/" ? "/" + name : path + "/" + name
                    if scope.ignoresItem(full) { continue }
                    let values = try? url.resourceValues(forKeys: keys)
                    let directory = values?.isDirectory == true && values?.isPackage != true && values?.isSymbolicLink != true
                    entries.append((name, directory))
                    if directory && !scope.skipsContents(full) { children.append(full) }
                }
                return (entries, children)
            }
        }
        guard let result else { markDenied(path); return [] }
        switch result {
        case .success(let (entries, descendants)):
            denied.remove(path); link(path)
            directories[path] = SearchDirectory(path: path, packed: SearchDirectory.pack(entries), count: entries.count)
            return descendants
        case .failure(let error):
            let failure = error as NSError
            if failure.code == NSFileReadNoSuchFileError || failure.domain == NSPOSIXErrorDomain && failure.code == 2 { removeTree(path) }
            else { markDenied(path) }
            return []
        }
    }

    private func snapshot() -> SearchSnapshot {
        SearchSnapshot(roots: scope.roots.filter { directories[$0] != nil || denied.contains($0) }.map { SearchHit(path: $0, isDirectory: true) }, directories: Array(directories.values), denied: denied.sorted())
    }
}

