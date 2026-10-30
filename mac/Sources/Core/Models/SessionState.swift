import Foundation

struct TabSnapshot: Codable, Equatable {
    var id: UUID = UUID()
    var path: String? = nil
    var back: [String] = []
    var forward: [String] = []
}
struct PaneSnapshot: Codable, Equatable {
    var id: UUID = UUID()
    var tabs: [TabSnapshot] = []
    var selectedTab: Int = 0
}
struct WindowSnapshot: Codable, Equatable {
    var frame: [Double]? = nil
    var layout: PaneLayoutTree = .pane(UUID())
    var panes: [PaneSnapshot] = []
    var activePane: UUID? = nil
    var isFullScreen: Bool = false
}
struct Workspace: Codable, Equatable {
    var name: String = ""
    var layout: PaneLayoutTree = .pane(UUID())
    var panes: [PaneSnapshot] = []
    var activePane: UUID? = nil
    var savedAt: Date = Date()
}
struct SessionState: Codable, Equatable {
    var settings: AppSettings = AppSettings()
    var windows: [WindowSnapshot] = []
    var favorites: [String] = []
    var finderFavoritesImported: Bool = false
    var workspaces: [Workspace] = []
    var folderVisits: FolderVisitLog.Snapshot = .init()
    var folderViews: [String: FolderViewSettings] = [:]
}
struct SessionLoadResult { var state: SessionState; var warning: String? }

private extension KeyedDecodingContainer {
    func value<T: Decodable>(_ key: Key, _ fallback: @autoclosure () -> T) -> T { (try? decode(T.self, forKey: key)) ?? fallback() }
}
extension TabSnapshot {
    init(from decoder: Decoder) throws {
        let c = try decoder.container(keyedBy: CodingKeys.self)
        id = c.value(.id, UUID()); path = try? c.decode(String.self, forKey: .path)
        back = c.value(.back, []); forward = c.value(.forward, [])
    }
}
extension PaneSnapshot {
    init(from decoder: Decoder) throws {
        let c = try decoder.container(keyedBy: CodingKeys.self)
        id = c.value(.id, UUID()); tabs = c.value(.tabs, []); selectedTab = c.value(.selectedTab, 0)
    }
}
extension WindowSnapshot {
    init(from decoder: Decoder) throws {
        let c = try decoder.container(keyedBy: CodingKeys.self)
        frame = try? c.decode([Double].self, forKey: .frame); layout = c.value(.layout, .pane(UUID()))
        panes = c.value(.panes, []); activePane = try? c.decode(UUID.self, forKey: .activePane)
        isFullScreen = c.value(.isFullScreen, false)
    }
}
extension Workspace {
    init(from decoder: Decoder) throws {
        let c = try decoder.container(keyedBy: CodingKeys.self)
        name = c.value(.name, ""); layout = c.value(.layout, .pane(UUID())); panes = c.value(.panes, [])
        activePane = try? c.decode(UUID.self, forKey: .activePane); savedAt = c.value(.savedAt, Date.distantPast)
    }
}
extension SessionState {
    init(from decoder: Decoder) throws {
        let c = try decoder.container(keyedBy: CodingKeys.self)
        settings = c.value(.settings, AppSettings()); windows = c.value(.windows, [])
        favorites = c.value(.favorites, []); finderFavoritesImported = c.value(.finderFavoritesImported, false)
        workspaces = c.value(.workspaces, []); folderVisits = c.value(.folderVisits, .init()); folderViews = c.value(.folderViews, [:])
    }
}

enum SessionValidation {
    static func panes(_ source: [PaneSnapshot], usedPanes: inout Set<UUID>, usedTabs: inout Set<UUID>) -> [PaneSnapshot] {
        var result: [PaneSnapshot] = []
        for var pane in source {
            guard usedPanes.insert(pane.id).inserted else { continue }
            pane.tabs = Array(pane.tabs.prefix(32)).map { tab in
                var tab = tab
                if !usedTabs.insert(tab.id).inserted { tab.id = UUID(); usedTabs.insert(tab.id) }
                tab.path = tab.path.flatMap(ModelPath.clean)
                tab.back = Array(tab.back.compactMap(ModelPath.clean).suffix(50))
                tab.forward = Array(tab.forward.compactMap(ModelPath.clean).suffix(50))
                return tab
            }
            pane.selectedTab = pane.tabs.isEmpty ? -1 : min(max(pane.selectedTab, 0), pane.tabs.count - 1)
            result.append(pane)
            if result.count == 4 { break }
        }
        if result.isEmpty {
            let tab = TabSnapshot(path: PathText.home.path)
            let pane = PaneSnapshot(tabs: [tab])
            usedPanes.insert(pane.id); usedTabs.insert(tab.id); result.append(pane)
        }
        return result
    }
    static func window(_ source: WindowSnapshot, usedPanes: inout Set<UUID>, usedTabs: inout Set<UUID>) -> WindowSnapshot {
        var w = source
        w.panes = panes(w.panes, usedPanes: &usedPanes, usedTabs: &usedTabs)
        // Panes absent from the visible tree remain available with all of their tabs.
        w.layout = w.layout.normalized(keeping: Set(w.panes.map(\.id))) ?? .pane(w.panes[0].id)
        if !w.layout.panes.contains(w.activePane ?? UUID()) { w.activePane = w.layout.panes[0] }
        if let frame = w.frame {
            if frame.count == 4 && frame.allSatisfy(\.isFinite) {
                w.frame = [frame[0], frame[1], min(max(frame[2], 640), 10000), min(max(frame[3], 400), 10000)]
            } else { w.frame = nil }
        }
        return w
    }
    static func workspace(_ source: Workspace) -> Workspace {
        var usedPanes = Set<UUID>(), usedTabs = Set<UUID>()
        let w = window(WindowSnapshot(layout: source.layout, panes: source.panes, activePane: source.activePane), usedPanes: &usedPanes, usedTabs: &usedTabs)
        return Workspace(name: PathText.nfc(source.name.trimmingCharacters(in: .whitespacesAndNewlines)), layout: w.layout, panes: w.panes, activePane: w.activePane, savedAt: source.savedAt)
    }
    static func workspaces(_ source: [Workspace]) -> [Workspace] {
        var names = Set<String>()
        return Array(source.filter {
            let name = $0.name.trimmingCharacters(in: .whitespacesAndNewlines)
            return !name.isEmpty && names.insert(ModelPath.key(name)).inserted
        }.prefix(20).map(workspace))
    }
    static func state(_ source: SessionState) -> SessionState {
        var state = source
        var usedPanes = Set<UUID>(), usedTabs = Set<UUID>()
        state.windows = (state.windows.isEmpty ? [WindowSnapshot()] : state.windows).map {
            window($0, usedPanes: &usedPanes, usedTabs: &usedTabs)
        }
        state.favorites = ModelPath.unique(state.favorites, limit: 100)
        state.workspaces = workspaces(state.workspaces)
        state.folderVisits = state.folderVisits.normalized()
        var views: [String: FolderViewSettings] = [:]
        for key in state.folderViews.keys.sorted() {
            guard let path = ModelPath.clean(key) else { continue }
            views[path] = state.folderViews[key]
            if views.count == 500 { break }
        }
        state.folderViews = views
        return state
    }
}

enum SessionStore {
    static var defaultURL: URL {
        PathText.home.appendingPathComponent("Library/Application Support/Folder Splitter/session.json")
    }
    static func load(from url: URL) -> SessionLoadResult {
        let decoder = JSONDecoder()
        do {
            let state = try decoder.decode(SessionState.self, from: Data(contentsOf: url))
            return SessionLoadResult(state: SessionValidation.state(state), warning: nil)
        } catch {
            let backup = url.appendingPathExtension("bak")
            if let data = try? Data(contentsOf: backup), let state = try? decoder.decode(SessionState.self, from: data) {
                return SessionLoadResult(state: SessionValidation.state(state), warning: tr("The session file could not be read. The previous session was restored from its backup.", "세션 파일을 읽을 수 없어 백업에서 이전 세션을 복원했습니다."))
            }
            let missing = (error as NSError).domain == NSCocoaErrorDomain && (error as NSError).code == NSFileReadNoSuchFileError
            return SessionLoadResult(state: SessionValidation.state(SessionState()), warning: missing ? nil : tr("The session file could not be read. A new session has been opened.", "세션 파일을 읽을 수 없어 새로운 세션을 열었습니다."))
        }
    }
    static func save(_ state: SessionState, to url: URL) throws {
        let manager = FileManager.default
        try manager.createDirectory(at: url.deletingLastPathComponent(), withIntermediateDirectories: true)
        let encoder = JSONEncoder(); encoder.outputFormatting = [.prettyPrinted, .sortedKeys]
        let data = try encoder.encode(SessionValidation.state(state))
        // Never replace a useful backup with a corrupt primary file.
        if let previous = try? Data(contentsOf: url), (try? JSONDecoder().decode(SessionState.self, from: previous)) != nil {
            try previous.write(to: url.appendingPathExtension("bak"), options: .atomic)
        }
        try data.write(to: url, options: .atomic)
    }
}
