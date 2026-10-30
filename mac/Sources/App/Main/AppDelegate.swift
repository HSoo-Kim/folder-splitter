import AppKit
import Carbon.HIToolbox

/// Command-line options. Unknown `-Flag value` pairs (from LaunchServices/Xcode) are skipped.
struct LaunchOptions {
    var sessionURL: URL?
    var indexRoots: [String] = []
    var indexStore: URL?
    var indexDisabled = false
    var qaOutput: URL?
    var language: AppLanguage?
    var folders: [URL] = []

    static func parse(_ arguments: [String]) -> LaunchOptions {
        var options = LaunchOptions()
        var index = 1
        func value() -> String? {
            index += 1
            return index < arguments.count ? arguments[index] : nil
        }
        while index < arguments.count {
            let argument = arguments[index]
            switch argument {
            case "--session": options.sessionURL = value().map { URL(fileURLWithPath: ($0 as NSString).expandingTildeInPath) }
            case "--index-root": if let root = value() { options.indexRoots.append((root as NSString).expandingTildeInPath) }
            case "--index-store": options.indexStore = value().map { URL(fileURLWithPath: ($0 as NSString).expandingTildeInPath) }
            case "--no-index": options.indexDisabled = true
            case "--qa": options.qaOutput = value().map { URL(fileURLWithPath: ($0 as NSString).expandingTildeInPath) }
            case "--language": options.language = value().flatMap(AppLanguage.init(rawValue:))
            default:
                if argument.hasPrefix("-psn_") { break }
                if argument.hasPrefix("-") { index += 1; break }   // -NSSomething YES
                if let url = PathText.resolve(argument, relativeTo: URL(fileURLWithPath: FileManager.default.currentDirectoryPath)) {
                    options.folders.append(url)
                }
            }
            index += 1
        }
        return options
    }
}

@MainActor
final class AppDelegate: NSObject, NSApplicationDelegate, AppCommands, NSMenuItemValidation {
    private(set) var windowControllers: [MainWindowController] = []
    private weak var lastKeyWindow: MainWindowController?
    private var options = LaunchOptions()
    private var search: PathSearchService?
    private var session: SessionCoordinator!
    private var launched = false
    /// Open/reveal requests that arrived before the first window existed.
    private var pending: [(urls: [URL], reveal: Bool)] = []
    private var indexStarted = false
    private var terminationSignal: DispatchSourceSignal?

    var frontController: MainWindowController? {
        (NSApp.keyWindow?.windowController as? MainWindowController)
            ?? (NSApp.mainWindow?.windowController as? MainWindowController)
            ?? lastKeyWindow ?? windowControllers.last
    }

    // MARK: Launch

    func applicationWillFinishLaunching(_ notification: Notification) {
        let events = NSAppleEventManager.shared()
        // "Reveal in Finder" from other apps once Folder Splitter is the default file viewer.
        events.setEventHandler(self, andSelector: #selector(handleReveal(_:reply:)),
                               forEventClass: AEEventClass(kAEMiscStandards), andEventID: AEEventID(kAEMakeObjectsVisible))
        events.setEventHandler(self, andSelector: #selector(handleURL(_:reply:)),
                               forEventClass: AEEventClass(kInternetEventClass), andEventID: AEEventID(kAEGetURL))
    }

    func applicationDidFinishLaunching(_ notification: Notification) {
        options = LaunchOptions.parse(CommandLine.arguments)
        session = SessionCoordinator(url: options.sessionURL ?? SessionStore.defaultURL)
        SessionCoordinator.shared = session
        if let language = options.language {
            var forced = SettingsStore.shared.settings
            forced.language = language
            SettingsStore.shared.load(forced)
        }
        let settings = SettingsStore.shared.settings
        applyAppearance(settings.appearance)

        let roots = options.indexRoots.isEmpty ? ["/"] : options.indexRoots
        let storeURL = options.indexStore
            ?? (options.sessionURL.map { $0.deletingPathExtension().appendingPathExtension("search.index") })
            ?? PathSearchService.defaultStoreURL
        let search = PathSearchService(roots: roots, storeURL: storeURL, excludedFolders: settings.searchExcludedFolders)
        self.search = search
        AppServices.shared = AppServices(search: search, fileOperations: FileOperationCenter(),
                                         favorites: session.favorites, history: session.visits,
                                         viewMemory: session.views)
        installHooks(search)
        NSApp.mainMenu = MainMenu.build()
        NSApp.servicesProvider = self
        NSUpdateDynamicServices()
        session.windowsProvider = { [weak self] in self?.windowControllers.map(\.snapshot) ?? [] }

        if options.qaOutput == nil, settings.restoreSessionOnLaunch, !session.restoredWindows.isEmpty {
            for snapshot in session.restoredWindows { addWindow(MainWindowController(snapshot: snapshot)) }
        } else if pending.isEmpty && options.folders.isEmpty {
            addWindow(MainWindowController(snapshot: nil))
        }
        launched = true
        if !options.folders.isEmpty { pending.append((options.folders, false)) }
        flushPending()
        for controller in windowControllers { controller.showWindow(nil) }
        if let warning = session.loadWarning { frontController?.showNotice(warning) }

        if options.qaOutput == nil {
            GlobalHotKey.shared.apply(settings.globalHotKey) { [weak self] in self?.toggleFromHotKey() }
        }
        NotificationCenter.default.addObserver(self, selector: #selector(settingsChanged(_:)), name: .settingsDidChange, object: nil)

        // `kill` / launchd stop requests quit like ⌘Q, so the session is saved.
        signal(SIGTERM, SIG_IGN)
        let termination = DispatchSource.makeSignalSource(signal: SIGTERM, queue: .main)
        termination.setEventHandler { NSApp.terminate(nil) }
        termination.resume()
        terminationSignal = termination

        if let output = options.qaOutput {
            QAHarness.run(output: output)
        } else if !settings.hasShownWelcome {
            WelcomeWindowController.present { [weak self] in
                SettingsStore.shared.update { $0.hasShownWelcome = true }
                self?.startIndexIfEnabled()
            }
        } else {
            startIndexIfEnabled()
        }
        NSApp.activate()
    }

    private func installHooks(_ search: PathSearchService) {
        UIHooks.showInfo = { InfoWindows.show(for: $0) }
        UIHooks.presentBulkRename = { BulkRenameSheetController.present(for: $0, in: $1) }
        UIHooks.openPreviewWindow = { PreviewWindows.open($0) }
        SettingsHooks.rebuildSearchIndex = { [weak search] in search?.rebuild() }
        SettingsHooks.searchDeniedPaths = { [weak search] in search?.deniedPaths ?? [] }
        SettingsHooks.searchDefaultExclusions = PathSearchService.defaultExclusions
    }

    func startIndexIfEnabled() {
        guard !options.indexDisabled, SettingsStore.shared.settings.searchIndexEnabled, !indexStarted else { return }
        indexStarted = true
        search?.start()
    }

    private func applyAppearance(_ mode: AppearanceMode) {
        switch mode {
        case .system: NSApp.appearance = nil
        case .light: NSApp.appearance = NSAppearance(named: .aqua)
        case .dark: NSApp.appearance = NSAppearance(named: .darkAqua)
        }
    }

    @objc private func settingsChanged(_ note: Notification) {
        let settings = SettingsStore.shared.settings
        guard let old = note.userInfo?["old"] as? AppSettings else { return }
        if old.appearance != settings.appearance { applyAppearance(settings.appearance) }
        if old.language != settings.language {
            // Same mechanism as System Settings ▸ Language ▸ Applications: system-provided
            // strings (kinds, folder names) follow it after the restart Settings offers.
            switch settings.language {
            case .system: UserDefaults.standard.removeObject(forKey: "AppleLanguages")
            case .english: UserDefaults.standard.set(["en"], forKey: "AppleLanguages")
            case .korean: UserDefaults.standard.set(["ko"], forKey: "AppleLanguages")
            }
        }
        if old.globalHotKey != settings.globalHotKey {
            GlobalHotKey.shared.apply(settings.globalHotKey) { [weak self] in self?.toggleFromHotKey() }
        }
        if old.searchExcludedFolders != settings.searchExcludedFolders {
            search?.setExcludedFolders(settings.searchExcludedFolders)
        }
        if old.searchIndexEnabled != settings.searchIndexEnabled {
            if settings.searchIndexEnabled { startIndexIfEnabled() }
            else { search?.stop(); indexStarted = false }
        }
    }

    // MARK: Lifecycle

    func applicationShouldTerminateAfterLastWindowClosed(_ sender: NSApplication) -> Bool { false }

    func applicationShouldHandleReopen(_ sender: NSApplication, hasVisibleWindows flag: Bool) -> Bool {
        if windowControllers.isEmpty { reopenLastWindow() }
        else if !flag { frontController?.showWindow(nil) }
        return true
    }

    func applicationWillTerminate(_ notification: Notification) {
        session?.saveNow()
        search?.stop()
    }

    func applicationSupportsSecureRestorableState(_ app: NSApplication) -> Bool { true }

    private func addWindow(_ controller: MainWindowController) {
        controller.onChange = { [weak self] in self?.session.scheduleSave() }
        windowControllers.append(controller)
        if launched { controller.showWindow(nil) }
        session.scheduleSave()
    }

    func windowWillClose(_ controller: MainWindowController) {
        session.lastClosedWindow = controller.snapshot
        windowControllers.removeAll { $0 === controller }
        session.scheduleSave()
    }

    func windowBecameKey(_ controller: MainWindowController) { lastKeyWindow = controller }

    private func reopenLastWindow() {
        let snapshot = session.lastClosedWindow
        session.lastClosedWindow = nil
        addWindow(MainWindowController(snapshot: snapshot))
        windowControllers.last?.showWindow(nil)
    }

    // MARK: Windows

    @objc func newWindow(_ sender: Any?) {
        let folder = frontController?.activePane.currentFolder
        newWindow(folder: folder, selecting: [])
    }

    func newWindow(folder: URL?, selecting: [URL]) {
        let controller = MainWindowController(snapshot: nil, initialFolder: folder)
        cascade(controller)
        addWindow(controller)
        if !selecting.isEmpty { controller.activePane.currentList?.select(selecting) }
    }

    /// A tab torn off its window (or moved with "Move to New Window").
    func openWindow(with tab: TabState, at screenPoint: NSPoint? = nil) {
        let paneID = UUID()
        let snapshot = WindowSnapshot(frame: nil, layout: .pane(paneID),
                                      panes: [PaneSnapshot(id: paneID, tabs: [tab.snapshot], selectedTab: 0)],
                                      activePane: paneID, isFullScreen: false)
        let controller = MainWindowController(snapshot: snapshot)
        if let screenPoint, let window = controller.window {
            let size = frontController?.window?.frame.size ?? window.frame.size
            window.setFrame(NSRect(x: screenPoint.x - 120, y: screenPoint.y - size.height + 20, width: size.width, height: size.height), display: false)
        } else {
            cascade(controller)
        }
        addWindow(controller)
    }

    private func cascade(_ controller: MainWindowController) {
        guard let front = frontController?.window, let window = controller.window else { return }
        window.setFrame(front.frame, display: false)
        window.setFrameTopLeftPoint(window.cascadeTopLeft(from: NSPoint(x: front.frame.minX, y: front.frame.maxY)))
    }

    func pane(_ id: UUID, inWindow number: Int) -> PaneController? {
        let controller = windowControllers.first { $0.window?.windowNumber == number }
        return controller?.panes[id] ?? windowControllers.lazy.compactMap { $0.panes[id] }.first
    }

    // MARK: Opening from outside

    func application(_ application: NSApplication, open urls: [URL]) {
        pending.append((urls, false))
        flushPending()
    }

    @objc private func handleReveal(_ event: NSAppleEventDescriptor, reply: NSAppleEventDescriptor) {
        pending.append((Self.fileURLs(in: event), true))
        flushPending()
    }

    @objc private func handleURL(_ event: NSAppleEventDescriptor, reply: NSAppleEventDescriptor) {
        guard let text = event.paramDescriptor(forKeyword: keyDirectObject)?.stringValue,
              let components = URLComponents(string: text) else { return }
        let items = components.queryItems ?? []
        let paths = items.filter { $0.name == "path" }.compactMap(\.value)
        let urls = paths.compactMap { PathText.resolve($0) }
        guard !urls.isEmpty else { return }
        switch components.host {
        case "reveal":
            pending.append((urls, true))
            flushPending()
        case "open":
            let place = items.first { $0.name == "in" }?.value ?? "tab"
            guard launched, let front = frontController ?? windowControllers.first else {
                pending.append((urls, false))
                return
            }
            let disposition: OpenDisposition
            switch place {
            case "current": disposition = .current
            case "other": disposition = .otherPane
            case "right": disposition = .newPane(.right)
            case "below": disposition = .newPane(.bottom)
            case "window": disposition = .newWindow
            default: disposition = .newTab(activate: true)
            }
            for url in urls { front.open(url, disposition: disposition, from: nil) }
            NSApp.activate()
        default:
            break
        }
    }

    /// Services ▸ "Open in Folder Splitter" (Finder and other apps).
    @objc(openInFolderSplitter:userData:error:)
    func openInFolderSplitter(_ pasteboard: NSPasteboard, userData: String?, error: AutoreleasingUnsafeMutablePointer<NSString?>) {
        let urls = pasteboard.readObjects(forClasses: [NSURL.self], options: [.urlReadingFileURLsOnly: true]) as? [URL] ?? []
        guard !urls.isEmpty else { return }
        pending.append((urls, false))
        flushPending()
    }

    private func flushPending() {
        guard launched, !pending.isEmpty else { return }
        let requests = pending
        pending = []
        if windowControllers.isEmpty { addWindow(MainWindowController(snapshot: nil)) }
        guard let front = frontController else { return }
        for request in requests {
            if request.reveal {
                front.reveal(request.urls)
            } else {
                for url in request.urls { front.open(url, disposition: .newTab(activate: true), from: nil) }
            }
        }
        front.showWindow(nil)
        NSApp.activate()
    }

    private static func fileURLs(in event: NSAppleEventDescriptor) -> [URL] {
        guard let direct = event.paramDescriptor(forKeyword: keyDirectObject) else { return [] }
        func url(from descriptor: NSAppleEventDescriptor) -> URL? {
            if let url = descriptor.fileURLValue { return url }
            return descriptor.coerce(toDescriptorType: typeFileURL)?.fileURLValue
        }
        if direct.descriptorType == typeAEList {
            guard direct.numberOfItems > 0 else { return [] }
            return (1...direct.numberOfItems).compactMap { direct.atIndex($0).flatMap(url(from:)) }
        }
        return url(from: direct).map { [$0] } ?? []
    }

    // MARK: Global shortcut

    private func toggleFromHotKey() {
        if NSApp.isActive, let window = frontController?.window, window.isVisible, window.isKeyWindow {
            NSApp.hide(nil)
            return
        }
        NSApp.unhide(nil)
        NSApp.activate()
        if windowControllers.isEmpty { reopenLastWindow() }
        else { frontController?.window?.makeKeyAndOrderFront(nil) }
    }

    // MARK: AppCommands

    @objc func showSettings(_ sender: Any?) { SettingsWindowController.shared.show() }
    @objc func showShortcutHelp(_ sender: Any?) { ShortcutHelpWindowController.shared.show() }

    func validateMenuItem(_ item: NSMenuItem) -> Bool { true }
}
