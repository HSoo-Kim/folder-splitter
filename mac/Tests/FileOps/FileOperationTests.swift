import Foundation
import Testing
import Darwin

private final class Sandbox {
    let root: URL
    let engine = FileOperationEngine()
    init() throws {
        root = FileManager.default.temporaryDirectory.appendingPathComponent("FileOpsTests-\(UUID().uuidString)", isDirectory: true)
        try FileManager.default.createDirectory(at: root, withIntermediateDirectories: false)
    }
    deinit { try? FileManager.default.removeItem(at: root) }
    func folder(_ path: String) throws -> URL {
        let url = root.appendingPathComponent(path, isDirectory: true)
        try FileManager.default.createDirectory(at: url, withIntermediateDirectories: true)
        return url
    }
    func file(_ path: String, _ contents: String = "contents") throws -> URL {
        let url = root.appendingPathComponent(path)
        try FileManager.default.createDirectory(at: url.deletingLastPathComponent(), withIntermediateDirectories: true)
        try Data(contents.utf8).write(to: url)
        return url
    }
    func read(_ url: URL) throws -> String { try String(contentsOf: url, encoding: .utf8) }
}
private final class CapturedProgress: @unchecked Sendable {
    private let lock = NSLock()
    private var storage: [FileOperationProgress] = []
    func append(_ value: FileOperationProgress) { lock.lock(); storage.append(value); lock.unlock() }
    var values: [FileOperationProgress] { lock.lock(); defer { lock.unlock() }; return storage }
}
private final class CapturedURLs: @unchecked Sendable {
    private let lock = NSLock()
    private var storage: [URL] = []
    func append(_ values: [URL]) { lock.lock(); storage += values; lock.unlock() }
    var values: [URL] { lock.lock(); defer { lock.unlock() }; return storage }
}
private actor Conflicts {
    var calls = 0
    func respond(_ choice: FileConflictChoice, all: Bool = false) -> FileConflictResolution { calls += 1; return .init(choice: choice, applyToAll: all) }
}

@Suite(.serialized)
struct FileOperationTests {
    @Test func recursiveCopyProgressAndUnicode() async throws {
        let box = try Sandbox()
        let source = try box.folder("source/한글 묶음")
        _ = try box.file("source/한글 묶음/a.txt", "abc")
        _ = try box.file("source/한글 묶음/nested/é.txt", "한국어")
        let target = try box.folder("target")
        let captured = CapturedProgress()
        let result = await box.engine.transfer([source], to: target, mode: .copy, progress: { captured.append($0) }, conflict: { _ in .init(choice: .stop) })
        #expect(result.outcome.succeeded)
        #expect(try box.read(target.appendingPathComponent("한글 묶음/nested/é.txt")) == "한국어")
        let values = captured.values
        #expect(values.last?.completedBytes == 12)
        #expect(values.last?.totalBytes == 12)
        #expect(zip(values, values.dropFirst()).allSatisfy { $0.completedBytes <= $1.completedBytes })
        #expect(FileOperationEngine.exists(source))
    }

    @Test func cancellationAfterPartialCopyCleansOutput() async throws {
        let box = try Sandbox()
        let source = try box.folder("source/bundle")
        for number in 0..<20 { _ = try box.file("source/bundle/file\(number).txt", String(repeating: "a", count: 4096)) }
        let target = try box.folder("target")
        let cancellation = FileOperationCancellation()
        let progress = CapturedProgress()
        let result = await box.engine.transfer([source], to: target, mode: .copy, cancellation: cancellation, progress: {
            progress.append($0)
            if $0.completedBytes > 0 { cancellation.cancel() }
        }, conflict: { _ in .init(choice: .stop) })
        #expect(result.outcome.cancelled)
        #expect(progress.values.contains { $0.completedBytes > 0 && $0.completedBytes < $0.totalBytes })
        #expect(try FileManager.default.contentsOfDirectory(atPath: target.path).isEmpty)
        #expect(FileOperationEngine.exists(source))
    }

    @Test func conflictsApplyToAllAndKeepBothNames() async throws {
        let box = try Sandbox()
        let a = try box.file("source/a.txt", "new a")
        let b = try box.file("source/b.txt", "new b")
        _ = try box.file("target/a.txt", "old a")
        _ = try box.file("target/a 2.txt", "older a")
        _ = try box.file("target/b.txt", "old b")
        let conflicts = Conflicts()
        let result = await box.engine.transfer([a, b], to: box.root.appendingPathComponent("target"), mode: .copy, conflict: { _ in await conflicts.respond(.keepBoth, all: true) })
        #expect(result.outcome.succeeded)
        #expect(result.outcome.produced.map(\.lastPathComponent) == ["a 3.txt", "b 2.txt"])
        #expect(await conflicts.calls == 1)
        #expect(try box.read(box.root.appendingPathComponent("target/a.txt")) == "old a")
    }

    @Test func skipAndStopLeaveOriginals() async throws {
        let box = try Sandbox()
        let a = try box.file("source/a", "new")
        _ = try box.file("target/a", "old")
        let target = box.root.appendingPathComponent("target")
        let skipped = await box.engine.transfer([a], to: target, mode: .copy, conflict: { _ in .init(choice: .skip) })
        #expect(skipped.outcome.skipped == [a])
        let stopped = await box.engine.transfer([a], to: target, mode: .move, conflict: { _ in .init(choice: .stop) })
        #expect(stopped.outcome.cancelled)
        #expect(try box.read(target.appendingPathComponent("a")) == "old")
        #expect(FileOperationEngine.exists(a))
    }

    @Test func replaceIsRecoverableInTrashAndUndoRedo() async throws {
        let box = try Sandbox()
        let a = try box.file("source/replace-\(UUID().uuidString).txt", "new")
        let target = try box.folder("target")
        let original = try box.file("target/\(a.lastPathComponent)", "old")
        let result = await box.engine.transfer([a], to: target, mode: .copy, conflict: { _ in .init(choice: .replace) })
        defer { for item in result.trashed { try? FileManager.default.removeItem(at: item.to) } }
        #expect(result.outcome.succeeded)
        let trash = try #require(result.trashed.first)
        #expect(trash.from == original)
        #expect(trash.to.deletingLastPathComponent() != original.deletingLastPathComponent())
        #expect(try box.read(trash.to) == "old")
        #expect(try box.read(original) == "new")
        let undo = box.engine.applyUndo(result.undo)
        #expect(undo.outcome.succeeded)
        #expect(try box.read(original) == "old")
        let redo = box.engine.applyUndo(undo.undo)
        #expect(redo.outcome.succeeded)
        #expect(try box.read(original) == "new")
        // Undo once more restores the replaced file and cleans every trash artifact.
        let finalUndo = box.engine.applyUndo(redo.undo)
        for step in finalUndo.undo { if case .relocate(let from, _) = step, from.path.contains(".Trash") { try? FileManager.default.removeItem(at: from) } }
    }

    @Test func moveSameVolumePreservesFileIdentity() async throws {
        let box = try Sandbox()
        let a = try box.file("source/a.txt")
        let before = try FileManager.default.attributesOfItem(atPath: a.path)[.systemFileNumber] as? NSNumber
        let target = try box.folder("target")
        let result = await box.engine.transfer([a], to: target, mode: .move, conflict: { _ in .init(choice: .stop) })
        #expect(result.outcome.succeeded)
        #expect(!FileOperationEngine.exists(a))
        let moved = try #require(result.outcome.produced.first)
        #expect(try FileManager.default.attributesOfItem(atPath: moved.path)[.systemFileNumber] as? NSNumber == before)
        let undone = box.engine.applyUndo(result.undo)
        #expect(undone.outcome.succeeded)
        #expect(FileOperationEngine.exists(a))
    }

    @Test func movingOrCopyingIntoDescendantsIsRefused() async throws {
        let box = try Sandbox()
        let source = try box.folder("source")
        let child = try box.folder("source/child")
        for mode in [TransferMode.copy, .move] {
            let nested = await box.engine.transfer([source], to: child, mode: mode, conflict: { _ in .init(choice: .replace) })
            #expect(nested.outcome.failures.count == 1)
        }
        #expect(FileOperationEngine.exists(source))
    }

    @Test func renameValidationAndCaseOnly() throws {
        let box = try Sandbox()
        let a = try box.file("a.txt", "a")
        _ = try box.file("b.txt", "b")
        for name in ["", ".", "..", "a/b", "a:b", "\0", "b.txt"] {
            #expect(throws: (any Error).self) { try box.engine.rename(a, to: name) }
        }
        let upper = try box.engine.rename(a, to: "A.txt")
        #expect(upper.lastPathComponent == "A.txt")
        #expect(try box.read(upper) == "a")
        #expect(try FileManager.default.contentsOfDirectory(atPath: box.root.path).contains("A.txt"))
    }

    @Test func renameSwapAndRollbackOnCommitFailure() throws {
        let box = try Sandbox()
        let a = try box.file("a.txt", "A")
        let b = try box.file("b.txt", "B")
        let targets = try box.engine.rename([.init(item: a, newName: "b.txt"), .init(item: b, newName: "a.txt")])
        #expect(try box.read(targets[0]) == "A")
        #expect(try box.read(targets[1]) == "B")
        // A name over NAME_MAX passes semantic validation, but its commit fails after
        // the first item committed. Both original paths/content must be restored.
        #expect(throws: (any Error).self) {
            try box.engine.rename([.init(item: a, newName: "c.txt"), .init(item: b, newName: String(repeating: "x", count: 300))])
        }
        #expect(try box.read(a) == "B")
        #expect(try box.read(b) == "A")
        #expect(!FileOperationEngine.exists(box.root.appendingPathComponent("c.txt")))
        #expect(try FileManager.default.contentsOfDirectory(atPath: box.root.path).sorted() == ["a.txt", "b.txt"])
    }

    @Test func renameClashDoesNotPartiallyChangeBatch() throws {
        let box = try Sandbox()
        let a = try box.file("a", "a")
        let b = try box.file("b", "b")
        _ = try box.file("occupied", "safe")
        #expect(throws: (any Error).self) { try box.engine.rename([.init(item: a, newName: "free"), .init(item: b, newName: "occupied")]) }
        #expect(try box.read(a) == "a")
        #expect(try box.read(b) == "b")
    }

    @Test func localizedFolderAndDuplicateNumbering() throws {
        let box = try Sandbox()
        let old = Loc.language
        defer { Loc.language = old }
        for language in [AppLanguage.english, .korean] {
            Loc.language = language
            let parent = try box.folder(language.rawValue)
            let base = language == .english ? "untitled folder" : "무제 폴더"
            #expect(try box.engine.newFolder(in: parent).lastPathComponent == base)
            #expect(try box.engine.newFolder(in: parent).lastPathComponent == base + " 2")
            let source = try box.file("\(language.rawValue)/보고서.txt", "text")
            let copy = language == .english ? "보고서 copy" : "보고서 복사본"
            #expect(box.engine.duplicate([source]).outcome.produced.first?.lastPathComponent == copy + ".txt")
            #expect(box.engine.duplicate([source]).outcome.produced.first?.lastPathComponent == copy + " 2.txt")
        }
    }

    @Test func containingFolderUndoRedo() throws {
        let box = try Sandbox()
        let a = try box.file("a", "A")
        let b = try box.file("b", "B")
        let result = try box.engine.newFolder(containing: [a, b])
        let folder = try #require(result.outcome.produced.first)
        #expect(try box.read(folder.appendingPathComponent("a")) == "A")
        let undo = box.engine.applyUndo(result.undo)
        #expect(undo.outcome.succeeded)
        #expect(try box.read(a) == "A")
        #expect(try box.read(b) == "B")
        let redo = box.engine.applyUndo(undo.undo)
        #expect(redo.outcome.succeeded)
        #expect(try box.read(folder.appendingPathComponent("b")) == "B")
    }

    @Test func archivesContainSingleAndMultipleItemsAtTopLevel() throws {
        let box = try Sandbox()
        let a = try box.file("source/a.txt", "A")
        let folder = try box.folder("source/한글")
        _ = try box.file("source/한글/b.txt", "B")
        let single = try #require(try box.engine.compress([a]).outcome.produced.first)
        #expect(single.lastPathComponent == "a.txt.zip")
        let multiple = try #require(try box.engine.compress([a, folder]).outcome.produced.first)
        let first = try box.folder("unzip1")
        let second = try box.folder("unzip2")
        for (archive, destination) in [(single, first), (multiple, second)] {
            let process = Process()
            process.executableURL = URL(fileURLWithPath: "/usr/bin/ditto")
            process.arguments = ["-x", "-k", archive.path, destination.path]
            try process.run(); process.waitUntilExit()
            #expect(process.terminationStatus == 0)
            #expect(try box.read(destination.appendingPathComponent("a.txt")) == "A")
        }
        #expect(try box.read(second.appendingPathComponent("한글/b.txt")) == "B")
        #expect(try box.engine.compress([a, folder]).outcome.produced.first?.lastPathComponent == multiple.deletingPathExtension().lastPathComponent + " 2.zip")
        let folderArchive = try #require(try box.engine.compress([folder]).outcome.produced.first)
        let third = try box.folder("unzip3")
        let process = Process()
        process.executableURL = URL(fileURLWithPath: "/usr/bin/ditto")
        process.arguments = ["-x", "-k", folderArchive.path, third.path]
        try process.run(); process.waitUntilExit()
        #expect(process.terminationStatus == 0)
        #expect(try box.read(third.appendingPathComponent("한글/b.txt")) == "B")
    }

    @Test func aliasResolvesAndDoesNotCopyData() throws {
        let box = try Sandbox()
        let target = try box.file("대상.txt", "original")
        let result = box.engine.makeAlias([target])
        #expect(result.outcome.succeeded)
        let alias = try #require(result.outcome.produced.first)
        #expect(try URL(resolvingAliasFileAt: alias, options: [.withoutUI, .withoutMounting]).standardizedFileURL == target.standardizedFileURL)
        #expect(try alias.resourceValues(forKeys: [.isAliasFileKey]).isAliasFile == true)
    }

    @Test func symlinkedDestinationCannotHideRecursion() async throws {
        let box = try Sandbox()
        let source = try box.folder("source")
        let child = try box.folder("source/child")
        let link = box.root.appendingPathComponent("linked-child")
        try FileManager.default.createSymbolicLink(at: link, withDestinationURL: child)
        let result = await box.engine.transfer([source], to: link, mode: .copy, conflict: { _ in .init(choice: .keepBoth) })
        #expect(result.outcome.failures.count == 1)
        #expect(try FileManager.default.contentsOfDirectory(atPath: child.path).isEmpty)
    }

    @Test func keepBothUsesIncomingKindForDottedFolder() async throws {
        let box = try Sandbox()
        let source = try box.folder("source/report.data")
        _ = try box.file("source/report.data/contents", "saved")
        _ = try box.file("target/report.data", "existing file")
        let result = await box.engine.transfer([source], to: box.root.appendingPathComponent("target"), mode: .copy, conflict: { _ in .init(choice: .keepBoth) })
        #expect(result.outcome.succeeded)
        #expect(result.outcome.produced.first?.lastPathComponent == "report.data 2")
        #expect(try box.read(box.root.appendingPathComponent("target/report.data 2/contents")) == "saved")
    }

    @Test(arguments: [false, true])
    func failedSourceRemovalKeepsCompleteCrossVolumeCopy(throughUndo: Bool) async throws {
        let box = try Sandbox()
        let source = try box.folder("source/bundle")
        _ = try box.file("source/bundle/removable.txt", "must survive")
        let blocked = try box.folder("source/bundle/read-only")
        _ = try box.file("source/bundle/read-only/kept.txt", "also survives")
        let target = try box.folder("target")
        let destination = target.appendingPathComponent("bundle")
        try FileManager.default.setAttributes([.posixPermissions: 0o555], ofItemAtPath: blocked.path)
        defer {
            try? FileManager.default.setAttributes([.posixPermissions: 0o755], ofItemAtPath: blocked.path)
            try? FileManager.default.setAttributes([.posixPermissions: 0o755], ofItemAtPath: destination.appendingPathComponent("read-only").path)
        }
        // Only the rename result is substituted. Copying and partially failing removal
        // operate on real files and real read-only directory permissions.
        let engine = FileOperationEngine(renameItem: { _, _ in EXDEV })
        let result: FileEngineResult
        if throughUndo {
            result = engine.applyUndo([.relocate(source, destination)])
        } else {
            result = await engine.transfer([source], to: target, mode: .move, conflict: { _ in .init(choice: .stop) })
        }
        #expect(result.outcome.failures.count == 1)
        #expect(result.outcome.produced == [destination])
        #expect(try box.read(destination.appendingPathComponent("removable.txt")) == "must survive")
        #expect(try box.read(destination.appendingPathComponent("read-only/kept.txt")) == "also survives")
        #expect(try box.read(blocked.appendingPathComponent("kept.txt")) == "also survives")
        #expect(try (FileManager.default.attributesOfItem(atPath: blocked.path)[.posixPermissions] as? NSNumber)?.intValue == 0o555)
        #expect(try (FileManager.default.attributesOfItem(atPath: destination.appendingPathComponent("read-only").path)[.posixPermissions] as? NSNumber)?.intValue == 0o555)
        #expect(result.undo.contains { if case .trash(let url) = $0 { return url == destination }; return false })
        #expect(!result.undo.contains { if case .relocate = $0 { return true }; return false })
    }

    @Test func newContainingFolderKeepsCopyWhenOriginalRemovalFails() throws {
        let box = try Sandbox()
        let source = try box.folder("selection/bundle")
        let blocked = try box.folder("selection/bundle/read-only")
        _ = try box.file("selection/bundle/removable.txt", "complete")
        _ = try box.file("selection/bundle/read-only/kept.txt", "read only")
        try FileManager.default.setAttributes([.posixPermissions: 0o555], ofItemAtPath: blocked.path)
        var copiedDirectory: URL?
        defer {
            try? FileManager.default.setAttributes([.posixPermissions: 0o755], ofItemAtPath: blocked.path)
            if let copiedDirectory { try? FileManager.default.setAttributes([.posixPermissions: 0o755], ofItemAtPath: copiedDirectory.appendingPathComponent("read-only").path) }
        }
        let engine = FileOperationEngine(renameItem: { _, _ in EXDEV })
        let result = try engine.newFolder(containing: [source])
        let created = try #require(result.outcome.produced.first)
        let destination = created.appendingPathComponent("bundle")
        copiedDirectory = destination
        #expect(result.outcome.failures.count == 1)
        #expect(try box.read(destination.appendingPathComponent("removable.txt")) == "complete")
        #expect(try box.read(destination.appendingPathComponent("read-only/kept.txt")) == "read only")
        #expect(result.undo.contains { if case .trash(let url) = $0 { return url.standardizedFileURL.path == destination.standardizedFileURL.path }; return false })
    }

    @Test func cancellingCrossVolumeUndoPreservesOriginalAndCleansCopy() throws {
        let box = try Sandbox()
        let source = try box.folder("source")
        for index in 0..<10 { _ = try box.file("source/\(index).txt", String(repeating: "x", count: 1000)) }
        let destination = box.root.appendingPathComponent("destination")
        let engine = FileOperationEngine(renameItem: { _, _ in EXDEV })
        let cancellation = FileOperationCancellation()
        let result = engine.applyUndo([.relocate(source, destination)], cancellation: cancellation, progress: { if $0.completedBytes > 0 { cancellation.cancel() } })
        #expect(result.outcome.cancelled)
        #expect(!FileOperationEngine.exists(destination))
        #expect(try FileManager.default.contentsOfDirectory(atPath: source.path).count == 10)
        #expect(try FileManager.default.contentsOfDirectory(atPath: box.root.path) == ["source"])
    }

    @Test func sameVolumeMoveDoesNotRequireReadingChildren() async throws {
        let box = try Sandbox()
        let source = try box.folder("source/unreadable")
        _ = try box.file("source/unreadable/child/kept.txt", "retained")
        let unreadable = source.appendingPathComponent("child")
        let target = try box.folder("target")
        let destination = target.appendingPathComponent("unreadable")
        let progress = CapturedProgress()
        try FileManager.default.setAttributes([.posixPermissions: 0o000], ofItemAtPath: unreadable.path)
        defer {
            try? FileManager.default.setAttributes([.posixPermissions: 0o755], ofItemAtPath: unreadable.path)
            try? FileManager.default.setAttributes([.posixPermissions: 0o755], ofItemAtPath: destination.appendingPathComponent("child").path)
        }
        let result = await box.engine.transfer([source], to: target, mode: .move, progress: { progress.append($0) }, conflict: { _ in .init(choice: .stop) })
        #expect(result.outcome.succeeded)
        #expect(result.outcome.produced == [destination])
        #expect(progress.values.allSatisfy { $0.totalBytes == 0 })
    }

    @Test func sizeEstimateHonoursCancellationAndMissingEntries() throws {
        let box = try Sandbox()
        #expect(try FileOperationEngine.size(box.root.appendingPathComponent("vanished")) == 0)
        let cancellation = FileOperationCancellation()
        cancellation.cancel()
        #expect(throws: (any Error).self) { try FileOperationEngine.size(box.root, cancellation: cancellation) }
    }

    @Test func sameFolderCopyUsesLocalizedDuplicateNamesAndMoveDoesNothing() async throws {
        let box = try Sandbox()
        let old = Loc.language
        defer { Loc.language = old }
        for language in [AppLanguage.english, .korean] {
            Loc.language = language
            let parent = try box.folder(language.rawValue)
            let source = try box.file("\(language.rawValue)/한글.txt", "original")
            let copyBase = language == .english ? "한글 copy" : "한글 복사본"
            for suffix in ["", " 2"] {
                let result = await box.engine.transfer([source], to: parent, mode: .copy, conflict: { _ in .init(choice: .stop) })
                #expect(result.outcome.succeeded)
                #expect(result.outcome.produced.first?.lastPathComponent == copyBase + suffix + ".txt")
                #expect(try box.read(try #require(result.outcome.produced.first)) == "original")
            }
            let moved = await box.engine.transfer([source], to: parent, mode: .move, conflict: { _ in .init(choice: .replace) })
            #expect(moved.outcome.succeeded)
            #expect(moved.outcome.produced.isEmpty)
            #expect(moved.undo.isEmpty)
            #expect(try box.read(source) == "original")
        }
    }

    @Test func multiItemArchiveStagesBesideSourcesAndCleansUp() throws {
        let box = try Sandbox()
        let first = try box.file("source/a.txt", "first")
        let second = try box.file("source/b.txt", "second")
        let parent = first.deletingLastPathComponent()
        let observed = CapturedURLs()
        let result = try box.engine.compress([first, second], progress: { _ in
            let folders = (try? FileManager.default.contentsOfDirectory(at: parent, includingPropertiesForKeys: nil)) ?? []
            observed.append(folders.filter { $0.lastPathComponent.hasPrefix(".folder-splitter-archive-stage-") })
        })
        #expect(!observed.values.isEmpty)
        #expect(observed.values.allSatisfy { $0.deletingLastPathComponent().standardizedFileURL == parent.standardizedFileURL })
        #expect(observed.values.allSatisfy { !FileOperationEngine.exists($0) })
        let archive = try #require(result.outcome.produced.first)
        let extracted = try box.folder("extracted")
        let process = Process()
        process.executableURL = URL(fileURLWithPath: "/usr/bin/ditto")
        process.arguments = ["-x", "-k", archive.path, extracted.path]
        try process.run(); process.waitUntilExit()
        #expect(process.terminationStatus == 0)
        #expect(try box.read(extracted.appendingPathComponent("a.txt")) == "first")
        #expect(try box.read(extracted.appendingPathComponent("b.txt")) == "second")
        let cancellation = FileOperationCancellation()
        #expect(throws: (any Error).self) {
            try box.engine.compress([first, second], cancellation: cancellation, progress: { _ in cancellation.cancel() })
        }
        #expect(try FileManager.default.contentsOfDirectory(atPath: parent.path).sorted() == [archive.lastPathComponent, "a.txt", "b.txt"].sorted())
    }

    @Test func directCopyNeverDeletesExistingDestination() throws {
        let box = try Sandbox()
        let source = try box.file("source", "new")
        let destination = try box.file("destination", "old")
        #expect(throws: (any Error).self) { try box.engine.copy(source, to: destination, token: FileOperationCancellation(), base: 0, total: 3, progress: { _ in }) }
        #expect(try box.read(destination) == "old")
    }
}
