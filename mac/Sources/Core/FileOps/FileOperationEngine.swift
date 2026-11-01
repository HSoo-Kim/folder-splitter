import Foundation
import Darwin

final class FileOperationCancellation: @unchecked Sendable {
    private let lock = NSLock()
    private var value = false
    func cancel() { lock.lock(); value = true; lock.unlock() }
    var isCancelled: Bool { lock.lock(); defer { lock.unlock() }; return value }
}

struct FileOperationProgress: Sendable {
    let completedBytes: Int64
    let totalBytes: Int64
    let item: URL?
}

enum FileConflictChoice: Sendable { case replace, keepBoth, skip, stop }
struct FileConflictResolution: Sendable {
    let choice: FileConflictChoice
    var applyToAll = false
}
struct FileConflict: Sendable {
    let source: URL
    let destination: URL
    let sourceBytes: Int64
    let destinationBytes: Int64
    let sourceModified: Date?
    let destinationModified: Date?
    let remainingItems: Int
    var sourceOnlineOnly = false
    var destinationOnlineOnly = false
}

enum FileEngineError: LocalizedError {
    case invalidName, exists(URL), recursiveDestination, cancelled, trashUnavailable(URL, String), rollback(String)
    case sourceRemovalFailed(URL, URL, String)
    case onlineOnly(URL)
    var errorDescription: String? {
        switch self {
        case .invalidName: return tr("A name cannot be empty or contain /.", "이름은 비어 있거나 / 문자를 포함할 수 없습니다.")
        case .onlineOnly(let url):
            let name = FileName.display(url.lastPathComponent)
            return tr("“\(name)” is online-only in Dropbox, so its contents aren't on this Mac. Copying or moving it now would create an empty file. Make it available offline in Dropbox, then try again.", "‘\(name)’ 파일은 Dropbox에 온라인 전용으로 저장되어 있어 이 Mac에 내용이 없습니다. 지금 복사하거나 이동하면 빈 파일이 만들어집니다. Dropbox에서 오프라인으로 사용할 수 있게 설정한 다음 다시 시도하십시오.")
        case .exists(let url): return tr("An item named “\(url.lastPathComponent)” already exists.", "‘\(url.lastPathComponent)’ 이름의 항목이 이미 있습니다.")
        case .recursiveDestination: return tr("An item cannot be copied or moved onto itself or into itself.", "항목을 자기 자신 또는 자신의 하위 폴더로 복사하거나 이동할 수 없습니다.")
        case .cancelled: return tr("The operation was stopped.", "작업이 정지되었습니다.")
        case .trashUnavailable(let url, let detail): return tr("“\(url.path)” cannot be moved to the Trash. \(detail)", "‘\(url.path)’ 항목을 휴지통으로 이동할 수 없습니다. \(detail)")
        case .rollback(let detail): return tr("The rename could not be rolled back completely: \(detail)", "이름 변경을 완전히 되돌릴 수 없습니다: \(detail)")
        case .sourceRemovalFailed(let source, let destination, let detail):
            return tr("Copied to “\(destination.path)”, but some originals in “\(source.path)” couldn’t be removed. The complete copy has been kept. \(detail)", "‘\(destination.path)’에 복사했지만 ‘\(source.path)’의 일부 원본을 제거할 수 없습니다. 완전한 복사본은 유지되었습니다. \(detail)")
        }
    }
}

struct FileLocationChange: Sendable { let from: URL; let to: URL }
enum FileUndoStep: Sendable {
    case relocate(URL, URL)
    case trash(URL)
    case renameBatch([FileLocationChange])
}
struct FileEngineResult: Sendable {
    var outcome = FileOperationOutcome()
    /// In execution order; each step creates its own inverse for redo.
    var undo: [FileUndoStep] = []
    var trashed: [FileLocationChange] = []
    var trashUnavailable: [URL] = []
}

private final class CopyContext {
    let token: FileOperationCancellation
    let total: Int64
    let base: Int64
    let source: URL
    let progress: @Sendable (FileOperationProgress) -> Void
    var finishedBytes: Int64 = 0
    var errorCode: Int32?
    var onlineOnly: URL?
    init(_ token: FileOperationCancellation, total: Int64, base: Int64, source: URL,
         progress: @escaping @Sendable (FileOperationProgress) -> Void) {
        self.token = token; self.total = total; self.base = base; self.source = source; self.progress = progress
    }
}

private let copyProgressCallback: copyfile_callback_t = { what, stage, state, source, destination, raw in
    guard let raw else { return COPYFILE_CONTINUE }
    let context = Unmanaged<CopyContext>.fromOpaque(raw).takeUnretainedValue()
    if context.token.isCancelled || what == COPYFILE_RECURSE_ERROR || stage == COPYFILE_ERR { return COPYFILE_QUIT }
    if what == COPYFILE_RECURSE_FILE && stage == COPYFILE_START, let source {
        let url = URL(fileURLWithPath: String(cString: source))
        if OnlineOnlyItems.isStub(url) { context.onlineOnly = url; return COPYFILE_QUIT }
    }
    if (what == COPYFILE_RECURSE_DIR || what == COPYFILE_RECURSE_DIR_CLEANUP) && stage == COPYFILE_FINISH, let source, let destination {
        // With COPYFILE_CLONE, copyfile installs directory permissions before visiting
        // children. Temporarily grant access only on our private staging copy, then put
        // the original mode back once the directory's children have been copied.
        var info = stat()
        if lstat(source, &info) != 0 { context.errorCode = errno; return COPYFILE_QUIT }
        if (info.st_mode & 0o700) != 0o700 {
            let mode = (info.st_mode & 0o7777) | (what == COPYFILE_RECURSE_DIR ? 0o700 : 0)
            if chmod(destination, mode) != 0 { context.errorCode = errno; return COPYFILE_QUIT }
        }
    }
    if what == COPYFILE_COPY_DATA {
        var bytes: off_t = 0
        copyfile_state_get(state, UInt32(COPYFILE_STATE_COPIED), &bytes)
        context.progress(FileOperationProgress(completedBytes: min(context.total, context.base + context.finishedBytes + Int64(bytes)), totalBytes: context.total, item: context.source))
    } else if what == COPYFILE_RECURSE_FILE && stage == COPYFILE_FINISH, let source {
        // Clones write no bytes and therefore never emit COPYFILE_COPY_DATA.
        var info = stat()
        if lstat(source, &info) == 0 && (info.st_mode & S_IFMT) == S_IFREG { context.finishedBytes += Int64(info.st_size) }
        context.progress(FileOperationProgress(completedBytes: min(context.total, context.base + context.finishedBytes), totalBytes: context.total, item: context.source))
    }
    return context.token.isCancelled ? COPYFILE_QUIT : COPYFILE_CONTINUE
}

/// All synchronous methods are worker-thread primitives. The app adapter dispatches them;
/// transfer itself always enters a detached task before touching the filesystem.
final class FileOperationEngine: @unchecked Sendable {
    typealias ConflictHandler = @Sendable (FileConflict) async -> FileConflictResolution
    private let fm = FileManager.default
    private let renameItem: @Sendable (URL, URL) -> Int32
    private let dropboxRoots: [URL]

    /// The injected rename reports an errno (zero on success), allowing another filesystem
    /// implementation to request the same copy-and-remove path as a cross-volume rename.
    init(dropboxRoots: [URL] = [], renameItem: @escaping @Sendable (URL, URL) -> Int32 = { FileOperationEngine.renameExclusively($0, $1) }) {
        self.dropboxRoots = dropboxRoots.map { $0.resolvingSymlinksInPath().standardizedFileURL }
        self.renameItem = renameItem
    }
    static func renameExclusively(_ source: URL, _ destination: URL) -> Int32 {
        let result = source.path.withCString { src in destination.path.withCString { dst in renamex_np(src, dst, UInt32(RENAME_EXCL)) } }
        return result == 0 ? 0 : errno
    }
    static func sameItem(_ lhs: URL, _ rhs: URL) -> Bool {
        var a = stat(), b = stat()
        return lhs.path.withCString { lstat($0, &a) } == 0 && rhs.path.withCString { lstat($0, &b) } == 0 && a.st_dev == b.st_dev && a.st_ino == b.st_ino
    }

    static func exists(_ url: URL) -> Bool {
        var info = stat()
        return url.path.withCString { lstat($0, &info) } == 0
    }
    /// A cancellable estimate, not a prerequisite for moving or copying an item. Entries
    /// that disappear or cannot be inspected are left to copyfile's actual error handling.
    static func size(_ url: URL, cancellation: FileOperationCancellation? = nil) throws -> Int64 {
        if cancellation?.isCancelled == true { throw FileEngineError.cancelled }
        guard let values = try? url.resourceValues(forKeys: [.isDirectoryKey, .isSymbolicLinkKey, .fileSizeKey]) else { return 0 }
        if values.isSymbolicLink == true { return 0 }
        guard values.isDirectory == true else { return Int64(values.fileSize ?? 0) }
        var total: Int64 = 0
        guard let walk = FileManager.default.enumerator(at: url, includingPropertiesForKeys: [.isRegularFileKey, .fileSizeKey], errorHandler: { _, _ in cancellation?.isCancelled != true }) else { return 0 }
        for case let child as URL in walk {
            if cancellation?.isCancelled == true { throw FileEngineError.cancelled }
            if let value = try? child.resourceValues(forKeys: [.isRegularFileKey, .fileSizeKey]), value.isRegularFile == true { total += Int64(value.fileSize ?? 0) }
        }
        if cancellation?.isCancelled == true { throw FileEngineError.cancelled }
        return total
    }
    static func validateName(_ name: String) throws {
        guard !name.isEmpty, name != ".", name != "..", !name.contains("/"), !name.contains("\0") else { throw FileEngineError.invalidName }
    }
    static func isNamingFolder(_ item: URL) throws -> Bool {
        let values = try item.resourceValues(forKeys: [.isDirectoryKey, .isPackageKey])
        return values.isDirectory == true && values.isPackage != true
    }
    private func checkMove(_ source: URL, to destination: URL) throws {
        // Resolve parent links but not the item itself: moving a symbolic link must not
        // inspect or restrict the target it happens to point at.
        func location(_ item: URL) -> String {
            item.deletingLastPathComponent().resolvingSymlinksInPath().appendingPathComponent(item.lastPathComponent).standardizedFileURL.path
        }
        func contains(_ path: String, root: URL) -> Bool {
            path == root.path || path.hasPrefix(root.path.hasSuffix("/") ? root.path : root.path + "/")
        }
        let sourcePath = location(source), destinationPath = location(destination)
        if let root = dropboxRoots.first(where: { contains(sourcePath, root: $0) }),
           !contains(destinationPath, root: root), let stub = OnlineOnlyItems.firstStub(in: source) {
            throw FileEngineError.onlineOnly(stub)
        }
    }
    static func unique(_ proposed: URL, isDirectory: Bool? = nil) -> URL {
        if !exists(proposed) { return proposed }
        let folder = isDirectory ?? ((try? isNamingFolder(proposed)) == true)
        let ext = folder ? "" : proposed.pathExtension
        let stem = ext.isEmpty ? proposed.lastPathComponent : proposed.deletingPathExtension().lastPathComponent
        var index = 2
        while true {
            let name = "\(stem) \(index)" + (ext.isEmpty ? "" : ".\(ext)")
            let candidate = proposed.deletingLastPathComponent().appendingPathComponent(name)
            if !exists(candidate) { return candidate }
            index += 1
        }
    }
    static func rejectRecursion(_ source: URL, _ destination: URL) throws {
        let src = source.resolvingSymlinksInPath().standardizedFileURL.path
        let dst = destination.resolvingSymlinksInPath().standardizedFileURL.path
        var a = stat(), b = stat()
        let sameNode = source.path.withCString { lstat($0, &a) } == 0 && destination.path.withCString { lstat($0, &b) } == 0 && a.st_dev == b.st_dev && a.st_ino == b.st_ino
        if src == dst || sameNode { throw FileEngineError.recursiveDestination }
        if (a.st_mode & S_IFMT) == S_IFDIR {
            if dst.hasPrefix(src.hasSuffix("/") ? src : src + "/") { throw FileEngineError.recursiveDestination }
            // Compare ancestor identities as well: case-insensitive paths and symlinked
            // destination parents must not hide a folder's relationship to itself.
            var ancestor = destination.deletingLastPathComponent().resolvingSymlinksInPath().standardizedFileURL
            while true {
                var info = stat()
                if ancestor.path.withCString({ lstat($0, &info) }) == 0 && info.st_dev == a.st_dev && info.st_ino == a.st_ino { throw FileEngineError.recursiveDestination }
                if ancestor.path == "/" { break }
                let parent = ancestor.deletingLastPathComponent().standardizedFileURL
                if parent.path == ancestor.path { break }
                ancestor = parent
            }
        }
    }
    func trash(_ item: URL) throws -> URL {
        var result: NSURL?
        do { try fm.trashItem(at: item, resultingItemURL: &result) }
        catch {
            let ns = error as NSError
            let underlying = ns.userInfo[NSUnderlyingErrorKey] as? NSError
            let unsupportedPOSIX = [Int(ENOTSUP), Int(EOPNOTSUPP), Int(ENOSYS)]
            if (ns.domain == NSCocoaErrorDomain && (ns.code == NSFeatureUnsupportedError || ns.code == NSFileWriteUnsupportedSchemeError)) ||
                (ns.domain == NSPOSIXErrorDomain && unsupportedPOSIX.contains(ns.code)) ||
                (underlying?.domain == NSPOSIXErrorDomain && unsupportedPOSIX.contains(underlying?.code ?? 0)) {
                throw FileEngineError.trashUnavailable(item, error.localizedDescription)
            }
            // Trash can be unavailable on a read/write mounted filesystem without .Trashes.
            let volume = try? item.resourceValues(forKeys: [.volumeSupportsRenamingKey, .volumeIsReadOnlyKey])
            if volume?.volumeIsReadOnly != true && ns.domain == NSCocoaErrorDomain && ns.code == NSFileWriteUnknownError {
                throw FileEngineError.trashUnavailable(item, error.localizedDescription)
            }
            throw error
        }
        guard let result else { throw FileEngineError.trashUnavailable(item, tr("No Trash location was returned.", "휴지통 위치를 확인할 수 없습니다.")) }
        return result as URL
    }
    /// Restrictive source modes may already have been restored on completed subfolders.
    /// Make only our uncommitted staging tree removable; never change original items.
    private func removeStaging(_ url: URL) {
        guard Self.exists(url) else { return }
        func allowAccess(_ item: URL) {
            item.path.withCString { path in
                var info = stat()
                if lstat(path, &info) == 0 && (info.st_mode & S_IFMT) != S_IFLNK && (info.st_flags & UInt32(UF_IMMUTABLE)) != 0 {
                    _ = chflags(path, info.st_flags & ~UInt32(UF_IMMUTABLE))
                }
                if lstat(path, &info) == 0 && (info.st_mode & S_IFMT) == S_IFDIR && (info.st_mode & 0o700) != 0o700 { _ = chmod(path, (info.st_mode & 0o7777) | 0o700) }
            }
        }
        allowAccess(url)
        if let walk = fm.enumerator(at: url, includingPropertiesForKeys: nil) {
            for case let child as URL in walk { allowAccess(child) }
        }
        try? fm.removeItem(at: url)
    }
    func copy(_ source: URL, to destination: URL, token: FileOperationCancellation,
              base: Int64, total: Int64, progress: @escaping @Sendable (FileOperationProgress) -> Void) throws {
        if token.isCancelled { throw FileEngineError.cancelled }
        if OnlineOnlyItems.isStub(source) { throw FileEngineError.onlineOnly(source) }
        if Self.exists(destination) { throw FileEngineError.exists(destination) }
        let staging = destination.deletingLastPathComponent().appendingPathComponent(".folder-splitter-copy-\(UUID().uuidString)")
        defer { removeStaging(staging) }
        guard let state = copyfile_state_alloc() else { throw POSIXError(.ENOMEM) }
        defer { copyfile_state_free(state) }
        let context = CopyContext(token, total: total, base: base, source: source, progress: progress)
        copyfile_state_set(state, UInt32(COPYFILE_STATE_STATUS_CB), unsafeBitCast(copyProgressCallback, to: UnsafeRawPointer.self))
        copyfile_state_set(state, UInt32(COPYFILE_STATE_STATUS_CTX), Unmanaged.passUnretained(context).toOpaque())
        let flags = copyfile_flags_t(COPYFILE_ALL | COPYFILE_RECURSIVE | COPYFILE_CLONE | COPYFILE_EXCL | COPYFILE_NOFOLLOW)
        let result = source.path.withCString { src in staging.path.withCString { dst in copyfile(src, dst, state, flags) } }
        let savedErrno = context.errorCode ?? errno
        if result != 0 || token.isCancelled {
            if let stub = context.onlineOnly { throw FileEngineError.onlineOnly(stub) }
            if token.isCancelled { throw FileEngineError.cancelled }
            throw POSIXError(POSIXErrorCode(rawValue: savedErrno) ?? .EIO)
        }
        var stagedInfo = stat()
        let locked = staging.path.withCString { lstat($0, &stagedInfo) } == 0 &&
            (stagedInfo.st_mode & S_IFMT) != S_IFLNK && (stagedInfo.st_flags & UInt32(UF_IMMUTABLE)) != 0
        if locked && staging.path.withCString({ chflags($0, stagedInfo.st_flags & ~UInt32(UF_IMMUTABLE)) }) != 0 {
            throw POSIXError(POSIXErrorCode(rawValue: errno) ?? .EIO)
        }
        let committed = staging.path.withCString { src in destination.path.withCString { dst in renamex_np(src, dst, UInt32(RENAME_EXCL)) } }
        if committed != 0 { throw POSIXError(POSIXErrorCode(rawValue: errno) ?? .EIO) }
        if locked && destination.path.withCString({ chflags($0, stagedInfo.st_flags) }) != 0 {
            let error = POSIXError(POSIXErrorCode(rawValue: errno) ?? .EIO)
            removeStaging(destination)
            throw error
        }
    }
    func relocate(_ source: URL, to destination: URL,
                  cancellation: FileOperationCancellation = FileOperationCancellation(),
                  progress: @escaping @Sendable (FileOperationProgress) -> Void = { _ in }) throws {
        if cancellation.isCancelled { throw FileEngineError.cancelled }
        try Self.rejectRecursion(source, destination)
        if Self.exists(destination) { throw FileEngineError.exists(destination) }
        try checkMove(source, to: destination)
        let code = renameItem(source, destination)
        if code == 0 { return }
        guard code == EXDEV else { throw POSIXError(POSIXErrorCode(rawValue: code) ?? .EIO) }
        let total = try Self.size(source, cancellation: cancellation)
        progress(.init(completedBytes: 0, totalBytes: total, item: source))
        let inventory = try CopiedSourceInventory(source)
        try copy(source, to: destination, token: cancellation, base: 0, total: total, progress: progress)
        progress(.init(completedBytes: total, totalBytes: total, item: source))
        // Preserve the committed copy if anything new, changed, or unremovable remains.
        do { try inventory.removeCopiedEntries(destination: destination) }
        catch { throw FileEngineError.sourceRemovalFailed(source, destination, error.localizedDescription) }
    }
    private func duplicateDestination(for item: URL) throws -> URL {
        let folder = try Self.isNamingFolder(item)
        let ext = folder ? "" : item.pathExtension
        let stem = ext.isEmpty ? item.lastPathComponent : item.deletingPathExtension().lastPathComponent
        let name = stem + tr(" copy", " 복사본") + (ext.isEmpty ? "" : ".\(ext)")
        return Self.unique(item.deletingLastPathComponent().appendingPathComponent(name), isDirectory: folder)
    }
    func transfer(_ items: [URL], to folder: URL, mode: TransferMode,
                  cancellation: FileOperationCancellation = FileOperationCancellation(),
                  progress: @escaping @Sendable (FileOperationProgress) -> Void = { _ in },
                  conflict: @escaping ConflictHandler) async -> FileEngineResult {
        await Task.detached(priority: .userInitiated) { [self] in
            var result = FileEngineResult()
            var total: Int64 = 0, done: Int64 = 0
            var all: FileConflictChoice?
            progress(.init(completedBytes: 0, totalBytes: 0, item: items.first))
            for (index, source) in items.enumerated() {
                if cancellation.isCancelled { result.outcome.cancelled = true; break }
                var destination = folder.appendingPathComponent(source.lastPathComponent)
                var replaced: URL?
                do {
                    if mode == .move { try checkMove(source, to: destination) }
                    if Self.sameItem(source.deletingLastPathComponent().resolvingSymlinksInPath(), folder.resolvingSymlinksInPath()) {
                        if mode == .move { result.outcome.skipped.append(source); continue }
                        destination = try duplicateDestination(for: source)
                    }
                    try Self.rejectRecursion(source, destination)
                    if Self.exists(destination) {
                        let resolution: FileConflictResolution
                        if let all { resolution = .init(choice: all) }
                        else {
                            let remainingConflicts = items.dropFirst(index + 1).filter { Self.exists(folder.appendingPathComponent($0.lastPathComponent)) }.count
                            let sourceSize = try Self.size(source, cancellation: cancellation)
                            let destinationSize = try Self.size(destination, cancellation: cancellation)
                            resolution = await conflict(.init(source: source, destination: destination, sourceBytes: sourceSize, destinationBytes: destinationSize, sourceModified: try? source.resourceValues(forKeys: [.contentModificationDateKey]).contentModificationDate, destinationModified: try? destination.resourceValues(forKeys: [.contentModificationDateKey]).contentModificationDate, remainingItems: remainingConflicts, sourceOnlineOnly: OnlineOnlyItems.isStub(source), destinationOnlineOnly: OnlineOnlyItems.isStub(destination)))
                        }
                        if resolution.applyToAll { all = resolution.choice }
                        switch resolution.choice {
                        case .stop: result.outcome.cancelled = true
                        case .skip: result.outcome.skipped.append(source)
                        case .keepBoth: destination = Self.unique(destination, isDirectory: try Self.isNamingFolder(source))
                        case .replace:
                            replaced = try trash(destination)
                            result.trashed.append(.init(from: destination, to: replaced!))
                        }
                        if result.outcome.cancelled { break }
                        if resolution.choice == .skip { continue }
                    }
                    var requiresCopy = mode == .copy
                    if mode == .move {
                        let code = renameItem(source, destination)
                        if code == EXDEV { requiresCopy = true }
                        else if code != 0 { throw POSIXError(POSIXErrorCode(rawValue: code) ?? .EIO) }
                    }
                    if requiresCopy {
                        let size = try Self.size(source, cancellation: cancellation)
                        total += size
                        progress(.init(completedBytes: done, totalBytes: total, item: source))
                        let inventory = mode == .move ? try CopiedSourceInventory(source) : nil
                        try copy(source, to: destination, token: cancellation, base: done, total: total, progress: progress)
                        done += size
                        progress(.init(completedBytes: done, totalBytes: total, item: source))
                        if mode == .move {
                            do { try inventory?.removeCopiedEntries(destination: destination) }
                            catch { throw FileEngineError.sourceRemovalFailed(source, destination, error.localizedDescription) }
                        }
                    }
                    result.undo.insert(mode == .move ? .relocate(destination, source) : .trash(destination), at: 0)
                    if let replaced { result.undo.insert(.relocate(replaced, destination), at: 1) }
                    result.outcome.produced.append(destination)
                } catch {
                    if case FileEngineError.sourceRemovalFailed = error {
                        result.outcome.produced.append(destination)
                        result.undo.insert(.trash(destination), at: 0)
                        if let replaced { result.undo.insert(.relocate(replaced, destination), at: 1) }
                        result.outcome.failures.append(.init(url: source, message: error.localizedDescription))
                    } else if let replaced, !Self.exists(destination) {
                        do { try relocate(replaced, to: destination) }
                        catch { result.outcome.failures.append(.init(url: replaced, message: error.localizedDescription)) }
                    }
                    if cancellation.isCancelled { result.outcome.cancelled = true; break }
                    if case FileEngineError.sourceRemovalFailed = error { continue }
                    result.outcome.failures.append(.init(url: source, message: error.localizedDescription))
                }
            }
            return result
        }.value
    }

    func rename(_ item: URL, to name: String) throws -> URL {
        try rename([FileRename(item: item, newName: name)])[0]
    }
    func rename(_ changes: [FileRename]) throws -> [URL] {
        var destinations: [URL] = []
        var originals = Set<String>()
        for change in changes {
            try Self.validateName(change.newName)
            guard Self.exists(change.item), originals.insert(change.item.standardizedFileURL.path).inserted else { throw FileEngineError.exists(change.item) }
            destinations.append(change.item.deletingLastPathComponent().appendingPathComponent(change.newName.precomposedStringWithCanonicalMapping))
        }
        var keys = Set<String>()
        for (index, target) in destinations.enumerated() {
            let insensitive = (try? target.deletingLastPathComponent().resourceValues(forKeys: [.volumeSupportsCaseSensitiveNamesKey]).volumeSupportsCaseSensitiveNames) == false
            let key = insensitive ? target.path.precomposedStringWithCanonicalMapping.lowercased() : target.path.precomposedStringWithCanonicalMapping
            guard keys.insert(key).inserted else { throw FileEngineError.exists(target) }
            if Self.exists(target) {
                let attributes = try fm.attributesOfItem(atPath: target.path)
                let isSource = changes.contains { (try? fm.attributesOfItem(atPath: $0.item.path)[.systemFileNumber] as? NSNumber) == (attributes[.systemFileNumber] as? NSNumber) && $0.item.deletingLastPathComponent().standardizedFileURL == target.deletingLastPathComponent().standardizedFileURL }
                guard isSource else { throw FileEngineError.exists(target) }
            }
            _ = index
        }
        let temps = changes.map { $0.item.deletingLastPathComponent().appendingPathComponent(".folder-splitter-rename-\(UUID().uuidString)") }
        var staged = 0, committed = 0
        do {
            for index in changes.indices { try fm.moveItem(at: changes[index].item, to: temps[index]); staged += 1 }
            for index in changes.indices { try fm.moveItem(at: temps[index], to: destinations[index]); committed += 1 }
        } catch {
            var rollbackErrors: [String] = []
            for index in (0..<committed).reversed() { do { try fm.moveItem(at: destinations[index], to: temps[index]) } catch { rollbackErrors.append(error.localizedDescription) } }
            for index in (0..<staged).reversed() { do { try fm.moveItem(at: temps[index], to: changes[index].item) } catch { rollbackErrors.append(error.localizedDescription) } }
            if !rollbackErrors.isEmpty { throw FileEngineError.rollback(rollbackErrors.joined(separator: "\n")) }
            throw error
        }
        return destinations
    }
    func newFolder(in parent: URL, containingItems: Bool = false) throws -> URL {
        let name = containingItems ? tr("New Folder With Items", "새로운 폴더(항목 포함)") : tr("untitled folder", "무제 폴더")
        let url = Self.unique(parent.appendingPathComponent(name, isDirectory: true), isDirectory: true)
        try fm.createDirectory(at: url, withIntermediateDirectories: false)
        return url
    }
    func duplicate(_ items: [URL], cancellation: FileOperationCancellation = FileOperationCancellation(), progress: @escaping @Sendable (FileOperationProgress) -> Void = { _ in }) -> FileEngineResult {
        var result = FileEngineResult()
        var total: Int64 = 0, done: Int64 = 0
        for item in items {
            if cancellation.isCancelled { result.outcome.cancelled = true; break }
            do {
                let target = try duplicateDestination(for: item)
                let size = try Self.size(item, cancellation: cancellation)
                total += size
                progress(.init(completedBytes: done, totalBytes: total, item: item))
                try copy(item, to: target, token: cancellation, base: done, total: total, progress: progress)
                done += size
                progress(.init(completedBytes: done, totalBytes: total, item: item))
                result.outcome.produced.append(target); result.undo.insert(.trash(target), at: 0)
            } catch { if cancellation.isCancelled { result.outcome.cancelled = true; break }; result.outcome.failures.append(.init(url: item, message: error.localizedDescription)) }
        }
        return result
    }
    func newFolder(containing items: [URL]) throws -> FileEngineResult {
        guard let first = items.first else { return FileEngineResult() }
        let parent = first.deletingLastPathComponent().standardizedFileURL
        guard items.allSatisfy({ $0.deletingLastPathComponent().standardizedFileURL == parent }) else { throw FileEngineError.recursiveDestination }
        let folder = try newFolder(in: parent, containingItems: true)
        var result = FileEngineResult(outcome: .init(produced: [folder]), undo: [.trash(folder)])
        for item in items {
            let target = folder.appendingPathComponent(item.lastPathComponent)
            do {
                try relocate(item, to: target)
                result.undo.insert(.relocate(target, item), at: 0)
            } catch {
                if case FileEngineError.sourceRemovalFailed = error {
                    // Keep the new folder and its complete copy; rolling back by deleting
                    // it would lose originals already removed from the source tree.
                    result.undo.insert(.trash(target), at: 0)
                }
                result.outcome.failures.append(.init(url: item, message: error.localizedDescription))
                break
            }
        }
        return result
    }
    func makeAlias(_ items: [URL]) -> FileEngineResult {
        var result = FileEngineResult()
        for item in items {
            do {
                let target = Self.unique(item.deletingLastPathComponent().appendingPathComponent(item.lastPathComponent + tr(" alias", " 가상본")), isDirectory: true)
                let bookmark = try item.bookmarkData(options: .suitableForBookmarkFile, includingResourceValuesForKeys: nil, relativeTo: nil)
                try URL.writeBookmarkData(bookmark, to: target)
                result.outcome.produced.append(target); result.undo.insert(.trash(target), at: 0)
            } catch { result.outcome.failures.append(.init(url: item, message: error.localizedDescription)) }
        }
        return result
    }
    func compress(_ items: [URL], cancellation: FileOperationCancellation = FileOperationCancellation(),
                  progress: @escaping @Sendable (FileOperationProgress) -> Void = { _ in }) throws -> FileEngineResult {
        guard let first = items.first else { return FileEngineResult() }
        for item in items {
            if cancellation.isCancelled { throw FileEngineError.cancelled }
            if let stub = OnlineOnlyItems.firstStub(in: item) { throw FileEngineError.onlineOnly(stub) }
        }
        let parent = first.deletingLastPathComponent()
        let target = Self.unique(parent.appendingPathComponent(items.count == 1 ? first.lastPathComponent + ".zip" : tr("Archive.zip", "아카이브.zip")), isDirectory: false)
        let stage = parent.appendingPathComponent(".folder-splitter-archive-stage-\(UUID().uuidString)", isDirectory: true)
        let archiveOutput = parent.appendingPathComponent(".folder-splitter-archive-\(UUID().uuidString).zip")
        defer { removeStaging(stage); try? fm.removeItem(at: archiveOutput) }
        var arguments = ["-c", "-k", "--sequesterRsrc"]
        if items.count == 1 {
            // ditto --keepParent on a regular file adds its enclosing directory, unlike
            // Finder. Keep the parent only for folders so the archive has the right root.
            if try first.resourceValues(forKeys: [.isDirectoryKey]).isDirectory == true { arguments.append("--keepParent") }
            arguments += [first.path, archiveOutput.path]
        }
        else {
            try fm.createDirectory(at: stage, withIntermediateDirectories: false)
            var total: Int64 = 0, done: Int64 = 0
            for item in items {
                let destination = stage.appendingPathComponent(item.lastPathComponent)
                guard !Self.exists(destination) else { throw FileEngineError.exists(destination) }
                let size = try Self.size(item, cancellation: cancellation)
                total += size
                progress(.init(completedBytes: done, totalBytes: total, item: item))
                try copy(item, to: destination, token: cancellation, base: done, total: total, progress: progress)
                done += size
                progress(.init(completedBytes: done, totalBytes: total, item: item))
            }
            arguments += [stage.path, archiveOutput.path]
        }
        // ditto does not expose byte progress. Do not display the staging-copy estimate
        // as if it measured the subsequent compression phase.
        progress(.init(completedBytes: 0, totalBytes: 0, item: first))
        let process = Process()
        process.executableURL = URL(fileURLWithPath: "/usr/bin/ditto"); process.arguments = arguments
        let errors = Pipe(); process.standardError = errors
        if cancellation.isCancelled { throw FileEngineError.cancelled }
        try process.run()
        let monitor = DispatchSource.makeTimerSource(queue: .global(qos: .utility))
        monitor.schedule(deadline: .now(), repeating: .milliseconds(100))
        monitor.setEventHandler { if cancellation.isCancelled && process.isRunning { process.terminate() } }
        monitor.resume()
        let data = errors.fileHandleForReading.readDataToEndOfFile()
        process.waitUntilExit(); monitor.cancel()
        if cancellation.isCancelled { throw FileEngineError.cancelled }
        guard process.terminationStatus == 0 else { throw NSError(domain: "FolderSplitter.Archive", code: Int(process.terminationStatus), userInfo: [NSLocalizedDescriptionKey: String(data: data, encoding: .utf8) ?? tr("The archive could not be created.", "압축 파일을 생성할 수 없습니다.")]) }
        let committed = archiveOutput.path.withCString { src in target.path.withCString { dst in renamex_np(src, dst, UInt32(RENAME_EXCL)) } }
        if committed != 0 { throw POSIXError(POSIXErrorCode(rawValue: errno) ?? .EIO) }
        return FileEngineResult(outcome: .init(produced: [target]), undo: [.trash(target)])
    }
    func trash(_ items: [URL]) -> FileEngineResult {
        var result = FileEngineResult()
        for item in items {
            do { let target = try trash(item); result.trashed.append(.init(from: item, to: target)); result.undo.insert(.relocate(target, item), at: 0) }
            catch FileEngineError.trashUnavailable { result.trashUnavailable.append(item) }
            catch { result.outcome.failures.append(.init(url: item, message: error.localizedDescription)) }
        }
        return result
    }
    func applyUndo(_ steps: [FileUndoStep],
                   cancellation: FileOperationCancellation = FileOperationCancellation(),
                   progress: @escaping @Sendable (FileOperationProgress) -> Void = { _ in }) -> FileEngineResult {
        var result = FileEngineResult()
        for step in steps {
            if cancellation.isCancelled { result.outcome.cancelled = true; break }
            let affected: URL
            switch step {
            case .relocate(let from, _), .trash(let from): affected = from
            case .renameBatch(let changes): affected = changes.first?.from ?? URL(fileURLWithPath: "/")
            }
            do {
                switch step {
                case .relocate(let from, let to):
                    try relocate(from, to: to, cancellation: cancellation, progress: progress)
                    result.undo.insert(.relocate(to, from), at: 0)
                    result.outcome.produced.append(to)
                case .trash(let item): let target = try trash(item); result.undo.insert(.relocate(target, item), at: 0)
                case .renameBatch(let changes):
                    let targets = try rename(changes.map { .init(item: $0.from, newName: $0.to.lastPathComponent) })
                    result.undo.insert(.renameBatch(zip(targets, changes).map { .init(from: $0.0, to: $0.1.from) }), at: 0)
                }
            } catch {
                if case FileEngineError.sourceRemovalFailed(_, let destination, _) = error {
                    result.outcome.produced.append(destination)
                    result.undo.insert(.trash(destination), at: 0)
                    result.outcome.failures.append(.init(url: affected, message: error.localizedDescription))
                } else if !cancellation.isCancelled {
                    result.outcome.failures.append(.init(url: affected, message: error.localizedDescription))
                }
                if cancellation.isCancelled { result.outcome.cancelled = true }
                break
            }
        }
        return result
    }
}
