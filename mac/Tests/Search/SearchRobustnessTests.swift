import Foundation
import CoreServices
import Testing

private final class RobustSearchTree {
    let root = FileManager.default.temporaryDirectory.appendingPathComponent("FolderSplitter-search-robust-" + UUID().uuidString).resolvingSymlinksInPath()
    var store: URL { root.appendingPathComponent("cache/index") }
    init() throws { try FileManager.default.createDirectory(at: root, withIntermediateDirectories: true) }
    deinit { try? FileManager.default.removeItem(at: root) }
    @discardableResult func file(_ name: String) throws -> URL {
        let url = root.appendingPathComponent(name)
        try FileManager.default.createDirectory(at: url.deletingLastPathComponent(), withIntermediateDirectories: true)
        try Data().write(to: url)
        return url
    }
    var scope: SearchScope { SearchScope(roots: [root.path], excluded: [], storeURL: store) }
}

private func robustEventually(_ predicate: () async -> Bool) async throws {
    for _ in 0..<700 {
        if await predicate() { return }
        try await Task.sleep(for: .milliseconds(30))
    }
    Issue.record("Search did not reconcile before its deadline")
}

private func robustHits(_ snapshot: SearchSnapshot, _ term: String) -> [SearchHit] {
    snapshot.search(SearchQuery(term), priorityRoots: [], limit: 100, cancelled: { false })
}

@Suite("Search robustness", .serialized)
struct SearchRobustnessTests {
    @Test func watcherExclusionsRespectSystemLimitAndCloudBranches() {
        let paths = PathSearchService.defaultExclusions.filter { $0.hasPrefix("/") || $0.hasPrefix("~") }.map(SearchText.path)
        let excluded = SearchIndexWatcher.exclusionPaths(paths, root: "/")
        #expect(excluded.count == 8)
        for required in ["/private", "/System", "/Library", "~/Library", "~/.Trash", "~/.dropbox"].map(SearchText.path) {
            #expect(excluded.contains(required))
        }
        for cloud in SearchScope.cloudRoots() {
            let branch = SearchIndexWatcher.exclusionPaths(paths, root: cloud)
            #expect(!branch.contains { SearchText.within(cloud, $0) })
        }
    }

    @Test func cloudRootsDoNotOpenTheRestOfLibrary() throws {
        let tree = try RobustSearchTree()
        try tree.file("Library/CloudStorage/Dropbox/cloud-needle.txt")
        try tree.file("Library/Mobile Documents/com~apple~CloudDocs/icloud-needle.txt")
        try tree.file("Library/Application Support/private-needle.txt")
        try tree.file("Library/Mobile Documents/other-app/private-needle.txt")
        try tree.file(".dropbox/private-needle.txt")
        let roots = [tree.root.path] + SearchScope.cloudRoots(home: tree.root.path)
        let scope = SearchScope(roots: roots, excluded: [], storeURL: tree.store, home: tree.root.path)
        let snapshot = SearchIndexBuilder(scope: scope).build(cancelled: { false }, progress: { _ in })
        #expect(scope.excluded.contains(tree.root.path + "/Library"))
        #expect(scope.skipsContents(tree.root.path + "/Library"))
        #expect(!scope.skipsContents(tree.root.path + "/Library/CloudStorage/Dropbox"))
        #expect(robustHits(snapshot, "cloud-needle").count == 2)
        #expect(robustHits(snapshot, "private-needle").isEmpty)
        let library = tree.root.path + "/Library"
        let blockedScope = SearchScope(roots: roots, excluded: [library], storeURL: tree.store, home: tree.root.path)
        #expect(blockedScope.fingerprint != scope.fingerprint)
        let blocked = SearchIndexBuilder(scope: blockedScope, snapshot: snapshot).reconcile([library], cancelled: { false })
        #expect(robustHits(blocked, "cloud-needle").isEmpty)
        let restored = SearchIndexBuilder(scope: scope, snapshot: blocked).reconcile([library], cancelled: { false })
        #expect(robustHits(restored, "cloud-needle").count == 2)
        #expect(robustHits(restored, "private-needle").isEmpty)
    }

    @Test func droppedSubtreeAndAttributeEventsStayLocal() throws {
        let tree = try RobustSearchTree()
        let a = try tree.file("affected/child/old.txt")
        let b = try tree.file("untouched/old.txt")
        let builder = SearchIndexBuilder(scope: tree.scope)
        _ = builder.build(cancelled: { false }, progress: { _ in })
        try FileManager.default.removeItem(at: a)
        try FileManager.default.removeItem(at: b)
        try tree.file("affected/child/new.txt")
        var batch = SearchIndexWatcher.Batch()
        let subtree = tree.root.path + "/affected"
        batch.append(path: subtree, flags: FSEventStreamEventFlags(kFSEventStreamEventFlagMustScanSubDirs | kFSEventStreamEventFlagUserDropped), id: 42)
        #expect(!batch.requiresScan)
        #expect(batch.rescanPaths == [subtree])
        #expect(batch.paths.isEmpty)
        let rescanned = builder.reconcile(batch.rescanPaths, cancelled: { false })
        #expect(robustHits(rescanned, "new.txt").count == 1)
        #expect(robustHits(rescanned, "old.txt").map(\.path) == [b.path])
        try tree.file("affected/child/deep-unseen.txt")
        try tree.file("affected/direct-visible.txt")
        var attributes = SearchIndexWatcher.Batch()
        attributes.append(path: subtree, flags: FSEventStreamEventFlags(kFSEventStreamEventFlagItemIsDir | kFSEventStreamEventFlagItemXattrMod), id: 43)
        let shallow = builder.reconcile(attributes.shallowPaths, shallow: attributes.shallowPaths, cancelled: { false })
        #expect(robustHits(shallow, "direct-visible").count == 1)
        #expect(robustHits(shallow, "deep-unseen").isEmpty)
    }

    @Test func loadedDeniedFolderIsRecheckedAndQuitPersists() async throws {
        let tree = try RobustSearchTree()
        let file = try tree.file("denied/recovered-needle.txt")
        var snapshot = SearchIndexBuilder(scope: tree.scope).build(cancelled: { false }, progress: { _ in })
        let denied = file.deletingLastPathComponent().path
        snapshot.directories.removeAll { SearchText.within($0.path, denied) }
        snapshot.denied = [denied]
        snapshot.eventID = FSEventsGetCurrentEventId()
        snapshot.volumeUUIDs = SearchStore.volumeUUIDs([tree.root.path])
        try SearchStore.save(snapshot, scope: tree.scope, to: tree.store)
        let service = PathSearchService(roots: [tree.root.path], storeURL: tree.store)
        service.start()
        defer { service.stop() }
        try await robustEventually { await service.search("recovered-needle", priorityRoots: [], limit: 10).count == 1 }
        #expect(!service.deniedPaths.contains(denied))
        service.stop(savingWithin: 2)
        let saved = try #require(SearchStore.load(from: tree.store, scope: tree.scope, volumeUUIDs: snapshot.volumeUUIDs))
        #expect(saved.denied.isEmpty)
        #expect(robustHits(saved, "recovered-needle").count == 1)
    }

    @Test func unfinishedTraversalRecordsWorkForResume() throws {
        let tree = try RobustSearchTree()
        for n in 0..<5 { try tree.file("folder-\(n)/resume-needle.txt") }
        var scanned = 0
        let partial = SearchIndexBuilder(scope: tree.scope).build(cancelled: { scanned > 0 }, progress: { scanned += $0 })
        #expect(!partial.denied.isEmpty)
        try SearchStore.save(partial, scope: tree.scope, to: tree.store)
        let restored = try #require(SearchStore.load(from: tree.store, scope: tree.scope, volumeUUIDs: []))
        let resumed = SearchIndexBuilder(scope: tree.scope, snapshot: restored).reconcile(Set(restored.denied), cancelled: { false })
        #expect(robustHits(resumed, "resume-needle").count == 5)
        #expect(resumed.denied.isEmpty)
    }

    @Test func stalledReadHasBoundedWait() {
        let release = DispatchSemaphore(value: 0)
        defer { release.signal() }
        let start = Date()
        let value: Int? = SearchFilesystem.perform(within: 0.02) { release.wait(); return 1 }
        #expect(value == nil)
        #expect(Date().timeIntervalSince(start) < 1)
        #expect(SearchFilesystem.perform { 2 } == 2)
    }
}
