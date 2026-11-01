import Foundation
import Testing

struct AlgorithmsTests {
    @Test func filterRequiresEveryWordAndNormalizesKorean() {
        let korean = "한글 보고서".decomposedStringWithCanonicalMapping
        let filter = NameFilter("  보고서\tFINAL\n한글  ")
        #expect(!filter.isEmpty)
        #expect(filter.matches(korean + " FINAL.txt"))
        #expect(!filter.matches("한글 FINAL.txt"))
        #expect(!filter.matches("보고서 final.txt"))
        #expect(NameFilter(" \t\n").isEmpty)
        #expect(NameFilter("").matches("anything"))
        #expect(NameFilter("éCOLE").matches("E\u{301}cole.txt"))
    }

    @Test func replacementNumberingAndExtensions() throws {
        try withDirectory { root in
            let first = try file("Report.TXT", in: root)
            let second = try file("Other.tar.gz", in: root)
            var options = BulkRenameOptions()
            options.replace = "사진-{n:3}-{n}"
            options.startNumber = 8
            let plans = BulkRenamePlanner.preview([first, second], options: options,
                existingNames: ["report.txt", "other.tar.gz"])
            #expect(plans.map(\.newName) == ["사진-008-8.TXT", "사진-009-9.gz"])
            #expect(plans.allSatisfy { $0.problem == nil && $0.changes })
            #expect(plans.map(\.item) == [first, second])
            #expect(plans.map(\.oldName) == ["Report.TXT", "Other.tar.gz"])
            let unchanged = BulkRenamePlanner.preview([first], options: BulkRenameOptions(), existingNames: ["report.txt"])
            #expect(unchanged.first?.changes == false)
            #expect(unchanged.first?.problem == nil)
            options.startNumber = Int.max
            options.replace = "{n}"
            let largeNumbers = BulkRenamePlanner.preview([first, second], options: options, existingNames: [])
            #expect(largeNumbers.map(\.newName) == ["\(Int.max).TXT", "\(UInt(Int.max) + 1).gz"])
        }
    }

    @Test func numberingClampsWidthsAndPadsNegativeMagnitude() throws {
        try withDirectory { root in
            let item = try file("original.txt", in: root)
            var options = BulkRenameOptions()
            options.startNumber = -1
            options.replace = "{n:3}"
            #expect(BulkRenamePlanner.preview([item], options: options, existingNames: []).first?.newName == "-001.txt")
            options.startNumber = 1
            for pattern in ["{n:999999999999}", "{n:9999999999999999999999999999999999}"] {
                options.replace = pattern
                #expect(BulkRenamePlanner.preview([item], options: options, existingNames: []).first?.newName == "000000001.txt")
            }
            options.replace = "{n:0}"
            #expect(BulkRenamePlanner.preview([item], options: options, existingNames: []).first?.newName == "1.txt")
        }
    }

    @Test func directoryNameAndLiteralCaseSensitivity() throws {
        try withDirectory { root in
            let folder = root.appendingPathComponent("Report.folder", isDirectory: true)
            try FileManager.default.createDirectory(at: folder, withIntermediateDirectories: false)
            let document = try file("Report.txt", in: root)
            var options = BulkRenameOptions()
            options.find = "REPORT"
            options.replace = "New"
            var plans = BulkRenamePlanner.preview([folder, document], options: options, existingNames: [])
            #expect(plans.map(\.newName) == ["New.folder", "New.txt"])
            options.caseSensitive = true
            plans = BulkRenamePlanner.preview([folder, document], options: options, existingNames: [])
            #expect(plans.map(\.newName) == ["Report.folder", "Report.txt"])
            options.find = ""
            options.replace = "Whole"
            plans = BulkRenamePlanner.preview([folder, document], options: options, existingNames: [])
            #expect(plans.map(\.newName) == ["Whole", "Whole.txt"])
        }
    }

    @Test func regularExpressionCapturesNumberingAndErrors() throws {
        try withDirectory { root in
            let item = try file("draft-Report.txt", in: root)
            var options = BulkRenameOptions()
            options.find = "^draft-(.*)$"
            options.replace = "$1-{n:3}"
            options.useRegex = true
            let plans = BulkRenamePlanner.preview([item], options: options, existingNames: [])
            #expect(plans.first?.newName == "Report-001.txt")
            #expect(plans.first?.problem == nil)
            options.find = "["
            let invalid = BulkRenamePlanner.preview([item], options: options, existingNames: [])
            #expect(invalid.first?.newName == "draft-Report.txt")
            if case .invalidPattern(let message) = invalid.first?.problem {
                #expect(!message.isEmpty)
            } else {
                Issue.record("Expected an invalid regular expression problem")
            }
        }
    }

    @Test func conflictsIncludeUnchangedAndCanonicalNames() throws {
        try withDirectory { root in
            let first = try file("a.txt", in: root)
            let unchanged = try file("b.txt", in: root)
            var options = BulkRenameOptions()
            options.find = "a"
            options.replace = "b"
            let duplicate = BulkRenamePlanner.preview([first, unchanged], options: options, existingNames: ["a.txt", "b.txt"])
            #expect(duplicate.map(\.problem) == [.duplicate, .duplicate])
            options.find = ""
            options.replace = "한글".decomposedStringWithCanonicalMapping
            let exists = BulkRenamePlanner.preview([first], options: options, existingNames: ["한글.txt"])
            #expect(exists.first?.problem == .exists)
            #expect(exists.first?.newName == "한글.txt")
            let other = try file("c.txt", in: root)
            let sameTarget = BulkRenamePlanner.preview([first, other], options: options, existingNames: [])
            #expect(sameTarget.map(\.problem) == [.duplicate, .duplicate])
            options.replace = "A"
            let caseOnly = BulkRenamePlanner.preview([first], options: options, existingNames: ["a.txt"])
            #expect(caseOnly.first?.problem == nil)
            #expect(caseOnly.first?.newName == "A.txt")
        }
    }

    @Test func swapsAreAllowedAndBlockedMovesDoNotVacate() throws {
        try withDirectory { root in
            let first = try file("ab.txt", in: root)
            let second = try file("ba.txt", in: root)
            var options = BulkRenameOptions()
            options.find = "^(.)(.)$"
            options.replace = "$2$1"
            options.useRegex = true
            let swaps = BulkRenamePlanner.preview([first, second], options: options, existingNames: ["ab.txt", "ba.txt"])
            #expect(swaps.map(\.newName) == ["ba.txt", "ab.txt"])
            #expect(swaps.allSatisfy { $0.problem == nil })

            let a = try file("a.txt", in: root)
            let b = try file("b.txt", in: root)
            options.find = "^[ab]$"
            options.replace = "{n}"
            options.startNumber = 1
            let existing = BulkRenamePlanner.preview([a, b], options: options, existingNames: ["a.txt", "b.txt", "1.txt"])
            #expect(existing.map(\.problem) == [.exists, nil])

            let one = try file("1.txt", in: root)
            let two = try file("2.txt", in: root)
            options.find = "^[12]$"
            options.replace = "{n}"
            options.startNumber = 2
            let blocked = BulkRenamePlanner.preview([one, two], options: options, existingNames: ["1.txt", "2.txt", "3.txt"])
            #expect(blocked.map(\.problem) == [.exists, .exists])
        }
    }

    @Test func invalidNamesAndEmptyStem() throws {
        try withDirectory { root in
            let item = try file("plain", in: root)
            var options = BulkRenameOptions()
            options.find = "plain"
            let empty = BulkRenamePlanner.preview([item], options: options, existingNames: [])
            #expect(empty.first?.problem == .empty)
            for replacement in ["a/b", "a\0b"] {
                options.replace = replacement
                let invalid = BulkRenamePlanner.preview([item], options: options, existingNames: [])
                #expect(invalid.first?.problem == .invalidCharacter)
            }
            options.replace = "."
            #expect(BulkRenamePlanner.preview([item], options: options, existingNames: []).first?.problem == .empty)
            let dotfile = try file(".hidden", in: root)
            options.find = "hidden"
            options.replace = "visible"
            #expect(BulkRenamePlanner.preview([dotfile], options: options, existingNames: []).first?.newName == ".visible")
        }
    }

    @Test func bulkRenamePreservesPackageExtensionsAndColonNames() throws {
        try withDirectory { root in
            let package = root.appendingPathComponent("Lecture.rtfd", isDirectory: true)
            try FileManager.default.createDirectory(at: package, withIntermediateDirectories: false)
            let colon = try file("Meeting 3:15.txt", in: root)
            let unchanged = BulkRenamePlanner.preview([colon], options: BulkRenameOptions(), existingNames: ["meeting 3:15.txt"])
            #expect(unchanged.first?.problem == nil)
            #expect(unchanged.first?.changes == false)
            var options = BulkRenameOptions()
            options.replace = "Class {n}:notes"
            let plans = BulkRenamePlanner.preview([package, colon], options: options, existingNames: [])
            #expect(plans.map(\.newName) == ["Class 1:notes.rtfd", "Class 2:notes.txt"])
            #expect(plans.allSatisfy { $0.problem == nil })
        }
    }

    @Test func comparisonPresenceUnicodeAndDeterministicOrder() throws {
        try withComparisonFolders { left, right in
            let z = try file("z-only.txt", in: left)
            let a = try file("A-only.txt", in: left)
            let rightOnly = try file("right.txt", in: right)
            let instant = Date(timeIntervalSince1970: 1_700_000_000)
            _ = try file("한글.TXT".decomposedStringWithCanonicalMapping, in: left, modified: instant)
            _ = try file("한글.txt", in: right, modified: instant)
            let ldir = left.appendingPathComponent("Folder", isDirectory: true)
            let rdir = right.appendingPathComponent("folder", isDirectory: true)
            try FileManager.default.createDirectory(at: ldir, withIntermediateDirectories: false)
            try FileManager.default.createDirectory(at: rdir, withIntermediateDirectories: false)
            _ = try file("ignored-child", in: ldir)
            let result = try FolderComparison.compare(left: left, right: right)
            #expect(identities(result.onlyLeft) == identities([a, z]))
            #expect(identities(result.onlyRight) == identities([rightOnly]))
            #expect(result.leftNewer.isEmpty && result.rightNewer.isEmpty)
            #expect(result.leftSizeDiffers.isEmpty && result.rightSizeDiffers.isEmpty)
            #expect(try FolderComparison.compare(left: left, right: right) == result)
        }
    }

    @Test func comparisonTimeToleranceAndSize() throws {
        try withComparisonFolders { left, right in
            let instant = Date(timeIntervalSince1970: 1_700_000_000)
            let lnew = try file("left-newer", in: left, modified: instant.addingTimeInterval(3))
            _ = try file("left-newer", in: right, modified: instant)
            _ = try file("right-newer", in: left, modified: instant)
            let rnew = try file("right-newer", in: right, modified: instant.addingTimeInterval(3))
            _ = try file("tolerance", in: left, modified: instant.addingTimeInterval(2))
            _ = try file("tolerance", in: right, modified: instant)
            let lsize = try file("size", in: left, bytes: 2, modified: instant.addingTimeInterval(1))
            let rsize = try file("size", in: right, bytes: 3, modified: instant)
            let result = try FolderComparison.compare(left: left, right: right)
            #expect(identities(result.leftNewer) == identities([lnew]))
            #expect(identities(result.rightNewer) == identities([rnew]))
            #expect(identities(result.leftSizeDiffers) == identities([lsize]))
            #expect(identities(result.rightSizeDiffers) == identities([rsize]))
        }
    }

    @Test func comparisonDistinguishesFileAndFolderAndThrowsForMissingRoot() throws {
        try withComparisonFolders { left, right in
            let folder = left.appendingPathComponent("same", isDirectory: true)
            try FileManager.default.createDirectory(at: folder, withIntermediateDirectories: false)
            let document = try file("same", in: right)
            let result = try FolderComparison.compare(left: left, right: right)
            #expect(identities(result.leftSizeDiffers) == identities([folder]))
            #expect(identities(result.rightSizeDiffers) == identities([document]))
            #expect(throws: (any Error).self) {
                try FolderComparison.compare(left: left.appendingPathComponent("missing"), right: right)
            }
        }
    }

    private func identities(_ urls: [URL]) -> [String] {
        urls.map { $0.standardizedFileURL.resolvingSymlinksInPath().path }
    }

    private func withDirectory(_ body: (URL) throws -> Void) throws {
        let root = FileManager.default.temporaryDirectory.resolvingSymlinksInPath().appendingPathComponent(UUID().uuidString, isDirectory: true)
        try FileManager.default.createDirectory(at: root, withIntermediateDirectories: false)
        defer { try? FileManager.default.removeItem(at: root) }
        try body(root)
    }

    private func withComparisonFolders(_ body: (URL, URL) throws -> Void) throws {
        try withDirectory { root in
            let left = root.appendingPathComponent("left", isDirectory: true)
            let right = root.appendingPathComponent("right", isDirectory: true)
            try FileManager.default.createDirectory(at: left, withIntermediateDirectories: false)
            try FileManager.default.createDirectory(at: right, withIntermediateDirectories: false)
            try body(left, right)
        }
    }

    private func file(_ name: String, in folder: URL, bytes: Int = 1,
                      modified: Date? = nil) throws -> URL {
        let url = folder.appendingPathComponent(name)
        try Data(repeating: 65, count: bytes).write(to: url)
        if let modified {
            try FileManager.default.setAttributes([.modificationDate: modified], ofItemAtPath: url.path)
        }
        return url
    }
}
