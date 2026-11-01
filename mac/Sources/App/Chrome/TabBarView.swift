import AppKit

struct TabBarItem: Equatable {
    let id: UUID
    var title: String
    var icon: NSImage?
    var toolTip: String?
    var isBlank: Bool
}

@MainActor
protocol TabBarViewDelegate: AnyObject {
    func tabBar(_ bar: TabBarView, didSelect id: UUID)
    func tabBar(_ bar: TabBarView, didClose id: UUID)
    func tabBarDidRequestNewTab(_ bar: TabBarView)
    func tabBar(_ bar: TabBarView, didMove id: UUID, to index: Int)
    func tabBar(_ bar: TabBarView, menuFor id: UUID) -> NSMenu?
    func tabBar(_ bar: TabBarView, acceptTab payload: TabDragPayload, at index: Int) -> Bool
    func tabBar(_ bar: TabBarView, validateFiles urls: [URL], onto id: UUID, operation: NSDragOperation) -> NSDragOperation
    func tabBar(_ bar: TabBarView, acceptFiles urls: [URL], onto id: UUID, operation: NSDragOperation) -> Bool
    func tabBar(_ bar: TabBarView, didTearOff id: UUID, at screenPoint: NSPoint)
    func tabBarDidRequestReopenClosedTab(_ bar: TabBarView)
    /// The pane's own × (shown only while the window has more than one pane).
    func tabBarDidRequestClosePane(_ bar: TabBarView)
}

@MainActor
final class TabBarView: NSView, NSDraggingSource {
    weak var delegate: TabBarViewDelegate?
    var paneID = UUID()
    var paneNumber = 1 {
        didSet {
            setAccessibilityIdentifier(AccessibilityID.tabBar(paneNumber))
            closePane.setAccessibilityIdentifier(AccessibilityID.closePane(paneNumber))
        }
    }
    var canReopenClosedTab = false { didSet { reopen.isEnabled = canReopenClosedTab } }
    /// Shows the pane's close button at the trailing end of the bar.
    var canClosePane = false { didSet { closePane.isHidden = !canClosePane; needsLayout = true } }
    var isActivePane = false { didSet { updateAppearance() } }
    private var items: [TabBarItem] = []
    private var selected: UUID?
    private var cells: [UUID: ChromeTabCell] = [:]
    private let add = NSButton()
    private let overflow = NSButton()
    private let reopen = NSButton()
    private let closePane = NSButton()
    private let accent = NSView()
    private var visibleIDs: [UUID] = []
    private var draggedID: UUID?
    private var grabOffset: CGFloat = 0
    private var insertionIndex: Int?
    private var springTask: Task<Void, Never>?
    private var springID: UUID?
    private var scrollDistance: CGFloat = 0
    private var settingsObserver: NSObjectProtocol?
    override var isFlipped: Bool { true }

    override init(frame frameRect: NSRect) {
        super.init(frame: frameRect)
        wantsLayer = true
        for (button, symbol, label, action) in [
            (add, "plus", tr("New Tab", "새로운 탭"), #selector(newTab)),
            (overflow, "chevron.down", tr("Show All Tabs", "모든 탭 보기"), #selector(showAllTabs)),
            (reopen, "arrow.uturn.backward", tr("Reopen Closed Tab", "닫은 탭 다시 열기"), #selector(reopenTab)),
            (closePane, "xmark.rectangle", tr("Close Pane", "영역 닫기"), #selector(closePaneClicked))
        ] {
            button.image = NSImage(systemSymbolName: symbol, accessibilityDescription: label)
            button.title = ""; button.bezelStyle = .texturedRounded; button.isBordered = false
            button.target = self; button.action = action; button.toolTip = label
            button.setAccessibilityLabel(label); addSubview(button)
        }
        reopen.isEnabled = false
        closePane.isHidden = true
        closePane.setAccessibilityIdentifier(AccessibilityID.closePane(paneNumber))
        accent.wantsLayer = true; addSubview(accent)
        registerForDraggedTypes([.folderSplitterTab, .fileURL])
        setAccessibilityRole(.tabGroup); setAccessibilityLabel(tr("Tabs", "탭"))
        setAccessibilityIdentifier(AccessibilityID.tabBar(paneNumber))
        settingsObserver = NotificationCenter.default.addObserver(forName: .settingsDidChange, object: nil, queue: .main) { [weak self] _ in
            MainActor.assumeIsolated { self?.updateAppearance() }
        }
        updateAppearance()
    }
    required init?(coder: NSCoder) { fatalError("init(coder:) is unavailable") }
    deinit { if let settingsObserver { NotificationCenter.default.removeObserver(settingsObserver) } }
    override var intrinsicContentSize: NSSize { NSSize(width: NSView.noIntrinsicMetric, height: Metrics.tabBarHeight) }

    func setItems(_ items: [TabBarItem], selected: UUID?, animated: Bool) {
        self.items = items; self.selected = selected
        let ids = Set(items.map(\.id))
        for id in Array(cells.keys) where !ids.contains(id) {
            guard let cell = cells.removeValue(forKey: id) else { continue }
            if animated && !Motion.reduceMotion {
                Motion.animate(response: Motion.quickResponse, {
                    cell.animator().alphaValue = 0
                    cell.animator().setFrameSize(NSSize(width: 0, height: cell.frame.height))
                }, completion: { cell.removeFromSuperview() })
            } else { cell.removeFromSuperview() }
        }
        for item in items {
            let cell: ChromeTabCell
            if let existing = cells[item.id] { cell = existing }
            else {
                cell = ChromeTabCell(item: item); cell.bar = self
                cells[item.id] = cell; addSubview(cell, positioned: .below, relativeTo: accent)
                cell.frame = NSRect(x: 4, y: 4, width: 0, height: Metrics.tabHeight)
            }
            cell.item = item; cell.selected = item.id == selected
            cell.alphaValue = isActivePane ? 1 : 0.78
        }
        positionTabs(animated: animated)
    }
    override func layout() { super.layout(); positionTabs(animated: false) }
    private func positionTabs(animated: Bool) {
        // Trailing controls: + (and ▾), reopen, and the pane's × when there are several panes.
        let paneClose: CGFloat = canClosePane ? 28 : 0
        let available = max(0, bounds.width - 64 - paneClose)
        let count = items.count
        let overflowing = CGFloat(count) * Metrics.tabMinWidth > available
        let tabSpace = max(0, available - (overflowing ? 26 : 0))
        let capacity = max(1, Int(tabSpace / Metrics.tabMinWidth))
        var displayed = Array(items.prefix(overflowing ? capacity : count))
        if let selected, !displayed.contains(where: { $0.id == selected }), let item = items.first(where: { $0.id == selected }), !displayed.isEmpty {
            displayed[displayed.count-1] = item
        }
        visibleIDs = displayed.map(\.id)
        let gap: CGFloat = insertionIndex == nil ? 0 : 24
        let maxWidth = count == 1 ? max(Metrics.tabMaxWidth, tabSpace) : Metrics.tabMaxWidth
        let width = min(maxWidth, max(0, (tabSpace-gap)/CGFloat(max(1, displayed.count))))
        var x: CGFloat = 4
        for (index, item) in displayed.enumerated() {
            if insertionIndex == index { x += gap }
            guard let cell = cells[item.id] else { continue }
            cell.isHidden = false
            let target = NSRect(x: x, y: 4, width: max(0, width-2), height: Metrics.tabHeight)
            if draggedID != item.id {
                if animated && !Motion.reduceMotion { Motion.animate(response: Motion.quickResponse) { cell.animator().frame = target } }
                else { cell.frame = target }
            }
            x += width
        }
        for (id, cell) in cells where !visibleIDs.contains(id) { cell.isHidden = true }
        if insertionIndex == displayed.count { x += gap }
        add.frame = NSRect(x: min(x+2, max(0, bounds.width-58-paneClose)), y: 3, width: 26, height: 26)
        overflow.isHidden = !overflowing
        overflow.frame = NSRect(x: add.frame.maxX, y: 3, width: 26, height: 26)
        reopen.frame = NSRect(x: min(overflowing ? overflow.frame.maxX : add.frame.maxX, max(0, bounds.width-28-paneClose)), y: 3, width: 26, height: 26)
        closePane.frame = NSRect(x: max(0, bounds.width-28), y: 3, width: 26, height: 26)
        accent.frame = NSRect(x: 0, y: 0, width: bounds.width, height: 2)
    }
    private func updateAppearance() {
        effectiveAppearance.performAsCurrentDrawingAppearance {
            accent.layer?.backgroundColor = Palette.paneHighlight(SettingsStore.shared.settings.paneHighlight).cgColor
        }
        if let layer = accent.layer { Motion.fade(layer, to: isActivePane ? 1 : 0) }
        for cell in cells.values { cell.alphaValue = isActivePane ? 1 : 0.78 }
        needsDisplay = true
    }
    override func viewDidChangeEffectiveAppearance() { super.viewDidChangeEffectiveAppearance(); updateAppearance() }
    override func draw(_ dirtyRect: NSRect) {
        NSColor.windowBackgroundColor.setFill(); bounds.fill()
        Palette.separator.setFill(); NSRect(x: 0, y: bounds.height-1, width: bounds.width, height: 1).fill()
    }
    @objc private func newTab() { delegate?.tabBarDidRequestNewTab(self) }
    @objc private func reopenTab() { delegate?.tabBarDidRequestReopenClosedTab(self) }
    @objc private func closePaneClicked() { delegate?.tabBarDidRequestClosePane(self) }
    @objc private func showAllTabs() {
        let menu = NSMenu()
        for item in items {
            let entry = NSMenuItem(title: item.title, action: #selector(selectMenuTab(_:)), keyEquivalent: "")
            entry.target = self; entry.representedObject = item.id; entry.image = item.icon
            entry.state = item.id == selected ? .on : .off; menu.addItem(entry)
        }
        menu.popUp(positioning: nil, at: NSPoint(x: 0, y: overflow.bounds.maxY), in: overflow)
    }
    @objc private func selectMenuTab(_ sender: NSMenuItem) { if let id = sender.representedObject as? UUID { select(id) } }
    fileprivate func select(_ id: UUID) { delegate?.tabBar(self, didSelect: id) }
    fileprivate func close(_ id: UUID) { delegate?.tabBar(self, didClose: id) }
    fileprivate func menu(_ id: UUID) -> NSMenu? { delegate?.tabBar(self, menuFor: id) }
    fileprivate func track(_ cell: ChromeTabCell, event: NSEvent) {
        select(cell.item.id)
        guard let window else { return }
        let start = convert(event.locationInWindow, from: nil)
        grabOffset = start.x-cell.frame.minX
        var didDrag = false
        while let next = window.nextEvent(matching: [.leftMouseDragged, .leftMouseUp], until: .distantFuture, inMode: .eventTracking, dequeue: true) {
            if next.type == .leftMouseUp {
                draggedID = nil; positionTabs(animated: didDrag); return
            }
            let point = convert(next.locationInWindow, from: nil)
            if !didDrag && hypot(point.x-start.x, point.y-start.y) < 5 { continue }
            didDrag = true; draggedID = cell.item.id
            if point.y < -12 || point.y > bounds.height+12 {
                beginExternalDrag(cell, event: next); return
            }
            let rawX = point.x-grabOffset
            let maximum = max(0, bounds.width-cell.frame.width)
            let resisted = rawX < 0 ? Motion.rubberband(rawX, dimension: bounds.width) : (rawX > maximum ? maximum+Motion.rubberband(rawX-maximum, dimension: bounds.width) : rawX)
            cell.frame.origin.x = resisted
            if let old = items.firstIndex(where: { $0.id == cell.item.id }) {
                let candidate = visibleIDs.filter { $0 != cell.item.id }.compactMap { cells[$0] }.filter { point.x > $0.frame.midX }.count
                if candidate != old {
                    let item = items.remove(at: old); items.insert(item, at: min(candidate, items.count))
                    delegate?.tabBar(self, didMove: item.id, to: candidate); positionTabs(animated: true)
                }
            }
        }
    }
    private func beginExternalDrag(_ cell: ChromeTabCell, event: NSEvent) {
        let payload = TabDragPayload(windowNumber: window?.windowNumber ?? 0, paneID: paneID, tabID: cell.item.id)
        guard let data = try? JSONEncoder().encode(payload) else { return }
        let writer = NSPasteboardItem(); writer.setData(data, forType: .folderSplitterTab)
        let dragging = NSDraggingItem(pasteboardWriter: writer)
        let image = NSImage(size: cell.bounds.size)
        if let rep = cell.bitmapImageRepForCachingDisplay(in: cell.bounds) { cell.cacheDisplay(in: cell.bounds, to: rep); image.addRepresentation(rep) }
        dragging.setDraggingFrame(cell.frame, contents: image)
        beginDraggingSession(with: [dragging], event: event, source: self)
    }
    func draggingSession(_ session: NSDraggingSession, sourceOperationMaskFor context: NSDraggingContext) -> NSDragOperation { .move }
    func draggingSession(_ session: NSDraggingSession, endedAt screenPoint: NSPoint, operation: NSDragOperation) {
        let id = draggedID; draggedID = nil; positionTabs(animated: true)
        if operation.isEmpty, let id, !NSApp.windows.contains(where: { $0.isVisible && $0.frame.contains(screenPoint) }) {
            delegate?.tabBar(self, didTearOff: id, at: screenPoint)
        }
    }
    override func scrollWheel(with event: NSEvent) {
        guard abs(event.scrollingDeltaX) > abs(event.scrollingDeltaY), let selected, let index = items.firstIndex(where: { $0.id == selected }) else { super.scrollWheel(with: event); return }
        if event.phase == .began { scrollDistance = 0 }
        scrollDistance += event.scrollingDeltaX
        if abs(scrollDistance) >= (event.hasPreciseScrollingDeltas ? 24 : 1) {
            let next = index + (scrollDistance > 0 ? -1 : 1)
            if items.indices.contains(next) { select(items[next].id) }
            scrollDistance = 0
        }
    }
    override func draggingEntered(_ sender: NSDraggingInfo) -> NSDragOperation { draggingUpdated(sender) }
    override func draggingUpdated(_ sender: NSDraggingInfo) -> NSDragOperation {
        let point = convert(sender.draggingLocation, from: nil)
        if sender.draggingPasteboard.data(forType: .folderSplitterTab) != nil {
            let index = visibleIDs.compactMap { cells[$0] }.filter { point.x > $0.frame.midX }.count
            if insertionIndex != index { insertionIndex = index; positionTabs(animated: true) }
            return .move
        }
        guard let cell = visibleIDs.compactMap({ cells[$0] }).first(where: { $0.frame.contains(point) }),
              let urls = sender.draggingPasteboard.readObjects(forClasses: [NSURL.self], options: [.urlReadingFileURLsOnly: true]) as? [URL], !urls.isEmpty else { cancelSpringLoad(); return [] }
        let operation = delegate?.tabBar(self, validateFiles: urls, onto: cell.item.id, operation: sender.draggingSourceOperationMask) ?? []
        guard !operation.isEmpty else { cancelSpringLoad(); return [] }
        if springID != cell.item.id {
            cancelSpringLoad(); springID = cell.item.id
            springTask = Task { [weak self] in
                try? await Task.sleep(for: .milliseconds(500))
                guard !Task.isCancelled, let self else { return }; self.select(cell.item.id)
            }
        }
        return operation
    }
    override func draggingExited(_ sender: NSDraggingInfo?) { clearDrop() }
    override func prepareForDragOperation(_ sender: NSDraggingInfo) -> Bool { true }
    override func performDragOperation(_ sender: NSDraggingInfo) -> Bool {
        defer { clearDrop() }
        if let data = sender.draggingPasteboard.data(forType: .folderSplitterTab), let payload = try? JSONDecoder().decode(TabDragPayload.self, from: data) {
            return delegate?.tabBar(self, acceptTab: payload, at: insertionIndex ?? items.count) ?? false
        }
        let point = convert(sender.draggingLocation, from: nil)
        guard let cell = visibleIDs.compactMap({ cells[$0] }).first(where: { $0.frame.contains(point) }), let urls = sender.draggingPasteboard.readObjects(forClasses: [NSURL.self], options: [.urlReadingFileURLsOnly: true]) as? [URL], !urls.isEmpty else { return false }
        return delegate?.tabBar(self, acceptFiles: urls, onto: cell.item.id, operation: sender.draggingSourceOperationMask) ?? false
    }
    private func cancelSpringLoad() { springTask?.cancel(); springTask = nil; springID = nil }
    private func clearDrop() { cancelSpringLoad(); insertionIndex = nil; positionTabs(animated: true) }
}

@MainActor
private final class ChromeTabCell: NSView {
    weak var bar: TabBarView?
    var item: TabBarItem { didSet { refresh() } }
    var selected = false { didSet { refresh() } }
    private let title = NSTextField(labelWithString: "")
    private let icon = NSImageView()
    private let close = TabCloseButton()
    private var hovered = false { didSet { refresh() } }
    private var tracking: NSTrackingArea?
    override var isFlipped: Bool { true }
    init(item: TabBarItem) {
        self.item = item; super.init(frame: .zero)
        wantsLayer = true; layer?.cornerRadius = Metrics.mediumRadius
        title.lineBreakMode = .byTruncatingTail; addSubview(title); addSubview(icon)
        close.target = self; close.action = #selector(closeTab)
        close.setAccessibilityLabel(tr("Close Tab", "탭 닫기")); close.toolTip = tr("Close Tab  ⌘W", "탭 닫기  ⌘W"); addSubview(close)
        setAccessibilityElement(true); setAccessibilityRole(.radioButton)
        setAccessibilityCustomActions([NSAccessibilityCustomAction(name: tr("Close Tab", "탭 닫기"), target: self, selector: #selector(accessibilityClose))])
        refresh()
    }
    required init?(coder: NSCoder) { fatalError("init(coder:) is unavailable") }
    private func refresh() {
        title.stringValue = item.title; title.font = selected ? Typography.tabTitleSelected : Typography.tabTitle
        title.textColor = .labelColor; icon.image = item.icon ?? NSImage(systemSymbolName: item.isBlank ? "square.dashed" : "folder", accessibilityDescription: nil)
        needsLayout = true
        toolTip = item.toolTip ?? item.title
        needsDisplay = true
        setAccessibilityLabel(item.title); setAccessibilityValue(selected ? 1 : 0)
    }
    override func viewDidChangeEffectiveAppearance() { super.viewDidChangeEffectiveAppearance(); needsDisplay = true }
    override func draw(_ dirtyRect: NSRect) {
        let shape = NSBezierPath(roundedRect: bounds.insetBy(dx: 0.5, dy: 0.5), xRadius: Metrics.mediumRadius, yRadius: Metrics.mediumRadius)
        (selected ? NSColor.controlColor : (hovered ? NSColor.quaternaryLabelColor : .clear)).setFill(); shape.fill()
        if selected && Motion.increaseContrast { Palette.separator.setStroke(); shape.stroke() }
    }
    /// Like the Windows app: the current tab always shows its close button, other tabs too when
    /// they are wide enough; narrow background tabs show it under the pointer. It sits at the
    /// trailing edge so the folder icon stays visible.
    private static let alwaysCloseWidth: CGFloat = 96
    override func layout() {
        super.layout()
        let showsClose = selected || hovered || bounds.width >= Self.alwaysCloseWidth
        close.isHidden = !showsClose
        close.isProminent = selected
        icon.frame = NSRect(x: 7, y: 5, width: 16, height: 16)
        close.frame = NSRect(x: bounds.width - 22, y: 4, width: 18, height: 18)
        let trailing: CGFloat = showsClose ? 26 : 8
        title.frame = NSRect(x: 28, y: 5, width: max(0, bounds.width - 28 - trailing), height: 17)
    }
    override func updateTrackingAreas() {
        super.updateTrackingAreas(); if let tracking { removeTrackingArea(tracking) }
        let area = NSTrackingArea(rect: bounds, options: [.mouseEnteredAndExited, .activeInKeyWindow, .inVisibleRect], owner: self)
        addTrackingArea(area); tracking = area
    }
    override func mouseEntered(with event: NSEvent) { hovered = true }
    override func mouseExited(with event: NSEvent) { hovered = false }
    override func mouseDown(with event: NSEvent) { bar?.track(self, event: event) }
    override func otherMouseDown(with event: NSEvent) { if event.buttonNumber == 2 { bar?.close(item.id) } }
    override func menu(for event: NSEvent) -> NSMenu? { bar?.menu(item.id) }
    override func accessibilityPerformPress() -> Bool { bar?.select(item.id); return true }
    @objc private func closeTab() { bar?.close(item.id) }
    @objc private func accessibilityClose() -> Bool { bar?.close(item.id); return true }
}

/// The "×" of a tab: a small circular target that highlights under the pointer and on press.
@MainActor
private final class TabCloseButton: NSButton {
    /// Full-strength glyph on the selected tab, secondary on the others.
    var isProminent = false { didSet { if isProminent != oldValue { needsDisplay = true } } }
    private var hovering = false { didSet { needsDisplay = true } }
    private var tracking: NSTrackingArea?

    init() {
        super.init(frame: .zero)
        title = ""
        isBordered = false
        setButtonType(.momentaryChange)
    }

    required init?(coder: NSCoder) { fatalError("init(coder:) is unavailable") }

    override func draw(_ dirtyRect: NSRect) {
        if isHighlighted || hovering {
            (isHighlighted ? NSColor.tertiaryLabelColor : NSColor.quaternaryLabelColor).setFill()
            NSBezierPath(roundedRect: bounds, xRadius: Metrics.smallRadius, yRadius: Metrics.smallRadius).fill()
        }
        let configuration = NSImage.SymbolConfiguration(pointSize: 9, weight: .bold)
            .applying(.init(paletteColors: [isProminent || hovering ? .labelColor : .secondaryLabelColor]))
        guard let glyph = NSImage(systemSymbolName: "xmark", accessibilityDescription: nil)?
            .withSymbolConfiguration(configuration) else { return }
        let size = glyph.size
        glyph.draw(in: NSRect(x: (bounds.width - size.width) / 2, y: (bounds.height - size.height) / 2,
                              width: size.width, height: size.height))
    }

    override func updateTrackingAreas() {
        super.updateTrackingAreas()
        if let tracking { removeTrackingArea(tracking) }
        let area = NSTrackingArea(rect: .zero, options: [.mouseEnteredAndExited, .activeInActiveApp, .inVisibleRect], owner: self)
        addTrackingArea(area)
        tracking = area
    }

    override func mouseEntered(with event: NSEvent) { hovering = true }
    override func mouseExited(with event: NSEvent) { hovering = false }
    override var isHidden: Bool { didSet { if isHidden { hovering = false } } }
}
