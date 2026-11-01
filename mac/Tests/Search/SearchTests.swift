import Foundation
import Testing

private final class SearchTestTree {
    let root: URL
    let store: URL
    init() throws {
        root = FileManager.default.temporaryDirectory.appendingPathComponent("FolderSplitter-search-" + UUID().uuidString).resolvingSymlinksInPath()
        store = root.appendingPathComponent("cache/paths.index")
        try FileManager.default.createDirectory(at: root, withIntermediateDirectories: true)
    }
    deinit { try? FileManager.default.removeItem(at: root) }
    @discardableResult func directory(_ path: String) throws -> URL {
        let url = root.appendingPathComponent(path)
        try FileManager.default.createDirectory(at: url, withIntermediateDirectories: true)
        return url
    }
    @discardableResult func file(_ path: String) throws -> URL {
        let url = root.appendingPathComponent(path)
        try FileManager.default.createDirectory(at: url.deletingLastPathComponent(), withIntermediateDirectories: true)
        try Data().write(to: url)
        return url
    }
    func scope(excluded: [String] = []) -> SearchScope {
        SearchScope(roots: [root.path], excluded: excluded, storeURL: store)
    }
    func snapshot(excluded: [String] = []) -> SearchSnapshot {
        SearchIndexBuilder(scope: scope(excluded: excluded)).build(cancelled: { false }, progress: { _ in })
    }
}

private struct SearchWaitTimeout: Error {}
private func searchEventually(timeout: TimeInterval = 20, _ predicate: () async throws -> Bool) async throws {
    let end = Date().addingTimeInterval(timeout)
    while Date() < end {
        if try await predicate() { return }
        try await Task.sleep(for: .milliseconds(30))
    }
    throw SearchWaitTimeout()
}

private func hits(_ snapshot: SearchSnapshot, _ text: String, priority: [String] = [], limit: Int = 500) -> [SearchHit] {
    snapshot.search(SearchQuery(text), priorityRoots: priority, limit: limit, cancelled: { false })
}

@Suite("Search query and packed index")
struct SearchQueryTests {
    @Test func fragmentsQuotesAndPathBoundaries() {
        let path = "/Research/KMA_shared_2026/Raw_data_15min_rain/rn_15m_2022_08.nc"
        #expect(SearchQuery("nc 15m 2022").matches(path))
        #expect(SearchQuery(".NC 2022_08").matches(path))
        #expect(!SearchQuery("shared nc").matches(path))
        #expect(SearchQuery("15min_rain/rn_15m").matches(path))
        #expect(!SearchQuery("rain/n_15m").matches(path))
        #expect(!SearchQuery("min/rn_15m").matches(path))
        #expect(SearchQuery("\"meeting notes\" docx").matches("/notes/Team meeting notes.docx"))
        #expect(!SearchQuery("\"meeting notes\"").matches("/notes/meeting other notes.docx"))
        #expect(SearchQuery("  \"meeting notes").terms == ["meeting notes"])
        #expect(SearchQuery("rain//rn").terms == ["rain/rn"])
        #expect(!SearchQuery("   \"\"").matches(path))
        #expect(SearchQuery("자료/보고서").matches("/작업/참고자료/보고서.txt".decomposedStringWithCanonicalMapping))
    }

    @Test func nfcOwnNameAndAnchors() throws {
        let tree = try SearchTestTree()
        try tree.file("참고자료/월간 보고서.TXT".decomposedStringWithCanonicalMapping)
        try tree.file("참고자료/child/report.txt")
        let snapshot = tree.snapshot()
        let result = hits(snapshot, "\"월간 보고서\" txt")
        #expect(result.count == 1)
        let first = try #require(result.first)
        #expect(first.path == PathText.nfc(first.path))
        #expect(hits(snapshot, "자료/월간").count == 1)
        #expect(hits(snapshot, "자료 txt").isEmpty)
        #expect(hits(snapshot, "자료/child").map(\.name) == ["child"])
        #expect(hits(snapshot, "txt", limit: 0).isEmpty)
        #expect(snapshot.search(SearchQuery("txt"), priorityRoots: [], limit: 50, cancelled: { true }).isEmpty)
    }

    @Test func scopePackagesAndSymlinks() throws {
        let tree = try SearchTestTree()
        for directory in [".git", "node_modules", "excluded", ".Spotlight-V100", ".fseventsd", ".DocumentRevisions-V100", ".TemporaryItems"] {
            try tree.file(directory + "/hidden-needle.txt")
        }
        try tree.file("Example.app/Contents/hidden-needle.txt")
        try tree.file("normal/visible-needle.txt")
        let target = try tree.directory("target")
        try tree.file("target/linked-needle.txt")
        try FileManager.default.createSymbolicLink(at: tree.root.appendingPathComponent("alias"), withDestinationURL: target)
        let snapshot = tree.snapshot(excluded: [tree.root.appendingPathComponent("excluded").path])
        #expect(hits(snapshot, "hidden-needle").isEmpty)
        #expect(hits(snapshot, "visible-needle").count == 1)
        #expect(hits(snapshot, "linked-needle").count == 1)
        #expect(hits(snapshot, "Example.app").first?.isDirectory == false)
        #expect(hits(snapshot, "excluded").first?.isDirectory == true)
        #expect(hits(snapshot, ".git").count == 1)
        #expect(hits(snapshot, "alias").first?.isDirectory == false)
    }

    @Test func rankingBoundariesAndPaging() throws {
        let tree = try SearchTestTree()
        for index in 0..<550 { try tree.file(String(format: "all/needle-%03d.txt", index)) }
        try tree.file("current/needle-last.txt")
        try tree.file("current-other/needle-first.txt")
        try tree.directory("favorite/needle-folder")
        try tree.file("frequent/needle-file.txt")
        let snapshot = tree.snapshot()
        let priority = ["current", "favorite", "frequent"].map { tree.root.appendingPathComponent($0).path }
        let full = hits(snapshot, "needle", priority: priority)
        #expect(full.count == 500)
        #expect(full.prefix(3).map(\.name) == ["needle-last.txt", "needle-folder", "needle-file.txt"])
        #expect(hits(snapshot, "needle", priority: priority, limit: 50) == Array(full.prefix(50)))
        #expect(hits(snapshot, "needle", priority: priority, limit: 200) == Array(full.prefix(200)))
        #expect(hits(snapshot, "needle", limit: -1).isEmpty)
    }

    @Test func binaryRoundTripAndCorruption() throws {
        let tree = try SearchTestTree()
        try tree.file("폴더/보고서.txt")
        var snapshot = tree.snapshot()
        snapshot.eventID = 12345
        snapshot.volumeUUIDs = SearchStore.volumeUUIDs([tree.root.path])
        try SearchStore.save(snapshot, scope: tree.scope(), to: tree.store)
        let loaded = try #require(SearchStore.load(from: tree.store, scope: tree.scope(), volumeUUIDs: snapshot.volumeUUIDs))
        #expect(loaded.eventID == 12345)
        #expect(loaded.count == snapshot.count)
        #expect(hits(loaded, "보고서") == hits(snapshot, "보고서"))
        #expect(SearchStore.load(from: tree.store, scope: tree.scope(excluded: [tree.root.path + "/폴더"]), volumeUUIDs: snapshot.volumeUUIDs) == nil)
        #expect(SearchStore.load(from: tree.store, scope: tree.scope(), volumeUUIDs: ["different-volume"]) == nil)
        var bytes = try Data(contentsOf: tree.store)
        bytes[bytes.count / 2] ^= 0x55
        try bytes.write(to: tree.store)
        #expect(SearchStore.load(from: tree.store, scope: tree.scope(), volumeUUIDs: snapshot.volumeUUIDs) == nil)
        for length in [0, 1, 16, 47] {
            try Data(repeating: 255, count: length).write(to: tree.store)
            #expect(SearchStore.load(from: tree.store, scope: tree.scope(), volumeUUIDs: snapshot.volumeUUIDs) == nil)
        }
    }
}

@Suite("Live search service", .serialized)
struct SearchServiceTests {
    @Test func corruptSnapshotRebuildAndExclusionChanges() async throws {
        let tree = try SearchTestTree()
        try tree.file("folder/find-me.txt")
        try FileManager.default.createDirectory(at: tree.store.deletingLastPathComponent(), withIntermediateDirectories: true)
        try Data("corrupt".utf8).write(to: tree.store)
        let service = PathSearchService(roots: [tree.root.path], storeURL: tree.store)
        service.start(); service.start()
        defer { service.stop() }
        try await searchEventually { service.state.isSearchable }
        #expect(await service.search("find-me", priorityRoots: [], limit: 50).count == 1)
        service.setExcludedFolders([tree.root.appendingPathComponent("folder").path])
        try await searchEventually { await service.search("find-me", priorityRoots: [], limit: 50).isEmpty }
        #expect(await service.search("folder", priorityRoots: [], limit: 50).contains { $0.path == tree.root.appendingPathComponent("folder").path })
        service.setExcludedFolders([])
        try await searchEventually { await service.search("find-me", priorityRoots: [], limit: 50).count == 1 }
        service.rebuild()
        try await searchEventually { service.state.isSearchable }
        #expect(await service.search("find-me", priorityRoots: [], limit: 50).count == 1)
    }

    @Test func fseventsCreateRenameDeleteAndOfflineReplay() async throws {
        let tree = try SearchTestTree()
        try tree.directory("live")
        let offlineRename = try tree.file("live/offline-before.txt")
        let offlineDelete = try tree.file("live/offline-delete.txt")
        let service = PathSearchService(roots: [tree.root.path], storeURL: tree.store)
        service.start()
        defer { service.stop() }
        try await searchEventually { service.state.isSearchable }
        try await searchEventually {
            SearchStore.load(from: tree.store, scope: tree.scope(), volumeUUIDs: SearchStore.volumeUUIDs([tree.root.path])) != nil
        }
        let initialBytes = try Data(contentsOf: tree.store)
        let initialSnapshot = try #require(SearchStore.load(from: tree.store, scope: tree.scope(), volumeUUIDs: SearchStore.volumeUUIDs([tree.root.path])))
        let first = try tree.file("live/watch-created.txt")
        try await searchEventually { await service.search("watch-created", priorityRoots: [], limit: 50).count == 1 }
        let renamed = tree.root.appendingPathComponent("live/watch-renamed.txt")
        try FileManager.default.moveItem(at: first, to: renamed)
        try await searchEventually {
            let old = await service.search("watch-created", priorityRoots: [], limit: 50)
            let new = await service.search("watch-renamed", priorityRoots: [], limit: 50)
            return old.isEmpty && new.count == 1
        }
        // Reconciliation must publish promptly without serializing the entire index per batch.
        #expect(try Data(contentsOf: tree.store) == initialBytes)
        let nested = try tree.file("live/new-directory/nested-watch.txt")
        try await searchEventually { await service.search("nested-watch", priorityRoots: [], limit: 50).count == 1 }
        try FileManager.default.removeItem(at: nested.deletingLastPathComponent())
        try FileManager.default.removeItem(at: renamed)
        try await searchEventually {
            let old = await service.search("watch-renamed", priorityRoots: [], limit: 50)
            let nested = await service.search("nested-watch", priorityRoots: [], limit: 50)
            return old.isEmpty && nested.isEmpty
        }
        try tree.file("live/stopped-save.txt")
        try await searchEventually { await service.search("stopped-save", priorityRoots: [], limit: 50).count == 1 }
        #expect(try Data(contentsOf: tree.store) == initialBytes)
        service.stop()
        try await searchEventually {
            guard let snapshot = SearchStore.load(from: tree.store, scope: tree.scope(), volumeUUIDs: SearchStore.volumeUUIDs([tree.root.path])) else { return false }
            return hits(snapshot, "watch").isEmpty && hits(snapshot, "stopped-save").count == 1 && snapshot.eventID > initialSnapshot.eventID
        }
        #expect(await service.search("watch", priorityRoots: [], limit: 50).isEmpty)
        try tree.file("live/offline-created.txt")
        try FileManager.default.moveItem(at: offlineRename, to: tree.root.appendingPathComponent("live/offline-after.txt"))
        try FileManager.default.removeItem(at: offlineDelete)
        let restored = PathSearchService(roots: [tree.root.path], storeURL: tree.store)
        restored.start()
        defer { restored.stop() }
        try await searchEventually {
            let created = await restored.search("offline-created", priorityRoots: [], limit: 50)
            let renamed = await restored.search("offline-after", priorityRoots: [], limit: 50)
            let old = await restored.search("offline-before", priorityRoots: [], limit: 50)
            let deleted = await restored.search("offline-delete", priorityRoots: [], limit: 50)
            return created.count == 1 && renamed.count == 1 && old.isEmpty && deleted.isEmpty
        }
        #expect(restored.state.isSearchable)
        restored.stop()
        try await searchEventually {
            guard let snapshot = SearchStore.load(from: tree.store, scope: tree.scope(), volumeUUIDs: SearchStore.volumeUUIDs([tree.root.path])) else { return false }
            return hits(snapshot, "offline-created").count == 1 && hits(snapshot, "offline-after").count == 1
                && hits(snapshot, "offline-before").isEmpty && hits(snapshot, "offline-delete").isEmpty
        }
    }
}
