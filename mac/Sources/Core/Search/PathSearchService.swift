import Foundation
import CoreServices

/// Searches use immutable snapshots. Event delivery, bounded filesystem reads and persistence
/// have independent queues, so a privacy prompt cannot stop event intake, saving or searches.
final class PathSearchService: PathSearching, @unchecked Sendable {
    static var defaultStoreURL: URL {
        FileManager.default.homeDirectoryForCurrentUser
            .appendingPathComponent("Library/Application Support/Folder Splitter/Search/paths.v1.index")
    }

    /// Absolute/tilde paths exclude contents. Bare names apply at any depth. The folder remains searchable.
    static let defaultExclusions = ["/System", "/Library", "/private", "/dev", "/cores", "/usr", "/bin", "/sbin", "/opt", "/Volumes", "~/Library", "~/.Trash", "~/.dropbox", ".Spotlight-V100", ".fseventsd", ".DocumentRevisions-V100", ".TemporaryItems", ".git", "node_modules"]

    private let roots: [String]
    private let storeURL: URL
    private let lock = NSLock()
    private let worker = DispatchQueue(label: "FolderSplitter.Search.index", qos: .utility)
    private let events = DispatchQueue(label: "FolderSplitter.Search.events", qos: .utility)
    private let saver = DispatchQueue(label: "FolderSplitter.Search.save", qos: .utility)
    private var storedState: SearchIndexState = .disabled
    private var index = SearchSnapshot()
    private var publishedScope: SearchScope?
    private var exclusions: [String]
    private var running = false
    private var generation: UInt64 = 0
    private var notificationPending = false
    // Worker-queue-owned state.
    private var watcher: SearchIndexWatcher?
    private var builder: SearchIndexBuilder?
    private var scope: SearchScope?
    private var pending: Set<String> = []
    private var shallow: Set<String> = []
    private var pendingID: UInt64 = 0
    private var batchStarted: Date?
    private var timer: DispatchWorkItem?
    private var checkpoint: UInt64 = 0
    private var volumes: [String] = []
    private var fullRescanPending = false
    private var rescanRequested: Date?
    private var rescanTimer: DispatchWorkItem?
    private var lastFullScan = Date.distantPast
    private var retryTimer: DispatchWorkItem?
    // Save-queue-owned state.
    private var dirtySnapshot: SearchSnapshot?
    private var dirtyScope: SearchScope?
    private var saveToken: UInt64?
    private var saveTimer: DispatchWorkItem?
    private var lastSaveAttempt = Date.distantPast
    private static let saveInterval: TimeInterval = 5 * 60

    init(roots: [String] = ["/"], storeURL: URL = PathSearchService.defaultStoreURL, excludedFolders: [String] = []) {
        let normalized = Array(Set(roots.map(SearchText.path))).sorted { $0.count < $1.count }
        var distinct: [String] = []
        for root in normalized where !distinct.contains(where: { SearchText.within(root, $0) }) { distinct.append(root) }
        // These explicit roots bypass only their own branch of the ~/Library exclusion.
        for cloud in SearchScope.cloudRoots() where distinct.contains(where: { SearchText.within(cloud, $0) }) && !distinct.contains(cloud) {
            distinct.append(cloud)
        }
        self.roots = distinct
        self.storeURL = storeURL
        exclusions = excludedFolders.map(SearchText.path)
    }

    var state: SearchIndexState { lock.lock(); defer { lock.unlock() }; return storedState }
    var deniedPaths: [String] { lock.lock(); defer { lock.unlock() }; return index.denied }

    func start() { launch(force: false) }
    func stop() { stop(savingWithin: 0) }

    /// Wait only for the save queue, never a filesystem/index worker waiting on consent. The
    /// deadline also bounds an unavailable cache volume; an atomic write may finish afterwards.
    func stop(savingWithin seconds: TimeInterval) {
        let done = DispatchSemaphore(value: 0)
        lock.lock()
        guard running else { lock.unlock(); return }
        running = false; generation &+= 1; storedState = .disabled
        let snapshot = index
        let scope = publishedScope
        saver.async {
            self.saveTimer?.cancel(); self.saveTimer = nil
            self.saveToken = nil
            if let scope { self.dirtySnapshot = snapshot; self.dirtyScope = scope }
            self.persistDirty()
            done.signal()
        }
        lock.unlock()
        notify()
        worker.async { self.clearWorker() }
        if seconds > 0 { _ = done.wait(timeout: .now() + seconds) }
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
            self.startWatcher(token: token)
            let changed = Set(previous + updated).filter { scope.inScope($0) }
            self.publishReconciled(Set(changed), token: token)
        }
    }

    func search(_ text: String, priorityRoots: [String], limit: Int) async -> [SearchHit] {
        guard !Task.isCancelled, limit > 0, let snapshot = searchableIndex() else { return [] }
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
            self.builder = SearchIndexBuilder(scope: scope, snapshot: saved ?? SearchSnapshot())
            if let saved {
                self.publish(saved, token: token)
                self.saver.async { self.lastSaveAttempt = Date() }
            }
            guard self.active(token), self.startWatcher(token: token) else { return }
            if let saved {
                // FDA may have been granted since the last launch. Also resumes unfinished scans.
                self.publishReconciled(Set(saved.denied), token: token)
            } else { self.fullScan(token: token, initial: true) }
            self.scheduleDeniedRetry(token: token)
        }
    }

    @discardableResult private func startWatcher(token: UInt64) -> Bool {
        watcher?.stop()
        watcher = SearchIndexWatcher(roots: roots, exclusions: scope?.excluded ?? [], since: checkpoint, queue: events) { [weak self] batch in
            self?.worker.async { [weak self] in self?.receive(batch, token: token) }
        }
        if watcher == nil {
            setState(.failed(tr("The search index could not watch these folders.", "검색 색인에서 이 폴더의 변경 사항을 감시할 수 없습니다.")), token: token)
        }
        return watcher != nil
    }

    private func active(_ token: UInt64) -> Bool {
        lock.lock(); defer { lock.unlock() }; return running && generation == token
    }
    private func currentIndex() -> SearchSnapshot { lock.lock(); defer { lock.unlock() }; return index }
    private func searchableIndex() -> SearchSnapshot? {
        lock.lock(); defer { lock.unlock() }; return running ? index : nil
    }

    private func clearWorker() {
        timer?.cancel(); timer = nil
        rescanTimer?.cancel(); rescanTimer = nil
        retryTimer?.cancel(); retryTimer = nil
        watcher?.stop(); watcher = nil
        builder = nil; scope = nil
        pending.removeAll(); shallow.removeAll(); pendingID = 0; batchStarted = nil
        fullRescanPending = false
        rescanRequested = nil
    }

    private func fullScan(token: UInt64, initial: Bool = false) {
        guard active(token), let builder else { return }
        lastFullScan = Date()
        if initial { setState(.building(scanned: 0), token: token) }
        var lastProgress = Date.distantPast
        var snapshot = builder.build(cancelled: { !self.active(token) }, progress: { scanned in
            if initial && Date().timeIntervalSince(lastProgress) >= 0.5 {
                lastProgress = Date(); self.setState(.building(scanned: scanned), token: token)
            }
        }, partial: { partial in
            // Keep serving the old complete index during automatic rescans.
            if initial { self.publishStamped(partial, token: token) }
        })
        snapshot.eventID = checkpoint; snapshot.volumeUUIDs = volumes
        publishDirty(snapshot, token: token)
    }

    private func receive(_ batch: SearchIndexWatcher.Batch, token: UInt64) {
        guard active(token), let scope else { return }
        pendingID = max(pendingID, batch.eventID)
        fullRescanPending = fullRescanPending || batch.requiresScan
        for path in batch.rescanPaths {
            if scope.roots.contains(path) || !scope.inScope(path) { fullRescanPending = true }
            else if !scope.ignoresItem(path) { pending.insert(path); shallow.remove(path) }
        }
        let deep = batch.paths.filter { scope.inScope($0) && !scope.ignoresItem($0) }
        pending.formUnion(deep); shallow.subtract(deep)
        let attributes = batch.shallowPaths.filter { scope.inScope($0) && !scope.ignoresItem($0) && !pending.contains($0) }
        shallow.formUnion(attributes)
        if fullRescanPending { scheduleFullRescan(token: token) }
        guard !pending.isEmpty || !shallow.isEmpty else {
            if batch.historyDone, !fullRescanPending, pendingID > checkpoint {
                checkpoint = pendingID
                publishStamped(currentIndex(), token: token)
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
        guard active(token) else { return }
        let paths = pending.union(shallow)
        let attributes = shallow
        // Never checkpoint past a dropped-event gap until its full rescan is complete.
        if !fullRescanPending { checkpoint = max(checkpoint, pendingID) }
        pending.removeAll(); shallow.removeAll(); batchStarted = nil; timer = nil
        publishReconciled(paths, shallow: attributes, token: token)
    }

    private func scheduleFullRescan(token: UInt64) {
        rescanTimer?.cancel()
        let now = Date()
        if rescanRequested == nil { rescanRequested = now }
        // Prefer thirty idle seconds, but continuous file traffic cannot postpone the scan forever.
        let idleDeadline = min(now.addingTimeInterval(30), rescanRequested!.addingTimeInterval(120))
        let deadline = max(lastFullScan.addingTimeInterval(10 * 60), idleDeadline)
        let delay = max(0, deadline.timeIntervalSince(now))
        let task = DispatchWorkItem { [weak self] in
            guard let self, self.active(token), self.fullRescanPending else { return }
            self.rescanTimer = nil
            // Use the event ID from before the scan; events arriving during it still reconcile.
            self.checkpoint = max(self.checkpoint, self.pendingID)
            self.fullRescanPending = false
            self.rescanRequested = nil
            self.fullScan(token: token)
        }
        rescanTimer = task
        worker.asyncAfter(deadline: .now() + delay, execute: task)
    }

    private func scheduleDeniedRetry(token: UInt64) {
        let task = DispatchWorkItem { [weak self] in
            guard let self, self.active(token) else { return }
            self.publishReconciled(Set(self.currentIndex().denied), token: token)
            self.scheduleDeniedRetry(token: token)
        }
        retryTimer = task
        worker.asyncAfter(deadline: .now() + 60, execute: task)
    }

    private func publishReconciled(_ paths: Set<String>, shallow: Set<String> = [], token: UInt64) {
        guard !paths.isEmpty, active(token), let builder else { return }
        let snapshot = builder.reconcile(paths, shallow: shallow, cancelled: { !self.active(token) }, partial: { self.publishStamped($0, token: token) })
        publishStamped(snapshot, token: token)
    }

    private func publishStamped(_ snapshot: SearchSnapshot, token: UInt64) {
        var snapshot = snapshot
        snapshot.eventID = checkpoint; snapshot.volumeUUIDs = volumes
        publishDirty(snapshot, token: token)
    }

    private func publishDirty(_ snapshot: SearchSnapshot, token: UInt64) {
        guard active(token), let scope else { return }
        publish(snapshot, token: token)
        // Enqueue under the lifecycle lock so no older write can overtake the quit snapshot.
        lock.lock()
        guard running && generation == token else { lock.unlock(); return }
        saver.async {
            self.dirtySnapshot = snapshot; self.dirtyScope = scope
            self.saveToken = token
            guard self.saveTimer == nil else { return }
            let delay = max(0, Self.saveInterval - Date().timeIntervalSince(self.lastSaveAttempt))
            let task = DispatchWorkItem { [weak self] in self?.saveTimer = nil; self?.persistDirty() }
            self.saveTimer = task
            self.saver.asyncAfter(deadline: .now() + delay, execute: task)
        }
        lock.unlock()
    }

    private func persistDirty() {
        guard let snapshot = dirtySnapshot, let scope = dirtyScope else { return }
        lastSaveAttempt = Date()
        do {
            try SearchStore.save(snapshot, scope: scope, to: storeURL)
            dirtySnapshot = nil; dirtyScope = nil
        } catch {
            if let token = saveToken {
                setState(.failed(tr("The search index could not be saved: ", "검색 색인을 저장할 수 없습니다: ") + error.localizedDescription), token: token)
            }
            // Preserve the dirty snapshot for the next periodic save or bounded quit attempt.
            let task = DispatchWorkItem { [weak self] in self?.saveTimer = nil; self?.persistDirty() }
            saveTimer = task
            saver.asyncAfter(deadline: .now() + Self.saveInterval, execute: task)
        }
    }

    private func publish(_ snapshot: SearchSnapshot, token: UInt64) {
        lock.lock()
        guard running && generation == token else { lock.unlock(); return }
        index = snapshot; publishedScope = scope; storedState = .ready(entries: snapshot.count)
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
