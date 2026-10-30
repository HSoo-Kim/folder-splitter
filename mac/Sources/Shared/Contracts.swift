import Foundation

// Cross-slice contracts. Foundation-only so Core and its tests compile without AppKit.
// Concrete implementations live in Core (models, search, file operations) and are wired
// together by App/Main.

// MARK: - Files

/// One row of a folder listing. Names are NFC so Korean text compares and filters correctly
/// regardless of how the file system stored it.
struct FileItem: Hashable, Sendable, Identifiable {
    var id: URL { url }
    let url: URL
    let name: String
    /// True for folders and volumes. Packages (.app, .rtfd …) are files from the UI's point of view.
    let isDirectory: Bool
    let isPackage: Bool
    let isHidden: Bool
    let isSymbolicLink: Bool
    let isAlias: Bool
    /// Logical size in bytes; nil for folders.
    let size: Int64?
    let modified: Date?
    let created: Date?
    let added: Date?
    /// Localized kind, e.g. "PDF document" / "PDF 문서".
    let kind: String
    let typeIdentifier: String?
    let tags: [String]

    /// Name with the extension removed when extensions are hidden (folders keep their name).
    func displayName(showExtension: Bool) -> String {
        guard !showExtension, !isDirectory else { return name }
        let ext = (name as NSString).pathExtension
        guard !ext.isEmpty, name.count > ext.count + 1 else { return name }
        return (name as NSString).deletingPathExtension
    }
}

// MARK: - Search

struct SearchHit: Hashable, Sendable {
    let path: String
    let isDirectory: Bool
    var url: URL { URL(fileURLWithPath: path, isDirectory: isDirectory) }
    var name: String { (path as NSString).lastPathComponent }
}

enum SearchIndexState: Equatable, Sendable {
    case disabled
    case loading
    case building(scanned: Int)
    case ready(entries: Int)
    case failed(String)

    var isSearchable: Bool {
        if case .ready = self { return true }
        return false
    }
}

extension Notification.Name {
    /// Posted on the main thread whenever `PathSearching.state` changes or the index content changes.
    static let searchIndexDidChange = Notification.Name("FolderSplitter.searchIndexDidChange")
}

protocol PathSearching: AnyObject, Sendable {
    var state: SearchIndexState { get }
    /// File-name fragment search. Runs off the main thread and honours task cancellation.
    /// `priorityRoots` (current folder, open tabs, favorites, frequent folders) rank first.
    func search(_ text: String, priorityRoots: [String], limit: Int) async -> [SearchHit]
}

// MARK: - Layout

enum SplitEdge: String, Codable, CaseIterable, Sendable { case left, right, top, bottom }

// MARK: - File operations

enum TransferMode: Sendable { case copy, move }

struct FileOperationFailure: Hashable, Sendable {
    let url: URL
    let message: String
}

struct FileOperationOutcome: Sendable {
    /// Items that now exist because of the operation (copies, moved items, new archive …).
    var produced: [URL] = []
    var skipped: [URL] = []
    var failures: [FileOperationFailure] = []
    var cancelled = false

    var succeeded: Bool { failures.isEmpty && !cancelled }
}

struct FileRename: Hashable, Sendable {
    let item: URL
    let newName: String
}

/// Everything that changes files on disk goes through this, so progress, name conflicts and
/// undo behave the same from the list, the sidebar, drag and drop and menus.
@MainActor
protocol FileOperating: AnyObject {
    /// App-wide undo stack for file operations (Finder-style: Undo Move, Undo Trash …).
    var undoManager: UndoManager { get }
    /// Copies or moves items into `folder`, asking about name conflicts and showing progress.
    func transfer(_ items: [URL], to folder: URL, mode: TransferMode) async -> FileOperationOutcome
    func moveToTrash(_ items: [URL]) async -> FileOperationOutcome
    /// Throws a localized error for empty names, "/" or ":", or an existing item.
    func rename(_ item: URL, to newName: String) throws -> URL
    /// All-or-nothing; one undo reverts the batch. Handles swaps (a→b, b→a).
    func rename(_ renames: [FileRename]) throws -> [URL]
    /// Creates "untitled folder" / "무제 폴더" (numbered if taken) and returns it.
    func newFolder(in folder: URL) throws -> URL
    /// Creates a new folder in the items' parent and moves the items into it.
    func newFolder(containing items: [URL]) async -> FileOperationOutcome
    func duplicate(_ items: [URL]) async -> FileOperationOutcome
    /// Finder-compatible zip ("Archive.zip" for several items, "<name>.zip" for one).
    func compress(_ items: [URL]) async -> FileOperationOutcome
    func makeAlias(_ items: [URL]) -> FileOperationOutcome
}

// MARK: - Persistent stores used by UI slices

extension Notification.Name {
    static let favoritesDidChange = Notification.Name("FolderSplitter.favoritesDidChange")
    static let folderHistoryDidChange = Notification.Name("FolderSplitter.folderHistoryDidChange")
}

@MainActor
protocol FavoritesProviding: AnyObject {
    var favorites: [URL] { get }
    func contains(_ url: URL) -> Bool
    /// Appends when `index` is nil. Ignores duplicates; keeps at most 100.
    func add(_ url: URL, at index: Int?)
    func remove(_ url: URL)
    func move(from source: Int, to destination: Int)
}

@MainActor
protocol FolderHistoryProviding: AnyObject {
    func recordVisit(_ folder: URL)
    /// Most recent first, at most 20.
    var recentFolders: [URL] { get }
    /// Paths opened three or more times (decaying counts), used to rank search results.
    var frequentFolders: [String] { get }
    func forgetAll()
}

@MainActor
protocol FolderViewMemory: AnyObject {
    func viewSettings(for folder: URL) -> FolderViewSettings?
    func setViewSettings(_ settings: FolderViewSettings, for folder: URL)
}
