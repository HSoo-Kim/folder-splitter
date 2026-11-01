import Foundation

enum AppearanceMode: String, Codable, CaseIterable, Sendable { case system, light, dark }

/// File list text size. Point sizes live in `Typography` (App).
enum TextSize: String, Codable, CaseIterable, Sendable { case small, medium, large }

enum RowDensity: String, Codable, CaseIterable, Sendable { case compact, comfortable }

/// Outline color of the active pane.
enum PaneHighlight: String, Codable, CaseIterable, Sendable { case accent, blue, green, orange }

enum GlobalHotKeyChoice: String, Codable, CaseIterable, Sendable {
    case off
    case controlOptionCommandF   // ⌃⌥⌘F
    case controlOptionCommandE   // ⌃⌥⌘E

    var displayName: String {
        switch self {
        case .off: return tr("Off", "끔")
        case .controlOptionCommandF: return "⌃⌥⌘F"
        case .controlOptionCommandE: return "⌃⌥⌘E"
        }
    }
}

enum ViewMode: String, Codable, CaseIterable, Sendable { case list, icons }

enum SortKey: String, Codable, CaseIterable, Sendable {
    case name, kind, modified, created, added, size

    var title: String {
        switch self {
        case .name: return tr("Name", "이름")
        case .kind: return tr("Kind", "종류")
        case .modified: return tr("Date Modified", "수정일")
        case .created: return tr("Date Created", "생성일")
        case .added: return tr("Date Added", "추가일")
        case .size: return tr("Size", "크기")
        }
    }
}

/// View state remembered per folder (like Finder's per-folder view options).
struct FolderViewSettings: Codable, Hashable, Sendable {
    var viewMode: ViewMode = .list
    var sortKey: SortKey = .name
    var ascending: Bool = true
    /// Icon view tile edge in points (48...256).
    var iconSize: Double = 72

    static let `default` = FolderViewSettings()

    init(viewMode: ViewMode = .list, sortKey: SortKey = .name, ascending: Bool = true, iconSize: Double = 72) {
        self.viewMode = viewMode
        self.sortKey = sortKey
        self.ascending = ascending
        self.iconSize = iconSize
    }

    init(from decoder: Decoder) throws {
        let c = try decoder.container(keyedBy: CodingKeys.self)
        viewMode = (try? c.decode(ViewMode.self, forKey: .viewMode)) ?? .list
        sortKey = (try? c.decode(SortKey.self, forKey: .sortKey)) ?? .name
        ascending = (try? c.decode(Bool.self, forKey: .ascending)) ?? true
        let size = (try? c.decode(Double.self, forKey: .iconSize)) ?? 72
        iconSize = size.isFinite ? min(max(size, 48), 256) : 72
    }
}

/// App-wide preferences. Decoding tolerates missing or invalid keys so older and newer
/// session files both load.
struct AppSettings: Codable, Equatable, Sendable {
    var language: AppLanguage = .system
    var appearance: AppearanceMode = .system
    var textSize: TextSize = .medium
    var rowDensity: RowDensity = .comfortable
    var paneHighlight: PaneHighlight = .accent
    var showHiddenFiles = false
    var showAllExtensions = true
    var foldersFirst = true
    var showSidebar = true
    var showFolderTree = true
    var showPreview = false
    var showPreviewDetails = true
    var showStatusBar = true
    var sidebarWidth: Double = 220
    var previewWidth: Double = 320
    var defaultView = FolderViewSettings.default
    var globalHotKey: GlobalHotKeyChoice = .controlOptionCommandF
    var restoreSessionOnLaunch = true
    /// Read GitHub release information at launch, hourly and when the app returns after a while.
    var checkForUpdates = true
    var searchIndexEnabled = true
    /// Extra folders whose contents are not indexed (absolute paths).
    var searchExcludedFolders: [String] = []
    var hasShownWelcome = false

    init() {}

    init(from decoder: Decoder) throws {
        let c = try decoder.container(keyedBy: CodingKeys.self)
        var s = AppSettings()
        func read<T: Decodable>(_ key: CodingKeys, _ apply: (T) -> Void) {
            if let value = try? c.decode(T.self, forKey: key) { apply(value) }
        }
        read(.language) { s.language = $0 }
        read(.appearance) { s.appearance = $0 }
        read(.textSize) { s.textSize = $0 }
        read(.rowDensity) { s.rowDensity = $0 }
        read(.paneHighlight) { s.paneHighlight = $0 }
        read(.showHiddenFiles) { s.showHiddenFiles = $0 }
        read(.showAllExtensions) { s.showAllExtensions = $0 }
        read(.foldersFirst) { s.foldersFirst = $0 }
        read(.showSidebar) { s.showSidebar = $0 }
        read(.showFolderTree) { s.showFolderTree = $0 }
        read(.showPreview) { s.showPreview = $0 }
        read(.showPreviewDetails) { s.showPreviewDetails = $0 }
        read(.showStatusBar) { s.showStatusBar = $0 }
        read(.sidebarWidth) { (v: Double) in if v.isFinite { s.sidebarWidth = min(max(v, 150), 480) } }
        read(.previewWidth) { (v: Double) in if v.isFinite { s.previewWidth = min(max(v, 200), 900) } }
        read(.defaultView) { s.defaultView = $0 }
        read(.globalHotKey) { s.globalHotKey = $0 }
        read(.restoreSessionOnLaunch) { s.restoreSessionOnLaunch = $0 }
        read(.checkForUpdates) { s.checkForUpdates = $0 }
        read(.searchIndexEnabled) { s.searchIndexEnabled = $0 }
        read(.searchExcludedFolders) { s.searchExcludedFolders = $0 }
        read(.hasShownWelcome) { s.hasShownWelcome = $0 }
        self = s
    }
}

extension Notification.Name {
    /// Posted by `SettingsStore` on the main thread. `userInfo["old"]` holds the previous `AppSettings`.
    static let settingsDidChange = Notification.Name("FolderSplitter.settingsDidChange")
}

/// The single live copy of `AppSettings`. Views read `SettingsStore.shared.settings` and observe
/// `.settingsDidChange`; only settings UI and menu toggles mutate it.
@MainActor
final class SettingsStore {
    static let shared = SettingsStore()

    private(set) var settings = AppSettings()
    /// Persistence hook, installed by the session coordinator.
    var onChange: ((AppSettings) -> Void)?

    /// Replaces the settings without notifying (used once while restoring the session).
    func load(_ settings: AppSettings) {
        self.settings = settings
        Loc.language = settings.language
    }

    func update(_ mutate: (inout AppSettings) -> Void) {
        var next = settings
        mutate(&next)
        guard next != settings else { return }
        let old = settings
        settings = next
        Loc.language = next.language
        onChange?(next)
        NotificationCenter.default.post(name: .settingsDidChange, object: self, userInfo: ["old": old])
    }
}
