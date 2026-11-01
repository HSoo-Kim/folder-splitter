import AppKit

private final class FileWorkerResult<Value>: @unchecked Sendable {
    // Access is synchronized by the worker's completion semaphore.
    var value: Value?
}

private final class FileOperationUndoManager: UndoManager {
    // Only accessed on the main thread. Native menu validation must not offer a second
    // undo while an asynchronous file operation is still computing its inverse.
    var operationInFlight = false
    override var canUndo: Bool { !operationInFlight && super.canUndo }
    override var canRedo: Bool { !operationInFlight && super.canRedo }
    override func undo() { if !operationInFlight { super.undo() } }
    override func redo() { if !operationInFlight { super.redo() } }
}

@MainActor
private final class FileUndoJournal {
    var steps: [FileUndoStep]
    init(_ steps: [FileUndoStep]) { self.steps = steps }
}

@MainActor
final class FileOperationCenter: FileOperating {
    private let fileUndoManager = FileOperationUndoManager()
    var undoManager: UndoManager { fileUndoManager }
    private let engine = FileOperationEngine()
    private var progressPanel: FileProgressPanel?
    private var rows: [UUID: FileProgressRow] = [:]
    private var order: [UUID] = []
    private var refreshTimer: Timer?
    private var conflictQueue: [(FileConflict, CheckedContinuation<FileConflictResolution, Never>)] = []
    private var presentingConflict = false

    init() { undoManager.groupsByEvent = false }

    func transfer(_ items: [URL], to folder: URL, mode: TransferMode) async -> FileOperationOutcome {
        let action = mode == .copy ? tr("Copy", "복사하기") : tr("Move", "이동")
        let title = mode == .copy ? tr("Copying \(items.count) items to “\(folder.lastPathComponent)”", "항목 \(items.count)개를 ‘\(folder.lastPathComponent)’에 복사하는 중") : tr("Moving \(items.count) items to “\(folder.lastPathComponent)”", "항목 \(items.count)개를 ‘\(folder.lastPathComponent)’(으)로 이동하는 중")
        let operation = beginProgress(title)
        let result = await engine.transfer(items, to: folder, mode: mode, cancellation: operation.token, progress: { operation.snapshot.set($0) }, conflict: { [weak self] conflict in
            guard let self else { return .init(choice: .stop) }
            return await self.resolve(conflict)
        })
        endProgress(operation.id)
        return finish(result, action: action)
    }
    func moveToTrash(_ items: [URL]) async -> FileOperationOutcome {
        let engine = self.engine
        var result = await Task.detached { engine.trash(items) }.value
        if !result.trashUnavailable.isEmpty {
            let alert = NSAlert()
            alert.alertStyle = .critical
            alert.messageText = tr("Delete immediately?", "즉시 삭제하겠습니까?")
            alert.informativeText = tr("These items cannot be moved to the Trash. This can't be undone.\n", "이 항목을 휴지통으로 이동할 수 없습니다. 이 작업은 실행 취소할 수 없습니다.\n") + result.trashUnavailable.map(\.path).joined(separator: "\n")
            alert.addButton(withTitle: tr("Cancel", "취소"))
            let destructive = alert.addButton(withTitle: tr("Delete Immediately", "즉시 삭제"))
            destructive.hasDestructiveAction = true
            if await present(alert) == .alertSecondButtonReturn {
                let unavailable = result.trashUnavailable
                let failures = await Task.detached { unavailable.compactMap { item -> FileOperationFailure? in
                    do { try FileManager.default.removeItem(at: item); return nil }
                    catch { return .init(url: item, message: error.localizedDescription) }
                } }.value
                result.outcome.failures += failures
            } else { result.outcome.skipped += result.trashUnavailable }
        }
        return finish(result, action: tr("Move to Trash", "휴지통으로 이동"))
    }
    func rename(_ item: URL, to newName: String) throws -> URL {
        let engine = self.engine
        let target = try worker { try engine.rename(item, to: newName) }
        register([.renameBatch([.init(from: target, to: item)])], action: tr("Rename", "이름 변경"))
        return target
    }
    func rename(_ renames: [FileRename]) throws -> [URL] {
        let engine = self.engine
        let targets = try worker { try engine.rename(renames) }
        register([.renameBatch(zip(targets, renames).map { .init(from: $0.0, to: $0.1.item) })], action: tr("Rename", "이름 변경"))
        return targets
    }
    func newFolder(in folder: URL) throws -> URL {
        let engine = self.engine
        let target = try worker { try engine.newFolder(in: folder) }
        register([.trash(target)], action: tr("New Folder", "새로운 폴더"))
        return target
    }
    func newFolder(containing items: [URL]) async -> FileOperationOutcome {
        let engine = self.engine
        do {
            let result = try await Task.detached { try engine.newFolder(containing: items) }.value
            return finish(result, action: tr("New Folder with Selection", "선택 항목을 포함한 새로운 폴더"))
        } catch { return finish(failed(items.first, error), action: tr("New Folder", "새로운 폴더")) }
    }
    func duplicate(_ items: [URL]) async -> FileOperationOutcome {
        let engine = self.engine
        let operation = beginProgress(tr("Duplicating \(items.count) items", "항목 \(items.count)개를 복제하는 중"))
        let result = await Task.detached { engine.duplicate(items, cancellation: operation.token, progress: { operation.snapshot.set($0) }) }.value
        endProgress(operation.id)
        return finish(result, action: tr("Duplicate", "복제"))
    }
    func compress(_ items: [URL]) async -> FileOperationOutcome {
        let engine = self.engine
        let operation = beginProgress(tr("Compressing \(items.count) items", "항목 \(items.count)개를 압축하는 중"))
        defer { endProgress(operation.id) }
        do {
            let result = try await Task.detached { try engine.compress(items, cancellation: operation.token, progress: { operation.snapshot.set($0) }) }.value
            return finish(result, action: tr("Compress", "압축"))
        } catch {
            if operation.token.isCancelled { return .init(cancelled: true) }
            return finish(failed(items.first, error), action: tr("Compress", "압축"))
        }
    }
    func makeAlias(_ items: [URL]) -> FileOperationOutcome {
        let engine = self.engine
        let result = worker { engine.makeAlias(items) }
        return finish(result, action: tr("Make Alias", "가상본 만들기"))
    }

    // The shared protocol's rename/create/alias methods are synchronous. Work is dispatched
    // to a worker even for these small metadata operations; no filesystem calls run on main.
    private func worker<T>(_ body: @escaping () -> T) -> T {
        let completion = DispatchSemaphore(value: 0)
        let result = FileWorkerResult<T>()
        // dispatch_sync can execute inline on the caller, including the main thread.
        DispatchQueue.global(qos: .userInitiated).async {
            result.value = body()
            completion.signal()
        }
        completion.wait()
        return result.value!
    }
    private func worker<T>(_ body: @escaping () throws -> T) throws -> T {
        let result: Result<T, Error> = worker { Result(catching: body) }
        return try result.get()
    }
    private func failed(_ url: URL?, _ error: Error) -> FileEngineResult {
        FileEngineResult(outcome: .init(failures: [.init(url: url ?? URL(fileURLWithPath: "/"), message: error.localizedDescription)]))
    }
    private func finish(_ result: FileEngineResult, action: String) -> FileOperationOutcome {
        register(result.undo, action: action)
        if !result.outcome.failures.isEmpty { showFailures(result.outcome.failures) }
        return result.outcome
    }
    private func register(_ steps: [FileUndoStep], action: String) {
        guard !steps.isEmpty else { return }
        registerJournal(FileUndoJournal(steps), action: action)
    }
    private func registerJournal(_ journal: FileUndoJournal, action: String) {
        let grouping = !undoManager.isUndoing && !undoManager.isRedoing
        if grouping { undoManager.beginUndoGrouping() }
        // UndoManager does not retain its target; the handler (which it does retain) keeps the
        // journal alive for exactly as long as the action is on the undo or redo stack.
        undoManager.registerUndo(withTarget: journal) { [weak self, journal] _ in
            MainActor.assumeIsolated { self?.performUndo(journal, action: action) }
        }
        undoManager.setActionName(action)
        if grouping { undoManager.endUndoGrouping() }
    }
    private func performUndo(_ journal: FileUndoJournal, action: String) {
        let redoing = undoManager.isRedoing
        let inverse = FileUndoJournal([])
        // UndoManager must receive its inverse while isUndoing/isRedoing is true.
        // Fill this journal only after background work finishes; the busy gate prevents
        // invoking it before then, without blocking the app or nesting its run loop.
        registerJournal(inverse, action: action)
        fileUndoManager.operationInFlight = true
        let title = redoing ? tr("Redoing \(action)", "\(action) 실행 복귀 중") : tr("Undoing \(action)", "\(action) 실행 취소 중")
        let operation = beginProgress(title)
        let engine = self.engine
        let steps = journal.steps
        Task {
            let result = await Task.detached(priority: .userInitiated) {
                engine.applyUndo(steps, cancellation: operation.token, progress: { operation.snapshot.set($0) })
            }.value
            inverse.steps = result.undo
            if inverse.steps.isEmpty { undoManager.removeAllActions(withTarget: inverse) }
            fileUndoManager.operationInFlight = false
            endProgress(operation.id)
            if !result.outcome.failures.isEmpty { showFailures(result.outcome.failures) }
        }
    }
    private func showFailures(_ failures: [FileOperationFailure]) {
        let alert = NSAlert()
        alert.alertStyle = .warning
        alert.messageText = tr("Some items could not be changed.", "일부 항목을 변경할 수 없습니다.")
        alert.informativeText = failures.map { "\($0.url.path)\n\($0.message)" }.joined(separator: "\n\n")
        alert.addButton(withTitle: tr("OK", "확인"))
        Task { _ = await present(alert) }
    }
    private func present(_ alert: NSAlert) async -> NSApplication.ModalResponse {
        if let window = NSApp.mainWindow ?? NSApp.keyWindow, window !== progressPanel {
            // Another operation may currently own a sheet. Wait for its dismissal rather
            // than replacing it or starting a nested modal loop.
            while window.attachedSheet != nil { try? await Task.sleep(for: .milliseconds(50)) }
            return await withCheckedContinuation { continuation in
                alert.beginSheetModal(for: window) { continuation.resume(returning: $0) }
            }
        }
        return alert.runModal()
    }
    private func resolve(_ conflict: FileConflict) async -> FileConflictResolution {
        await withCheckedContinuation { continuation in
            conflictQueue.append((conflict, continuation))
            showNextConflict()
        }
    }
    private func showNextConflict() {
        guard !presentingConflict, !conflictQueue.isEmpty else { return }
        presentingConflict = true
        let (conflict, continuation) = conflictQueue.removeFirst()
        let alert = FileConflictPresentation.alert(for: conflict)
        Task {
            let response = await present(alert)
            let choice: FileConflictChoice
            switch response {
            case .alertFirstButtonReturn: choice = .keepBoth
            case .alertThirdButtonReturn: choice = .replace
            case NSApplication.ModalResponse(rawValue: NSApplication.ModalResponse.alertFirstButtonReturn.rawValue + 3): choice = .skip
            default: choice = .stop
            }
            continuation.resume(returning: .init(choice: choice, applyToAll: alert.suppressionButton?.state == .on))
            presentingConflict = false
            showNextConflict()
        }
    }
    private func beginProgress(_ title: String) -> (id: UUID, token: FileOperationCancellation, snapshot: FileProgressSnapshot) {
        let id = UUID(), token = FileOperationCancellation(), snapshot = FileProgressSnapshot()
        rows[id] = FileProgressRow(title: title, cancellation: token, snapshot: snapshot)
        order.append(id)
        if refreshTimer == nil {
            refreshTimer = Timer.scheduledTimer(withTimeInterval: 0.1, repeats: true) { [weak self] _ in
                MainActor.assumeIsolated { self?.refreshProgress() }
            }
        }
        return (id, token, snapshot)
    }
    private func refreshProgress() {
        let visible = order.compactMap { rows[$0] }.filter { Date().timeIntervalSince($0.started) >= 0.5 }
        visible.forEach { $0.refresh() }
        if visible.contains(where: { !$0.isVisible }) {
            visible.forEach { $0.isVisible = true }
            if progressPanel == nil { progressPanel = FileProgressPanel() }
            progressPanel?.show(visible)
        }
    }
    private func endProgress(_ id: UUID) {
        rows.removeValue(forKey: id)?.removeFromSuperview()
        order.removeAll { $0 == id }
        let visible = order.compactMap { rows[$0] }.filter(\.isVisible)
        if visible.isEmpty { progressPanel?.dismiss() } else { progressPanel?.show(visible) }
        if rows.isEmpty { refreshTimer?.invalidate(); refreshTimer = nil }
    }
}
