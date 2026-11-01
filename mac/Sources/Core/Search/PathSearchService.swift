import Foundation
import CoreServices

/// An immutable packed index is shared by searches; filesystem work and FSEvents reconciliation
/// belong to one background queue. Dirty snapshots are saved at most every five minutes, plus
/// shutdown; FSEvents replays changes since the last saved snapshot's own checkpoint after a crash.
/// Call start after wiring settings; stop before application exit.
final class PathSearchService: PathSearching, @unchecked Sendable {
    static var defaultStoreURL: URL {
        FileManager.default.homeDirectoryForCurrentUser
            .appendingPathComponent("Library/Application Support/Folder Splitter/Search/paths.v1.index")
    }

    /// Absolute/tilde paths exclude contents. Bare names apply at any depth. The folder remains searchable.
    static let defaultExclusions = ["/System", "/Library", "/private", "/dev", "/cores", "/usr", "/bin", "/sbin", "/opt", "/Volumes", "~/Library", "~/.Trash", ".Spotlight-V100", ".fseventsd", ".DocumentRevisions-V100", ".TemporaryItems", ".git", "node_modules"]

    private let roots: [String]
    private let storeURL: URL
    private let lock = NSLock()
    private let worker = DispatchQueue(label: "FolderSplitter.Search.index", qos: .utility)
    private var storedState: SearchIndexState = .disabled
    private var index = SearchSnapshot()
    private var exclusions: [String]
    private var running = false
    private var generation: UInt64 = 0
    private var notificationPending = false
    // Worker-queue-owned state.
    private var watcher: SearchIndexWatcher?
    private var builder: SearchIndexBuilder?
    private var scope: SearchScope?
    private var pending: Set<String> = []
    private var pendingID: UInt64 = 0
    private var invalidated = false
    private var batchStarted: Date?
    private var timer: DispatchWorkItem?
    private var checkpoint: UInt64 = 0
    private var volumes: [String] = []
    private var dirtySnapshot: SearchSnapshot?
    private var dirtyScope: SearchScope?
    private var saveTimer: DispatchWorkItem?
    private var lastSaveAttempt: DispatchTime?
    private static let saveInterval: TimeInterval = 5 * 60

    init(roots: [String] = ["/"], storeURL: URL = PathSearchService.defaultStoreURL, excludedFolders: [String] = []) {
        let normalized = Array(Set(roots.map(SearchText.path))).sorted { $0.count < $1.count }
        var distinct: [String] = []
        for root in normalized where !distinct.contains(where: { SearchText.within(root, $0) }) { distinct.append(root) }
        self.roots = distinct
        self.storeURL = storeURL
        exclusions = excludedFolders.map(SearchText.path)
    }

    var state: SearchIndexState { lock.lock(); defer { lock.unlock() }; return storedState }
    var deniedPaths: [String] { lock.lock(); defer { lock.unlock() }; return index.denied }

    func start() { launch(force: false) }

    func stop() {
        lock.lock()
        guard running else { lock.unlock(); return }
        running = false; generation &+= 1; storedState = .disabled
        lock.unlock()
        notify()
        // Retain the service until this off-main shutdown write completes. Pending, unreconciled
        // events are not checkpointed; the next launch will replay them.
        worker.async {
            self.saveTimer?.cancel(); self.saveTimer = nil
            self.persistDirty(token: nil)
            self.clearWorker()
        }
    }

    func rebuild() { launch(force: true) }

    func setExcludedFolders(_ paths: [String]) {
        let updated = Array(Set(paths.map(SearchText.path))).sorted()
        lock.lock()
        let previous = exclusions
        guard Set(previous) != Set(updated) else { lock.unlock(); return }
        exclusions = updated
        let token = generation
        let active = running
        lock.unlock()
        guard active else { return }
        worker.async { [weak self] in
            guard let self, self.active(token), self.builder != nil else { return }
            let scope = SearchScope(roots: self.roots, excluded: updated, storeURL: self.storeURL)
            self.scope = scope
            self.builder = SearchIndexBuilder(scope: scope, snapshot: self.currentIndex())
            let changed = Set(previous + updated).filter { scope.inScope($0) }
            var snapshot = self.builder!.reconcile(Set(changed), cancelled: { !self.active(token) })
            snapshot.eventID = self.checkpoint; snapshot.volumeUUIDs = self.volumes
            self.publishDirty(snapshot, token: token)
        }
    }

    func search(_ text: String, priorityRoots: [String], limit: Int) async -> [SearchHit] {
        guard !Task.isCancelled, limit > 0 else { return [] }
        guard let snapshot = searchableIndex() else { return [] }
        let query = SearchQuery(text)
        let task = Task.detached(priority: .userInitiated) {
            snapshot.search(query, priorityRoots: priorityRoots, limit: limit, cancelled: { Task.isCancelled })
        }
        return await withTaskCancellationHandler(operation: { await task.value }, onCancel: { task.cancel() })
    }

    private func launch(force: Bool) {
        lock.lock()
        if running && !force { lock.unlock(); return }
        running = true; generation &+= 1
        let token = generation
        let exclusions = self.exclusions
        storedState = .loading
        lock.unlock()
        notify()
        worker.async { [weak self] in
            guard let self, self.active(token) else { return }
            self.clearWorker()
            let scope = SearchScope(roots: self.roots, excluded: exclusions, storeURL: self.storeURL)
            self.scope = scope
            self.volumes = SearchStore.volumeUUIDs(self.roots)
            let saved = force ? nil : SearchStore.load(from: self.storeURL, scope: scope, volumeUUIDs: self.volumes)
            self.checkpoint = saved?.eventID ?? FSEventsGetCurrentEventId()
            if saved != nil { self.lastSaveAttempt = .now() }
            self.builder = SearchIndexBuilder(scope: scope, snapshot: saved ?? SearchSnapshot())
            if let saved { self.publish(saved, token: token) }
            guard self.active(token) else { return }
            self.watcher = SearchIndexWatcher(roots: self.roots, since: self.checkpoint, queue: self.worker) { [weak self] batch in
                self?.receive(batch, token: token)
            }
            guard self.watcher != nil else {
                self.setState(.failed(tr("The search index could not watch these folders.", "검색 색인에서 이 폴더의 변경 사항을 감시할 수 없습니다.")), token: token)
                return
            }
            if saved == nil { self.fullScan(token: token) }
            // A saved snapshot stays ready while FSEvents replays offline changes from its checkpoint.
        }
    }

    private func active(_ token: UInt64) -> Bool {
        lock.lock(); defer { lock.unlock() }; return running && generation == token
    }

    private func currentIndex() -> SearchSnapshot { lock.lock(); defer { lock.unlock() }; return index }

    private func clearWorker() {
        timer?.cancel(); timer = nil
        saveTimer?.cancel(); saveTimer = nil
        dirtySnapshot = nil; dirtyScope = nil; lastSaveAttempt = nil
        watcher?.stop(); watcher = nil
        builder = nil; scope = nil
        pending.removeAll(); pendingID = 0; invalidated = false; batchStarted = nil
    }

    private func searchableIndex() -> SearchSnapshot? {
        lock.lock(); defer { lock.unlock() }; return running ? index : nil
    }

    private func fullScan(token: UInt64) {
        guard active(token), let builder else { return }
        setState(.building(scanned: 0), token: token)
        var lastProgress = Date.distantPast
        var snapshot = builder.build(cancelled: { !self.active(token) }) { scanned in
            if Date().timeIntervalSince(lastProgress) >= 0.5 {
                lastProgress = Date(); self.setState(.building(scanned: scanned), token: token)
            }
        }
        snapshot.eventID = checkpoint; snapshot.volumeUUIDs = volumes
        publishDirty(snapshot, token: token)
    }

    private func receive(_ batch: SearchIndexWatcher.Batch, token: UInt64) {
        guard active(token), let scope else { return }
        pendingID = max(pendingID, batch.eventID)
        invalidated = invalidated || batch.requiresScan
        pending.formUnion(batch.paths.filter { scope.inScope($0) && !scope.ignoresItem($0) })
        guard !pending.isEmpty || invalidated else {
            // HistoryDone checkpoints no-op replay, but never advances past unprocessed changes.
            if batch.historyDone, pendingID > checkpoint {
                checkpoint = pendingID
                var snapshot = currentIndex(); snapshot.eventID = checkpoint
                publishDirty(snapshot, token: token)
            }
            return
        }
        let now = Date()
        if batchStarted == nil { batchStarted = now }
        let remaining = max(0, 10 - now.timeIntervalSince(batchStarted!))
        timer?.cancel()
        let task = DispatchWorkItem { [weak self] in self?.flush(token: token) }
        timer = task
        worker.asyncAfter(deadline: .now() + min(2, remaining), execute: task)
    }

    private func flush(token: UInt64) {
        guard active(token), let builder else { return }
        let paths = pending
        let rescan = invalidated
        checkpoint = max(checkpoint, pendingID)
        pending.removeAll(); pendingID = 0; invalidated = false; batchStarted = nil; timer = nil
        if rescan {
            checkpoint = FSEventsGetCurrentEventId()
            volumes = SearchStore.volumeUUIDs(roots)
            watcher?.stop()
            watcher = SearchIndexWatcher(roots: roots, since: checkpoint, queue: worker) { [weak self] batch in
                self?.receive(batch, token: token)
            }
            guard watcher != nil else {
                setState(.failed(tr("The search index could not watch these folders.", "검색 색인에서 이 폴더의 변경 사항을 감시할 수 없습니다.")), token: token)
                return
            }
            fullScan(token: token)
            return
        }
        var snapshot = builder.reconcile(paths, cancelled: { !self.active(token) })
        snapshot.eventID = checkpoint; snapshot.volumeUUIDs = volumes
        publishDirty(snapshot, token: token)
    }

    private func publishDirty(_ snapshot: SearchSnapshot, token: UInt64) {
        guard active(token), let scope else { return }
        publish(snapshot, token: token)
        // Keep the snapshot and its event ID together; never persist a newer watcher checkpoint
        // against older content, even while a subsequent event batch is waiting to reconcile.
        dirtySnapshot = snapshot
        dirtyScope = scope
        scheduleSave(token: token)
    }

    private func scheduleSave(token: UInt64) {
        guard saveTimer == nil, dirtySnapshot != nil, active(token) else { return }
        let now = DispatchTime.now()
        let elapsed = lastSaveAttempt.map {
            Double(now.uptimeNanoseconds - $0.uptimeNanoseconds) / 1_000_000_000
        } ?? Self.saveInterval
        let delay = max(0, Self.saveInterval - elapsed)
        if delay == 0 {
            persistDirty(token: token)
        } else {
            let task = DispatchWorkItem { [weak self] in
                guard let self, self.active(token) else { return }
                self.saveTimer = nil
                self.persistDirty(token: token)
            }
            saveTimer = task
            worker.asyncAfter(deadline: now + delay, execute: task)
        }
    }

    private func persistDirty(token: UInt64?) {
        guard let snapshot = dirtySnapshot, let scope = dirtyScope else { return }
        lastSaveAttempt = .now()
        do {
            try SearchStore.save(snapshot, scope: scope, to: storeURL)
            dirtySnapshot = nil; dirtyScope = nil
        } catch {
            if let token {
                setState(.failed(tr("The search index could not be saved: ", "검색 색인을 저장할 수 없습니다: ") + error.localizedDescription), token: token)
                scheduleSave(token: token)
            }
        }
    }

    private func publish(_ snapshot: SearchSnapshot, token: UInt64) {
        lock.lock()
        guard running && generation == token else { lock.unlock(); return }
        index = snapshot; storedState = .ready(entries: snapshot.count)
        lock.unlock(); notify()
    }

    private func setState(_ value: SearchIndexState, token: UInt64) {
        lock.lock()
        guard running && generation == token else { lock.unlock(); return }
        storedState = value; lock.unlock(); notify()
    }

    private func notify() {
        lock.lock()
        guard !notificationPending else { lock.unlock(); return }
        notificationPending = true; lock.unlock()
        DispatchQueue.main.asyncAfter(deadline: .now() + 0.5) { [weak self] in
            guard let self else { return }
            self.lock.lock(); self.notificationPending = false; self.lock.unlock()
            NotificationCenter.default.post(name: .searchIndexDidChange, object: self)
        }
    }
}
