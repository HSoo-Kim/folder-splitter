import AppKit

/// Where a folder opened from a list, sidebar, search result or menu should go.
enum OpenDisposition: Equatable {
    /// Replace the current tab's folder (adds to its back history).
    case current
    /// New tab in the active pane. `activate: false` = open in the background (⌘-click).
    case newTab(activate: Bool)
    /// The pane used just before the active one; created (split right) if only one pane shows.
    case otherPane
    /// Split the active pane on that edge and open there.
    case newPane(SplitEdge)
    case newWindow
}

@MainActor
protocol NavigationHandling: AnyObject {
    /// Folder or volume: show it. File, package or app: show its parent folder with it selected.
    func open(_ url: URL, disposition: OpenDisposition)
}

/// Implemented by the pane that owns a `FileListViewController`.
@MainActor
protocol FileListHost: NavigationHandling {
    /// The user clicked or typed in the list; its pane becomes the active pane.
    func fileListDidActivate(_ list: NSViewController)
    func fileListSelectionDidChange(_ list: NSViewController)
    /// The listing loaded, reloaded, changed on disk or failed. Read state from the list.
    func fileListContentDidChange(_ list: NSViewController)
    /// Folder shown by the other pane (most recently active other pane), nil if there is none.
    var otherPaneFolder: URL? { get }
}

/// App-wide services, created once at launch by `AppDelegate` before any window exists.
@MainActor
final class AppServices {
    static var shared: AppServices!

    let search: PathSearching
    let fileOperations: FileOperating
    let favorites: FavoritesProviding
    let history: FolderHistoryProviding
    let viewMemory: FolderViewMemory

    init(search: PathSearching, fileOperations: FileOperating, favorites: FavoritesProviding,
         history: FolderHistoryProviding, viewMemory: FolderViewMemory) {
        self.search = search
        self.fileOperations = fileOperations
        self.favorites = favorites
        self.history = history
        self.viewMemory = viewMemory
    }
}
