import AppKit

private final class SidebarNode: NSObject {
    let url: URL?
    var title: String
    var icon: NSImage?
    var children: [SidebarNode] = []
    var loaded = false
    var loadTask: Task<([SidebarEntry], String?), Never>?
    var missing = false
    var ejectable = false
    var error: String?
    let section: Bool
    init(_ title: String, url: URL? = nil, section: Bool = false) {
        self.title = title; self.url = url; self.section = section
    }
}

private final class SidebarOutline: NSOutlineView {
    var openRow: ((Int, Bool) -> Void)?
    var contextMenu: ((Int) -> NSMenu?)?
    var dragFinished: (() -> Void)?
    var lostFocus: (() -> Void)?
    override func resignFirstResponder() -> Bool {
        let result = super.resignFirstResponder()
        if result { lostFocus?() }; return result
    }
    override func draggingExited(_ sender: NSDraggingInfo?) { dragFinished?(); super.draggingExited(sender) }
    override func draggingEnded(_ sender: NSDraggingInfo) { dragFinished?(); super.draggingEnded(sender) }
    override func keyDown(with event: NSEvent) {
        if event.keyCode == 36 || event.keyCode == 76 {
            openRow?(selectedRow, event.modifierFlags.contains(.command)); return
        }
        super.keyDown(with: event)
    }
    override func otherMouseDown(with event: NSEvent) {
        if event.buttonNumber == 2 { openRow?(row(at: convert(event.locationInWindow, from: nil)), true) }
        else { super.otherMouseDown(with: event) }
    }
    override func menu(for event: NSEvent) -> NSMenu? {
        contextMenu?(row(at: convert(event.locationInWindow, from: nil)))
    }
}

private final class SidebarCell: NSTableCellView {
    let eject = NSButton()
    var onEject: (() -> Void)?
    private var titleToEject: NSLayoutConstraint!
    private var titleToEdge: NSLayoutConstraint!
    override init(frame: NSRect) {
        super.init(frame: frame)
        let icon = NSImageView(); icon.imageScaling = .scaleProportionallyDown
        let label = NSTextField(labelWithString: ""); label.lineBreakMode = .byTruncatingTail
        imageView = icon; textField = label
        eject.image = NSImage(systemSymbolName: "eject", accessibilityDescription: tr("Eject", "추출"))
        eject.bezelStyle = .inline; eject.isBordered = false; eject.target = self; eject.action = #selector(ejectPressed)
        eject.isHidden = true
        for child in [icon, label, eject] { child.translatesAutoresizingMaskIntoConstraints = false; addSubview(child) }
        // Cells are briefly laid out narrower than icon + spacing; the title edge yields (999)
        // so that never makes the constraints unsatisfiable.
        titleToEject = label.trailingAnchor.constraint(equalTo: eject.leadingAnchor, constant: -2)
        titleToEject.priority = NSLayoutConstraint.Priority(999)
        titleToEdge = label.trailingAnchor.constraint(equalTo: trailingAnchor, constant: -2)
        titleToEdge.priority = NSLayoutConstraint.Priority(999)
        NSLayoutConstraint.activate([
            icon.leadingAnchor.constraint(equalTo: leadingAnchor, constant: 2), icon.centerYAnchor.constraint(equalTo: centerYAnchor),
            icon.widthAnchor.constraint(equalToConstant: 18), icon.heightAnchor.constraint(equalToConstant: 18),
            label.leadingAnchor.constraint(equalTo: icon.trailingAnchor, constant: 7), label.centerYAnchor.constraint(equalTo: centerYAnchor),
            titleToEdge,
            eject.trailingAnchor.constraint(equalTo: trailingAnchor, constant: -2), eject.centerYAnchor.constraint(equalTo: centerYAnchor),
            eject.widthAnchor.constraint(equalToConstant: 20)
        ])
    }
    required init?(coder: NSCoder) { fatalError("init(coder:) has not been implemented") }
    func setEjectable(_ ejectable: Bool) {
        eject.isHidden = !ejectable
        NSLayoutConstraint.deactivate([titleToEject, titleToEdge])
        (ejectable ? titleToEject : titleToEdge)?.isActive = true
    }
    @objc private func ejectPressed() { onEject?() }
}

private final class SidebarHeader: NSView {
    let toggle = NSButton()
    var onToggle: (() -> Void)?
    init(title: String, expanded: Bool) {
        super.init(frame: .zero)
        let label = NSTextField(labelWithString: title); label.font = Typography.sectionHeader; label.textColor = .secondaryLabelColor
        toggle.title = expanded ? tr("Hide", "가리기") : tr("Show", "보기")
        toggle.bezelStyle = .inline; toggle.font = .systemFont(ofSize: 10); toggle.isBordered = false
        toggle.target = self; toggle.action = #selector(toggleSection); toggle.alphaValue = 0
        toggle.setAccessibilityLabel(tr("\(title), \(toggle.title)", "\(title), \(toggle.title)"))
        for child in [label, toggle] { child.translatesAutoresizingMaskIntoConstraints = false; addSubview(child) }
        NSLayoutConstraint.activate([
            label.leadingAnchor.constraint(equalTo: leadingAnchor), label.centerYAnchor.constraint(equalTo: centerYAnchor),
            toggle.trailingAnchor.constraint(equalTo: trailingAnchor, constant: -4), toggle.centerYAnchor.constraint(equalTo: centerYAnchor),
            label.trailingAnchor.constraint(lessThanOrEqualTo: toggle.leadingAnchor, constant: -4)
        ])
        addTrackingArea(NSTrackingArea(rect: .zero, options: [.mouseEnteredAndExited, .activeInKeyWindow, .inVisibleRect], owner: self))
    }
    required init?(coder: NSCoder) { fatalError("init(coder:) has not been implemented") }
    override func mouseEntered(with event: NSEvent) { toggle.alphaValue = 1 }
    override func mouseExited(with event: NSEvent) { toggle.alphaValue = 0 }
    @objc private func toggleSection() { onToggle?() }
}

/// Host as an NSSplitViewController sidebar item to inherit the system sidebar material.
/// AppServices must be installed before loading the view. Call followActiveFolder whenever
/// the active pane changes; navigation never changes keyboard focus during tree revelation.
@MainActor final class SidebarViewController: NSViewController, NSOutlineViewDataSource, NSOutlineViewDelegate, NSSplitViewDelegate {
    weak var navigator: NavigationHandling?
    var showsFolderTree = SettingsStore.shared.settings.showFolderTree {
        didSet { if showsFolderTree != oldValue, isViewLoaded { updateTreeVisibility(animated: true) } }
    }
    private let split = NSSplitView()
    private let sources = SidebarOutline()
    private let tree = SidebarOutline()
    private let treeContainer = NSView()
    private var sections: [SidebarNode] = []
    private var roots: [SidebarNode] = []
    private var activeFolder: URL?
    private var refreshGeneration = 0
    private var revealGeneration = 0
    private var observers: [NSObjectProtocol] = []
    private var workspaceObservers: [NSObjectProtocol] = []
    private var springTask: Task<Void, Never>?
    private var springURL: URL?
    private var savedFraction: CGFloat = 0.5
    private var treeVisibilityGeneration = 0
    private var animatingTree = false
    private var needsSplitLayout = true
    private var hasAppliedSplitLayout = false
    private var applyingSplitLayout = false
    private static let favoriteDrag = NSPasteboard.PasteboardType("io.github.hsoo-kim.FolderSplitter.favorite")

    override func loadView() {
        view = NSView(); view.setAccessibilityIdentifier(AccessibilityID.sidebar)
        split.isVertical = false; split.dividerStyle = .thin; split.delegate = self
        split.translatesAutoresizingMaskIntoConstraints = false; view.addSubview(split)
        NSLayoutConstraint.activate([split.leadingAnchor.constraint(equalTo: view.leadingAnchor), split.trailingAnchor.constraint(equalTo: view.trailingAnchor), split.topAnchor.constraint(equalTo: view.safeAreaLayoutGuide.topAnchor), split.bottomAnchor.constraint(equalTo: view.bottomAnchor)])
        let sourceScroll = makeOutline(sources, identifier: AccessibilityID.sidebar)
        let treeScroll = makeOutline(tree, identifier: AccessibilityID.folderTree)
        split.addArrangedSubview(sourceScroll); split.addArrangedSubview(treeContainer)
        let heading = NSTextField(labelWithString: tr("Folder Tree", "폴더 트리")); heading.font = Typography.sectionHeader; heading.textColor = .secondaryLabelColor
        heading.translatesAutoresizingMaskIntoConstraints = false; treeScroll.translatesAutoresizingMaskIntoConstraints = false
        treeContainer.addSubview(heading); treeContainer.addSubview(treeScroll)
        // The split view starts this container at zero height; let the bottom edge yield until it grows.
        let treeBottom = treeScroll.bottomAnchor.constraint(equalTo: treeContainer.bottomAnchor)
        treeBottom.priority = NSLayoutConstraint.Priority(999)
        NSLayoutConstraint.activate([heading.leadingAnchor.constraint(equalTo: treeContainer.leadingAnchor, constant: 16), heading.topAnchor.constraint(equalTo: treeContainer.topAnchor, constant: 10), treeScroll.topAnchor.constraint(equalTo: heading.bottomAnchor, constant: 6), treeScroll.leadingAnchor.constraint(equalTo: treeContainer.leadingAnchor), treeScroll.trailingAnchor.constraint(equalTo: treeContainer.trailingAnchor), treeBottom])
        let fraction = UserDefaults.standard.double(forKey: "sidebarTreeFraction")
        if fraction.isFinite, fraction > 0 { savedFraction = min(0.85, max(0.2, fraction)) }
        for name in [Notification.Name.favoritesDidChange, .folderHistoryDidChange, .settingsDidChange] {
            observers.append(NotificationCenter.default.addObserver(forName: name, object: nil, queue: .main) { [weak self] notification in
                MainActor.assumeIsolated {
                    guard let self else { return }
                    if notification.name == .settingsDidChange { self.showsFolderTree = SettingsStore.shared.settings.showFolderTree }
                    self.reloadSources()
                }
            })
        }
        for name in [NSWorkspace.didMountNotification, NSWorkspace.didUnmountNotification, NSWorkspace.didRenameVolumeNotification] {
            workspaceObservers.append(NSWorkspace.shared.notificationCenter.addObserver(forName: name, object: nil, queue: .main) { [weak self] _ in
                MainActor.assumeIsolated { self?.reloadSources() }
            })
        }
        reloadSources(); updateTreeVisibility(animated: false)
    }
    deinit {
        for token in observers { NotificationCenter.default.removeObserver(token) }
        for token in workspaceObservers { NSWorkspace.shared.notificationCenter.removeObserver(token) }
        springTask?.cancel()
    }
    override func viewDidLayout() {
        super.viewDidLayout()
        guard !animatingTree, needsSplitLayout || !showsFolderTree else { return }
        applySplitLayout()
    }
    private func applySplitLayout() {
        guard split.bounds.height > 0, !applyingSplitLayout else { return }
        applyingSplitLayout = true
        treeContainer.isHidden = !showsFolderTree
        split.adjustSubviews()
        if showsFolderTree {
            split.setPosition(split.bounds.height * savedFraction, ofDividerAt: 0)
        } else {
            // A plain NSSplitView can retain a hidden arranged subview's old allocation.
            split.subviews[0].frame = split.bounds
        }
        needsSplitLayout = false; hasAppliedSplitLayout = true
        applyingSplitLayout = false
    }
    private func makeOutline(_ outline: SidebarOutline, identifier: String) -> NSScrollView {
        let column = NSTableColumn(identifier: NSUserInterfaceItemIdentifier("name")); outline.addTableColumn(column); outline.outlineTableColumn = column
        outline.headerView = nil; outline.style = .sourceList; outline.backgroundColor = .clear
        outline.rowHeight = 27; outline.indentationPerLevel = 13; outline.floatsGroupRows = false
        outline.dataSource = self; outline.delegate = self; outline.target = self; outline.action = #selector(clicked(_:))
        outline.setAccessibilityIdentifier(identifier)
        outline.openRow = { [weak self, weak outline] row, newTab in
            guard let self, let outline else { return }; self.open(row: row, in: outline, newTab: newTab)
        }
        outline.contextMenu = { [weak self, weak outline] row in
            guard let self, let outline, let node = outline.item(atRow: row) as? SidebarNode else { return nil }
            return self.menu(for: node)
        }
        outline.dragFinished = { [weak self] in self?.cancelSpring() }
        outline.lostFocus = { [weak self] in self?.synchronizeSelection() }
        outline.registerForDraggedTypes([.fileURL, Self.favoriteDrag]); outline.setDraggingSourceOperationMask(.move, forLocal: true)
        let scroll = NSScrollView(); scroll.drawsBackground = false; scroll.hasVerticalScroller = true; scroll.autohidesScrollers = true; scroll.documentView = outline
        // The split already starts below the toolbar; automatic scroll insets would add it twice.
        scroll.automaticallyAdjustsContentInsets = false
        scroll.contentInsets = NSEdgeInsets(top: 0, left: 0, bottom: 0, right: 0)
        return scroll
    }
    private func updateTreeVisibility(animated: Bool) {
        treeVisibilityGeneration += 1; let generation = treeVisibilityGeneration
        needsSplitLayout = true
        guard animated, !Motion.reduceMotion, hasAppliedSplitLayout, split.bounds.height > 0 else {
            animatingTree = false; treeContainer.isHidden = !showsFolderTree; treeContainer.alphaValue = 1
            view.needsLayout = true
            if showsFolderTree { revealActiveFolder() }
            return
        }
        animatingTree = true; treeContainer.isHidden = false
        let height = split.bounds.height
        let upperHeight = showsFolderTree ? height * savedFraction : height
        let divider = split.dividerThickness
        let upper = NSRect(x: 0, y: 0, width: split.bounds.width, height: upperHeight)
        let lower = NSRect(x: 0, y: upperHeight + divider, width: split.bounds.width, height: max(0, height - upperHeight - divider))
        Motion.animate {
            self.split.subviews[0].animator().frame = upper
            self.treeContainer.animator().frame = lower
            self.treeContainer.animator().alphaValue = self.showsFolderTree ? 1 : 0
        } completion: { [weak self] in
            guard let self, generation == self.treeVisibilityGeneration else { return }
            self.animatingTree = false; self.treeContainer.alphaValue = 1
            self.applySplitLayout()
        }
        if showsFolderTree { revealActiveFolder() }
    }
    func splitViewDidResizeSubviews(_ notification: Notification) {
        guard showsFolderTree, hasAppliedSplitLayout, !needsSplitLayout, !applyingSplitLayout, !animatingTree, split.bounds.height > 0 else { return }
        let fraction = split.subviews[0].frame.height / split.bounds.height
        if fraction >= 0.2 && fraction <= 0.85 { savedFraction = fraction; UserDefaults.standard.set(Double(fraction), forKey: "sidebarTreeFraction") }
    }
    func splitView(_ splitView: NSSplitView, constrainMinCoordinate proposedMinimumPosition: CGFloat, ofSubviewAt dividerIndex: Int) -> CGFloat { 110 }
    func splitView(_ splitView: NSSplitView, constrainMaxCoordinate proposedMaximumPosition: CGFloat, ofSubviewAt dividerIndex: Int) -> CGFloat { max(110, splitView.bounds.height - 120) }

    private func reloadSources() {
        refreshGeneration += 1; let generation = refreshGeneration
        let favorites = AppServices.shared.favorites.favorites
        let recents = Array(AppServices.shared.history.recentFolders.prefix(8))
        Task {
            let result = await Task.detached(priority: .userInitiated) { () -> ([SidebarEntry], [SidebarEntry], [SidebarEntry], [SidebarEntry]) in
                let fm = FileManager.default
                let volumes = fm.mountedVolumeURLs(includingResourceValuesForKeys: [.localizedNameKey, .volumeIsEjectableKey, .volumeIsRemovableKey, .volumeIsLocalKey], options: .skipHiddenVolumes) ?? []
                var locations = volumes.map { SidebarEntry.read($0, isLocation: true) }
                let cloud = fm.homeDirectoryForCurrentUser.appendingPathComponent("Library/Mobile Documents/com~apple~CloudDocs", isDirectory: true)
                if fm.fileExists(atPath: cloud.path) { var entry = SidebarEntry.read(cloud); entry.title = tr("iCloud Drive", "iCloud Drive"); entry.symbol = "icloud"; locations.insert(entry, at: 0) }
                let roots = [SidebarEntry.read(fm.homeDirectoryForCurrentUser)] + volumes.filter { $0.path != fm.homeDirectoryForCurrentUser.path }.map { SidebarEntry.read($0) }
                return (favorites.map { SidebarEntry.read($0) }, locations, recents.map { SidebarEntry.read($0) }, roots)
            }.value
            guard generation == refreshGeneration else { return }
            let collapsed = Set(sections.filter { !sources.isItemExpanded($0) }.map(\.title))
            let favoriteSection = SidebarNode(tr("Favorites", "즐겨찾기"), section: true); favoriteSection.children = result.0.map(makeNode)
            let locationSection = SidebarNode(tr("Locations", "위치"), section: true); locationSection.children = result.1.map(makeNode)
            let recentSection = SidebarNode(tr("Recent Folders", "최근 사용한 폴더"), section: true); recentSection.children = result.2.map(makeNode)
            sections = [favoriteSection, locationSection, recentSection]
            sources.reloadData()
            for section in sections where !collapsed.contains(section.title) { sources.expandItem(section) }
            roots = result.3.map(makeNode); tree.reloadData()
            synchronizeSelection(); revealActiveFolder()
        }
    }
    private func makeNode(_ entry: SidebarEntry) -> SidebarNode {
        let node = SidebarNode(entry.title, url: entry.url); node.icon = entry.symbol.flatMap { NSImage(systemSymbolName: $0, accessibilityDescription: nil) } ?? entry.icon
        node.missing = entry.missing; node.ejectable = entry.ejectable; return node
    }
    func followActiveFolder(_ url: URL?) {
        activeFolder = url?.standardizedFileURL
        guard isViewLoaded else { return }
        synchronizeSelection(); revealActiveFolder()
    }
    private func synchronizeSelection() {
        for outline in [sources, tree] {
            let row = (0..<outline.numberOfRows).first { index in
                guard let url = (outline.item(atRow: index) as? SidebarNode)?.url else { return false }
                return PathText.nfc(url.standardizedFileURL.path) == activeFolder.map { PathText.nfc($0.path) }
            }
            if let row { outline.selectRowIndexes(IndexSet(integer: row), byExtendingSelection: false) }
            else { outline.deselectAll(nil) }
        }
    }
    private func revealActiveFolder() {
        revealGeneration += 1; let generation = revealGeneration
        guard showsFolderTree, let target = activeFolder,
              let root = roots.filter({ $0.url.map { sidebarContains(target, in: $0) } ?? false }).max(by: { ($0.url?.path.count ?? 0) < ($1.url?.path.count ?? 0) }) else { return }
        Task { [weak self] in
            guard let self else { return }
            var node = root
            while node.url.map({ PathText.nfc($0.standardizedFileURL.path) }) != PathText.nfc(target.path) {
                guard generation == self.revealGeneration else { return }
                await self.loadChildren(node)
                guard generation == self.revealGeneration else { return }
                self.tree.expandItem(node)
                if !node.children.contains(where: { $0.url.map { sidebarContains(target, in: $0) } ?? false }) {
                    // A newly created folder may not be in an already-expanded parent's cache.
                    node.loaded = false; await self.loadChildren(node)
                    guard generation == self.revealGeneration else { return }
                    self.tree.expandItem(node)
                }
                guard let next = node.children.first(where: { $0.url.map { sidebarContains(target, in: $0) } ?? false }) else { break }
                node = next
            }
            guard generation == self.revealGeneration else { return }
            self.synchronizeSelection()
            let row = self.tree.row(forItem: node)
            if row >= 0 { self.tree.scrollRowToVisible(row) }
        }
    }
    private func loadChildren(_ node: SidebarNode) async {
        guard !node.loaded, let url = node.url else { return }
        let hidden = SettingsStore.shared.settings.showHiddenFiles
        let task = node.loadTask ?? Task.detached(priority: .userInitiated) { () -> ([SidebarEntry], String?) in
            do {
                let urls = try FileManager.default.contentsOfDirectory(at: url, includingPropertiesForKeys: [.isDirectoryKey, .isPackageKey, .localizedNameKey], options: hidden ? [] : [.skipsHiddenFiles])
                let folders = urls.filter { let value = try? $0.resourceValues(forKeys: [.isDirectoryKey, .isPackageKey]); return value?.isDirectory == true && value?.isPackage != true }
                return (folders.map { SidebarEntry.read($0) }.sorted { $0.title.localizedStandardCompare($1.title) == .orderedAscending }, nil)
            } catch { return ([], error.localizedDescription) }
        }
        node.loadTask = task
        let result = await task.value
        guard !node.loaded else { return }
        node.children = result.0.map(makeNode); node.error = result.1; node.loaded = true; node.loadTask = nil
        tree.reloadItem(node, reloadChildren: true)
    }
    func outlineView(_ outlineView: NSOutlineView, numberOfChildrenOfItem item: Any?) -> Int {
        if let node = item as? SidebarNode { return node.children.count }
        return outlineView === sources ? sections.count : roots.count
    }
    func outlineView(_ outlineView: NSOutlineView, child index: Int, ofItem item: Any?) -> Any {
        if let node = item as? SidebarNode { return node.children[index] }
        return outlineView === sources ? sections[index] : roots[index]
    }
    func outlineView(_ outlineView: NSOutlineView, isItemExpandable item: Any) -> Bool {
        guard let node = item as? SidebarNode else { return false }
        return node.section || (outlineView === tree && (!node.loaded || !node.children.isEmpty))
    }
    func outlineViewItemWillExpand(_ notification: Notification) {
        guard notification.object as? NSOutlineView === tree, let node = notification.userInfo?["NSObject"] as? SidebarNode else { return }
        Task { await loadChildren(node) }
    }
    func outlineView(_ outlineView: NSOutlineView, isGroupItem item: Any) -> Bool { (item as? SidebarNode)?.section == true }
    func outlineView(_ outlineView: NSOutlineView, shouldShowOutlineCellForItem item: Any) -> Bool { (item as? SidebarNode)?.section != true }
    func outlineView(_ outlineView: NSOutlineView, shouldSelectItem item: Any) -> Bool { (item as? SidebarNode)?.url != nil }
    func outlineView(_ outlineView: NSOutlineView, viewFor tableColumn: NSTableColumn?, item: Any) -> NSView? {
        guard let node = item as? SidebarNode else { return nil }
        if node.section {
            let header = SidebarHeader(title: node.title, expanded: outlineView.isItemExpanded(node))
            header.onToggle = { [weak outlineView, weak node] in
                guard let outlineView, let node else { return }
                if outlineView.isItemExpanded(node) { outlineView.collapseItem(node) } else { outlineView.expandItem(node) }
                outlineView.reloadItem(node)
            }
            return header
        }
        let cell = SidebarCell(); cell.textField?.stringValue = node.title; cell.textField?.font = Typography.list(SettingsStore.shared.settings.textSize)
        cell.textField?.textColor = node.missing ? .disabledControlTextColor : .labelColor
        cell.imageView?.contentTintColor = .labelColor
        cell.imageView?.image = node.icon; cell.imageView?.alphaValue = node.missing ? 0.45 : 1
        cell.toolTip = node.missing ? tr("This folder is no longer available.", "이 폴더를 더 이상 사용할 수 없습니다.") : node.error ?? node.url?.path
        cell.setEjectable(node.ejectable)
        cell.onEject = { [weak self, weak node] in if let url = node?.url { self?.eject(url) } }
        return cell
    }
    @objc private func clicked(_ sender: NSOutlineView) { open(row: sender.clickedRow, in: sender, newTab: NSApp.currentEvent?.modifierFlags.contains(.command) == true) }
    private func open(row: Int, in outline: NSOutlineView, newTab: Bool) {
        guard let node = outline.item(atRow: row) as? SidebarNode, let url = node.url, !node.missing else { return }
        navigator?.open(url, disposition: newTab ? .newTab(activate: false) : .current)
        synchronizeSelection()
    }
    private func menu(for node: SidebarNode) -> NSMenu? {
        guard let url = node.url else { return nil }
        let menu = NSMenu()
        func add(_ title: String, _ selector: Selector, to targetMenu: NSMenu = menu) {
            let item = NSMenuItem(title: title, action: selector, keyEquivalent: ""); item.target = self; item.representedObject = url; targetMenu.addItem(item)
        }
        add(tr("Open in New Tab", "새로운 탭에서 열기"), #selector(openTab(_:)))
        add(tr("Open in Other Pane", "다른 영역에서 열기"), #selector(openOther(_:)))
        let splitItem = NSMenuItem(title: tr("Open in New Pane", "새로운 영역에서 열기"), action: nil, keyEquivalent: "")
        let submenu = NSMenu(); add(tr("Right", "오른쪽"), #selector(openRight(_:)), to: submenu); add(tr("Below", "아래쪽"), #selector(openBelow(_:)), to: submenu); splitItem.submenu = submenu; menu.addItem(splitItem)
        menu.addItem(.separator()); add(tr("Show in Finder", "Finder에서 보기"), #selector(showFinder(_:)))
        if AppServices.shared.favorites.contains(url) { add(tr("Remove from Sidebar", "사이드바에서 제거"), #selector(removeFavorite(_:))) }
        return menu
    }
    @objc private func openTab(_ sender: NSMenuItem) { if let url = sender.representedObject as? URL { navigator?.open(url, disposition: .newTab(activate: true)) } }
    @objc private func openOther(_ sender: NSMenuItem) { if let url = sender.representedObject as? URL { navigator?.open(url, disposition: .otherPane) } }
    @objc private func openRight(_ sender: NSMenuItem) { if let url = sender.representedObject as? URL { navigator?.open(url, disposition: .newPane(.right)) } }
    @objc private func openBelow(_ sender: NSMenuItem) { if let url = sender.representedObject as? URL { navigator?.open(url, disposition: .newPane(.bottom)) } }
    @objc private func showFinder(_ sender: NSMenuItem) {
        guard let url = sender.representedObject as? URL else { return }
        let configuration = NSWorkspace.OpenConfiguration(); configuration.activates = true
        NSWorkspace.shared.open([url], withApplicationAt: URL(fileURLWithPath: "/System/Library/CoreServices/Finder.app"), configuration: configuration) { _, error in
            if let error { Task { @MainActor in NSAlert(error: error).runModal() } }
        }
    }
    @objc private func removeFavorite(_ sender: NSMenuItem) { if let url = sender.representedObject as? URL { AppServices.shared.favorites.remove(url) } }
    private func eject(_ url: URL) {
        Task {
            let error = await Task.detached { () -> String? in
                do { try NSWorkspace.shared.unmountAndEjectDevice(at: url); return nil }
                catch { return error.localizedDescription }
            }.value
            if let error {
                let alert = NSAlert(); alert.messageText = tr("The disk could not be ejected.", "디스크를 추출할 수 없습니다.")
                alert.informativeText = error; alert.runModal()
            }
        }
    }
    func outlineView(_ outlineView: NSOutlineView, pasteboardWriterForItem item: Any) -> NSPasteboardWriting? {
        guard outlineView === sources, let url = (item as? SidebarNode)?.url, AppServices.shared.favorites.contains(url) else { return nil }
        // Favorites are references, not file-transfer sources. Never advertise a file URL.
        let writer = NSPasteboardItem(); writer.setString(url.absoluteString, forType: Self.favoriteDrag); return writer
    }
    func outlineView(_ outlineView: NSOutlineView, validateDrop info: NSDraggingInfo, proposedItem item: Any?, proposedChildIndex index: Int) -> NSDragOperation {
        guard outlineView === sources, let node = item as? SidebarNode else { cancelSpring(); return [] }
        if info.draggingPasteboard.availableType(from: [Self.favoriteDrag]) != nil {
            cancelSpring()
            return index >= 0 && node === sections.first ? .move : []
        }
        let urls = (info.draggingPasteboard.readObjects(forClasses: [NSURL.self], options: [.urlReadingFileURLsOnly: true]) as? [URL]) ?? []
        guard !urls.isEmpty else { cancelSpring(); return [] }
        if index >= 0 && node === sections.first { cancelSpring(); return .copy }
        guard index == NSOutlineViewDropOnItemIndex, let url = node.url, !node.missing,
              sections.first?.children.contains(node) == true,
              let mode = FileDrop.mode(for: urls, into: url, operation: info.draggingSourceOperationMask) else { cancelSpring(); return [] }
        if springURL != url {
            cancelSpring(); springURL = url
            springTask = Task { [weak self] in
                try? await Task.sleep(for: .milliseconds(600))
                guard !Task.isCancelled else { return }; self?.navigator?.open(url, disposition: .current)
            }
        }
        return mode == .copy ? .copy : .move
    }
    private func cancelSpring() { springTask?.cancel(); springTask = nil; springURL = nil }
    func outlineView(_ outlineView: NSOutlineView, acceptDrop info: NSDraggingInfo, item: Any?, childIndex index: Int) -> Bool {
        cancelSpring()
        guard outlineView === sources, let node = item as? SidebarNode else { return false }
        if let value = info.draggingPasteboard.string(forType: Self.favoriteDrag) {
            guard index >= 0, node === sections.first, let url = URL(string: value),
                  let source = AppServices.shared.favorites.favorites.firstIndex(of: url) else { return false }
            AppServices.shared.favorites.move(from: source, to: source < index ? index - 1 : index); return true
        }
        let urls = (info.draggingPasteboard.readObjects(forClasses: [NSURL.self], options: [.urlReadingFileURLsOnly: true]) as? [URL]) ?? []
        guard !urls.isEmpty else { return false }
        if index >= 0 && node === sections.first {
            Task {
                let folders = await Task.detached { urls.filter { (try? $0.resourceValues(forKeys: [.isDirectoryKey]))?.isDirectory == true } }.value
                for (offset, url) in folders.enumerated() { AppServices.shared.favorites.add(url, at: index + offset) }
            }
            return true
        }
        guard index == NSOutlineViewDropOnItemIndex, sections.first?.children.contains(node) == true,
              !node.missing, let destination = node.url,
              let mode = FileDrop.mode(for: urls, into: destination, operation: info.draggingSourceOperationMask) else { return false }
        Task { _ = await AppServices.shared.fileOperations.transfer(urls, to: destination, mode: mode) }
        return true
    }
}

private func sidebarContains(_ url: URL, in folder: URL) -> Bool {
    let path = PathText.nfc(url.standardizedFileURL.path), root = PathText.nfc(folder.standardizedFileURL.path)
    return path == root || root == "/" || path.hasPrefix(root.hasSuffix("/") ? root : root + "/")
}


private struct SidebarEntry: @unchecked Sendable {
    let url: URL
    var title: String
    let icon: NSImage
    var symbol: String?
    let missing: Bool
    let ejectable: Bool
    static func read(_ url: URL, isLocation: Bool = false) -> SidebarEntry {
        let values = try? url.resourceValues(forKeys: [.localizedNameKey, .volumeIsEjectableKey, .volumeIsRemovableKey, .volumeIsLocalKey])
        let home = FileManager.default.homeDirectoryForCurrentUser
        let symbols = ["Desktop": "desktopcomputer", "Documents": "doc", "Downloads": "arrow.down.circle", "Pictures": "photo", "Music": "music.note", "Movies": "film"]
        let symbol = url == home ? "house" : (url.deletingLastPathComponent() == home ? symbols[url.lastPathComponent] : nil)
        return SidebarEntry(url: url, title: values?.localizedName ?? url.lastPathComponent, icon: NSWorkspace.shared.icon(forFile: url.path), symbol: symbol, missing: !FileManager.default.fileExists(atPath: url.path), ejectable: isLocation && (values?.volumeIsEjectable == true || values?.volumeIsRemovable == true || values?.volumeIsLocal == false))
    }
}
