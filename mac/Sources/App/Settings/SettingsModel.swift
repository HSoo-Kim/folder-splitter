import AppKit
import Combine
import SwiftUI

@MainActor
enum SettingsHooks {
    static var rebuildSearchIndex: (() -> Void)?
    static var searchDeniedPaths: (() -> [String])?
    static var searchDefaultExclusions: [String] = []
}

/// Bridges the existing notification-based settings contract into SwiftUI. This is not
/// another settings store: every edit still goes through SettingsStore.update.
@MainActor
final class SettingsModel: ObservableObject {
    @Published private(set) var settings = SettingsStore.shared.settings
    @Published var selectedTab: SettingsTab = .general
    @Published private(set) var indexState: SearchIndexState = .loading
    @Published private(set) var deniedPaths: [String] = []
    @Published private(set) var defaultExclusions: [String] = []
    @Published private(set) var fullDiskAccess: Bool?
    @Published private(set) var loginEnabled = LoginItem.isEnabled
    @Published private(set) var loginNeedsApproval = LoginItem.needsApproval
    @Published private(set) var defaultViewer = DefaultFileViewer.isFolderSplitterDefault
    @Published var errorMessage: String?
    let initialLanguage: AppLanguage
    private var observers: Set<AnyCancellable> = []

    init() {
        initialLanguage = SettingsStore.shared.settings.language
        NotificationCenter.default.publisher(for: .settingsDidChange)
            .receive(on: RunLoop.main)
            .sink { [weak self] _ in self?.settings = SettingsStore.shared.settings }
            .store(in: &observers)
        NotificationCenter.default.publisher(for: .searchIndexDidChange)
            .receive(on: RunLoop.main)
            .sink { [weak self] _ in self?.refreshSearch() }
            .store(in: &observers)
        NSWorkspace.shared.notificationCenter.publisher(for: NSWorkspace.didActivateApplicationNotification)
            .receive(on: RunLoop.main)
            .sink { [weak self] _ in self?.refreshSystemState() }
            .store(in: &observers)
        refreshSearch()
        refreshSystemState()
    }

    func binding<Value>(_ keyPath: WritableKeyPath<AppSettings, Value>) -> Binding<Value> {
        Binding(get: { self.settings[keyPath: keyPath] }, set: { value in
            SettingsStore.shared.update { $0[keyPath: keyPath] = value }
        })
    }

    func refreshSearch() {
        if let services = AppServices.shared { indexState = services.search.state }
        deniedPaths = SettingsHooks.searchDeniedPaths?() ?? []
        defaultExclusions = SettingsHooks.searchDefaultExclusions
    }

    func refreshSystemState() {
        loginEnabled = LoginItem.isEnabled
        loginNeedsApproval = LoginItem.needsApproval
        defaultViewer = DefaultFileViewer.isFolderSplitterDefault
        Task { [weak self] in
            let granted = await Task.detached(priority: .utility) { FullDiskAccess.isGranted }.value
            self?.fullDiskAccess = granted
        }
    }

    func setLoginEnabled(_ enabled: Bool) {
        do { try LoginItem.setEnabled(enabled) }
        catch {
            errorMessage = tr("Couldn’t change Open at Login. ", "로그인 시 열기를 변경할 수 없습니다. ") + error.localizedDescription
        }
        refreshSystemState()
    }

    func setDefaultViewer(_ enabled: Bool) {
        DefaultFileViewer.setEnabled(enabled)
        defaultViewer = DefaultFileViewer.isFolderSplitterDefault
        errorMessage = DefaultFileViewer.lastError
    }

    func addExcludedFolder() {
        let panel = NSOpenPanel()
        panel.title = tr("Exclude Folders from Search", "검색에서 폴더 제외")
        panel.prompt = tr("Exclude", "제외")
        panel.canChooseDirectories = true
        panel.canChooseFiles = false
        panel.allowsMultipleSelection = true
        panel.canCreateDirectories = false
        let finish: (NSApplication.ModalResponse) -> Void = { response in
            guard response == .OK else { return }
            let paths = panel.urls.map { $0.standardizedFileURL.path.precomposedStringWithCanonicalMapping }
            SettingsStore.shared.update { settings in
                for path in paths where !settings.searchExcludedFolders.contains(path) {
                    settings.searchExcludedFolders.append(path)
                }
            }
        }
        if let window = SettingsWindowController.shared.window {
            panel.beginSheetModal(for: window, completionHandler: finish)
        } else { panel.begin(completionHandler: finish) }
    }

    func removeExcludedFolder(_ path: String) {
        SettingsStore.shared.update { $0.searchExcludedFolders.removeAll { $0 == path } }
    }
}

enum SettingsTab: String, CaseIterable {
    case general, appearance, search
    var title: String {
        switch self {
        case .general: return tr("General", "일반")
        case .appearance: return tr("Appearance", "모양")
        case .search: return tr("Search", "검색")
        }
    }
    var symbol: String {
        switch self {
        case .general: return "gearshape"
        case .appearance: return "paintbrush"
        case .search: return "magnifyingglass"
        }
    }
}
