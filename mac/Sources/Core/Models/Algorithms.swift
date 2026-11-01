import Foundation

struct NameFilter {
    private let words: [String]

    init(_ text: String) {
        words = PathText.nfc(text).lowercased().split(whereSeparator: \.isWhitespace).map(String.init)
    }

    var isEmpty: Bool { words.isEmpty }

    func matches(_ name: String) -> Bool {
        let normalized = PathText.nfc(name).lowercased()
        return words.allSatisfy { normalized.contains($0) }
    }
}

struct BulkRenameOptions: Equatable {
    var find = ""
    var replace = ""
    var useRegex = false
    var caseSensitive = false
    var startNumber = 1
}

struct BulkRenamePreview: Equatable {
    enum Problem: Equatable {
        case empty, invalidCharacter, duplicate, exists, invalidPattern(String)
    }

    let item: URL
    let oldName: String
    let newName: String
    let problem: Problem?
    var changes: Bool { oldName != newName }
}

enum BulkRenamePlanner {
    private struct Target: Hashable {
        let parent: String
        let name: String
    }

    private static let numbering = try! NSRegularExpression(pattern: #"\{n(?::([0-9]+))?\}"#)

    static func preview(_ items: [URL], options: BulkRenameOptions,
                        existingNames: Set<String>) -> [BulkRenamePreview] {
        let find = PathText.nfc(options.find)
        var expression: NSRegularExpression?
        var patternProblem: BulkRenamePreview.Problem?
        if options.useRegex && !find.isEmpty {
            do {
                expression = try NSRegularExpression(pattern: find,
                    options: options.caseSensitive ? [] : [.caseInsensitive])
            } catch {
                patternProblem = .invalidPattern(tr("The regular expression is invalid.", "정규 표현식이 올바르지 않습니다."))
            }
        }

        var oldTargets: [Target] = []
        var targets: [Target] = []
        var names: [(old: String, new: String)] = []
        var problems: [BulkRenamePreview.Problem?] = []
        oldTargets.reserveCapacity(items.count)
        targets.reserveCapacity(items.count)
        names.reserveCapacity(items.count)
        problems.reserveCapacity(items.count)

        for (index, item) in items.enumerated() {
            let oldName = PathText.nfc(item.lastPathComponent)
            let values = try? item.resourceValues(forKeys: [.isDirectoryKey, .isPackageKey])
            let directory = (values?.isDirectory ?? item.hasDirectoryPath) && values?.isPackage != true
            let ext = directory ? "" : (oldName as NSString).pathExtension
            let stem = ext.isEmpty ? oldName : (oldName as NSString).deletingPathExtension
            let replacement = expandNumber(PathText.nfc(options.replace), start: options.startNumber, offset: index)
            var renamed = stem
            if patternProblem == nil {
                if find.isEmpty {
                    if !replacement.isEmpty { renamed = replacement }
                } else if let expression {
                    renamed = expression.stringByReplacingMatches(in: stem,
                        range: NSRange(stem.startIndex..., in: stem), withTemplate: replacement)
                } else {
                    renamed = stem.replacingOccurrences(of: find, with: replacement,
                        options: options.caseSensitive ? [] : [.caseInsensitive])
                }
            }
            let newName = PathText.nfc(renamed + (ext.isEmpty ? "" : "." + ext))
            let parent = normalized(item.deletingLastPathComponent().standardizedFileURL.path)
            oldTargets.append(Target(parent: parent, name: normalized(oldName)))
            targets.append(Target(parent: parent, name: normalized(newName)))
            names.append((oldName, newName))
            let problem: BulkRenamePreview.Problem?
            if let patternProblem { problem = patternProblem }
            else if newName.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty || newName == "." || newName == ".." {
                problem = .empty
            } else if newName.contains("/") || newName.contains("\0") {
                problem = .invalidCharacter
            } else { problem = nil }
            problems.append(problem)
        }

        // Unchanged items still occupy their final names. Swaps and longer cycles are legal.
        var destinations: [Target: [Int]] = [:]
        for index in items.indices where problems[index] == nil {
            destinations[targets[index], default: []].append(index)
        }
        for indices in destinations.values where indices.count > 1 {
            for index in indices { problems[index] = .duplicate }
        }
        let occupied = Set(oldTargets)
        var vacated = Set(items.indices.compactMap { index -> Target? in
            problems[index] == nil && oldTargets[index] != targets[index] ? oldTargets[index] : nil
        })
        var blocked: [Target] = []
        for index in items.indices where problems[index] == nil && oldTargets[index] != targets[index] {
            let target = targets[index]
            if (existingNames.contains(target.name) || occupied.contains(target)) && !vacated.contains(target) {
                problems[index] = .exists
                if vacated.remove(oldTargets[index]) != nil { blocked.append(oldTargets[index]) }
            }
        }
        // If a move cannot happen, its source cannot be taken by another move either.
        var cursor = 0
        while cursor < blocked.count {
            let target = blocked[cursor]
            cursor += 1
            for index in destinations[target] ?? [] where problems[index] == nil && oldTargets[index] != target {
                problems[index] = .exists
                if vacated.remove(oldTargets[index]) != nil { blocked.append(oldTargets[index]) }
            }
        }
        return items.indices.map {
            BulkRenamePreview(item: items[$0], oldName: names[$0].old,
                              newName: names[$0].new, problem: problems[$0])
        }
    }

    private static func normalized(_ text: String) -> String {
        PathText.nfc(PathText.nfc(text).lowercased())
    }

    private static func expandNumber(_ replacement: String, start: Int, offset: Int) -> String {
        let matches = numbering.matches(in: replacement, range: NSRange(replacement.startIndex..., in: replacement))
        guard !matches.isEmpty else { return replacement }
        let sum = start.addingReportingOverflow(offset)
        let digits = sum.overflow ? String(UInt(start) + UInt(offset)) : String(sum.partialValue)
        let negative = digits.hasPrefix("-")
        let digitCount = digits.count - (negative ? 1 : 0)
        var result = replacement
        for match in matches.reversed() {
            guard let range = Range(match.range, in: result) else { continue }
            let width = Range(match.range(at: 1), in: replacement).map {
                min(9, max(1, Int(replacement[$0]) ?? 9))
            } ?? 0
            let padding = String(repeating: "0", count: max(0, width - digitCount))
            let value = negative ? "-" + padding + digits.dropFirst() : padding + digits
            result.replaceSubrange(range, with: value)
        }
        return result
    }
}

struct FolderComparisonResult: Equatable {
    var onlyLeft: [URL] = []
    var onlyRight: [URL] = []
    var leftNewer: [URL] = []
    var rightNewer: [URL] = []
    var leftSizeDiffers: [URL] = []
    var rightSizeDiffers: [URL] = []
}

enum FolderComparison {
    private struct Entry {
        let url: URL
        let directory: Bool
        let size: Int
        let modified: Date?
    }

    static func compare(left: URL, right: URL) throws -> FolderComparisonResult {
        let lhs = try entries(in: left)
        let rhs = try entries(in: right)
        var result = FolderComparisonResult()
        for key in Set(lhs.keys).union(rhs.keys).sorted() {
            let a = lhs[key] ?? []
            let b = rhs[key] ?? []
            for index in 0..<max(a.count, b.count) {
                guard index < a.count else { result.onlyRight.append(b[index].url); continue }
                guard index < b.count else { result.onlyLeft.append(a[index].url); continue }
                let l = a[index], r = b[index]
                if l.directory && r.directory { continue }
                if l.directory != r.directory {
                    result.leftSizeDiffers.append(l.url)
                    result.rightSizeDiffers.append(r.url)
                } else if let lm = l.modified, let rm = r.modified, abs(lm.timeIntervalSince(rm)) > 2 {
                    if lm > rm { result.leftNewer.append(l.url) }
                    else { result.rightNewer.append(r.url) }
                } else if l.size != r.size {
                    result.leftSizeDiffers.append(l.url)
                    result.rightSizeDiffers.append(r.url)
                }
            }
        }
        return result
    }

    private static func entries(in folder: URL) throws -> [String: [Entry]] {
        let keys: Set<URLResourceKey> = [.isDirectoryKey, .fileSizeKey, .contentModificationDateKey]
        let urls = try FileManager.default.contentsOfDirectory(at: folder, includingPropertiesForKeys: Array(keys))
        var result: [String: [Entry]] = [:]
        for url in urls.sorted(by: { $0.lastPathComponent.utf8.lexicographicallyPrecedes($1.lastPathComponent.utf8) }) {
            let values = try url.resourceValues(forKeys: keys)
            let name = PathText.nfc(PathText.nfc(url.lastPathComponent).lowercased())
            result[name, default: []].append(Entry(url: url, directory: values.isDirectory == true,
                size: values.fileSize ?? 0, modified: values.contentModificationDate))
        }
        return result
    }
}

enum FinderFavorites {
    static func read() -> [URL] {
        let directory = FileManager.default.homeDirectoryForCurrentUser
            .appendingPathComponent("Library/Application Support/com.apple.sharedfilelist", isDirectory: true)
        for ext in ["sfl3", "sfl2"] {
            let archive = directory.appendingPathComponent("com.apple.LSSharedFileList.FavoriteItems." + ext)
            guard let data = try? Data(contentsOf: archive), let items = archivedItems(data) else { continue }
            var result: [URL] = []
            var seen: Set<String> = []
            for item in items {
                guard let bookmark = item["Bookmark"] as? Data else { continue }
                var stale = false
                guard let url = try? URL(resolvingBookmarkData: bookmark, options: [.withoutUI, .withoutMounting],
                    relativeTo: nil, bookmarkDataIsStale: &stale), url.isFileURL,
                    let values = try? url.resourceValues(forKeys: [.isDirectoryKey]), values.isDirectory == true else { continue }
                let normalized = url.standardizedFileURL
                if seen.insert(PathText.nfc(normalized.path).lowercased()).inserted { result.append(normalized) }
            }
            return result
        }
        return []
    }

    private static func archivedItems(_ data: Data) -> [[String: Any]]? {
        do {
            let decoder = try NSKeyedUnarchiver(forReadingFrom: data)
            decoder.requiresSecureCoding = true
            decoder.decodingFailurePolicy = .setErrorAndReturn
            defer { decoder.finishDecoding() }
            let classes: [AnyClass] = [NSDictionary.self, NSArray.self, NSString.self, NSNumber.self,
                                       NSData.self, NSDate.self, NSURL.self, NSUUID.self]
            guard let root = decoder.decodeObject(of: classes, forKey: NSKeyedArchiveRootObjectKey) as? [String: Any],
                  decoder.error == nil else { return nil }
            return root["items"] as? [[String: Any]]
        } catch { return nil }
    }
}
