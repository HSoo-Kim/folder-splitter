import AppKit

/// Windows owned by one slice but opened from another. `AppDelegate` installs every hook at
/// launch, so callers never reference another slice's classes directly.
@MainActor
enum UIHooks {
    /// Get Info window for the items (Preview slice).
    static var showInfo: (([URL]) -> Void)?
    /// Batch rename sheet for the items, attached to the window (Models slice).
    static var presentBulkRename: (([URL], NSWindow) -> Void)?
    /// Opens items in a detached preview window (Preview slice).
    static var openPreviewWindow: ((URL) -> Void)?
}
