import AppKit

@MainActor
protocol AddressBarViewDelegate: AnyObject {
    func addressBar(_ bar: AddressBarView, open url: URL, disposition: OpenDisposition)
    func addressBarPriorityRoots(_ bar: AddressBarView) -> [String]
    func addressBarDidEndEditing(_ bar: AddressBarView)
    func addressBar(_ bar: AddressBarView, acceptFiles urls: [URL], into folder: URL, operation: NSDragOperation) -> Bool
}

// Icons are created on a worker queue, then handed off without further worker-side mutation.
private struct ChromeCrumb: @unchecked Sendable {
    let url: URL
    let title: String
    let icon: NSImage?
}

@MainActor
final class AddressBarView: NSView, NSTextFieldDelegate, NSTableViewDataSource, NSTableViewDelegate {
    weak var delegate: AddressBarViewDelegate?
    var paneNumber = 1 { didSet { field.setAccessibilityIdentifier(AccessibilityID.address(paneNumber)) } }
    var url: URL? { didSet { if !isEditing { loadBreadcrumbs() } } }
    var search: PathSearching? { didSet { if isEditing { query(reset: true) } } }
    private(set) var isEditing = false
    private let field = NSTextField()
    private let errorLabel = NSTextField(labelWithString: "")
    private var crumbs: [ChromeCrumb] = []
    private var crumbButtons: [NSView] = []
    /// Width the breadcrumbs were last built for; layout() rebuilds only when it changes.
    private var crumbsBuiltWidth: CGFloat = -1
    private var crumbGeneration = UUID()
    private var queryTask: Task<Void, Never>?
    private var queryGeneration = UUID()
    private var panel: NSPanel?
    private var panelGeneration = UUID()
    private var pendingSelection: Int?
    private let table = NSTableView()
    private let scroll = NSScrollView()
    private var hits: [SearchHit] = []
    private var icons: [String: NSImage] = [:]
    private var iconRequests: Set<String> = []
    private var footer: String?
    private var canShowMore = false
    private var limit = 50
    private var queryText = ""
    private var outsideMonitor: Any?
    private var indexObserver: NSObjectProtocol?
    private var settingsObserver: NSObjectProtocol?
    private var errorText: String?
    override var isFlipped: Bool { true }

    override init(frame frameRect: NSRect) {
        super.init(frame: frameRect)
        wantsLayer = true; layer?.cornerRadius = Metrics.mediumRadius
        field.font = Typography.address; field.isBordered = false; field.drawsBackground = false
        field.focusRingType = .none; field.delegate = self; field.isHidden = true
        field.placeholderString = tr("Type a path or search for a name", "경로 또는 검색할 이름 입력")
        field.setAccessibilityLabel(tr("Location and Search", "위치 및 검색"))
        field.setAccessibilityIdentifier(AccessibilityID.address(paneNumber)); addSubview(field)
        errorLabel.font = Typography.status; errorLabel.textColor = .systemRed; errorLabel.isHidden = true; addSubview(errorLabel)
        table.headerView = nil; table.rowHeight = 46; table.intercellSpacing = .zero
        table.style = .plain; table.columnAutoresizingStyle = .lastColumnOnlyAutoresizingStyle
        table.backgroundColor = .clear; table.selectionHighlightStyle = .regular
        let column = NSTableColumn(identifier: NSUserInterfaceItemIdentifier("result")); table.addTableColumn(column)
        table.delegate = self; table.dataSource = self; table.target = self; table.action = #selector(clickedResult)
        table.setAccessibilityIdentifier(AccessibilityID.searchResults)
        table.setAccessibilityLabel(tr("Search Results", "검색 결과"))
        scroll.documentView = table; scroll.hasVerticalScroller = true; scroll.drawsBackground = false; scroll.autohidesScrollers = true
        indexObserver = NotificationCenter.default.addObserver(forName: .searchIndexDidChange, object: nil, queue: .main) { [weak self] _ in
            MainActor.assumeIsolated { guard let self, self.isEditing else { return }; self.query(reset: false) }
        }
        settingsObserver = NotificationCenter.default.addObserver(forName: .settingsDidChange, object: nil, queue: .main) { [weak self] _ in
            MainActor.assumeIsolated {
                guard let self else { return }; self.needsDisplay = true
                if self.isEditing { self.query(reset: true) } else { self.loadBreadcrumbs() }
            }
        }
        setAccessibilityRole(.group)
    }
    required init?(coder: NSCoder) { fatalError("init(coder:) is unavailable") }
    deinit {
        if let indexObserver { NotificationCenter.default.removeObserver(indexObserver) }
        if let settingsObserver { NotificationCenter.default.removeObserver(settingsObserver) }
        if let outsideMonitor { NSEvent.removeMonitor(outsideMonitor) }
        queryTask?.cancel()
    }
    override var intrinsicContentSize: NSSize { NSSize(width: NSView.noIntrinsicMetric, height: Metrics.addressBarHeight) }
    override func draw(_ dirtyRect: NSRect) {
        (isEditing ? NSColor.textBackgroundColor : NSColor.controlBackgroundColor).setFill()
        NSBezierPath(roundedRect: bounds, xRadius: Metrics.mediumRadius, yRadius: Metrics.mediumRadius).fill()
        (errorText == nil ? Palette.separator : NSColor.systemRed).setStroke()
        let outline = NSBezierPath(roundedRect: bounds.insetBy(dx: 0.5, dy: 0.5), xRadius: Metrics.mediumRadius, yRadius: Metrics.mediumRadius)
        outline.lineWidth = Motion.increaseContrast ? 1.5 : 0.5; outline.stroke()
    }
    override func layout() {
        super.layout()
        let errorWidth = errorText == nil ? 0 : min(bounds.width * 0.48, errorLabel.intrinsicContentSize.width + 12)
        field.frame = NSRect(x: 8, y: (bounds.height-20)/2, width: max(0, bounds.width-16-errorWidth), height: 20)
        errorLabel.frame = NSRect(x: bounds.width-errorWidth, y: (bounds.height-15)/2, width: max(0, errorWidth-6), height: 15)
        // Rebuilding adds and removes subviews, which must not happen inside a layout pass
        // (AppKit would loop on "needs update constraints"). Defer it, and only on width changes.
        if abs(bounds.width - crumbsBuiltWidth) > 0.5 {
            crumbsBuiltWidth = bounds.width
            DispatchQueue.main.async { [weak self] in self?.layoutBreadcrumbs() }
        }
        if panel?.isVisible == true { positionPanel() }
    }
    override func mouseDown(with event: NSEvent) { beginEditing() }
    func beginEditing() {
        isEditing = true; field.isHidden = false
        for view in crumbButtons { view.isHidden = true }
        field.stringValue = url.map(PathText.abbreviated) ?? ""
        clearError(); window?.makeFirstResponder(field)
        field.currentEditor()?.selectAll(nil)
        installOutsideMonitor(); needsDisplay = true
        query(reset: true)
    }
    private func endEditing() {
        queryTask?.cancel(); dismissPanel(animated: true)
        isEditing = false; field.isHidden = true; clearError(); loadBreadcrumbs()
        if let outsideMonitor { NSEvent.removeMonitor(outsideMonitor); self.outsideMonitor = nil }
        delegate?.addressBarDidEndEditing(self)
    }
    private var liveText: String { (field.currentEditor() as? NSTextView)?.string ?? field.stringValue }
    func controlTextDidChange(_ obj: Notification) { clearError(); query(reset: true) }
    func controlTextDidEndEditing(_ obj: Notification) {
        if isEditing { endEditing() }
    }
    func control(_ control: NSControl, textView: NSTextView, doCommandBy commandSelector: Selector) -> Bool {
        if textView.hasMarkedText() && (commandSelector == #selector(NSResponder.insertNewline(_:)) || commandSelector == #selector(NSResponder.cancelOperation(_:))) { return false }
        switch commandSelector {
        case #selector(NSResponder.moveDown(_:)):
            moveSelection(1); return true
        case #selector(NSResponder.moveUp(_:)):
            moveSelection(-1); return true
        case #selector(NSResponder.insertNewline(_:)), #selector(NSResponder.insertNewlineIgnoringFieldEditor(_:)):
            let disposition: OpenDisposition = NSApp.currentEvent?.modifierFlags.contains(.command) == true ? .newTab(activate: true) : .current
            activateSelection(disposition: disposition); return true
        case #selector(NSResponder.cancelOperation(_:)):
            if panel?.isVisible == true { dismissPanel(animated: true) } else { endEditing() }; return true
        case #selector(NSResponder.insertTab(_:)):
            if PathText.looksLikePath(liveText) { completePath(); return true }; return false
        default: return false
        }
    }
    private func installOutsideMonitor() {
        guard outsideMonitor == nil else { return }
        outsideMonitor = NSEvent.addLocalMonitorForEvents(matching: [.leftMouseDown, .rightMouseDown, .otherMouseDown]) { [weak self] event in
            MainActor.assumeIsolated {
                guard let self else { return }
                if event.window === self.panel { return }
                if event.window === self.window, self.bounds.contains(self.convert(event.locationInWindow, from: nil)) { return }
                self.dismissPanel(animated: true)
            }
            return event
        }
    }
    private func query(reset: Bool) {
        queryTask?.cancel()
        let generation = UUID(); queryGeneration = generation
        let text = liveText; queryText = text
        if reset { limit = 50; pendingSelection = nil }
        guard !text.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty else { hits = []; dismissPanel(animated: true); return }
        let hidden = SettingsStore.shared.settings.showHiddenFiles
        if PathText.looksLikePath(text) {
            let base = url
            queryTask = Task { [weak self] in
                let values = await Task.detached(priority: .userInitiated) { Self.pathCompletions(text, base: base, hidden: hidden) }.value
                guard !Task.isCancelled, let self, self.queryGeneration == generation, self.isEditing else { return }
                self.present(values, footer: values.isEmpty ? tr("No matching folders", "일치하는 폴더 없음") : nil, more: false)
            }
            return
        }
        guard let search else { present([], footer: tr("Search is unavailable. Type a folder path.", "검색을 사용할 수 없습니다. 폴더 경로를 입력하십시오."), more: false); return }
        guard search.state.isSearchable else { present([], footer: indexMessage(search.state), more: false); return }
        let roots = delegate?.addressBarPriorityRoots(self) ?? url.map { [$0.path] } ?? []
        let currentLimit = limit
        queryTask = Task { [weak self] in
            let values = await search.search(text, priorityRoots: roots, limit: currentLimit+1)
            guard !Task.isCancelled, let self, self.queryGeneration == generation, self.isEditing else { return }
            let more = values.count > currentLimit
            let footer = more ? (currentLimit < 500 ? tr("Show More Results", "결과 더 보기") : tr("Type more of the name to narrow the results", "이름을 더 입력하여 결과를 좁히십시오")) : (values.isEmpty ? tr("No results", "검색 결과 없음") : nil)
            self.present(Array(values.prefix(currentLimit)), footer: footer, more: more && currentLimit < 500)
        }
    }
    private func indexMessage(_ state: SearchIndexState) -> String {
        switch state {
        case .building(let scanned): return tr("Preparing search… \(scanned.formatted()) items", "검색 준비 중… 항목 \(scanned.formatted())개")
        case .loading: return tr("Preparing search…", "검색 준비 중…")
        case .disabled: return tr("Search is disabled. Type a folder path.", "검색이 꺼져 있습니다. 폴더 경로를 입력하십시오.")
        case .failed(let message): return tr("Search unavailable: \(message)", "검색을 사용할 수 없음: \(message)")
        case .ready: return ""
        }
    }
    nonisolated private static func pathCompletions(_ text: String, base: URL?, hidden: Bool) -> [SearchHit] {
        guard let resolved = PathText.resolve(text, relativeTo: base) else { return [] }
        let trailingSlash = text.hasSuffix("/") || text == "~"
        let parent = trailingSlash ? resolved : resolved.deletingLastPathComponent()
        let prefix = trailingSlash ? "" : PathText.nfc(resolved.lastPathComponent)
        return childFolders(parent, hidden: hidden).filter { prefix.isEmpty || PathText.nfc($0.lastPathComponent).range(of: prefix, options: [.anchored, .caseInsensitive, .diacriticInsensitive]) != nil }.prefix(500).map { SearchHit(path: $0.path, isDirectory: true) }
    }
    nonisolated private static func childFolders(_ url: URL, hidden: Bool) -> [URL] {
        let options: FileManager.DirectoryEnumerationOptions = hidden ? [] : [.skipsHiddenFiles]
        let keys: Set<URLResourceKey> = [.isDirectoryKey, .isPackageKey]
        let children = (try? FileManager.default.contentsOfDirectory(at: url, includingPropertiesForKeys: Array(keys), options: options)) ?? []
        return children.filter { child in
            guard let values = try? child.resourceValues(forKeys: keys) else { return false }
            return values.isDirectory == true && values.isPackage != true
        }.sorted { $0.lastPathComponent.localizedStandardCompare($1.lastPathComponent) == .orderedAscending }
    }
    private func completePath() {
        guard !hits.isEmpty else { return }
        if table.selectedRow >= 0, table.selectedRow < hits.count {
            replaceText(PathText.abbreviated(hits[table.selectedRow].url) + "/"); return
        }
        var prefix = PathText.nfc(hits[0].path)
        for hit in hits.dropFirst() {
            let other = PathText.nfc(hit.path)
            while !prefix.isEmpty && !other.lowercased().hasPrefix(prefix.lowercased()) { prefix.removeLast() }
        }
        if !prefix.isEmpty {
            let value = PathText.abbreviated(URL(fileURLWithPath: prefix)) + (hits.count == 1 ? "/" : "")
            replaceText(value)
        }
    }
    private func replaceText(_ value: String) {
        field.stringValue = value
        if let editor = field.currentEditor() as? NSTextView { editor.string = value; editor.setSelectedRange(NSRange(location: (value as NSString).length, length: 0)) }
        query(reset: true)
    }
    private func moveSelection(_ delta: Int) {
        if panel?.isVisible != true { query(reset: false) }
        let rows = hits.count + (canShowMore ? 1 : 0)
        guard rows > 0 else { return }
        let row = min(rows-1, max(0, table.selectedRow + delta))
        table.selectRowIndexes(IndexSet(integer: row), byExtendingSelection: false); table.scrollRowToVisible(row)
    }
    @objc private func clickedResult() {
        let disposition: OpenDisposition = NSApp.currentEvent?.modifierFlags.contains(.command) == true ? .newTab(activate: true) : .current
        activateSelection(disposition: disposition)
    }
    private func activateSelection(disposition: OpenDisposition) {
        let row = table.selectedRow
        if panel?.isVisible == true, row == hits.count, canShowMore {
            pendingSelection = hits.count; limit = limit == 50 ? 200 : 500
            query(reset: false)
            return
        }
        if panel?.isVisible == true, hits.indices.contains(row) { open(hits[row].url, disposition: disposition); return }
        guard let candidate = PathText.resolve(liveText, relativeTo: url), PathText.looksLikePath(liveText) else { return }
        queryTask?.cancel()
        queryTask = Task { [weak self] in
            let exists = await Task.detached(priority: .userInitiated) { FileManager.default.fileExists(atPath: candidate.path) }.value
            guard !Task.isCancelled, let self else { return }
            if exists { self.open(candidate, disposition: disposition) } else { self.showPathError() }
        }
    }
    private func open(_ destination: URL, disposition: OpenDisposition) {
        endEditing(); delegate?.addressBar(self, open: destination, disposition: disposition)
    }
    private func showPathError() {
        errorText = tr("Location not found", "위치를 찾을 수 없음"); errorLabel.stringValue = errorText ?? ""; errorLabel.isHidden = false
        needsLayout = true; needsDisplay = true
        guard !Motion.reduceMotion, let layer else { return }
        let shake = CASpringAnimation(keyPath: "transform.translation.x")
        shake.fromValue = 8; shake.toValue = 0; shake.mass = 1; shake.stiffness = 600; shake.damping = 18; shake.duration = shake.settlingDuration
        layer.add(shake, forKey: "invalidPath")
        NSAccessibility.post(element: field, notification: .valueChanged)
    }
    private func clearError() { errorText = nil; errorLabel.isHidden = true; needsLayout = true; needsDisplay = true }
    private func present(_ values: [SearchHit], footer: String?, more: Bool) {
        hits = values; self.footer = footer; canShowMore = more
        let paths = Set(values.map(\.path))
        icons = icons.filter { paths.contains($0.key) }
        table.reloadData(); table.deselectAll(nil)
        if let row = pendingSelection, values.indices.contains(row) {
            table.selectRowIndexes(IndexSet(integer: row), byExtendingSelection: false); table.scrollRowToVisible(row)
        } else if !values.isEmpty && !PathText.looksLikePath(queryText) {
            table.selectRowIndexes(IndexSet(integer: 0), byExtendingSelection: false)
        }
        pendingSelection = nil
        ensurePanel(); positionPanel()
        guard let panel, let window else { return }
        panel.appearance = effectiveAppearance
        let wasVisible = panel.isVisible
        panelGeneration = UUID()
        panel.alphaValue = 1
        if panel.parent !== window { panel.parent?.removeChildWindow(panel); window.addChildWindow(panel, ordered: .above) }
        panel.orderFront(nil)
        if !wasVisible, let layer = panel.contentView?.layer {
            layer.opacity = 0; Motion.fade(layer, to: 1)
            if !Motion.reduceMotion { layer.setValue(0.98, forKeyPath: "transform.scale"); Motion.spring(layer, keyPath: "transform.scale", to: 1, response: Motion.quickResponse) }
        }
    }
    private func ensurePanel() {
        guard panel == nil else { return }
        let panel = NSPanel(contentRect: .zero, styleMask: [.borderless, .nonactivatingPanel], backing: .buffered, defer: false)
        panel.hasShadow = true; panel.isOpaque = false; panel.backgroundColor = .clear; panel.hidesOnDeactivate = true
        let effect = NSVisualEffectView(); effect.material = .menu; effect.state = .active; effect.blendingMode = .behindWindow
        effect.wantsLayer = true; effect.layer?.cornerRadius = Metrics.largeRadius; effect.layer?.masksToBounds = true
        if Motion.reduceTransparency { effect.layer?.backgroundColor = NSColor.windowBackgroundColor.cgColor }
        effect.addSubview(scroll); panel.contentView = effect; self.panel = panel
    }
    private func positionPanel() {
        guard let panel, let window else { return }
        let anchor = window.convertToScreen(convert(bounds, to: nil))
        let screen = window.screen?.visibleFrame ?? NSScreen.main?.visibleFrame ?? anchor
        let width = min(max(bounds.width, 360), screen.width-16)
        let height = min(CGFloat(hits.count+(footer == nil ? 0 : 1))*46+8, 376, max(54, anchor.minY-screen.minY-8))
        panel.setFrame(NSRect(x: min(max(screen.minX+8, anchor.minX), screen.maxX-width-8), y: anchor.minY-height-4, width: width, height: height), display: true)
        scroll.frame = NSRect(x: 4, y: 4, width: width-8, height: height-8)
        table.tableColumns.first?.width = width-8
    }
    private func dismissPanel(animated: Bool) {
        queryTask?.cancel(); queryGeneration = UUID()
        guard let panel, panel.isVisible else { return }
        let generation = UUID(); panelGeneration = generation
        if animated {
            if let layer = panel.contentView?.layer, !Motion.reduceMotion { Motion.spring(layer, keyPath: "transform.scale", to: 0.98, response: Motion.quickResponse) }
            Motion.animate(response: Motion.quickResponse, { panel.animator().alphaValue = 0 }, completion: { [weak self, weak panel] in
                guard let self, let panel, self.panelGeneration == generation else { return }
                panel.orderOut(nil); panel.alphaValue = 1; panel.parent?.removeChildWindow(panel)
            })
        } else { panel.orderOut(nil); panel.parent?.removeChildWindow(panel) }
    }
    func numberOfRows(in tableView: NSTableView) -> Int { hits.count + (footer == nil ? 0 : 1) }
    func tableView(_ tableView: NSTableView, shouldSelectRow row: Int) -> Bool { row < hits.count || canShowMore }
    func tableView(_ tableView: NSTableView, viewFor tableColumn: NSTableColumn?, row: Int) -> NSView? {
        guard hits.indices.contains(row) else {
            let label = NSTextField(labelWithString: footer ?? ""); label.font = Typography.address; label.textColor = canShowMore ? .controlAccentColor : .secondaryLabelColor; label.alignment = .center
            return label
        }
        let hit = hits[row]
        let view = ChromeSearchRow()
        view.configure(hit: hit, query: queryText, icon: icons[hit.path])
        if icons[hit.path] == nil && !iconRequests.contains(hit.path) {
            iconRequests.insert(hit.path)
            DispatchQueue.global(qos: .utility).async { [weak self] in
                let icon = NSWorkspace.shared.icon(forFile: hit.path)
                DispatchQueue.main.async {
                    guard let self else { return }; self.iconRequests.remove(hit.path); self.icons[hit.path] = icon
                    if let index = self.hits.firstIndex(where: { $0.path == hit.path }) { self.table.reloadData(forRowIndexes: IndexSet(integer: index), columnIndexes: IndexSet(integer: 0)) }
                }
            }
        }
        return view
    }
    private func loadBreadcrumbs() {
        let generation = UUID(); crumbGeneration = generation
        guard let url else { crumbs = []; layoutBreadcrumbs(); return }
        DispatchQueue.global(qos: .userInitiated).async { [weak self] in
            let root = (try? url.resourceValues(forKeys: [.volumeURLKey]).volume) ?? URL(fileURLWithPath: "/", isDirectory: true)
            var chain: [URL] = []; var current = url.standardizedFileURL
            while current.path != root.path && current.path != "/" { chain.append(current); current.deleteLastPathComponent() }
            chain.append(root); chain.reverse()
            let crumbs = chain.enumerated().map { index, location -> ChromeCrumb in
                let title = FileManager.default.displayName(atPath: location.path)
                let icon: NSImage? = index == 0 ? NSWorkspace.shared.icon(forFile: location.path) : (location.standardizedFileURL == PathText.home.standardizedFileURL ? NSImage(systemSymbolName: "house", accessibilityDescription: nil) : nil)
                return ChromeCrumb(url: location, title: title, icon: icon)
            }
            DispatchQueue.main.async {
                guard let self, self.crumbGeneration == generation, !self.isEditing else { return }
                self.crumbs = crumbs; self.layoutBreadcrumbs()
            }
        }
    }
    private func layoutBreadcrumbs() {
        guard !isEditing else { return }
        crumbsBuiltWidth = bounds.width
        crumbButtons.forEach { $0.removeFromSuperview() }; crumbButtons = []
        guard !crumbs.isEmpty else { return }
        let widths = crumbs.map { min(190, ($0.title as NSString).size(withAttributes: [.font: Typography.address]).width + ($0.icon == nil ? 18 : 37)) + 20 }
        var indices = Array(crumbs.indices)
        var collapsed: [Int] = []
        var total = widths.reduce(0, +)
        while total > bounds.width-12 && indices.count > 2 {
            let index = indices.remove(at: 1); collapsed.append(index); total -= widths[index]
            if collapsed.count == 1 { total += 38 }
        }
        var x: CGFloat = 5
        for (position, index) in indices.enumerated() {
            if position == 1 && !collapsed.isEmpty {
                let ellipsis = CrumbButton(); ellipsis.title = "…"; ellipsis.onPress = { [weak self, weak ellipsis] _ in guard let ellipsis else { return }; self?.showCollapsed(collapsed, sender: ellipsis) }
                ellipsis.setAccessibilityLabel(tr("More Path Components", "경로 구성요소 더 보기"))
                ellipsis.frame = NSRect(x: x, y: 2, width: 34, height: bounds.height-4); addSubview(ellipsis); crumbButtons.append(ellipsis); x += 34
            }
            let crumb = crumbs[index]
            let remaining = max(0, bounds.width-x-5)
            let reserved = indices.dropFirst(position+1).reduce(CGFloat(0)) { $0 + min(widths[$1], 55) } + (position == 0 && !collapsed.isEmpty ? 34 : 0)
            let width = min(widths[index], max(0, remaining-reserved))
            let button = CrumbButton(); button.title = crumb.title; button.image = crumb.icon?.copy() as? NSImage; button.image?.size = NSSize(width: 16, height: 16); button.imagePosition = .imageLeading
            button.toolTip = PathText.abbreviated(crumb.url); button.folder = crumb.url
            button.onPress = { [weak self] event in
                guard let self else { return }
                let disposition: OpenDisposition = event?.buttonNumber == 2 ? .newTab(activate: false) : (event?.modifierFlags.contains(.command) == true ? .newTab(activate: true) : .current)
                self.delegate?.addressBar(self, open: crumb.url, disposition: disposition)
            }
            button.onDrop = { [weak self] urls, operation in guard let self else { return false }; return self.delegate?.addressBar(self, acceptFiles: urls, into: crumb.url, operation: operation) ?? false }
            button.frame = NSRect(x: x, y: 2, width: max(0, width-17), height: bounds.height-4)
            addSubview(button); crumbButtons.append(button)
            let arrow = CrumbButton(); arrow.image = NSImage(systemSymbolName: "chevron.right", accessibilityDescription: tr("Subfolders", "하위 폴더")); arrow.title = ""
            arrow.setAccessibilityLabel(tr("Subfolders of \(crumb.title)", "\(crumb.title)의 하위 폴더"))
            arrow.onPress = { [weak self, weak arrow] _ in guard let arrow else { return }; self?.showChildren(crumb.url, sender: arrow) }
            arrow.frame = NSRect(x: x+max(0,width-17), y: 2, width: min(17, width), height: bounds.height-4)
            addSubview(arrow); crumbButtons.append(arrow); x += width
        }
    }
    private func showCollapsed(_ indices: [Int], sender: NSView) {
        let menu = NSMenu()
        for index in indices { menu.addItem(menuItem(title: crumbs[index].title, url: crumbs[index].url, icon: crumbs[index].icon)) }
        menu.popUp(positioning: nil, at: NSPoint(x: 0, y: sender.bounds.maxY), in: sender)
    }
    private func showChildren(_ folder: URL, sender: NSView) {
        let hidden = SettingsStore.shared.settings.showHiddenFiles
        let menu = NSMenu()
        let loading = NSMenuItem(title: tr("Loading…", "불러오는 중…"), action: nil, keyEquivalent: ""); loading.isEnabled = false; menu.addItem(loading)
        Task { [weak self, weak sender] in
            let entries = await Task.detached(priority: .userInitiated) {
                Self.childFolders(folder, hidden: hidden).map { ChromeCrumb(url: $0, title: FileManager.default.displayName(atPath: $0.path), icon: NSWorkspace.shared.icon(forFile: $0.path)) }
            }.value
            guard let self, let sender, sender.window != nil else { return }
            menu.removeAllItems()
            if entries.isEmpty {
                let empty = NSMenuItem(title: tr("No Subfolders", "하위 폴더 없음"), action: nil, keyEquivalent: ""); empty.isEnabled = false; menu.addItem(empty)
            }
            for entry in entries { menu.addItem(self.menuItem(title: entry.title, url: entry.url, icon: entry.icon)) }
            menu.update()
        }
        menu.popUp(positioning: nil, at: NSPoint(x: 0, y: sender.bounds.maxY), in: sender)
    }
    private func menuItem(title: String, url: URL, icon: NSImage?) -> NSMenuItem {
        let item = NSMenuItem(title: title, action: #selector(openMenuLocation(_:)), keyEquivalent: "")
        item.target = self; item.representedObject = url; item.image = icon?.copy() as? NSImage; item.image?.size = NSSize(width: 16, height: 16)
        return item
    }
    @objc private func openMenuLocation(_ item: NSMenuItem) {
        guard let location = item.representedObject as? URL else { return }
        let disposition: OpenDisposition = NSApp.currentEvent?.modifierFlags.contains(.command) == true ? .newTab(activate: true) : .current
        delegate?.addressBar(self, open: location, disposition: disposition)
    }
}

@MainActor
private final class CrumbButton: NSButton {
    var folder: URL?
    var onPress: ((NSEvent?) -> Void)?
    var onDrop: (([URL], NSDragOperation) -> Bool)?
    override init(frame frameRect: NSRect) {
        super.init(frame: frameRect)
        isBordered = false; bezelStyle = .inline; font = Typography.address; lineBreakMode = .byTruncatingMiddle
        target = self; action = #selector(pressed)
        registerForDraggedTypes([.fileURL])
    }
    convenience init() { self.init(frame: .zero) }
    required init?(coder: NSCoder) { fatalError("init(coder:) is unavailable") }
    @objc private func pressed() { onPress?(NSApp.currentEvent) }
    override func otherMouseDown(with event: NSEvent) { if event.buttonNumber == 2 { onPress?(event) } }
    override func draggingEntered(_ sender: NSDraggingInfo) -> NSDragOperation { highlight(true); return draggingUpdated(sender) }
    override func draggingUpdated(_ sender: NSDraggingInfo) -> NSDragOperation {
        guard folder != nil else { return [] }
        return sender.draggingSourceOperationMask.contains(.move) && !NSEvent.modifierFlags.contains(.option) ? .move : .copy
    }
    override func draggingExited(_ sender: NSDraggingInfo?) { highlight(false) }
    override func performDragOperation(_ sender: NSDraggingInfo) -> Bool {
        defer { highlight(false) }
        guard let urls = sender.draggingPasteboard.readObjects(forClasses: [NSURL.self], options: [.urlReadingFileURLsOnly: true]) as? [URL], !urls.isEmpty else { return false }
        return onDrop?(urls, draggingUpdated(sender)) ?? false
    }
}

@MainActor
private final class ChromeSearchRow: NSTableCellView {
    private let nameLabel = NSTextField(labelWithString: "")
    private let parentLabel = NSTextField(labelWithString: "")
    private let kindLabel = NSTextField(labelWithString: "")
    private let fileIcon = NSImageView()
    override init(frame frameRect: NSRect) {
        super.init(frame: frameRect)
        for label in [nameLabel, parentLabel, kindLabel] { label.lineBreakMode = .byTruncatingMiddle; addSubview(label) }
        parentLabel.font = Typography.status; parentLabel.textColor = .secondaryLabelColor
        kindLabel.font = Typography.status; kindLabel.textColor = .secondaryLabelColor; kindLabel.alignment = .right
        addSubview(fileIcon)
    }
    required init?(coder: NSCoder) { fatalError("init(coder:) is unavailable") }
    func configure(hit: SearchHit, query: String, icon: NSImage?) {
        fileIcon.image = icon ?? NSImage(systemSymbolName: hit.isDirectory ? "folder" : "doc", accessibilityDescription: nil)
        let name = PathText.nfc(hit.name)
        let attributed = NSMutableAttributedString(string: name, attributes: [.font: Typography.address, .foregroundColor: NSColor.labelColor])
        let tokens = PathText.nfc(query).split(whereSeparator: { $0.isWhitespace || $0 == "/" || $0 == "\"" }).map(String.init)
        for token in tokens where !token.isEmpty {
            var searchRange = name.startIndex..<name.endIndex
            while let match = name.range(of: token, options: [.caseInsensitive, .diacriticInsensitive], range: searchRange) {
                attributed.addAttribute(.font, value: NSFont.systemFont(ofSize: Typography.address.pointSize, weight: .semibold), range: NSRange(match, in: name))
                guard match.upperBound < name.endIndex else { break }; searchRange = match.upperBound..<name.endIndex
            }
        }
        nameLabel.attributedStringValue = attributed
        parentLabel.stringValue = PathText.abbreviated(hit.url.deletingLastPathComponent())
        kindLabel.stringValue = hit.isDirectory ? tr("Folder", "폴더") : tr("File", "파일")
        setAccessibilityLabel("\(name), \(parentLabel.stringValue), \(kindLabel.stringValue)")
    }
    override func layout() {
        super.layout()
        fileIcon.frame = NSRect(x: 8, y: (bounds.height-24)/2, width: 24, height: 24)
        let kindWidth: CGFloat = 48
        kindLabel.frame = NSRect(x: max(40,bounds.width-kindWidth-8), y: (bounds.height-16)/2, width: kindWidth, height: 16)
        nameLabel.frame = NSRect(x: 40, y: bounds.height-23, width: max(0,bounds.width-104), height: 18)
        parentLabel.frame = NSRect(x: 40, y: 5, width: max(0,bounds.width-104), height: 15)
    }
}
