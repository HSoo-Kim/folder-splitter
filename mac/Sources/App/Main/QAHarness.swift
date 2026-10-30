import AppKit
import Carbon.HIToolbox
import Quartz

/// `--qa <dir>`: drives the real app end to end with key events sent through NSApp (menus,
/// key equivalents, responder chain, field editors), Apple Events sent to itself, and window
/// snapshots, against a fixture folder it creates. Writes report.md, results.json and PNGs.
/// Launch with a throwaway `--session` and `--index-root <dir>/fixture` (scripts/qa.sh does).
@MainActor
enum QAHarness {
    static func run(output: URL) {
        Task { @MainActor in await QARunner(output: output).run() }
    }
}

@MainActor
private final class QARunner {
    struct Check: Codable {
        let name: String
        let passed: Bool
        let detail: String
        let screenshots: [String]
    }

    let output: URL
    let fixture: URL
    private var checks: [Check] = []
    private var shots: [String] = []
    private var captureMethod = "none"
    private var keyLog: [String] = []
    private let runStart = Date()
    private var app: AppDelegate { NSApp.delegate as! AppDelegate }
    private var front: MainWindowController { app.frontController! }
    private var korean: Bool { Loc.isKorean }

    init(output: URL) {
        self.output = output
        fixture = output.appendingPathComponent("fixture", isDirectory: true).standardizedFileURL
    }

    // MARK: Run

    func run() async {
        try? FileManager.default.createDirectory(at: output, withIntermediateDirectories: true)
        do { try buildFixture() } catch {
            record("fixture", false, "could not build fixture: \(error)")
            finish()
            return
        }
        app.startIndexIfEnabled()
        NSApp.activate()
        if !(await waitFor(2) { NSApp.isActive }) {
            // Launched from a background shell: ask once more the old way.
            let selector = NSSelectorFromString("activateIgnoringOtherApps:")
            typealias Activate = @convention(c) (AnyObject, Selector, Bool) -> Void
            unsafeBitCast(NSApp.method(for: selector), to: Activate.self)(NSApp, selector, true)
        }
        front.window?.makeKeyAndOrderFront(nil)
        _ = await waitFor(5) { NSApp.isActive && NSApp.keyWindow != nil }
        record("app became active", NSApp.isActive, "isActive=\(NSApp.isActive) key=\(NSApp.keyWindow?.title ?? "nil")")

        for (chars, flags) in [("?", NSEvent.ModifierFlags([.command, .shift])), ("N", [.command, .shift]), ("t", [.command]),
                               ("2", [.command, .option]), ("c", [.control, .option]), ("P", [.command, .shift])] {
            if let event = NSEvent.keyEvent(with: .keyDown, location: .zero, modifierFlags: flags, timestamp: 0, windowNumber: 0,
                                            context: nil, characters: chars, charactersIgnoringModifiers: chars, isARepeat: false, keyCode: 0) {
                keyLog.append("matcher \(flags.rawValue >> 16) '\(chars)' → \(Self.menuItem(matching: event, in: NSApp.mainMenu)?.title ?? "none")")
            }
        }
        await stepOpenFixture()
        await stepNewTabTypedPath()
        await stepTwoPanes()
        await stepAddressSearch()
        await stepKoreanSearch()
        await stepCopyToOtherPane()
        await stepDuplicateUndo()
        await stepNewFolderTrashUndo()
        await stepCloseReopenTab()
        await stepFilter()
        await stepViewModes()
        await stepHiddenFiles()
        await stepEnclosingBackForward()
        await stepLayouts()
        await stepPreview()
        await stepRevealAppleEvent()
        await stepURLScheme()
        await stepTabDock()
        await stepTextSize()
        await stepDialogs()
        await stepMouse()
        await stepQuickLookAndMenu()
        await stepLargeFolder()
        await stepDarkMode()
        await stepSession()
        finish()
    }

    private func finish() {
        let passed = checks.filter(\.passed).count
        var report = "# Folder Splitter QA (\(korean ? "ko" : "en"))\n\n\(passed)/\(checks.count) checks passed.\n\n"
        report += "| # | Check | Result | Detail | Screenshots |\n|---|---|---|---|---|\n"
        for (index, check) in checks.enumerated() {
            let detail = check.detail.replacingOccurrences(of: "|", with: "\\|").replacingOccurrences(of: "\n", with: " ")
            report += "| \(index + 1) | \(check.name) | \(check.passed ? "pass" : "**FAIL**") | \(detail) | \(check.screenshots.joined(separator: ", ")) |\n"
        }
        try? report.write(to: output.appendingPathComponent("report.md"), atomically: true, encoding: .utf8)
        if let data = try? JSONEncoder().encode(checks) {
            try? data.write(to: output.appendingPathComponent("results.json"))
        }
        try? keyLog.joined(separator: "\n").write(to: output.appendingPathComponent("keys.log"), atomically: true, encoding: .utf8)
        SessionCoordinator.shared.saveNow()
        NSApp.terminate(nil)
    }

    private func record(_ name: String, _ passed: Bool, _ detail: String) {
        checks.append(Check(name: name, passed: passed, detail: detail, screenshots: shots))
        shots = []
    }

    // MARK: Fixture

    private func buildFixture() throws {
        let fm = FileManager.default
        try? fm.removeItem(at: fixture)
        func dir(_ path: String) throws { try fm.createDirectory(at: fixture.appendingPathComponent(path), withIntermediateDirectories: true) }
        func file(_ path: String, _ text: String) throws { try text.write(to: fixture.appendingPathComponent(path), atomically: true, encoding: .utf8) }
        try dir("Documents"); try dir("Photos"); try dir("Projects/alpha/src"); try dir("Projects/beta"); try dir("Empty")
        try file("README.md", "# Fixture\n")
        try file(".hidden-note", "hidden\n")
        try file("Documents/notes.txt", "Meeting notes\n")
        try file("Documents/Budget 2026.csv", "a,b\n1,2\n")
        // Korean name stored decomposed (NFD), as many Macs do.
        try file("Documents/" + "회의록 2026.txt".decomposedStringWithCanonicalMapping, "회의\n")
        try file("Projects/alpha/src/main.swift", "print(\"hi\")\n")
        try Data(count: 3 * 1024 * 1024).write(to: fixture.appendingPathComponent("big.bin"))
        try makePDF(fixture.appendingPathComponent("Documents/report 2026 final.pdf"))
        try makeImage(fixture.appendingPathComponent("Photos/sunset.png"), color: .systemOrange)
        try makeImage(fixture.appendingPathComponent("Photos/beach.png"), color: .systemTeal)
    }

    private func makePDF(_ url: URL) throws {
        var box = CGRect(x: 0, y: 0, width: 595, height: 842)
        guard let context = CGContext(url as CFURL, mediaBox: &box, nil) else { throw CocoaError(.fileWriteUnknown) }
        for page in 1...2 {
            context.beginPDFPage(nil)
            let text = NSAttributedString(string: "Report 2026 — page \(page)", attributes: [.font: NSFont.systemFont(ofSize: 36)])
            NSGraphicsContext.saveGraphicsState()
            NSGraphicsContext.current = NSGraphicsContext(cgContext: context, flipped: false)
            text.draw(at: NSPoint(x: 60, y: 700))
            NSGraphicsContext.restoreGraphicsState()
            context.endPDFPage()
        }
        context.closePDF()
    }

    private func makeImage(_ url: URL, color: NSColor) throws {
        let image = NSImage(size: NSSize(width: 640, height: 400), flipped: false) { rect in
            color.setFill(); rect.fill()
            NSColor.white.withAlphaComponent(0.85).setFill()
            NSBezierPath(ovalIn: NSRect(x: 380, y: 200, width: 140, height: 140)).fill()
            return true
        }
        guard let tiff = image.tiffRepresentation, let rep = NSBitmapImageRep(data: tiff),
              let png = rep.representation(using: .png, properties: [:]) else { throw CocoaError(.fileWriteUnknown) }
        try png.write(to: url)
    }

    // MARK: Helpers

    private var pane: PaneController { front.activePane }
    private func pane(_ number: Int) -> PaneController? {
        let visible = front.visiblePanes
        return number >= 1 && number <= visible.count ? visible[number - 1] : nil
    }
    private var list: FileListViewController? { pane.currentList }
    private func url(_ path: String) -> URL { fixture.appendingPathComponent(path).standardizedFileURL }

    private func waitFor(_ seconds: Double, _ condition: () -> Bool) async -> Bool {
        let deadline = Date().addingTimeInterval(seconds)
        while Date() < deadline {
            if condition() { return true }
            try? await Task.sleep(nanoseconds: 50_000_000)
        }
        return condition()
    }

    private func settle(_ seconds: Double = 0.35) async {
        try? await Task.sleep(nanoseconds: UInt64(seconds * 1_000_000_000))
    }

    /// True when the window server would deliver keys to us (active app with a key window).
    private var live: Bool { NSApp.isActive && NSApp.keyWindow != nil }

    /// Sends a key through the same stages AppKit uses — the window's key equivalents, the menu
    /// bar (validated against the window's responder chain), then the first responder or field
    /// editor — without depending on the app being active, so focus changes made by whoever is
    /// using the Mac during a run can't swallow keystrokes.
    private func key(_ characters: String, code: Int = 0, _ modifiers: NSEvent.ModifierFlags = []) async {
        guard let window = NSApp.keyWindow ?? front.window else { return }
        let isLetter = characters.count == 1 && characters.first!.isLetter
        let chars = modifiers.contains(.shift) && isLetter ? characters.uppercased() : characters
        let ignoring = modifiers.contains(.shift) ? chars : characters.lowercased()
        for type in [NSEvent.EventType.keyDown, .keyUp] {
            guard let event = NSEvent.keyEvent(with: type, location: .zero, modifierFlags: modifiers,
                                               timestamp: ProcessInfo.processInfo.systemUptime,
                                               windowNumber: window.windowNumber, context: nil,
                                               characters: chars, charactersIgnoringModifiers: ignoring,
                                               isARepeat: false, keyCode: UInt16(code)) else { continue }
            if type == .keyDown {
                let outcome = deliver(event, in: window)
                keyLog.append("\(String(format: "%.2f", Date().timeIntervalSince(runStart))) \(modifiers.rawValue >> 16) '\(chars)' live=\(live) → \(outcome)")
            }
        }
        await settle(0.12)
    }

    private func deliver(_ event: NSEvent, in window: NSWindow) -> String {
        let firstResponder = window.firstResponder.map { String(describing: Swift.type(of: $0)) } ?? "nil"
        return "(fr=\(firstResponder)) " + route(event, in: window)
    }

    @discardableResult
    private func route(_ event: NSEvent, in window: NSWindow) -> String {
        let target = window.attachedSheet ?? NSApp.windows.first { $0.isVisible && $0.parent === window && $0.canBecomeKey } ?? window
        let commandish = !event.modifierFlags.intersection([.command, .control]).isEmpty
        if commandish {
            if target.performKeyEquivalent(with: event) { return "window view" }
            if let item = Self.menuItem(matching: event, in: NSApp.mainMenu), let action = item.action {
                guard let receiver = item.target ?? Self.responder(for: action, in: target) else { return "no receiver for \(item.title)" }
                let enabled = (receiver as? NSMenuItemValidation)?.validateMenuItem(item) ?? true
                if enabled { NSApp.sendAction(action, to: receiver, from: item) }
                return "\(item.title) → \(Swift.type(of: receiver)) enabled=\(enabled)"
            }
            return "no menu item"
        }
        // Field editors only see keys through an active input context; drive them directly.
        if let editor = target.firstResponder as? NSTextView, editor.isFieldEditor {
            switch Int(event.keyCode) {
            case kVK_Return: editor.doCommand(by: #selector(NSResponder.insertNewline(_:)))
            case kVK_Escape: editor.doCommand(by: #selector(NSResponder.cancelOperation(_:)))
            case kVK_DownArrow: editor.doCommand(by: #selector(NSResponder.moveDown(_:)))
            case kVK_UpArrow: editor.doCommand(by: #selector(NSResponder.moveUp(_:)))
            case kVK_Tab: editor.doCommand(by: #selector(NSResponder.insertTab(_:)))
            default: editor.insertText(event.characters ?? "", replacementRange: editor.selectedRange())
            }
            return "field editor"
        }
        target.sendEvent(event)
        return "window.sendEvent"
    }

    private static func menuItem(matching event: NSEvent, in menu: NSMenu?) -> NSMenuItem? {
        guard let menu else { return nil }
        let relevant: NSEvent.ModifierFlags = [.command, .option, .control, .shift]
        let pressed = event.modifierFlags.intersection(relevant)
        let key = event.charactersIgnoringModifiers ?? ""
        for item in menu.items {
            if let found = menuItem(matching: event, in: item.submenu) { return found }
            guard !item.keyEquivalent.isEmpty, !item.isHidden else { continue }
            var mask = item.keyEquivalentModifierMask.intersection(relevant)
            let equivalent = item.keyEquivalent
            if equivalent != equivalent.lowercased() { mask.insert(.shift) }
            // Shifted punctuation (⌘?, ⌘+) is written as the character itself.
            var compared = pressed
            if !(equivalent.first?.isLetter ?? true) && !mask.contains(.shift) { compared.remove(.shift) }
            if equivalent.lowercased() == key.lowercased() && mask == compared { return item }
        }
        return nil
    }

    private static func responder(for action: Selector, in window: NSWindow) -> AnyObject? {
        var responder: NSResponder? = window.firstResponder
        while let current = responder {
            if current.responds(to: action) { return current }
            responder = current.nextResponder
        }
        if let delegate = window.delegate as? NSObject, delegate.responds(to: action) { return delegate }
        if let controller = window.windowController {
            if controller.responds(to: action) { return controller }
            if let supplemental = controller.supplementalTarget(forAction: action, sender: nil) as AnyObject? { return supplemental }
        }
        if NSApp.responds(to: action) { return NSApp }
        if let delegate = NSApp.delegate as? NSObject, delegate.responds(to: action) { return delegate }
        return nil
    }

    private func type(_ text: String) async {
        for character in text { await key(String(character)) }
    }

    private func returnKey(_ modifiers: NSEvent.ModifierFlags = []) async { await key("\r", code: kVK_Return, modifiers) }
    private func escape() async { await key("\u{1b}", code: kVK_Escape) }

    /// Captures a window as the window server composited it. CGWindowListCreateImage is
    /// unavailable to Swift on the macOS 15 SDK but still captures an app's own windows without
    /// Screen Recording permission, so it is resolved at run time. Falls back to rendering the
    /// layer tree, which can disagree with the screen for overlapping siblings.
    private func shot(_ name: String, window: NSWindow? = nil) async {
        await settle(0.5)
        guard let target = window ?? front.window else { return }
        target.displayIfNeeded()
        let server = Self.windowServerImage(of: target)
        captureMethod = server == nil ? "layer" : "windowServer"
        guard let image = server ?? Self.layerImage(of: target) else { return }
        let file = "\(korean ? "ko" : "en")-\(name).png"
        if let data = NSBitmapImageRep(cgImage: image).representation(using: .png, properties: [:]) {
            try? data.write(to: output.appendingPathComponent(file))
            shots.append(file)
        }
    }

    private static func windowServerImage(of window: NSWindow) -> CGImage? {
        typealias Capture = @convention(c) (CGRect, UInt32, UInt32, UInt32) -> Unmanaged<CGImage>?
        guard let symbol = dlsym(UnsafeMutableRawPointer(bitPattern: -2), "CGWindowListCreateImage") else { return nil }
        let capture = unsafeBitCast(symbol, to: Capture.self)
        let options = CGWindowImageOption([.boundsIgnoreFraming, .bestResolution]).rawValue
        guard let image = capture(.null, CGWindowListOption.optionIncludingWindow.rawValue,
                                  CGWindowID(window.windowNumber), options)?.takeRetainedValue(),
              image.width > 1 else { return nil }
        return image
    }

    private static func layerImage(of window: NSWindow) -> CGImage? {
        guard let view = window.contentView?.superview ?? window.contentView, let layer = view.layer else { return nil }
        view.layoutSubtreeIfNeeded()
        let scale = window.backingScaleFactor
        let width = Int(view.bounds.width * scale), height = Int(view.bounds.height * scale)
        guard width > 0, height > 0, let space = CGColorSpace(name: CGColorSpace.sRGB),
              let context = CGContext(data: nil, width: width, height: height, bitsPerComponent: 8, bytesPerRow: 0,
                                      space: space, bitmapInfo: CGImageAlphaInfo.premultipliedLast.rawValue) else { return nil }
        context.scaleBy(x: scale, y: scale)
        if layer.isGeometryFlipped || view.isFlipped {
            context.translateBy(x: 0, y: view.bounds.height)
            context.scaleBy(x: 1, y: -1)
        }
        layer.render(in: context)
        return context.makeImage()
    }

    /// Frames and visibility of a view subtree in window coordinates, for layout diagnosis.
    private func dumpHierarchy(of root: NSView, to name: String) {
        var lines: [String] = []
        func visit(_ view: NSView, depth: Int) {
            let frame = view.convert(view.bounds, to: nil)
            let flags = [view.isHidden ? "hidden" : nil, view.alphaValue < 1 ? "alpha=\(view.alphaValue)" : nil,
                         (view.layer?.opacity ?? 1) < 1 ? "layerOpacity=\(view.layer?.opacity ?? 1)" : nil,
                         view.accessibilityIdentifier().isEmpty ? nil : view.accessibilityIdentifier()].compactMap { $0 }
            lines.append(String(repeating: "  ", count: depth) + "\(Swift.type(of: view)) \(NSStringFromRect(frame)) \(flags.joined(separator: " "))")
            if depth < 6 { for sub in view.subviews { visit(sub, depth: depth + 1) } }
        }
        visit(root, depth: 0)
        try? lines.joined(separator: "\n").write(to: output.appendingPathComponent(name), atomically: true, encoding: .utf8)
    }

    /// CALayer tree of a view in its root's coordinates: finds sublayers drawn outside their view.
    private func dumpLayers(of root: NSView, to name: String) {
        guard let rootLayer = root.layer else { return }
        var lines: [String] = []
        func visit(_ layer: CALayer, depth: Int) {
            let frame = rootLayer.convert(layer.bounds, from: layer)
            let owner = (layer.delegate as? NSView).map { String(describing: Swift.type(of: $0)) } ?? "-"
            lines.append(String(repeating: "  ", count: depth) + "\(Swift.type(of: layer)) \(NSStringFromRect(frame)) view=\(owner) hidden=\(layer.isHidden) opacity=\(layer.opacity) bg=\(layer.backgroundColor != nil) contents=\(layer.contents != nil) z=\(layer.zPosition)")
            if depth < 4 { for sub in layer.sublayers ?? [] { visit(sub, depth: depth + 1) } }
        }
        visit(rootLayer, depth: 0)
        try? lines.joined(separator: "\n").write(to: output.appendingPathComponent(name), atomically: true, encoding: .utf8)
    }

    private func refocusMain() async {
        await activate()
        front.window?.makeKeyAndOrderFront(nil)
        _ = await waitFor(2) { NSApp.keyWindow === self.front.window }
    }

    /// Asks for activation (again): closing a window can hand activation to another app.
    private func activate() async {
        NSApp.activate()
        if await waitFor(1, { NSApp.isActive }) { return }
        let selector = NSSelectorFromString("activateIgnoringOtherApps:")
        typealias Activate = @convention(c) (AnyObject, Selector, Bool) -> Void
        unsafeBitCast(NSApp.method(for: selector), to: Activate.self)(NSApp, selector, true)
        _ = await waitFor(1) { NSApp.isActive }
    }

    /// Sends a menu action like a menu item would, also when macOS left us without a key window.
    private func perform(_ action: Selector) {
        if NSApp.target(forAction: action, to: nil, from: nil) != nil {
            NSApp.sendAction(action, to: nil, from: nil)
        } else if let window = front.window, let receiver = Self.responder(for: action, in: window) {
            NSApp.sendAction(action, to: receiver, from: nil)
        }
    }

    private func window(where predicate: (NSWindow) -> Bool) -> NSWindow? {
        NSApp.windows.first { $0.isVisible && predicate($0) }
    }

    private func findView(id: String, in view: NSView?) -> NSView? {
        guard let view else { return nil }
        if view.accessibilityIdentifier() == id { return view }
        for sub in view.subviews { if let found = findView(id: id, in: sub) { return found } }
        return nil
    }

    private func searchResultsTable() -> (NSTableView, NSWindow)? {
        for window in NSApp.windows where window.isVisible {
            if let view = findView(id: AccessibilityID.searchResults, in: window.contentView) {
                let table = (view as? NSTableView) ?? (view.subviews.first { $0 is NSTableView } as? NSTableView)
                    ?? ((view as? NSScrollView)?.documentView as? NSTableView)
                if let table { return (table, window) }
            }
        }
        return nil
    }

    private func selectionNames() -> [String] { (list?.selectedURLs ?? []).map { PathText.nfc($0.lastPathComponent) } }

    // MARK: Steps

    private func stepOpenFixture() async {
        front.open(fixture, disposition: .current, from: nil)
        let loaded = await waitFor(5) { self.list?.folder == self.fixture && self.list?.loadState == .loaded }
        let expected = (try? FileManager.default.contentsOfDirectory(atPath: fixture.path))?.filter { !$0.hasPrefix(".") }.count ?? -1
        await shot("01-list")
        dumpHierarchy(of: pane.view, to: "hierarchy-pane1.txt")
        dumpLayers(of: pane.view, to: "layers-pane1.txt")
        var hitInList = false
        var hitName = "nil"
        if let listView = list?.view, let content = front.window?.contentView {
            let point = content.convert(NSPoint(x: listView.bounds.midX, y: listView.bounds.midY), from: listView)
            let hit = content.hitTest(point)
            hitName = hit.map { String(describing: Swift.type(of: $0)) } ?? "nil"
            hitInList = hit?.isDescendant(of: listView) ?? false
        }
        record("open fixture folder in pane 1", loaded && list?.visibleItemCount == expected && hitInList,
               "loaded=\(loaded) items=\(list?.visibleItemCount ?? -1) expected=\(expected) title=\(front.window?.title ?? "") capture=\(captureMethod) hitAtListCenter=\(hitName)")
    }

    private func stepNewTabTypedPath() async {
        await key("t", code: kVK_ANSI_T, .command)
        let blank = await waitFor(2) { self.pane.tabs.count == 2 && self.pane.selectedTab?.isBlank == true }
        await shot("02-blank-tab")
        await type(url("Documents").path)
        await returnKey()
        let navigated = await waitFor(4) { self.pane.currentFolder == self.url("Documents") && self.list?.loadState == .loaded }
        record("⌘T opens a blank tab; typed path + Return navigates", blank && navigated,
               "tabs=\(pane.tabs.count) folder=\(pane.currentFolder?.path ?? "nil")")
    }

    private func stepTwoPanes() async {
        await key("2", code: kVK_ANSI_2, [.command, .option])
        let two = await waitFor(2) { self.front.visiblePaneCount == 2 }
        await settle(0.6)
        await key("2", code: kVK_ANSI_2, .control)
        let second = await waitFor(2) { self.front.activePane === self.pane(2) }
        front.open(url("Projects"), disposition: .current, from: pane(2))
        let opened = await waitFor(4) { self.pane(2)?.currentFolder == self.url("Projects") && self.pane(2)?.currentList?.loadState == .loaded }
        await shot("03-two-panes")
        record("⌥⌘2 splits into two panes; ⌃2 focuses pane 2", two && second && opened,
               "visible=\(front.visiblePaneCount) active2=\(second) pane2=\(pane(2)?.currentFolder?.lastPathComponent ?? "nil")")
        await key("1", code: kVK_ANSI_1, .control)
    }

    private func stepAddressSearch() async {
        _ = await waitFor(15) { AppServices.shared.search.state.isSearchable }
        let state = AppServices.shared.search.state
        await key("l", code: kVK_ANSI_L, .command)
        await type("report 2026")
        let shown = await waitFor(4) { (self.searchResultsTable()?.0.numberOfRows ?? 0) > 0 }
        if let (_, window) = searchResultsTable() {
            await shot("04-search-main")
            await shot("04-search-results", window: window)
        }
        await returnKey()
        let revealed = await waitFor(4) {
            self.pane.currentFolder == self.url("Documents") && self.selectionNames().contains("report 2026 final.pdf")
        }
        record("address bar name search finds and reveals a file", shown && revealed,
               "index=\(state) results=\(shown) folder=\(pane.currentFolder?.lastPathComponent ?? "nil") selection=\(selectionNames())")
    }

    private func stepKoreanSearch() async {
        front.open(fixture, disposition: .current, from: pane)
        _ = await waitFor(3) { self.pane.currentFolder == self.fixture }
        await key("l", code: kVK_ANSI_L, .command)
        await type("회의록")
        let shown = await waitFor(4) { (self.searchResultsTable()?.0.numberOfRows ?? 0) > 0 }
        await returnKey()
        let revealed = await waitFor(4) { self.selectionNames().contains("회의록 2026.txt") }
        record("Korean NFC query finds an NFD-named file", shown && revealed, "results=\(shown) selection=\(selectionNames())")
    }

    private func stepCopyToOtherPane() async {
        front.open(url("Documents/report 2026 final.pdf"), disposition: .current, from: pane(1))
        _ = await waitFor(3) { self.selectionNames().contains("report 2026 final.pdf") }
        list?.focus()
        let before = pane(2)?.currentList?.visibleItemCount ?? -1
        await key("c", code: kVK_ANSI_C, [.control, .option])
        let copied = await waitFor(5) { FileManager.default.fileExists(atPath: self.url("Projects/report 2026 final.pdf").path) }
        let listed = await waitFor(4) { (self.pane(2)?.currentList?.visibleItemCount ?? -1) == before + 1 }
        record("⌃⌥C copies the selection to the other pane", copied && listed, "copied=\(copied) pane2 items \(before)→\(pane(2)?.currentList?.visibleItemCount ?? -1)")
    }

    private func stepDuplicateUndo() async {
        list?.focus()
        await key("d", code: kVK_ANSI_D, .command)
        let copyName = korean ? "report 2026 final 복사본.pdf" : "report 2026 final copy.pdf"
        let duplicated = await waitFor(4) { FileManager.default.fileExists(atPath: self.url("Documents/\(copyName)").path) }
        await key("z", code: kVK_ANSI_Z, .command)
        let undone = await waitFor(4) { !FileManager.default.fileExists(atPath: self.url("Documents/\(copyName)").path) }
        record("⌘D duplicates with Finder naming; ⌘Z undoes", duplicated && undone, "duplicated=\(duplicated) undone=\(undone) name=\(copyName)")
    }

    private func stepNewFolderTrashUndo() async {
        list?.focus()
        await key("n", code: kVK_ANSI_N, [.command, .shift])
        let name = korean ? "무제 폴더" : "untitled folder"
        let created = await waitFor(4) { FileManager.default.fileExists(atPath: self.url("Documents/\(name)").path) }
        await settle(0.4)
        await shot("05-new-folder-rename")
        await escape()
        _ = await waitFor(2) { self.selectionNames().contains(name) }
        list?.focus()
        await key("\u{8}", code: kVK_Delete, .command)
        let trashed = await waitFor(4) { !FileManager.default.fileExists(atPath: self.url("Documents/\(name)").path) }
        await key("z", code: kVK_ANSI_Z, .command)
        let restored = await waitFor(4) { FileManager.default.fileExists(atPath: self.url("Documents/\(name)").path) }
        record("⇧⌘N new folder (rename starts), ⌘⌫ trash, ⌘Z puts it back", created && trashed && restored,
               "created=\(created) trashed=\(trashed) restored=\(restored)")
    }

    private func stepCloseReopenTab() async {
        let tabs = pane.tabs.count
        let folder = pane.currentFolder
        await key("w", code: kVK_ANSI_W, .command)
        let closed = await waitFor(2) { self.pane.tabs.count == tabs - 1 }
        await key("t", code: kVK_ANSI_T, [.command, .shift])
        let reopened = await waitFor(3) { self.pane.tabs.count == tabs && self.pane.currentFolder == folder }
        record("⌘W closes a tab; ⇧⌘T reopens it where it was", closed && reopened,
               "tabs \(tabs)→\(pane.tabs.count) folder=\(pane.currentFolder?.lastPathComponent ?? "nil")")
    }

    private func stepFilter() async {
        list?.focus()
        let total = list?.totalItemCount ?? 0
        await key("f", code: kVK_ANSI_F, .command)
        await type("report")
        let filtered = await waitFor(3) { (self.list?.visibleItemCount ?? total) < total && (self.list?.visibleItemCount ?? 0) >= 1 }
        await shot("06-filter")
        await escape()
        let cleared = await waitFor(3) { self.list?.visibleItemCount == total }
        record("⌘F quick filter narrows the list; Esc clears", filtered && cleared, "total=\(total) filtered=\(filtered) cleared=\(cleared)")
    }

    private func stepViewModes() async {
        list?.focus()
        await key("1", code: kVK_ANSI_1, .command)
        let icons = await waitFor(2) { self.list?.viewSettings.viewMode == .icons }
        await shot("07-icons")
        await key("2", code: kVK_ANSI_2, .command)
        let listMode = await waitFor(2) { self.list?.viewSettings.viewMode == .list }
        record("⌘1 icon view / ⌘2 list view", icons && listMode, "icons=\(icons) list=\(listMode)")
    }

    private func stepHiddenFiles() async {
        front.open(fixture, disposition: .current, from: pane)
        _ = await waitFor(3) { self.pane.currentFolder == self.fixture && self.list?.loadState == .loaded }
        let before = list?.totalItemCount ?? 0
        await key(".", code: kVK_ANSI_Period, [.command, .shift])
        let shown = await waitFor(3) { (self.list?.totalItemCount ?? 0) > before }
        await key(".", code: kVK_ANSI_Period, [.command, .shift])
        let hidden = await waitFor(3) { self.list?.totalItemCount == before }
        record("⇧⌘. shows and hides hidden files", shown && hidden, "before=\(before) shown=\(shown) hiddenAgain=\(hidden)")
    }

    private func stepEnclosingBackForward() async {
        front.open(url("Projects/alpha"), disposition: .current, from: pane)
        _ = await waitFor(3) { self.pane.currentFolder == self.url("Projects/alpha") }
        list?.focus()
        await key(String(UnicodeScalar(NSUpArrowFunctionKey)!), code: kVK_UpArrow, [.command, .numericPad, .function])
        let up = await waitFor(3) { self.pane.currentFolder == self.url("Projects") && self.selectionNames() == ["alpha"] }
        await key("[", code: kVK_ANSI_LeftBracket, .command)
        let back = await waitFor(3) { self.pane.currentFolder == self.url("Projects/alpha") }
        await key("]", code: kVK_ANSI_RightBracket, .command)
        let forward = await waitFor(3) { self.pane.currentFolder == self.url("Projects") }
        record("⌘↑ selects the folder you came from; ⌘[ / ⌘] history", up && back && forward,
               "up=\(up) back=\(back) forward=\(forward) selection=\(selectionNames())")
    }

    private func stepLayouts() async {
        await key("4", code: kVK_ANSI_4, [.command, .option])
        let four = await waitFor(2) { self.front.visiblePaneCount == 4 }
        await settle(0.6)
        await shot("08-four-panes")
        await key("1", code: kVK_ANSI_1, [.command, .option])
        let one = await waitFor(2) { self.front.visiblePaneCount == 1 }
        await key("2", code: kVK_ANSI_2, [.command, .option])
        let kept = await waitFor(2) { self.front.visiblePaneCount == 2 && self.pane(2)?.currentFolder == self.url("Projects") }
        await key("3", code: kVK_ANSI_3, [.command, .option])
        let three = await waitFor(2) { self.front.visiblePaneCount == 3 && self.front.layout.shape == .threeSideBySide }
        await settle(0.6)
        await shot("09-three-panes")
        await key("2", code: kVK_ANSI_2, [.command, .option])
        _ = await waitFor(2) { self.front.visiblePaneCount == 2 }
        record("⌥⌘4/⌥⌘1/⌥⌘2/⌥⌘3 layouts; hidden panes keep their tabs", four && one && kept && three,
               "four=\(four) one=\(one) hiddenKept=\(kept) three=\(three)")
    }

    private func stepPreview() async {
        await key("1", code: kVK_ANSI_1, .control)
        front.open(url("Photos/sunset.png"), disposition: .current, from: pane)
        _ = await waitFor(3) { self.selectionNames() == ["sunset.png"] }
        let before = SettingsStore.shared.settings.showPreview
        let probe = NSEvent.keyEvent(with: .keyDown, location: .zero, modifierFlags: [.command, .shift], timestamp: 0,
                                     windowNumber: front.window?.windowNumber ?? 0, context: nil, characters: "P",
                                     charactersIgnoringModifiers: "P", isARepeat: false, keyCode: UInt16(kVK_ANSI_P))
        let matched = probe.flatMap { Self.menuItem(matching: $0, in: NSApp.mainMenu) }?.title ?? "none"
        let responder = String(describing: NSApp.keyWindow?.firstResponder.map { Swift.type(of: $0) })
        await key("p", code: kVK_ANSI_P, [.command, .shift])
        let shown = await waitFor(2) { SettingsStore.shared.settings.showPreview }
        await settle(1.2)
        await shot("10-preview")
        await key("p", code: kVK_ANSI_P, [.command, .shift])
        let hidden = await waitFor(2) { !SettingsStore.shared.settings.showPreview }
        record("⇧⌘P shows the preview of the selection and hides it", shown && hidden,
               "before=\(before) shown=\(shown) hidden=\(hidden) menu=\(matched) responder=\(responder)")
    }

    private func stepRevealAppleEvent() async {
        front.open(url("Documents"), disposition: .current, from: pane)
        _ = await waitFor(3) { self.pane.currentFolder == self.url("Documents") }
        let target = NSAppleEventDescriptor(processIdentifier: ProcessInfo.processInfo.processIdentifier)
        let event = NSAppleEventDescriptor.appleEvent(withEventClass: AEEventClass(kAEMiscStandards),
                                                      eventID: AEEventID(kAEMakeObjectsVisible),
                                                      targetDescriptor: target, returnID: AEReturnID(kAutoGenerateReturnID),
                                                      transactionID: AETransactionID(kAnyTransactionID))
        let items = NSAppleEventDescriptor.list()
        items.insert(NSAppleEventDescriptor(fileURL: url("Photos/beach.png")), at: 1)
        event.setParam(items, forKeyword: keyDirectObject)
        var sent = true
        do { _ = try event.sendEvent(options: [.noReply], timeout: 2) } catch { sent = false }
        let revealed = await waitFor(4) { self.pane.currentFolder == self.url("Photos") && self.selectionNames() == ["beach.png"] }
        record("\"Reveal in Finder\" Apple Event (misc/mvis) shows the item", sent && revealed,
               "sent=\(sent) folder=\(pane.currentFolder?.lastPathComponent ?? "nil") selection=\(selectionNames())")
    }

    private func stepURLScheme() async {
        let before = pane.tabs.count
        let target = NSAppleEventDescriptor(processIdentifier: ProcessInfo.processInfo.processIdentifier)
        let event = NSAppleEventDescriptor.appleEvent(withEventClass: AEEventClass(kInternetEventClass), eventID: AEEventID(kAEGetURL),
                                                      targetDescriptor: target, returnID: AEReturnID(kAutoGenerateReturnID),
                                                      transactionID: AETransactionID(kAnyTransactionID))
        var components = URLComponents(string: "foldersplitter://open")!
        components.queryItems = [URLQueryItem(name: "path", value: url("Projects/beta").path), URLQueryItem(name: "in", value: "tab")]
        event.setParam(NSAppleEventDescriptor(string: components.string!), forKeyword: keyDirectObject)
        _ = try? event.sendEvent(options: [.noReply], timeout: 2)
        let opened = await waitFor(4) { self.pane.tabs.count == before + 1 && self.pane.currentFolder == self.url("Projects/beta") }
        record("foldersplitter://open?path=…&in=tab opens a new tab", opened, "tabs \(before)→\(pane.tabs.count)")
    }

    private func stepTabDock() async {
        guard let source = pane(1), let tab = source.selectedTab, let target = pane(2), let windowNumber = front.window?.windowNumber else {
            record("tab dropped on a pane edge splits it", false, "missing panes")
            return
        }
        let payload = TabDragPayload(windowNumber: windowNumber, paneID: source.id, tabID: tab.id)
        let accepted = front.paneLayoutView(PaneLayoutView(frame: .zero), accept: payload, on: target.id, zone: .edge(.bottom))
        let split = await waitFor(2) { self.front.visiblePaneCount == 3 && self.pane(3)?.selectedTab?.id == tab.id }
        await settle(0.6)
        await shot("11-tab-docked")
        record("tab dropped on a pane edge splits it (drop handler path)", accepted && split,
               "accepted=\(accepted) visible=\(front.visiblePaneCount)")
    }

    private func stepTextSize() async {
        await key("+", code: kVK_ANSI_Equal, .command)
        let bigger = await waitFor(2) { SettingsStore.shared.settings.textSize == .large }
        await key("-", code: kVK_ANSI_Minus, .command)
        let back = await waitFor(2) { SettingsStore.shared.settings.textSize == .medium }
        record("⌘+ / ⌘- change text size", bigger && back, "bigger=\(bigger) back=\(back)")
    }

    private func stepDialogs() async {
        await key(",", code: kVK_ANSI_Comma, .command)
        let settingsWindow = await waitFor(3) { self.window { $0.windowController is SettingsWindowController } != nil }
        if let window = window(where: { $0.windowController is SettingsWindowController }) {
            await shot("12-settings", window: window)
            window.close()
        }
        await refocusMain()
        await key("?", code: kVK_ANSI_Slash, [.command, .shift])
        let help = await waitFor(3) { self.window { $0.windowController is ShortcutHelpWindowController } != nil }
        if let window = window(where: { $0.windowController is ShortcutHelpWindowController }) {
            await shot("13-shortcuts", window: window)
            window.close()
        }
        await refocusMain()
        // Bulk rename sheet for two files.
        front.open(url("Documents"), disposition: .current, from: pane(1))
        _ = await waitFor(3) { self.pane(1)?.currentFolder == self.url("Documents") && (self.pane(1)?.currentList?.visibleItemCount ?? 0) >= 4 }
        if let list = pane(1)?.currentList {
            front.activatePane(pane(1)!)
            list.select([url("Documents/notes.txt"), url("Documents/Budget 2026.csv")])
            list.focus()
        }
        perform(#selector(FileCommands.bulkRename(_:)))
        let sheet = await waitFor(3) { self.front.window?.attachedSheet != nil }
        if let sheetWindow = front.window?.attachedSheet { await shot("14-bulk-rename", window: sheetWindow); front.window?.endSheet(sheetWindow) }
        await settle(0.4)
        // Get Info.
        await key("i", code: kVK_ANSI_I, .command)
        let info = await waitFor(3) { self.window { $0.title.contains("notes") || $0.title.contains("Budget") } != nil }
        if let window = window(where: { $0.title.contains("notes") || $0.title.contains("Budget") }) {
            await shot("15-info", window: window)
        }
        for window in NSApp.windows where window.isVisible && (window.title.contains("notes") || window.title.contains("Budget")) { window.close() }
        await refocusMain()
        // Compare with the other pane.
        let sheetBefore = front.window?.attachedSheet.map { String(describing: Swift.type(of: $0)) } ?? "none"
        let compareTarget = NSApp.target(forAction: #selector(PaneCommands.comparePanes(_:)), to: nil, from: nil)
        let targetName = compareTarget.map { String(describing: Swift.type(of: $0)) } ?? "nil"
        let folders = "\(front.activePane.currentFolder?.lastPathComponent ?? "nil")↔\(front.otherPane(of: front.activePane)?.currentFolder?.lastPathComponent ?? "nil")"
        perform(#selector(PaneCommands.comparePanes(_:)))
        let compare = await waitFor(3) { self.front.window?.attachedSheet != nil }
        await settle(0.8)
        if let sheetWindow = front.window?.attachedSheet { await shot("16-compare", window: sheetWindow); front.window?.endSheet(sheetWindow) }
        record("Settings ⌘,, shortcut help ⌘?, Rename Items…, Get Info ⌘I, Compare", settingsWindow && help && sheet && info && compare,
               "settings=\(settingsWindow) help=\(help) bulkRename=\(sheet) info=\(info) compare=\(compare) [sheetBefore=\(sheetBefore) target=\(targetName) panes=\(folders)]")
    }

    // MARK: Mouse, Quick Look, context menu, scale

    /// A real click through the window: mouse-up is queued first so controls that run their own
    /// tracking loop (NSButton) find it.
    private func click(_ view: NSView, at local: NSPoint? = nil, count: Int = 1, _ modifiers: NSEvent.ModifierFlags = []) async {
        guard let window = view.window else { return }
        let point = view.convert(local ?? NSPoint(x: view.bounds.midX, y: view.bounds.midY), to: nil)
        func event(_ type: NSEvent.EventType) -> NSEvent? {
            NSEvent.mouseEvent(with: type, location: point, modifierFlags: modifiers, timestamp: ProcessInfo.processInfo.systemUptime,
                               windowNumber: window.windowNumber, context: nil, eventNumber: 0, clickCount: count, pressure: 1)
        }
        guard let down = event(.leftMouseDown), let up = event(.leftMouseUp) else { return }
        NSApp.postEvent(up, atStart: false)
        window.sendEvent(down)
        await settle(0.25)
    }

    private func views(named className: String, in root: NSView?) -> [NSView] {
        guard let root else { return [] }
        var found = String(describing: Swift.type(of: root)) == className ? [root] : []
        for sub in root.subviews { found += views(named: className, in: sub) }
        return found
    }

    private func stepMouse() async {
        await refocusMain()
        guard let first = pane(1), let content = front.window?.contentView else {
            record("mouse: sidebar, tab, breadcrumb, divider, double-click", false, "no pane")
            return
        }
        front.activatePane(first)
        // Sidebar ▸ Recent Folders: click a fixture folder there (never a Favorite: those are the
        // real Desktop/Documents/Downloads, which would trigger privacy prompts).
        var sidebarOK = false
        var sidebarDetail = "no row"
        if let outline = views(named: "SidebarOutline", in: content).first(where: { $0.accessibilityIdentifier() == AccessibilityID.sidebar }) as? NSOutlineView {
            func title(_ row: Int) -> String? {
                guard let cell = outline.view(atColumn: 0, row: row, makeIfNecessary: true) else { return nil }
                return views(named: "NSTextField", in: cell).compactMap { ($0 as? NSTextField)?.stringValue }.first { !$0.isEmpty }
            }
            let recents = AppServices.shared.history.recentFolders.filter { PathText.isWithin($0, fixture) }
            sidebarDetail = "rows=" + (0..<outline.numberOfRows).compactMap(title).joined(separator: "|") + " recents=" + recents.map(\.lastPathComponent).joined(separator: "|")
            let header = (0..<outline.numberOfRows).first { title($0) == tr("Recent Folders", "최근 사용한 폴더") }
            if let header, let (row, target) = ((header + 1)..<outline.numberOfRows).lazy.compactMap({ row -> (Int, URL)? in
                guard let name = title(row), let match = recents.first(where: { TabState.displayName(of: $0) == name }) else { return nil }
                return (row, match)
            }).first {
                outline.scrollRowToVisible(row)
                let rect = outline.rect(ofRow: row)
                await click(outline, at: NSPoint(x: rect.midX, y: rect.midY))
                sidebarOK = await waitFor(3) { self.pane.currentFolder?.path == target.path }
                sidebarDetail = "\(target.lastPathComponent)→\(pane.currentFolder?.lastPathComponent ?? "nil")"
            }
        }
        // Tab click: select the first tab of pane 1.
        var tabOK = false
        let tabs = views(named: "ChromeTabCell", in: first.view).sorted { $0.convert($0.bounds, to: nil).minX < $1.convert($1.bounds, to: nil).minX }
        if tabs.count > 1, let firstTab = first.tabs.first?.id {
            await click(tabs[0])
            tabOK = await waitFor(2) { first.selectedTabID == firstTab }
        }
        // Breadcrumb click: go to the fixture root from a subfolder.
        front.open(url("Projects/alpha/src"), disposition: .current, from: first)
        _ = await waitFor(3) { first.currentFolder == self.url("Projects/alpha/src") }
        await settle(0.4)
        // Click the nearest ancestor crumb that is visible (narrow panes collapse the middle).
        var crumbOK = false
        let current = url("Projects/alpha/src")
        _ = await waitFor(3) { self.views(named: "CrumbButton", in: first.addressBar).contains { $0.toolTip == PathText.abbreviated(current) } }
        let crumbTips = views(named: "CrumbButton", in: first.addressBar).compactMap(\.toolTip)
        var ancestor = current.deletingLastPathComponent()
        while ancestor.path != "/" && !crumbTips.contains(PathText.abbreviated(ancestor)) { ancestor = ancestor.deletingLastPathComponent() }
        if let crumb = views(named: "CrumbButton", in: first.addressBar).first(where: { $0.toolTip == PathText.abbreviated(ancestor) }) {
            await click(crumb)
            crumbOK = await waitFor(3) { first.currentFolder?.path == ancestor.path }
        }
        front.open(fixture, disposition: .current, from: first)
        _ = await waitFor(3) { first.currentFolder?.path == self.fixture.path && (first.currentList?.visibleItemCount ?? 0) > 0 }
        // Double-click a folder row opens it.
        var doubleOK = false
        var doubleDetail = "no list"
        if let outline = first.currentList.flatMap({ findView(id: AccessibilityID.fileList(1), in: $0.view) }) as? NSOutlineView {
            _ = await waitFor(3) { first.currentList?.loadState == .loaded && outline.numberOfRows > 0 }
            func photosRow() -> Int? {
                (0..<outline.numberOfRows).first { row in
                    guard let cell = outline.view(atColumn: 0, row: row, makeIfNecessary: true) else { return false }
                    return views(named: "NSTextField", in: cell).contains { ($0 as? NSTextField)?.stringValue == "Photos" }
                }
            }
            // Wait for the new folder's rows, not just any rows (the old listing stays until then).
            _ = await waitFor(5) { photosRow() != nil }
            let row = photosRow()
            doubleDetail = "rows=\(outline.numberOfRows) photosRow=\(row.map(String.init) ?? "nil")"
            if let row {
                let rect = outline.rect(ofRow: row)
                await click(outline, at: NSPoint(x: rect.minX + 120, y: rect.midY), count: 1)
                await click(outline, at: NSPoint(x: rect.minX + 120, y: rect.midY), count: 2)
                doubleOK = await waitFor(3) { first.currentFolder == self.url("Photos") }
            }
        }
        // Divider drag: 1:1 tracking, ratio follows the pointer.
        var dividerOK = false
        let before = front.layout
        if let divider = views(named: "DividerView", in: content).first {
            let start = NSPoint(x: divider.bounds.midX, y: divider.bounds.midY)
            let window = divider.window!
            let origin = divider.convert(start, to: nil)
            func mouse(_ type: NSEvent.EventType, dx: CGFloat) {
                if let event = NSEvent.mouseEvent(with: type, location: NSPoint(x: origin.x + dx, y: origin.y), modifierFlags: [],
                                                  timestamp: ProcessInfo.processInfo.systemUptime, windowNumber: window.windowNumber,
                                                  context: nil, eventNumber: 0, clickCount: 1, pressure: 1) { window.sendEvent(event) }
            }
            mouse(.leftMouseDown, dx: 0)
            for step in 1...8 { mouse(.leftMouseDragged, dx: CGFloat(step) * 15) }
            mouse(.leftMouseUp, dx: 120)
            await settle(0.6)
            dividerOK = front.layout != before
        }
        await shot("18-after-mouse")
        record("mouse: sidebar row, tab, breadcrumb, double-click folder, divider drag",
               sidebarOK && tabOK && crumbOK && doubleOK && dividerOK,
               "sidebar=\(sidebarOK) (\(sidebarDetail)) tab=\(tabOK) crumb=\(crumbOK) (\(crumbTips.joined(separator: "|"))) doubleClick=\(doubleOK) (\(doubleDetail), now \(first.currentFolder?.lastPathComponent ?? "nil")) divider=\(dividerOK)")
    }

    private func stepQuickLookAndMenu() async {
        guard let first = pane(1) else { return }
        front.activatePane(first)
        front.open(url("Photos/sunset.png"), disposition: .current, from: first)
        _ = await waitFor(3) { self.selectionNames() == ["sunset.png"] }
        first.currentList?.focus()
        await key(" ", code: kVK_Space)
        let shown = await waitFor(3) { QLPreviewPanel.sharedPreviewPanelExists() && QLPreviewPanel.shared().isVisible }
        if shown { await settle(0.8); await shot("19-quick-look", window: QLPreviewPanel.shared()) }
        await key(" ", code: kVK_Space)
        let closed = await waitFor(3) { !(QLPreviewPanel.sharedPreviewPanelExists() && QLPreviewPanel.shared().isVisible) }
        // Context menu for the selected row, built exactly as a right-click would.
        var titles: [String] = []
        if let outline = first.currentList.flatMap({ findView(id: AccessibilityID.fileList(1), in: $0.view) }) as? NSOutlineView,
           outline.numberOfRows > 0, let window = outline.window {
            let rect = outline.rect(ofRow: max(outline.selectedRow, 0))
            let point = outline.convert(NSPoint(x: rect.midX, y: rect.midY), to: nil)
            if let event = NSEvent.mouseEvent(with: .rightMouseDown, location: point, modifierFlags: [], timestamp: 0,
                                              windowNumber: window.windowNumber, context: nil, eventNumber: 0, clickCount: 1, pressure: 1),
               let menu = outline.menu(for: event) {
                titles = menu.items.filter { !$0.isSeparatorItem }.map(\.title)
            }
        }
        let expected = [tr("Open", "열기"), tr("Get Info", "정보 가져오기"), tr("Quick Look", "훑어보기"), tr("Move to Trash", "휴지통으로 이동")]
        let menuOK = expected.allSatisfy { title in titles.contains { $0.hasPrefix(title) } }
        record("Space toggles Quick Look; context menu has Finder's commands", shown && closed && menuOK,
               "quickLook=\(shown)/\(closed) menu=\(titles.joined(separator: " · "))")
    }

    private func stepLargeFolder() async {
        let large = url("Large")
        try? FileManager.default.createDirectory(at: large, withIntermediateDirectories: true)
        for index in 0..<10_000 {
            FileManager.default.createFile(atPath: large.appendingPathComponent(String(format: "file %05d.txt", index)).path, contents: nil)
        }
        guard let first = pane(1) else { return }
        await settle(3)   // let the creation events drain; this times opening an existing big folder
        let started = Date()
        front.open(large, disposition: .current, from: first)
        let loaded = await waitFor(10) { first.currentFolder?.path == large.path && first.currentList?.loadState == .loaded && first.currentList?.totalItemCount == 10_000 }
        let seconds = Date().timeIntervalSince(started)
        await shot("20-large-folder")
        record("10,000-item folder loads quickly", loaded && seconds < 2.5,
               "loaded=\(loaded) items=\(first.currentList?.totalItemCount ?? -1) seconds=\(String(format: "%.2f", seconds))")
        try? FileManager.default.removeItem(at: large)
    }

    private func stepDarkMode() async {
        NSApp.appearance = NSAppearance(named: .darkAqua)
        await settle(0.6)
        await shot("17-dark")
        NSApp.appearance = nil
        record("dark appearance renders", true, "see screenshot")
    }

    private func stepSession() async {
        SessionCoordinator.shared.saveNow()
        let result = SessionStore.load(from: SessionCoordinator.shared.url)
        let window = result.state.windows.first
        let ok = result.warning == nil && window?.layout.panes.count == front.visiblePaneCount
            && window?.panes.flatMap(\.tabs).contains { $0.path == url("Projects").path } == true
        record("session saves layout and tabs", ok,
               "windows=\(result.state.windows.count) visible=\(window?.layout.panes.count ?? -1) tabs=\(window?.panes.flatMap(\.tabs).count ?? -1)")
    }
}
