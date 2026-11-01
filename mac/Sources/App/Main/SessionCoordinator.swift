import AppKit

/// Loads the session once, owns the persistent stores, and writes everything back (debounced
/// while running, synchronously on quit).
@MainActor
final class SessionCoordinator {
    static var shared: SessionCoordinator!

    let url: URL
    let favorites: FavoritesStore
    let visits: FolderVisitLog
    let views: FolderViewMemoryStore
    let workspaces: WorkspaceStore
    let loadWarning: String?
    private(set) var restoredWindows: [WindowSnapshot]
    private var finderFavoritesImported: Bool
    /// Supplies the open windows' state at save time.
    var windowsProvider: (() -> [WindowSnapshot])?
    /// The last window the user closed, reopened by a Dock click or the global shortcut.
    var lastClosedWindow: WindowSnapshot?
    private var pendingSave: DispatchWorkItem?
    private let writer = DispatchQueue(label: "FolderSplitter.session-writer", qos: .utility)

    init(url: URL) {
        self.url = url
        let result = SessionStore.load(from: url)
        let state = result.state
        loadWarning = result.warning
        restoredWindows = state.windows
        finderFavoritesImported = state.finderFavoritesImported
        favorites = FavoritesStore(paths: state.favorites)
        visits = FolderVisitLog(snapshot: state.folderVisits)
        views = FolderViewMemoryStore(entries: state.folderViews)
        workspaces = WorkspaceStore(workspaces: state.workspaces)
        SettingsStore.shared.load(state.settings)

        if !finderFavoritesImported {
            seedFavorites()
            finderFavoritesImported = true
        }
        let save: () -> Void = { [weak self] in self?.scheduleSave() }
        favorites.onChange = save
        visits.onChange = save
        views.onChange = save
        workspaces.onChange = save
        SettingsStore.shared.onChange = { [weak self] _ in self?.scheduleSave() }
    }

    /// First launch: Finder's sidebar favorites if readable, else Finder's defaults.
    private func seedFavorites() {
        guard favorites.favorites.isEmpty else { return }
        var seeds = FinderFavorites.read()
        if seeds.isEmpty {
            seeds = [StandardFolder.applications, .desktop, .documents, .downloads, .home].compactMap(\.url)
        }
        for url in seeds where FileManager.default.fileExists(atPath: url.path) {
            favorites.add(url, at: nil)
        }
    }

    var state: SessionState {
        var windows = windowsProvider?() ?? []
        if windows.isEmpty, let last = lastClosedWindow { windows = [last] }
        return SessionState(settings: SettingsStore.shared.settings, windows: windows,
                            favorites: favorites.paths, finderFavoritesImported: finderFavoritesImported,
                            workspaces: workspaces.workspaces, folderVisits: visits.snapshot,
                            folderViews: views.entries)
    }

    func scheduleSave() {
        pendingSave?.cancel()
        let work = DispatchWorkItem { [weak self] in self?.saveInBackground() }
        pendingSave = work
        DispatchQueue.main.asyncAfter(deadline: .now() + 1.0, execute: work)
    }

    private func saveInBackground() {
        let snapshot = state
        let url = self.url
        writer.async {
            do { try SessionStore.save(snapshot, to: url) }
            catch { NSLog("Folder Splitter: could not save session: %@", error.localizedDescription) }
        }
    }

    /// Writes immediately and waits (used while quitting).
    func saveNow() {
        pendingSave?.cancel()
        let snapshot = state
        let url = self.url
        writer.sync {
            do { try SessionStore.save(snapshot, to: url) }
            catch { NSLog("Folder Splitter: could not save session: %@", error.localizedDescription) }
        }
    }
}
