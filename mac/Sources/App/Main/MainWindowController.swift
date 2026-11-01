import AppKit

/// One browser window: sidebar | panes | preview. Owns the pane layout, which pane is active,
/// the closed tab/split history, and every command that spans panes.
@MainActor
final class MainWindowController: NSWindowController, NSWindowDelegate, WindowCommands, NSMenuItemValidation,
                                  PaneLayoutViewDelegate, NSToolbarDelegate {
    static let maxPanes = 4

    private let splitController = NSSplitViewController()
    private let sidebar = SidebarViewController()
    private let preview = PreviewPaneController()
    private let workspace = NSViewController()
    private let layoutView = PaneLayoutView(frame: .zero)
    private let notice = NoticeView()
    private let footer = WindowFooterView(frame: .zero)
    private var workspaceContainer: NSView?
    private var updateNotice: UpdateNoticeView?
    private var sidebarItem: NSSplitViewItem!
    private var previewItem: NSSplitViewItem!

    private(set) var panes: [UUID: PaneController] = [:]
    /// Stable slot order (also of hidden panes), so pane 1 stays first when layouts change.
    private var paneOrder: [UUID] = []
    private(set) var layout: PaneLayoutTree
    private var activePaneID: UUID
    /// Most recently active first; "the other pane" is the first visible entry that isn't active.
    private var paneMRU: [UUID] = []
    private var closeHistory = CloseHistory()
    private var layoutSegments: NSToolbarItemGroup?
    private var lastOpen: Task<Void, Never>?

    var onChange: (() -> Void)?

    init(snapshot: WindowSnapshot?, initialFolder: URL? = nil) {
        let window = MainWindow(contentRect: NSRect(x: 0, y: 0, width: 1280, height: 820),
                              styleMask: [.titled, .closable, .miniaturizable, .resizable, .fullSizeContentView],
                              backing: .buffered, defer: false)
        window.minSize = NSSize(width: 640, height: 420)
        window.toolbarStyle = .unified
        window.titlebarSeparatorStyle = .automatic
        window.tabbingMode = .disallowed
        window.isRestorable = false
        window.collectionBehavior.insert(.fullScreenPrimary)

        if let snapshot, !snapshot.panes.isEmpty {
            let restored = snapshot.panes.prefix(Self.maxPanes).map { PaneController(id: $0.id, snapshot: $0) }
            let valid = Set(restored.map(\.id))
            layout = snapshot.layout.normalized(keeping: valid) ?? .pane(restored[0].id)
            let visible = layout.panes
            activePaneID = snapshot.activePane.flatMap { visible.contains($0) ? $0 : nil } ?? visible[0]
            for pane in restored { panes[pane.id] = pane }
            paneOrder = restored.map(\.id)
        } else {
            let pane = PaneController()
            pane.addTab(folder: initialFolder ?? FileManager.default.homeDirectoryForCurrentUser)
            layout = .pane(pane.id)
            activePaneID = pane.id
            panes[pane.id] = pane
            paneOrder = [pane.id]
        }
        let active = activePaneID
        paneMRU = [active] + paneOrder.filter { $0 != active }
        super.init(window: window)
        for pane in panes.values { pane.windowController = self }
        window.delegate = self
        buildContent()
        buildToolbar()
        if let frame = snapshot?.frame, frame.count == 4,
           NSScreen.screens.contains(where: { $0.visibleFrame.intersects(NSRect(x: frame[0], y: frame[1], width: frame[2], height: frame[3])) }) {
            window.setFrame(NSRect(x: frame[0], y: frame[1], width: max(frame[2], 640), height: max(frame[3], 420)), display: false)
        } else {
            // Assigning contentViewController shrank the window to the empty view; size it like
            // a new Finder window, capped to the screen.
            let visible = NSScreen.main?.visibleFrame.size ?? NSSize(width: 1440, height: 900)
            window.setContentSize(NSSize(width: min(1280, visible.width - 80), height: min(820, visible.height - 60)))
            window.center()
        }
        if OffscreenWindows.enabled { window.setFrameOrigin(OffscreenWindows.origin) }
        // First window ever: give the sidebar Finder's comfortable width instead of its minimum;
        // after that the split view's autosave remembers what the user chose.
        if UserDefaults.standard.object(forKey: "NSSplitView Subview Frames FolderSplitter.MainSplit") == nil,
           !sidebarItem.isCollapsed {
            window.layoutIfNeeded()
            splitController.splitView.setPosition(CGFloat(SettingsStore.shared.settings.sidebarWidth), ofDividerAt: 0)
        }
        applyLayout(animated: false)
        activatePane(panes[activePaneID]!, force: true)
        NotificationCenter.default.addObserver(self, selector: #selector(settingsChanged(_:)), name: .settingsDidChange, object: nil)
        if snapshot?.isFullScreen == true {
            DispatchQueue.main.async { [weak window] in window?.toggleFullScreen(nil) }
        }
    }

    required init?(coder: NSCoder) { fatalError("init(coder:) is not used") }

    // MARK: Building

    private func buildContent() {
        let settings = SettingsStore.shared.settings
        layoutView.delegate = self
        let container = NSView()
        layoutView.translatesAutoresizingMaskIntoConstraints = false
        notice.translatesAutoresizingMaskIntoConstraints = false
        footer.translatesAutoresizingMaskIntoConstraints = false
        container.addSubview(layoutView)
        container.addSubview(footer)
        container.addSubview(notice)
        NSLayoutConstraint.activate([
            layoutView.topAnchor.constraint(equalTo: container.safeAreaLayoutGuide.topAnchor),
            layoutView.leadingAnchor.constraint(equalTo: container.leadingAnchor),
            layoutView.trailingAnchor.constraint(equalTo: container.trailingAnchor),
            layoutView.bottomAnchor.constraint(equalTo: footer.topAnchor),
            footer.leadingAnchor.constraint(equalTo: container.leadingAnchor),
            footer.trailingAnchor.constraint(equalTo: container.trailingAnchor),
            footer.bottomAnchor.constraint(equalTo: container.bottomAnchor),
            footer.heightAnchor.constraint(equalToConstant: WindowFooterView.height),
            notice.centerXAnchor.constraint(equalTo: container.centerXAnchor),
            notice.bottomAnchor.constraint(equalTo: footer.topAnchor, constant: -Metrics.xl - Metrics.statusBarHeight),
            notice.widthAnchor.constraint(lessThanOrEqualTo: container.widthAnchor, constant: -Metrics.xl * 2),
        ])
        workspaceContainer = container
        NotificationCenter.default.addObserver(self, selector: #selector(updateStateChanged), name: .updateStateDidChange, object: nil)
        workspace.view = container

        sidebar.navigator = self
        sidebar.showsFolderTree = settings.showFolderTree
        sidebarItem = NSSplitViewItem(sidebarWithViewController: sidebar)
        sidebarItem.minimumThickness = 160
        sidebarItem.maximumThickness = 420
        sidebarItem.canCollapse = true
        sidebarItem.isCollapsed = !settings.showSidebar
        let workspaceItem = NSSplitViewItem(viewController: workspace)
        workspaceItem.minimumThickness = 320
        previewItem = NSSplitViewItem(inspectorWithViewController: preview)
        previewItem.minimumThickness = 220
        previewItem.maximumThickness = 900
        previewItem.canCollapse = true
        previewItem.isCollapsed = !settings.showPreview
        splitController.splitViewItems = [sidebarItem, workspaceItem, previewItem]
        splitController.splitView.autosaveName = "FolderSplitter.MainSplit"
        contentViewController = splitController
    }

    private static let layoutGroupID = NSToolbarItem.Identifier("layout")
    private static let workspacesID = NSToolbarItem.Identifier("workspaces")
    private static let previewID = NSToolbarItem.Identifier("preview")

    private func buildToolbar() {
        let toolbar = NSToolbar(identifier: "FolderSplitter.Main")
        toolbar.delegate = self
        toolbar.displayMode = .iconOnly
        toolbar.allowsUserCustomization = false
        window?.toolbar = toolbar
    }

    func toolbarDefaultItemIdentifiers(_ toolbar: NSToolbar) -> [NSToolbarItem.Identifier] {
        [.toggleSidebar, .sidebarTrackingSeparator, Self.layoutGroupID, .flexibleSpace, Self.workspacesID,
         .inspectorTrackingSeparator, Self.previewID]
    }

    func toolbarAllowedItemIdentifiers(_ toolbar: NSToolbar) -> [NSToolbarItem.Identifier] {
        toolbarDefaultItemIdentifiers(toolbar)
    }

    func toolbar(_ toolbar: NSToolbar, itemForItemIdentifier id: NSToolbarItem.Identifier,
                 willBeInsertedIntoToolbar flag: Bool) -> NSToolbarItem? {
        switch id {
        case Self.layoutGroupID:
            let presets: [PaneLayoutPreset] = [.one, .two, .threeSideBySide, .four]
            let group = NSToolbarItemGroup(itemIdentifier: id,
                                           images: presets.map { LayoutGlyph.image(for: $0) },
                                           selectionMode: .selectOne,
                                           labels: presets.map(\.title),
                                           target: self, action: #selector(layoutSegmentChosen(_:)))
            group.label = tr("Layout", "레이아웃")
            group.toolTip = tr("Number of panes", "영역 수")
            for (index, preset) in presets.enumerated() {
                group.subitems[index].tag = preset.rawValue
                group.subitems[index].toolTip = preset.title + "  ⌥⌘\(index + 1)"
            }
            layoutSegments = group
            updateLayoutSegments()
            return group
        case Self.workspacesID:
            let item = NSMenuToolbarItem(itemIdentifier: id)
            item.image = NSImage(systemSymbolName: "square.stack.3d.up", accessibilityDescription: nil)
            item.label = tr("Workspaces", "작업 구성")
            item.toolTip = tr("Save and reopen sets of panes and tabs", "영역과 탭 구성을 저장하고 다시 열기")
            item.menu = WorkspaceMenu.make()
            item.showsIndicator = true
            return item
        case Self.previewID:
            let item = NSToolbarItem(itemIdentifier: id)
            item.image = NSImage(systemSymbolName: "sidebar.right", accessibilityDescription: nil)
            item.label = tr("Preview", "미리보기")
            item.toolTip = tr("Show or hide the preview  ⇧⌘P", "미리보기 보기/가리기  ⇧⌘P")
            item.isBordered = true
            item.target = self
            item.action = #selector(togglePreview(_:))
            return item
        default:
            return nil
        }
    }

    @objc private func layoutSegmentChosen(_ sender: NSToolbarItemGroup) {
        let index = sender.selectedIndex
        guard index >= 0, index < sender.subitems.count,
              let preset = PaneLayoutPreset(rawValue: sender.subitems[index].tag) else { return }
        apply(preset)
    }

    private func updateLayoutSegments() {
        guard let group = layoutSegments else { return }
        let count = layout.panes.count
        group.selectedIndex = [1, 2, 3, 4].firstIndex(of: count) ?? -1
    }

    // MARK: Panes

    var activePane: PaneController { panes[activePaneID]! }
    var visiblePaneCount: Int { layout.panes.count }
    var allPanes: [PaneController] { paneOrder.compactMap { panes[$0] } }
    var visiblePanes: [PaneController] { layout.panes.compactMap { panes[$0] } }

    func activatePane(_ pane: PaneController, force: Bool = false) {
        guard force || pane.id != activePaneID, layout.panes.contains(pane.id) else { return }
        activePaneID = pane.id
        paneMRU.removeAll { $0 == pane.id }
        paneMRU.insert(pane.id, at: 0)
        for other in panes.values { other.isActive = other.id == pane.id }
        activePaneChanged()
    }

    func otherPane(of pane: PaneController) -> PaneController? {
        paneMRU.lazy.filter { $0 != pane.id && self.layout.panes.contains($0) }.first.flatMap { panes[$0] }
    }

    private func applyLayout(animated: Bool) {
        let visible = layout.panes
        for (index, id) in visible.enumerated() {
            panes[id]?.number = index + 1
            panes[id]?.canClose = visible.count > 1
        }
        layoutView.apply(layout, animated: animated)
        if !visible.contains(activePaneID), let first = visible.first, let pane = panes[first] {
            activatePane(pane, force: true)
        }
        updateLayoutSegments()
        notifyChange()
    }

    /// Splits `pane` on `edge` with a new empty pane. Returns nil at the four-pane limit.
    @discardableResult
    func split(_ pane: PaneController, edge: SplitEdge) -> PaneController? {
        guard visiblePaneCount < Self.maxPanes else {
            showNotice(tr("Up to four panes fit in a window.", "한 윈도우에서 영역을 최대 4개까지 사용할 수 있습니다."))
            NSSound.beep()
            return nil
        }
        if panes.count >= Self.maxPanes, let hidden = paneOrder.first(where: { !layout.panes.contains($0) }) {
            discardHiddenPane(hidden)
        }
        let newPane = PaneController()
        newPane.windowController = self
        panes[newPane.id] = newPane
        let index = (paneOrder.firstIndex(of: pane.id) ?? paneOrder.count - 1) + 1
        paneOrder.insert(newPane.id, at: min(index, paneOrder.count))
        paneMRU.append(newPane.id)
        layout = layout.splitting(pane.id, with: newPane.id, edge: edge)
        applyLayout(animated: true)
        return newPane
    }

    /// A hidden pane has to make room: its tabs go to the close history so they can be reopened.
    private func discardHiddenPane(_ id: UUID) {
        guard let pane = panes[id] else { return }
        for (index, tab) in pane.snapshot.tabs.enumerated() {
            closeHistory.push(.tab(paneID: id, index: index, tab: tab))
        }
        panes[id] = nil
        paneOrder.removeAll { $0 == id }
        paneMRU.removeAll { $0 == id }
    }

    /// Closes a pane. Its tabs go into the close history with it, so one ⇧⌘T brings the pane
    /// back where it was, tabs included. Closing the last pane closes the window.
    func close(_ pane: PaneController) {
        guard visiblePaneCount > 1, let after = layout.removing(pane.id) else {
            window?.performClose(nil)
            return
        }
        closeHistory.push(.pane(pane: pane.snapshot, layoutBefore: layout, layoutAfter: after))
        removePane(pane.id)
        layout = after
        if let next = paneMRU.first(where: { after.panes.contains($0) }).flatMap({ panes[$0] }) {
            activatePane(next, force: true)
        }
        applyLayout(animated: true)
        refreshReopenStates()
    }

    private func removePane(_ id: UUID) {
        panes[id]?.view.removeFromSuperview()
        panes[id] = nil
        paneOrder.removeAll { $0 == id }
        paneMRU.removeAll { $0 == id }
    }

    private func apply(_ preset: PaneLayoutPreset) {
        guard let shape = LayoutShape(rawValue: preset.rawValue) else { return }
        let count = shape.paneCount
        // Keep the active pane visible (or a pane with tabs if the active one is empty).
        let keep = activePane.tabs.isEmpty
            ? (visiblePanes.first { !$0.tabs.isEmpty }?.id ?? activePaneID)
            : activePaneID
        var chosen = [keep] + paneOrder.filter { $0 != keep }
        while chosen.count < count {
            let pane = PaneController()
            pane.windowController = self
            panes[pane.id] = pane
            paneOrder.append(pane.id)
            paneMRU.append(pane.id)
            chosen.append(pane.id)
        }
        chosen = Array(chosen.prefix(count))
        let ordered = paneOrder.filter { chosen.contains($0) }
        layout = PaneLayoutTree.preset(shape, panes: ordered)
        closeHistory.discardPaneRecords()
        if let pane = panes[keep] { activatePane(pane, force: true) }
        applyLayout(animated: true)
        refreshReopenStates()
    }

    // MARK: Opening

    func open(_ url: URL, disposition: OpenDisposition, from pane: PaneController?) {
        let source = pane ?? activePane
        // Resolve concurrently, apply in request order: opening several folders at once must give
        // the same tab order (and frontmost tab) every time.
        let previous = lastOpen
        lastOpen = Task { @MainActor in
            let target = await LocationResolver.resolve(url)
            await previous?.value
            switch disposition {
            case .current:
                activatePane(source)
                source.navigate(to: target.folder, selecting: target.selection)
            case .newTab(let activate):
                source.addTab(folder: target.folder, selecting: target.selection, activate: activate)
            case .otherPane:
                openInOtherPane(target.folder, selecting: target.selection, from: source)
            case .newPane(let edge):
                if let created = split(source, edge: edge) {
                    activatePane(created)
                    created.addTab(folder: target.folder, selecting: target.selection)
                }
            case .newWindow:
                (NSApp.delegate as? AppDelegate)?.newWindow(folder: target.folder, selecting: target.selection)
            }
        }
    }

    func openInOtherPane(_ folder: URL, selecting: [URL], from pane: PaneController) {
        guard let other = otherPane(of: pane) ?? split(pane, edge: .right) else { return }
        activatePane(other)
        other.show(folder, selecting: selecting)
        other.focusList()
    }

    /// Reveal requests from other apps ("Show in Finder") and the Services menu.
    func reveal(_ urls: [URL]) {
        guard let first = urls.first else { return }
        Task { @MainActor in
            let target = await LocationResolver.resolve(first)
            let extra = urls.dropFirst().filter { $0.deletingLastPathComponent().standardizedFileURL == target.folder }
            activePane.show(target.folder, selecting: target.selection + extra)
            activePane.focusList()
        }
    }

    func searchPriorityRoots(from pane: PaneController) -> [String] {
        var roots: [String] = []
        if let folder = pane.currentFolder { roots.append(folder.path) }
        for other in allPanes { roots += other.openFolders.map(\.path) }
        roots += AppServices.shared.favorites.favorites.map(\.path)
        roots += AppServices.shared.history.frequentFolders
        var seen = Set<String>()
        return roots.filter { seen.insert($0).inserted }
    }

    // MARK: Pane callbacks

    func paneDidChange(_ pane: PaneController) {
        if pane.id == activePaneID { activePaneChanged() }
        notifyChange()
    }

    func paneContentDidChange(_ pane: PaneController) {
        guard pane.id == activePaneID else { return }
        activePaneChanged()
    }

    func paneSelectionDidChange(_ pane: PaneController) {
        guard pane.id == activePaneID else { return }
        if !previewItem.isCollapsed { preview.show(pane.selectedURLs) }
    }

    private func activePaneChanged() {
        let folder = activePane.currentFolder
        window?.title = folder == nil ? "Folder Splitter" : (activePane.selectedTab?.title ?? "Folder Splitter")
        window?.representedURL = folder
        sidebar.followActiveFolder(folder)
        if !previewItem.isCollapsed { preview.show(activePane.selectedURLs) }
    }

    private func notifyChange() { onChange?() }

    // MARK: Close history

    func recordClosedTab(_ tab: TabSnapshot, pane: UUID, index: Int) {
        guard tab.path != nil else { return }   // blank tabs are not worth restoring
        closeHistory.push(.tab(paneID: pane, index: index, tab: tab))
        refreshReopenStates()
    }

    func canReopenTab(in pane: UUID) -> Bool { closeHistory.hasTabs(in: pane) }

    func reopenClosedTab(in pane: PaneController) {
        guard let item = closeHistory.popLatest(tabIn: pane.id) else { return }
        restore(item)
    }

    @objc func reopenClosedTab(_ sender: Any?) {
        guard let item = closeHistory.popLatest() else { NSSound.beep(); return }
        restore(item)
    }

    private func restore(_ item: ClosedItem) {
        switch item {
        case .tab(let paneID, let index, let tab):
            let target = panes[paneID].flatMap { layout.panes.contains($0.id) ? $0 : nil } ?? activePane
            activatePane(target)
            target.addTab(folder: nil, at: index, history: tab)
        case .pane(let snapshot, let before, let after):
            guard visiblePaneCount < Self.maxPanes else { NSSound.beep(); break }
            let pane = PaneController(id: snapshot.id, snapshot: snapshot)
            pane.windowController = self
            panes[pane.id] = pane
            paneOrder.append(pane.id)
            if Self.sameStructure(layout, after) {
                layout = before
            } else {
                layout = layout.splitting(activePaneID, with: pane.id, edge: .right)
            }
            applyLayout(animated: true)
            activatePane(pane)
        }
        refreshReopenStates()
    }

    private func refreshReopenStates() {
        for pane in panes.values where pane.isViewLoaded { pane.refreshReopenState() }
    }

    private static func sameStructure(_ a: PaneLayoutTree, _ b: PaneLayoutTree) -> Bool {
        switch (a, b) {
        case (.pane(let x), .pane(let y)): return x == y
        case (.split(let va, _, let fa, let sa), .split(let vb, _, let fb, let sb)):
            return va == vb && sameStructure(fa, fb) && sameStructure(sa, sb)
        default: return false
        }
    }

    // MARK: Tab moves

    func acceptTab(_ payload: TabDragPayload, into target: PaneController, at index: Int?) -> Bool {
        guard let source = (NSApp.delegate as? AppDelegate)?.pane(payload.paneID, inWindow: payload.windowNumber) else { return false }
        if source === target {
            guard let from = target.tabs.firstIndex(where: { $0.id == payload.tabID }) else { return false }
            let destination = min(index ?? target.tabs.count, target.tabs.count)
            target.tabBar(tabBarPlaceholder, didMove: payload.tabID, to: destination > from ? destination - 1 : destination)
            return true
        }
        guard let detached = source.detachTab(payload.tabID) else { return false }
        target.insert(detached.tab, at: index)
        if let window = target.view.window { window.makeKeyAndOrderFront(nil) }
        activatePane(target)
        return true
    }

    /// Tab bars reorder themselves; this placeholder only satisfies the delegate signature.
    private var tabBarPlaceholder: TabBarView { TabBarView(frame: .zero) }

    func moveTab(_ tabID: UUID, from source: PaneController, toNewPaneAt edge: SplitEdge) {
        guard let created = split(source, edge: edge), let detached = source.detachTab(tabID) else { return }
        created.insert(detached.tab, at: nil)
        activatePane(created)
    }

    // MARK: Notices

    func showNotice(_ text: String) { notice.show(text) }

    // MARK: Update notice

    @objc private func updateStateChanged() { refreshUpdateNotice() }

    /// Only the front main window shows the update card; it rises out of the footer and sinks back.
    func refreshUpdateNotice() {
        let updates = UpdateController.shared
        let front = (NSApp.delegate as? AppDelegate)?.frontController === self
        let wanted = updates.wantsNotice && front && (window?.isVisible ?? false)
        guard let container = workspaceContainer else { return }
        if wanted, let release = updates.available {
            let card = updateNotice ?? {
                let card = UpdateNoticeView(frame: .zero)
                card.translatesAutoresizingMaskIntoConstraints = false
                container.addSubview(card)
                NSLayoutConstraint.activate([
                    card.trailingAnchor.constraint(equalTo: container.trailingAnchor, constant: -Metrics.l),
                    card.bottomAnchor.constraint(equalTo: footer.topAnchor, constant: -Metrics.l),
                ])
                updateNotice = card
                card.alphaValue = 0
                container.layoutSubtreeIfNeeded()
                card.layer?.transform = CATransform3DMakeTranslation(0, -12, 0)
                return card
            }()
            card.configure(release, current: updates.current, installable: updates.canInstallAvailable)
            Motion.animate(response: Motion.standardResponse, {
                card.animator().alphaValue = 1
                card.layer?.transform = CATransform3DIdentity
            })
        } else if let card = updateNotice {
            updateNotice = nil
            Motion.animate(response: Motion.quickResponse, {
                card.animator().alphaValue = 0
                card.layer?.transform = CATransform3DMakeTranslation(0, -12, 0)
            }, completion: { card.removeFromSuperview() })
        }
    }

    // MARK: Compare

    func compare(from pane: PaneController) {
        guard let window, let left = pane.currentFolder, let other = otherPane(of: pane), let right = other.currentFolder else { return }
        CompareSheetController.present(left: left, right: right, in: window) { [weak pane, weak other] leftItems, rightItems in
            // Each call carries one side; leave the other pane's selection alone.
            if !leftItems.isEmpty { pane?.currentList?.select(leftItems) }
            if !rightItems.isEmpty { other?.currentList?.select(rightItems) }
        }
    }

    // MARK: Snapshot

    var snapshot: WindowSnapshot {
        let frame = window?.frame ?? .zero
        let isFullScreen = window?.styleMask.contains(.fullScreen) ?? false
        return WindowSnapshot(frame: [frame.minX, frame.minY, frame.width, frame.height],
                              layout: layout, panes: allPanes.map(\.snapshot), activePane: activePaneID,
                              isFullScreen: isFullScreen)
    }

    var workspaceSnapshot: (layout: PaneLayoutTree, panes: [PaneSnapshot], active: UUID) {
        (layout, visiblePanes.map(\.snapshot), activePaneID)
    }

    /// Replaces this window's panes with a saved workspace.
    func load(_ workspace: Workspace) {
        for id in paneOrder { removePane(id) }
        let restored = workspace.panes.prefix(Self.maxPanes).map { PaneController(id: $0.id, snapshot: $0) }
        guard !restored.isEmpty else { return }
        for pane in restored {
            pane.windowController = self
            panes[pane.id] = pane
        }
        paneOrder = restored.map(\.id)
        layout = workspace.layout.normalized(keeping: Set(paneOrder)) ?? .pane(restored[0].id)
        let active = workspace.activePane.flatMap { layout.panes.contains($0) ? $0 : nil } ?? layout.panes[0]
        paneMRU = [active] + paneOrder.filter { $0 != active }
        closeHistory.discardPaneRecords()
        activePaneID = active
        applyLayout(animated: true)
        if let pane = panes[active] { activatePane(pane, force: true) }
    }

    // MARK: PaneLayoutViewDelegate

    func paneLayoutView(_ view: PaneLayoutView, viewFor pane: UUID) -> NSView {
        panes[pane]!.view
    }

    func paneLayoutView(_ view: PaneLayoutView, didChangeRatio ratio: Double, at path: [Int], animated: Bool) {
        layout = layout.settingRatio(ratio, at: path)
        layoutView.apply(layout, animated: animated)
        notifyChange()
    }

    func paneLayoutView(_ view: PaneLayoutView, canAccept payload: TabDragPayload, on pane: UUID, zone: DockZone) -> Bool {
        switch zone {
        case .center: return payload.paneID != pane
        case .edge: return visiblePaneCount < Self.maxPanes
        }
    }

    func paneLayoutView(_ view: PaneLayoutView, accept payload: TabDragPayload, on paneID: UUID, zone: DockZone) -> Bool {
        guard let target = panes[paneID] else { return false }
        switch zone {
        case .center:
            return acceptTab(payload, into: target, at: nil)
        case .edge(let edge):
            guard let created = split(target, edge: edge) else { return false }
            if acceptTab(payload, into: created, at: nil) { return true }
            close(created)
            return false
        }
    }

    // MARK: WindowCommands

    @objc func setPaneLayout(_ sender: Any?) {
        guard let tag = (sender as? NSMenuItem)?.tag ?? (sender as? NSToolbarItem)?.tag,
              let preset = PaneLayoutPreset(rawValue: tag) else { return }
        apply(preset)
    }

    @objc func equalizePanes(_ sender: Any?) {
        layout = layout.equalized()
        layoutView.apply(layout, animated: true)
        notifyChange()
    }

    @objc func focusNextPane(_ sender: Any?) { cyclePane(1) }
    @objc func focusPreviousPane(_ sender: Any?) { cyclePane(-1) }

    private func cyclePane(_ step: Int) {
        let visible = layout.panes
        guard visible.count > 1, let index = visible.firstIndex(of: activePaneID), let pane = panes[visible[(index + step + visible.count) % visible.count]] else { return }
        activatePane(pane)
        pane.focusList()
    }

    @objc func focusPane(_ sender: Any?) {
        guard let number = (sender as? NSMenuItem)?.tag else { return }
        let visible = layout.panes
        guard number >= 1, number <= visible.count, let pane = panes[visible[number - 1]] else {
            showNotice(tr("Pane \(number) isn't open.", "영역 \(number)이(가) 열려 있지 않습니다."))
            return
        }
        activatePane(pane)
        pane.focusList()
    }

    @objc func closePane(_ sender: Any?) {
        guard visiblePaneCount > 1 else { return }
        close(activePane)
    }

    @objc func toggleSidebar(_ sender: Any?) {
        SettingsStore.shared.update { $0.showSidebar.toggle() }
    }

    @objc func toggleFolderTree(_ sender: Any?) { SettingsStore.shared.update { $0.showFolderTree.toggle() } }
    @objc func togglePreview(_ sender: Any?) { SettingsStore.shared.update { $0.showPreview.toggle() } }
    @objc func toggleStatusBar(_ sender: Any?) { SettingsStore.shared.update { $0.showStatusBar.toggle() } }
    @objc func toggleHiddenFiles(_ sender: Any?) { SettingsStore.shared.update { $0.showHiddenFiles.toggle() } }
    @objc func toggleFilenameExtensions(_ sender: Any?) { SettingsStore.shared.update { $0.showAllExtensions.toggle() } }
    @objc func toggleFoldersFirst(_ sender: Any?) { SettingsStore.shared.update { $0.foldersFirst.toggle() } }

    @objc func increaseTextSize(_ sender: Any?) { stepTextSize(1) }
    @objc func decreaseTextSize(_ sender: Any?) { stepTextSize(-1) }

    private func stepTextSize(_ step: Int) {
        let sizes = TextSize.allCases
        let current = sizes.firstIndex(of: SettingsStore.shared.settings.textSize) ?? 1
        let next = min(max(current + step, 0), sizes.count - 1)
        guard next != current else { NSSound.beep(); return }
        SettingsStore.shared.update { $0.textSize = sizes[next] }
    }

    @objc func saveWorkspace(_ sender: Any?) {
        guard let window else { return }
        let store = SessionCoordinator.shared.workspaces
        let suggested = activePane.currentFolder.map { _ in activePane.selectedTab?.title ?? "" } ?? tr("Workspace", "작업 구성")
        WorkspaceSheets.askName(in: window, suggested: suggested, existing: store.workspaces.map(\.name)) { [weak self] name in
            guard let self, let name else { return }
            let current = self.workspaceSnapshot
            store.save(Workspace(name: name, layout: current.layout, panes: current.panes,
                                 activePane: current.active, savedAt: Date()))
            self.showNotice(tr("Saved “\(name)”.", "“\(name)”을(를) 저장했습니다."))
        }
    }

    @objc func openWorkspace(_ sender: Any?) {
        guard let name = (sender as? NSMenuItem)?.representedObject as? String,
              let workspace = SessionCoordinator.shared.workspaces.workspaces.first(where: { $0.name == name }) else { return }
        load(workspace)
    }

    @objc func manageWorkspaces(_ sender: Any?) {
        guard let window else { return }
        WorkspaceSheets.manage(in: window, store: SessionCoordinator.shared.workspaces) { [weak self] workspace in
            self?.load(workspace)
        }
    }

    func validateMenuItem(_ item: NSMenuItem) -> Bool {
        let settings = SettingsStore.shared.settings
        switch item.action {
        case #selector(reopenClosedTab(_:)): return !closeHistory.isEmpty
        case #selector(setPaneLayout(_:)):
            let shape = LayoutShape(rawValue: item.tag)
            item.state = shape != nil && layout.shape == shape ? .on : .off
            return true
        case #selector(focusNextPane(_:)), #selector(focusPreviousPane(_:)), #selector(equalizePanes(_:)), #selector(closePane(_:)):
            return visiblePaneCount > 1
        case #selector(focusPane(_:)): return item.tag <= visiblePaneCount
        case #selector(toggleSidebar(_:)):
            item.title = settings.showSidebar ? tr("Hide Sidebar", "사이드바 가리기") : tr("Show Sidebar", "사이드바 보기")
        case #selector(toggleFolderTree(_:)): item.state = settings.showFolderTree ? .on : .off
        case #selector(togglePreview(_:)):
            item.title = settings.showPreview ? tr("Hide Preview", "미리보기 가리기") : tr("Show Preview", "미리보기 보기")
        case #selector(toggleStatusBar(_:)):
            item.title = settings.showStatusBar ? tr("Hide Status Bar", "상태 막대 가리기") : tr("Show Status Bar", "상태 막대 보기")
        case #selector(toggleHiddenFiles(_:)):
            item.title = settings.showHiddenFiles ? tr("Hide Hidden Files", "숨김 파일 가리기") : tr("Show Hidden Files", "숨김 파일 보기")
        case #selector(toggleFilenameExtensions(_:)): item.state = settings.showAllExtensions ? .on : .off
        case #selector(toggleFoldersFirst(_:)): item.state = settings.foldersFirst ? .on : .off
        case #selector(increaseTextSize(_:)): return settings.textSize != TextSize.allCases.last
        case #selector(decreaseTextSize(_:)): return settings.textSize != TextSize.allCases.first
        case #selector(manageWorkspaces(_:)): return !SessionCoordinator.shared.workspaces.workspaces.isEmpty
        default: break
        }
        return true
    }

    // MARK: Settings

    @objc private func settingsChanged(_ note: Notification) {
        let settings = SettingsStore.shared.settings
        let old = note.userInfo?["old"] as? AppSettings
        if old?.showSidebar != settings.showSidebar {
            Motion.animate({ sidebarItem.animator().isCollapsed = !settings.showSidebar })
        }
        if old?.showPreview != settings.showPreview {
            Motion.animate({ previewItem.animator().isCollapsed = !settings.showPreview })
            if settings.showPreview { preview.show(activePane.selectedURLs) }
        }
        if old?.showFolderTree != settings.showFolderTree { sidebar.showsFolderTree = settings.showFolderTree }
    }

    // MARK: Command routing

    /// File and pane commands belong to the active pane even when focus is elsewhere in the
    /// window (sidebar, toolbar, nothing): AppKit asks here once the chain runs out.
    override func supplementalTarget(forAction action: Selector, sender: Any?) -> Any? {
        // While a text field is being edited, file commands stay off: ⌘⌫ must delete text,
        // never move the selected files to the Trash.
        if Self.fileCommands.contains(action) {
            guard !(window?.firstResponder is NSText), let list = activePane.currentList else { return nil }
            return list
        }
        if Self.paneCommands.contains(action) { return activePane }
        return super.supplementalTarget(forAction: action, sender: sender)
    }

    private static let fileCommands = selectors(in: FileCommands.self)
    private static let paneCommands = selectors(in: PaneCommands.self)

    private static func selectors(in proto: Protocol) -> Set<Selector> {
        var count: UInt32 = 0
        guard let list = protocol_copyMethodDescriptionList(proto, true, true, &count) else { return [] }
        defer { free(list) }
        return Set((0..<Int(count)).compactMap { list[$0].name })
    }

    // MARK: Keys not on the menu

    override func keyDown(with event: NSEvent) {
        // F6 / ⇧F6 move between panes (Folder Splitter for Windows habit).
        if event.keyCode == 97 {
            event.modifierFlags.contains(.shift) ? focusPreviousPane(nil) : focusNextPane(nil)
            return
        }
        super.keyDown(with: event)
    }

    // MARK: NSWindowDelegate

    func windowWillReturnUndoManager(_ window: NSWindow) -> UndoManager? {
        AppServices.shared.fileOperations.undoManager
    }

    func windowWillClose(_ notification: Notification) {
        (NSApp.delegate as? AppDelegate)?.windowWillClose(self)
    }

    func windowDidResize(_ notification: Notification) { notifyChange() }
    func windowDidMove(_ notification: Notification) { notifyChange() }

    func windowDidBecomeKey(_ notification: Notification) {
        (NSApp.delegate as? AppDelegate)?.windowBecameKey(self)
        // Keyboard goes to the active pane's list unless something inside already has focus.
        if window?.firstResponder === window { activePane.focusList() }
        // Every window re-evaluates, so the update card follows the front main window.
        NotificationCenter.default.post(name: .updateStateDidChange, object: nil)
    }
}

// MARK: - NavigationHandling (sidebar)

extension MainWindowController: NavigationHandling {
    func open(_ url: URL, disposition: OpenDisposition) {
        open(url, disposition: disposition, from: activePane)
    }
}

extension PaneLayoutPreset {
    var title: String {
        switch self {
        case .one: return tr("1 Pane", "영역 1개")
        case .two: return tr("2 Panes Side by Side", "영역 2개 (좌우)")
        case .twoStacked: return tr("2 Panes Stacked", "영역 2개 (위아래)")
        case .threeSideBySide: return tr("3 Panes (Large Left)", "영역 3개 (왼쪽 크게)")
        case .threeStacked: return tr("3 Panes (Large Top)", "영역 3개 (위쪽 크게)")
        case .threeColumns: return tr("3 Columns", "영역 3개 (세로 3열)")
        case .four: return tr("4 Panes", "영역 4개")
        }
    }
}

/// Template images that draw each layout exactly as it will appear.
enum LayoutGlyph {
    static func image(for preset: PaneLayoutPreset) -> NSImage {
        let size = NSSize(width: 20, height: 16)
        let image = NSImage(size: size, flipped: true) { rect in
            let frame = rect.insetBy(dx: 1.5, dy: 1.5)
            NSColor.black.setStroke()
            let outline = NSBezierPath(roundedRect: frame, xRadius: 2.5, yRadius: 2.5)
            outline.lineWidth = 1.3
            outline.stroke()
            let path = NSBezierPath()
            path.lineWidth = 1.3
            func v(_ x: CGFloat, _ y0: CGFloat = frame.minY, _ y1: CGFloat = frame.maxY) {
                path.move(to: NSPoint(x: x, y: y0)); path.line(to: NSPoint(x: x, y: y1))
            }
            func h(_ y: CGFloat, _ x0: CGFloat = frame.minX, _ x1: CGFloat = frame.maxX) {
                path.move(to: NSPoint(x: x0, y: y)); path.line(to: NSPoint(x: x1, y: y))
            }
            switch preset {
            case .one: break
            case .two: v(frame.midX)
            case .twoStacked: h(frame.midY)
            case .threeSideBySide: v(frame.midX); h(frame.midY, frame.midX)
            case .threeStacked: h(frame.midY); v(frame.midX, frame.midY)
            case .threeColumns: v(frame.minX + frame.width / 3); v(frame.minX + frame.width * 2 / 3)
            case .four: v(frame.midX); h(frame.midY)
            }
            path.stroke()
            return true
        }
        image.isTemplate = true
        image.accessibilityDescription = preset.title
        return image
    }
}

/// Small translucent capsule for transient status ("Pane 3 isn't open."), rising from the
/// bottom and fading away. Never steals focus or blocks clicks.
@MainActor
final class NoticeView: NSVisualEffectView {
    private let label = NSTextField(labelWithString: "")
    private var hideWork: DispatchWorkItem?

    override init(frame frameRect: NSRect) {
        super.init(frame: frameRect)
        material = .hudWindow
        blendingMode = .withinWindow
        state = .active
        wantsLayer = true
        layer?.cornerRadius = Metrics.largeRadius
        layer?.cornerCurve = .continuous
        label.font = .systemFont(ofSize: 13, weight: .medium)
        label.textColor = .labelColor
        label.lineBreakMode = .byTruncatingTail
        label.translatesAutoresizingMaskIntoConstraints = false
        addSubview(label)
        NSLayoutConstraint.activate([
            label.leadingAnchor.constraint(equalTo: leadingAnchor, constant: Metrics.l),
            label.trailingAnchor.constraint(equalTo: trailingAnchor, constant: -Metrics.l),
            label.topAnchor.constraint(equalTo: topAnchor, constant: Metrics.s),
            label.bottomAnchor.constraint(equalTo: bottomAnchor, constant: -Metrics.s),
        ])
        alphaValue = 0
        isHidden = true
        setAccessibilityRole(.staticText)
    }

    required init?(coder: NSCoder) { fatalError("init(coder:) is not used") }

    override func hitTest(_ point: NSPoint) -> NSView? { nil }

    func show(_ text: String) {
        label.stringValue = text
        isHidden = false
        if Motion.reduceTransparency {
            material = .windowBackground
        }
        NSAccessibility.post(element: self, notification: .announcementRequested,
                             userInfo: [.announcement: text, .priority: NSAccessibilityPriorityLevel.medium.rawValue])
        Motion.animate(response: Motion.quickResponse, { animator().alphaValue = 1 })
        hideWork?.cancel()
        let work = DispatchWorkItem { [weak self] in
            guard let self else { return }
            Motion.animate(response: Motion.standardResponse, { self.animator().alphaValue = 0 }, completion: {
                if self.alphaValue == 0 { self.isHidden = true }
            })
        }
        hideWork = work
        DispatchQueue.main.asyncAfter(deadline: .now() + 2.6, execute: work)
    }
}
