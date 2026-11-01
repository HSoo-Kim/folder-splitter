import AppKit

/// `--offscreen` (UI checks): the app's windows sit outside every display, so automation can read,
/// drive and capture them without anything appearing on the user's screen. With
/// `--capture-dir <dir>`, `kill -USR1 <pid>` writes every main window as `<dir>/window-<n>.png`
/// (front to back): screencapture cannot see windows off screen, so the app draws its own.
enum OffscreenWindows {
    private(set) static var enabled = false
    static let origin = NSPoint(x: -30_000, y: -30_000)
    private static var signalSource: DispatchSourceSignal?
    private static var observer: NSObjectProtocol?

    static func install(captureDirectory: URL?) {
        enabled = true
        // Main windows start off screen (MainWindowController); move anything else that shows up
        // on a display — settings, help, info windows, alerts — before it is drawn there.
        observer = NotificationCenter.default.addObserver(forName: NSApplication.didUpdateNotification, object: nil, queue: .main) { _ in
            MainActor.assumeIsolated { moveVisibleWindowsOffscreen() }
        }
        guard let captureDirectory else { return }
        signal(SIGUSR1, SIG_IGN)
        let source = DispatchSource.makeSignalSource(signal: SIGUSR1, queue: .main)
        source.setEventHandler { MainActor.assumeIsolated { capture(to: captureDirectory) } }
        source.resume()
        signalSource = source
    }

    /// The window as on screen (title bar, toolbar, content), drawn by the app itself.
    static func image(of window: NSWindow) -> NSBitmapImageRep? {
        guard let view = window.contentView?.superview ?? window.contentView else { return nil }
        view.layoutSubtreeIfNeeded()
        guard let rep = view.bitmapImageRepForCachingDisplay(in: view.bounds) else { return nil }
        view.cacheDisplay(in: view.bounds, to: rep)
        return rep
    }

    private static func moveVisibleWindowsOffscreen() {
        let screens = NSScreen.screens.map(\.frame)
        for window in NSApp.windows where window.isVisible && window.parent == nil && window.sheetParent == nil
            && screens.contains(where: { $0.intersects(window.frame) }) {
            window.setFrameOrigin(NSPoint(x: origin.x, y: origin.y - window.frame.height - 100))
        }
    }

    private static func capture(to directory: URL) {
        try? FileManager.default.createDirectory(at: directory, withIntermediateDirectories: true)
        for (index, window) in NSApp.orderedWindows.filter({ $0 is MainWindow }).enumerated() {
            try? image(of: window)?.representation(using: .png, properties: [:])?
                .write(to: directory.appendingPathComponent("window-\(index + 1).png"), options: .atomic)
        }
    }
}

/// Main windows: under `--offscreen`, AppKit must not pull them back onto a display.
final class MainWindow: NSWindow {
    override func constrainFrameRect(_ frameRect: NSRect, to screen: NSScreen?) -> NSRect {
        OffscreenWindows.enabled ? frameRect : super.constrainFrameRect(frameRect, to: screen)
    }
}

extension NSApplication {
    /// Activates the app, except under `--offscreen`: UI checks must not take focus from the user.
    func bringForward() {
        if !OffscreenWindows.enabled { activate() }
    }
}
