import AppKit

/// Accessibility identifiers. VoiceOver ignores them; the built-in QA harness finds views by them.
/// Panes are numbered 1…4 in reading order.
enum AccessibilityID {
    static func pane(_ number: Int) -> String { "fs.pane.\(number)" }
    static func tabBar(_ number: Int) -> String { "fs.pane.\(number).tabs" }
    static func address(_ number: Int) -> String { "fs.pane.\(number).address" }
    static func fileList(_ number: Int) -> String { "fs.pane.\(number).list" }
    static func filter(_ number: Int) -> String { "fs.pane.\(number).filter" }
    static func status(_ number: Int) -> String { "fs.pane.\(number).status" }
    static let sidebar = "fs.sidebar"
    static let folderTree = "fs.sidebar.tree"
    static let preview = "fs.preview"
    static let searchResults = "fs.search.results"
    static let dockOverlay = "fs.dock.overlay"
}

extension NSPasteboard.PasteboardType {
    /// A tab being dragged. Payload: `TabDragPayload` as JSON.
    static let folderSplitterTab = NSPasteboard.PasteboardType("io.github.hsoo-kim.FolderSplitter.tab")
    /// Present alongside file URLs when the user chose Cut, so Paste moves instead of copying.
    static let folderSplitterCut = NSPasteboard.PasteboardType("io.github.hsoo-kim.FolderSplitter.cut")
}

struct TabDragPayload: Codable, Equatable {
    let windowNumber: Int
    let paneID: UUID
    let tabID: UUID
}
