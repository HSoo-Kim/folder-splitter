import AppKit

// Menu commands travel the responder chain (first responder → … → window controller → app
// delegate). Each protocol names the selectors the menu bar uses; the owning class conforms,
// so a renamed method fails to compile instead of silently disabling a menu item.
// Owners validate their items with `validateMenuItem(_:)` / `validateUserInterfaceItem(_:)`.

/// Owner: `FileListViewController` (acts on its selection, or its folder when nothing is selected).
/// Standard selectors it also handles: copy:, cut:, paste:, selectAll:, deselectAll:.
@MainActor
@objc protocol FileCommands {
    func openSelection(_ sender: Any?)            // ⌘O (and ⌘↓ in the list)
    func openSelectionInNewTab(_ sender: Any?)    // ⌃⌘O
    func openSelectionInOtherPane(_ sender: Any?)
    func toggleQuickLook(_ sender: Any?)          // ⌘Y (and Space in the list)
    func getInfo(_ sender: Any?)                  // ⌘I
    func renameSelection(_ sender: Any?)          // Return in the list
    func bulkRename(_ sender: Any?)               // "Rename N Items…"
    func duplicateSelection(_ sender: Any?)       // ⌘D
    func makeAlias(_ sender: Any?)                // ⌃⌘A
    func showOriginal(_ sender: Any?)             // ⌘R (alias or symbolic link)
    func compressSelection(_ sender: Any?)
    func newFolder(_ sender: Any?)                // ⇧⌘N
    func newFolderWithSelection(_ sender: Any?)   // ⌃⌘N
    func moveToTrash(_ sender: Any?)              // ⌘⌫
    func copyPath(_ sender: Any?)                 // ⌥⌘C
    func pasteAsMove(_ sender: Any?)              // ⌥⌘V "Move Item Here"
    func copyToOtherPane(_ sender: Any?)          // ⌃⌥C
    func moveToOtherPane(_ sender: Any?)          // ⌃⌥X
    func showInFinder(_ sender: Any?)
    func openTerminalHere(_ sender: Any?)
    func setViewModeIcons(_ sender: Any?)         // ⌘1
    func setViewModeList(_ sender: Any?)          // ⌘2
    func sortBy(_ sender: Any?)                   // sender.tag = SortKey index in SortKey.allCases
    func toggleSortDirection(_ sender: Any?)
    func refresh(_ sender: Any?)
}

/// Owner: `PaneController` (one per pane; acts on its selected tab).
@MainActor
@objc protocol PaneCommands {
    func newTab(_ sender: Any?)                   // ⌘T
    func closeTab(_ sender: Any?)                 // ⌘W (closes the split when the pane has no tabs)
    func goBack(_ sender: Any?)                   // ⌘[
    func goForward(_ sender: Any?)                // ⌘]
    func goToEnclosingFolder(_ sender: Any?)      // ⌘↑
    func goToStandardFolder(_ sender: Any?)       // sender.tag = StandardFolder.rawValue
    func focusAddressBar(_ sender: Any?)          // ⌘L, ⇧⌘G
    func selectNextTab(_ sender: Any?)            // ⌃⇥, ⇧⌘]
    func selectPreviousTab(_ sender: Any?)        // ⌃⇧⇥, ⇧⌘[
    func addToSidebar(_ sender: Any?)             // ⌃⌘T (selected folders, else the current folder)
    func openFolderInOtherPane(_ sender: Any?)    // ⌃⌥O
    func comparePanes(_ sender: Any?)
    func focusFilter(_ sender: Any?)              // ⌘F quick filter field in the pane header
}

/// Owner: `MainWindowController`.
@MainActor
@objc protocol WindowCommands {
    func reopenClosedTab(_ sender: Any?)          // ⇧⌘T (tabs and splits, newest first)
    func setPaneLayout(_ sender: Any?)            // sender.tag = PaneLayoutPreset.rawValue
    func equalizePanes(_ sender: Any?)
    func focusNextPane(_ sender: Any?)            // ⌥⌘] (and F6)
    func focusPreviousPane(_ sender: Any?)        // ⌥⌘[ (and ⇧F6)
    func focusPane(_ sender: Any?)                // ⌃1…⌃4, sender.tag = pane number 1…4
    func closePane(_ sender: Any?)
    func toggleSidebar(_ sender: Any?)            // ⌃⌘S
    func toggleFolderTree(_ sender: Any?)
    func togglePreview(_ sender: Any?)            // ⇧⌘P
    func toggleStatusBar(_ sender: Any?)          // ⌘/
    func toggleHiddenFiles(_ sender: Any?)        // ⇧⌘.
    func toggleFilenameExtensions(_ sender: Any?)
    func toggleFoldersFirst(_ sender: Any?)
    func increaseTextSize(_ sender: Any?)         // ⌘+
    func decreaseTextSize(_ sender: Any?)         // ⌘-
    func saveWorkspace(_ sender: Any?)
    func openWorkspace(_ sender: Any?)            // sender.representedObject = workspace name
    func manageWorkspaces(_ sender: Any?)
}

/// Owner: `AppDelegate`.
@MainActor
@objc protocol AppCommands {
    func showSettings(_ sender: Any?)             // ⌘,
    func newWindow(_ sender: Any?)                // ⌘N
    func showShortcutHelp(_ sender: Any?)         // ⌘?
}

/// Go-menu targets, in menu order.
enum StandardFolder: Int, CaseIterable {
    case home = 1, desktop, documents, downloads, applications, utilities, iCloudDrive, computer

    var url: URL? {
        let fm = FileManager.default
        switch self {
        case .home: return fm.homeDirectoryForCurrentUser
        case .desktop: return fm.urls(for: .desktopDirectory, in: .userDomainMask).first
        case .documents: return fm.urls(for: .documentDirectory, in: .userDomainMask).first
        case .downloads: return fm.urls(for: .downloadsDirectory, in: .userDomainMask).first
        case .applications: return URL(fileURLWithPath: "/Applications", isDirectory: true)
        case .utilities: return URL(fileURLWithPath: "/Applications/Utilities", isDirectory: true)
        case .iCloudDrive:
            let url = fm.homeDirectoryForCurrentUser.appendingPathComponent("Library/Mobile Documents/com~apple~CloudDocs", isDirectory: true)
            return fm.fileExists(atPath: url.path) ? url : nil
        case .computer: return URL(fileURLWithPath: "/Volumes", isDirectory: true)
        }
    }

    var title: String {
        switch self {
        case .home: return tr("Home", "홈")
        case .desktop: return tr("Desktop", "데스크탑")
        case .documents: return tr("Documents", "문서")
        case .downloads: return tr("Downloads", "다운로드")
        case .applications: return tr("Applications", "응용 프로그램")
        case .utilities: return tr("Utilities", "유틸리티")
        case .iCloudDrive: return "iCloud Drive"
        case .computer: return tr("Computer", "컴퓨터")
        }
    }

    var symbolName: String {
        switch self {
        case .home: return "house"
        case .desktop: return "menubar.dock.rectangle"
        case .documents: return "doc"
        case .downloads: return "arrow.down.circle"
        case .applications: return "square.grid.3x3"
        case .utilities: return "wrench.and.screwdriver"
        case .iCloudDrive: return "icloud"
        case .computer: return "desktopcomputer"
        }
    }
}

/// Layout presets offered in the Panes menu. Raw values double as menu item tags.
enum PaneLayoutPreset: Int, CaseIterable {
    case one = 1
    case two = 2
    case threeSideBySide = 3      // one large left, two stacked right
    case four = 4
    case threeColumns = 31        // three equal columns
    case threeStacked = 32        // one large top, two side by side below
    case twoStacked = 21          // top and bottom
}
