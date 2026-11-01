import Foundation

// Persistence keys use a single canonical spelling while preserving user-facing capitalization.
enum ModelPath {
    static func clean(_ path: String) -> String? {
        guard path.hasPrefix("/"), !path.contains("\0"), path.count <= 4096 else { return nil }
        return PathText.nfc(URL(fileURLWithPath: path).standardizedFileURL.path)
    }
    static func key(_ path: String) -> String { PathText.nfc(path).lowercased() }
    static func unique(_ paths: [String], limit: Int) -> [String] {
        var seen = Set<String>()
        return Array(paths.compactMap { clean($0) }.filter { seen.insert(key($0)).inserted }.prefix(limit))
    }
}

extension Notification.Name {
    static let folderViewMemoryDidChange = Notification.Name("FolderSplitter.folderViewMemoryDidChange")
    static let workspacesDidChange = Notification.Name("FolderSplitter.workspacesDidChange")
}

@MainActor
final class FavoritesStore: FavoritesProviding {
    private(set) var paths: [String]
    var onChange: (() -> Void)?
    init(paths: [String]) { self.paths = ModelPath.unique(paths, limit: 100) }
    var favorites: [URL] { paths.map { URL(fileURLWithPath: $0) } }
    func contains(_ url: URL) -> Bool {
        guard let path = ModelPath.clean(url.path) else { return false }
        return paths.contains { ModelPath.key($0) == ModelPath.key(path) }
    }
    func add(_ url: URL, at index: Int?) {
        guard url.isFileURL, let path = ModelPath.clean(url.path), !contains(url), paths.count < 100 else { return }
        paths.insert(path, at: min(max(index ?? paths.count, 0), paths.count)); changed()
    }
    func remove(_ url: URL) {
        guard let index = paths.firstIndex(where: { ModelPath.key($0) == ModelPath.key(url.standardizedFileURL.path) }) else { return }
        paths.remove(at: index); changed()
    }
    func move(from source: Int, to destination: Int) {
        guard paths.indices.contains(source) else { return }
        let target = min(max(destination, 0), paths.count - 1)
        guard source != target else { return }
        paths.insert(paths.remove(at: source), at: target); changed()
    }
    private func changed() { onChange?(); NotificationCenter.default.post(name: .favoritesDidChange, object: self) }
}

@MainActor
final class FolderVisitLog: FolderHistoryProviding {
    struct Entry: Codable, Equatable {
        var path: String
        var count: Int
        var lastVisited: Date
        init(path: String, count: Int, lastVisited: Date = .distantPast) {
            self.path = path; self.count = count; self.lastVisited = lastVisited
        }
        init(from decoder: Decoder) throws {
            let c = try decoder.container(keyedBy: CodingKeys.self)
            path = (try? c.decode(String.self, forKey: .path)) ?? ""
            count = (try? c.decode(Int.self, forKey: .count)) ?? 0
            lastVisited = (try? c.decode(Date.self, forKey: .lastVisited)) ?? .distantPast
        }
    }
    struct Snapshot: Codable, Equatable {
        var entries: [Entry] = []
        init(entries: [Entry] = []) { self.entries = entries }
        init(from decoder: Decoder) throws {
            let c = try decoder.container(keyedBy: CodingKeys.self)
            entries = (try? c.decode([Entry].self, forKey: .entries)) ?? []
        }
        func normalized() -> Snapshot {
            var result: [Entry] = []
            for entry in entries {
                guard let path = ModelPath.clean(entry.path), path != "/", entry.count > 0 else { continue }
                if let i = result.firstIndex(where: { ModelPath.key($0.path) == ModelPath.key(path) }) {
                    result[i].count = max(result[i].count, min(entry.count, 100_000))
                } else if result.count < 100 {
                    result.append(Entry(path: path, count: min(entry.count, 100_000), lastVisited: entry.lastVisited))
                }
            }
            return Snapshot(entries: result)
        }
    }
    private var value: Snapshot
    private let now: () -> Date
    var onChange: (() -> Void)?
    init(snapshot: Snapshot, now: @escaping () -> Date = Date.init) { value = snapshot.normalized(); self.now = now }
    var snapshot: Snapshot { value }
    var recentFolders: [URL] { value.entries.prefix(20).map { URL(fileURLWithPath: $0.path) } }
    var frequentFolders: [String] {
        value.entries.enumerated().filter { $0.element.count >= 3 }.sorted {
            $0.element.count == $1.element.count ? $0.offset < $1.offset : $0.element.count > $1.element.count
        }.prefix(24).map { $0.element.path }
    }
    func recordVisit(_ folder: URL) {
        guard folder.isFileURL, let path = ModelPath.clean(folder.path), path != "/" else { return }
        var count = 1
        if let i = value.entries.firstIndex(where: { ModelPath.key($0.path) == ModelPath.key(path) }) {
            count = min(value.entries.remove(at: i).count + 1, 100_000)
        }
        value.entries.insert(Entry(path: path, count: count, lastVisited: now()), at: 0)
        if value.entries.reduce(0, { $0 + $1.count }) > 1000 {
            for i in value.entries.indices.reversed() {
                value.entries[i].count /= 2
                if value.entries[i].count == 0 && i > 0 { value.entries.remove(at: i) }
            }
            value.entries[0].count = max(value.entries[0].count, 1)
        }
        while value.entries.count > 100 {
            var drop = 1
            for i in 2..<value.entries.count where value.entries[i].count <= value.entries[drop].count { drop = i }
            value.entries.remove(at: drop)
        }
        changed()
    }
    func forgetAll() { guard !value.entries.isEmpty else { return }; value.entries.removeAll(); changed() }
    private func changed() { onChange?(); NotificationCenter.default.post(name: .folderHistoryDidChange, object: self) }
}

@MainActor
final class FolderViewMemoryStore: FolderViewMemory {
    private(set) var entries: [String: FolderViewSettings] = [:]
    private var order: [String] = [] // Least recently used first; lookups refresh recency.
    var onChange: (() -> Void)?
    init(entries: [String: FolderViewSettings]) {
        for key in entries.keys.sorted().prefix(500) {
            guard let path = ModelPath.clean(key) else { continue }
            self.entries[path] = entries[key]; order.append(path)
        }
    }
    func viewSettings(for folder: URL) -> FolderViewSettings? {
        guard let path = ModelPath.clean(folder.path), let settings = entries[path] else { return nil }
        touch(path); return settings
    }
    func setViewSettings(_ settings: FolderViewSettings, for folder: URL) {
        guard folder.isFileURL, let path = ModelPath.clean(folder.path) else { return }
        let changed = entries[path] != settings
        entries[path] = settings; touch(path)
        while order.count > 500 { entries.removeValue(forKey: order.removeFirst()) }
        if changed { onChange?(); NotificationCenter.default.post(name: .folderViewMemoryDidChange, object: self) }
    }
    private func touch(_ path: String) { order.removeAll { $0 == path }; order.append(path) }
}

@MainActor
final class WorkspaceStore {
    private(set) var workspaces: [Workspace]
    var onChange: (() -> Void)?
    init(workspaces: [Workspace]) { self.workspaces = SessionValidation.workspaces(workspaces) }
    func save(_ w: Workspace) {
        guard !w.name.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty else { return }
        let normalized = SessionValidation.workspace(w)
        if let i = index(normalized.name) { workspaces[i] = normalized }
        else if workspaces.count < 20 { workspaces.append(normalized) }
        else { return }
        changed()
    }
    func delete(named name: String) { guard let i = index(name) else { return }; workspaces.remove(at: i); changed() }
    func rename(_ old: String, to new: String) throws {
        let name = PathText.nfc(new.trimmingCharacters(in: .whitespacesAndNewlines))
        guard !name.isEmpty else { throw WorkspaceError.emptyName }
        guard let i = index(old) else { throw WorkspaceError.notFound }
        if let other = index(name), other != i { throw WorkspaceError.duplicateName }
        workspaces[i].name = name; changed()
    }
    private func index(_ name: String) -> Int? { workspaces.firstIndex { ModelPath.key($0.name) == ModelPath.key(name.trimmingCharacters(in: .whitespacesAndNewlines)) } }
    private func changed() { onChange?(); NotificationCenter.default.post(name: .workspacesDidChange, object: self) }
    enum WorkspaceError: LocalizedError {
        case emptyName, duplicateName, notFound
        var errorDescription: String? {
            switch self {
            case .emptyName: return tr("Type a workspace name.", "작업 구성 이름을 입력하십시오.")
            case .duplicateName: return tr("A workspace with this name already exists.", "이 이름의 작업 구성이 이미 있습니다.")
            case .notFound: return tr("This workspace is no longer available.", "이 작업 구성을 더 이상 사용할 수 없습니다.")
            }
        }
    }
}

enum ClosedItem: Equatable {
    case tab(paneID: UUID, index: Int, tab: TabSnapshot)
    case pane(pane: PaneSnapshot, layoutBefore: PaneLayoutTree, layoutAfter: PaneLayoutTree)
}
struct CloseHistory {
    private var items: [ClosedItem] = []
    mutating func push(_ item: ClosedItem) { items.append(item); if items.count > 50 { items.removeFirst() } }
    mutating func popLatest() -> ClosedItem? { items.popLast() }
    mutating func popLatest(tabIn pane: UUID) -> ClosedItem? {
        guard let i = items.lastIndex(where: { if case .tab(let id, _, _) = $0 { return id == pane }; return false }) else { return nil }
        return items.remove(at: i)
    }
    func hasTabs(in pane: UUID) -> Bool { items.contains { if case .tab(let id, _, _) = $0 { return id == pane }; return false } }
    var isEmpty: Bool { items.isEmpty }
    mutating func discardPaneRecords() { items.removeAll { if case .pane = $0 { return true }; return false } }
}
