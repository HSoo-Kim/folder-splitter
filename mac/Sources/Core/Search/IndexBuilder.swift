import Foundation

struct SearchScope: Sendable {
    static let namedExclusions: Set<String> = [".Spotlight-V100", ".fseventsd", ".DocumentRevisions-V100", ".TemporaryItems", ".git", "node_modules"]
    let roots: [String]
    let excluded: [String]
    let storePath: String

    init(roots: [String], excluded: [String], storeURL: URL) {
        self.roots = roots
        let defaults = PathSearchService.defaultExclusions.filter { $0.hasPrefix("/") || $0.hasPrefix("~") }
            .map(SearchText.path).filter { tree in !roots.contains { SearchText.within($0, tree) } }
        self.excluded = Array(Set(defaults + excluded.map(SearchText.path))).sorted()
        storePath = SearchText.path(storeURL.path)
    }

    func inScope(_ path: String) -> Bool { roots.contains { SearchText.within(path, $0) } }

    func skipsContents(_ path: String) -> Bool {
        if path == "/System/Volumes/Data" { return true }
        if excluded.contains(where: { SearchText.within(path, $0) }) { return true }
        // Explicit roots can be inside normally excluded trees, just as in the reference.
        let root = roots.filter { SearchText.within(path, $0) }.max { $0.count < $1.count } ?? "/"
        let relative = path == root ? "" : String(path.dropFirst(root == "/" ? 1 : root.count + 1))
        return relative.split(separator: "/").contains { Self.namedExclusions.contains(String($0)) }
    }

    func ignoresItem(_ path: String) -> Bool {
        if path == storePath || path.hasPrefix(storePath + ".") { return true }
        if path == "/System/Volumes/Data" || path.hasPrefix("/System/Volumes/Data/") { return true }
        let parent = (path as NSString).deletingLastPathComponent
        return skipsContents(parent)
    }

    var fingerprint: UInt64 {
        SearchStore.digest(Data((roots.joined(separator: "\0") + "\n" + excluded.joined(separator: "\0") + "\n" + Self.namedExclusions.sorted().joined(separator: "\0")).utf8))
    }
}

final class SearchIndexBuilder {
    let scope: SearchScope
    private var directories: [String: SearchDirectory]
    private var denied: Set<String>
    private let manager = FileManager.default
    private let keys: Set<URLResourceKey> = [.isDirectoryKey, .isSymbolicLinkKey, .isPackageKey]

    init(scope: SearchScope, snapshot: SearchSnapshot = SearchSnapshot()) {
        self.scope = scope
        directories = Dictionary(snapshot.directories.map { ($0.path, $0) }, uniquingKeysWith: { _, new in new })
        denied = Set(snapshot.denied)
    }

    func build(cancelled: () -> Bool, progress: (Int) -> Void) -> SearchSnapshot {
        directories.removeAll(keepingCapacity: true); denied.removeAll()
        var count = 0
        for root in scope.roots {
            readTree(root, cancelled: cancelled) { added in count += added; progress(count) }
        }
        return snapshot()
    }

    func reconcile(_ changed: Set<String>, cancelled: () -> Bool) -> SearchSnapshot {
        var trees: [String] = []
        var parents: Set<String> = []
        for path in changed.sorted(by: { $0.count < $1.count }) {
            if cancelled() { break }
            guard scope.inScope(path), !scope.ignoresItem(path), !hasOpaqueAncestor(path),
                  !trees.contains(where: { SearchText.within(path, $0) }) else { continue }
            if directories[path] != nil || traversable(path) {
                removeTree(path)
                readTree(path, cancelled: cancelled, progress: { _ in })
                trees.append(path)
            }
            let parent = (path as NSString).deletingLastPathComponent
            if scope.inScope(parent) { parents.insert(parent) }
        }
        for parent in parents where !cancelled() {
            if traversable(parent) && !scope.skipsContents(parent) && !hasOpaqueAncestor(parent) {
                let children = readDirectory(parent)
                for child in children where directories[child] == nil {
                    readTree(child, cancelled: cancelled, progress: { _ in })
                }
            } else {
                let inaccessible = denied.contains(parent)
                removeTree(parent)
                if inaccessible { denied.insert(parent) }
            }
        }
        return snapshot()
    }

    private func traversable(_ path: String) -> Bool {
        do {
            let values = try URL(fileURLWithPath: path).resourceValues(forKeys: keys)
            return values.isDirectory == true && values.isSymbolicLink != true && values.isPackage != true
        } catch {
            let failure = error as NSError
            if failure.code == NSFileReadNoPermissionError || failure.domain == NSPOSIXErrorDomain && (failure.code == 1 || failure.code == 13) {
                denied.insert(path)
            }
            return false
        }
    }

    private func hasOpaqueAncestor(_ path: String) -> Bool {
        var parent = (path as NSString).deletingLastPathComponent
        while scope.inScope(parent) {
            if scope.skipsContents(parent) || !traversable(parent) { return true }
            if parent == "/" { break }
            parent = (parent as NSString).deletingLastPathComponent
        }
        return false
    }

    private func removeTree(_ path: String) {
        for key in directories.keys.filter({ SearchText.within($0, path) }) { directories.removeValue(forKey: key) }
        denied = denied.filter { !SearchText.within($0, path) }
    }

    private func readTree(_ root: String, cancelled: () -> Bool, progress: (Int) -> Void) {
        var pending = [root]
        while let path = pending.popLast() {
            if cancelled() { return }
            guard !scope.skipsContents(path), traversable(path) else { continue }
            autoreleasepool {
                pending.append(contentsOf: readDirectory(path))
                progress(directories[path]?.count ?? 0)
            }
        }
    }

    private func readDirectory(_ path: String) -> [String] {
        denied.remove(path)
        var entries: [(String, Bool)] = []
        var children: [String] = []
        do {
            let urls = try manager.contentsOfDirectory(at: URL(fileURLWithPath: path), includingPropertiesForKeys: Array(keys), options: [])
            entries.reserveCapacity(urls.count)
            for url in urls {
                let full = path == "/" ? "/" + PathText.nfc(url.lastPathComponent) : path + "/" + PathText.nfc(url.lastPathComponent)
                if scope.ignoresItem(full) { continue }
                do {
                    let values = try url.resourceValues(forKeys: keys)
                    let directory = values.isDirectory == true && values.isPackage != true && values.isSymbolicLink != true
                    entries.append((PathText.nfc(url.lastPathComponent), directory))
                    if directory && !scope.skipsContents(full) { children.append(full) }
                } catch {
                    // A disappearing entry is harmless; inaccessible entries still remain findable.
                    if manager.fileExists(atPath: url.path) { entries.append((PathText.nfc(url.lastPathComponent), false)) }
                }
            }
        } catch {
            let error = error as NSError
            if error.code == NSFileReadNoPermissionError || error.domain == NSPOSIXErrorDomain && (error.code == 1 || error.code == 13) {
                denied.insert(path)
            } else if !manager.fileExists(atPath: path) {
                directories.removeValue(forKey: path)
                return []
            } else { denied.insert(path) }
        }
        directories[path] = SearchDirectory(path: path, packed: SearchDirectory.pack(entries), count: entries.count)
        return children
    }

    private func snapshot() -> SearchSnapshot {
        let roots = scope.roots.compactMap { path -> SearchHit? in
            guard let values = try? URL(fileURLWithPath: path).resourceValues(forKeys: keys) else { return nil }
            return SearchHit(path: path, isDirectory: values.isDirectory == true && values.isPackage != true && values.isSymbolicLink != true)
        }
        // Search ranking is independent of directory traversal order. Preserve the dictionary's
        // value iteration instead of sorting every directory again for each small event batch.
        return SearchSnapshot(roots: roots, directories: Array(directories.values), denied: denied.sorted())
    }
}
