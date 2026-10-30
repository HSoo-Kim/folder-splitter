import AppKit

@MainActor
protocol PaneLayoutViewDelegate: AnyObject {
    func paneLayoutView(_ view: PaneLayoutView, viewFor pane: UUID) -> NSView
    /// `animated`: the divider was released past a limit (or double-clicked) and should spring into place.
    func paneLayoutView(_ view: PaneLayoutView, didChangeRatio ratio: Double, at path: [Int], animated: Bool)
    /// Whether a tab may be dropped on `pane` in `zone` (edges need room for another pane).
    func paneLayoutView(_ view: PaneLayoutView, canAccept payload: TabDragPayload, on pane: UUID, zone: DockZone) -> Bool
    func paneLayoutView(_ view: PaneLayoutView, accept payload: TabDragPayload, on pane: UUID, zone: DockZone) -> Bool
}

/// Lays out up to four panes from a `PaneLayoutTree`. Panes enter by growing out of the edge they
/// split from and leave the same way; dividers track the pointer 1:1 and rubber-band at limits.
@MainActor
final class PaneLayoutView: NSView {
    weak var delegate: PaneLayoutViewDelegate?
    private(set) var tree: PaneLayoutTree?
    /// Clipping containers, one per visible pane; the pane's own view sits inside at full size.
    private var containers: [UUID: NSView] = [:]
    private var dividers: [DividerView] = []
    private let overlay = DockOverlayView(frame: .zero)
    private var overlayPane: UUID?
    private var dragRatioOverride: (path: [Int], ratio: Double)?

    static let minimumPaneSize = NSSize(width: 220, height: 140)

    override init(frame frameRect: NSRect) {
        super.init(frame: frameRect)
        wantsLayer = true
        registerForDraggedTypes([.folderSplitterTab])
        overlay.isHidden = true
        addSubview(overlay)
    }

    required init?(coder: NSCoder) { fatalError("init(coder:) is not used") }

    override var isFlipped: Bool { true }

    // MARK: Applying a tree

    func apply(_ newTree: PaneLayoutTree, animated: Bool) {
        let oldTree = tree
        let oldFrames = oldTree.map { frames(for: $0, in: bounds) } ?? [:]
        tree = newTree
        let newFrames = frames(for: newTree, in: bounds)
        let newPanes = Set(newTree.panes)
        let animate = animated && window != nil && !bounds.isEmpty

        // Panes leaving: collapse toward their far edge and fade, then remove.
        for (id, container) in containers where !newPanes.contains(id) {
            containers[id] = nil
            guard animate, let old = oldTree, let frame = oldFrames[id] else {
                container.removeFromSuperview()
                continue
            }
            let collapsed = Self.collapsed(frame, edge: Self.farEdge(of: id, in: old))
            Motion.animate({
                container.animator().frame = collapsed
                container.animator().alphaValue = 0
            }, completion: { container.removeFromSuperview() })
        }

        // Panes entering start collapsed at their far edge.
        for id in newTree.panes where containers[id] == nil {
            let container = PaneContainer(frame: .zero)
            if let delegate {
                let content = delegate.paneLayoutView(self, viewFor: id)
                content.removeFromSuperview()
                content.translatesAutoresizingMaskIntoConstraints = true
                content.autoresizingMask = []
                container.addSubview(content)
            }
            containers[id] = container
            addSubview(container, positioned: .below, relativeTo: dividers.first)
            if animate, let frame = newFrames[id] {
                container.frame = Self.collapsed(frame, edge: Self.farEdge(of: id, in: newTree))
                container.alphaValue = 0
            } else {
                container.frame = newFrames[id] ?? .zero
            }
        }

        rebuildDividers()
        layoutContents(newFrames)
        if animate {
            Motion.animate({
                for (id, frame) in newFrames {
                    containers[id]?.animator().frame = frame
                    containers[id]?.animator().alphaValue = 1
                }
            })
        } else {
            for (id, frame) in newFrames {
                containers[id]?.frame = frame
                containers[id]?.alphaValue = 1
            }
        }
        layoutDividers(for: newTree)
    }

    /// Re-parents the pane's view (after a pane controller rebuilt it).
    func reloadContent(for pane: UUID) {
        guard let container = containers[pane], let delegate else { return }
        container.subviews.forEach { $0.removeFromSuperview() }
        let content = delegate.paneLayoutView(self, viewFor: pane)
        content.translatesAutoresizingMaskIntoConstraints = true
        container.addSubview(content)
        content.frame = container.bounds
    }

    func frame(of pane: UUID) -> NSRect? { containers[pane]?.frame }

    override func layout() {
        super.layout()
        guard let tree else { return }
        let target = frames(for: tree, in: bounds)
        for (id, frame) in target { containers[id]?.frame = frame }
        layoutContents(target)
        layoutDividers(for: tree)
        if let overlayPane, let frame = target[overlayPane] { overlay.frame = frame }
    }

    private func layoutContents(_ frames: [UUID: NSRect]) {
        for (id, frame) in frames {
            guard let content = containers[id]?.subviews.first else { continue }
            content.frame = NSRect(origin: .zero, size: frame.size)
        }
    }

    // MARK: Geometry

    private func frames(for tree: PaneLayoutTree, in rect: NSRect) -> [UUID: NSRect] {
        var result: [UUID: NSRect] = [:]
        walk(tree, rect: rect, path: []) { node, rect, _ in
            if case .pane(let id) = node { result[id] = rect.integral }
        }
        return result
    }

    /// Visits every node with its rectangle and child path.
    private func walk(_ node: PaneLayoutTree, rect: NSRect, path: [Int],
                      _ visit: (PaneLayoutTree, NSRect, [Int]) -> Void) {
        visit(node, rect, path)
        guard case .split(let vertical, let storedRatio, let first, let second) = node else { return }
        let ratio = dragRatioOverride.flatMap { $0.path == path ? $0.ratio : nil } ?? storedRatio
        let (a, b) = Self.divide(rect, vertical: vertical, ratio: ratio)
        walk(first, rect: a, path: path + [0], visit)
        walk(second, rect: b, path: path + [1], visit)
    }

    private static func divide(_ rect: NSRect, vertical: Bool, ratio: Double) -> (NSRect, NSRect) {
        let gap = Metrics.paneGap
        if vertical {
            let firstWidth = ((rect.width - gap) * ratio).rounded()
            return (NSRect(x: rect.minX, y: rect.minY, width: firstWidth, height: rect.height),
                    NSRect(x: rect.minX + firstWidth + gap, y: rect.minY, width: rect.width - firstWidth - gap, height: rect.height))
        }
        let firstHeight = ((rect.height - gap) * ratio).rounded()
        return (NSRect(x: rect.minX, y: rect.minY, width: rect.width, height: firstHeight),
                NSRect(x: rect.minX, y: rect.minY + firstHeight + gap, width: rect.width, height: rect.height - firstHeight - gap))
    }

    /// The edge of the pane farthest from its sibling: where it grows from and shrinks back to.
    private static func farEdge(of pane: UUID, in tree: PaneLayoutTree) -> SplitEdge {
        func search(_ node: PaneLayoutTree) -> SplitEdge? {
            guard case .split(let vertical, _, let first, let second) = node else { return nil }
            if first.panes.contains(pane) {
                if case .pane = first { return vertical ? .left : .top }
                return search(first)
            }
            if second.panes.contains(pane) {
                if case .pane = second { return vertical ? .right : .bottom }
                return search(second)
            }
            return nil
        }
        return search(tree) ?? .right
    }

    private static func collapsed(_ frame: NSRect, edge: SplitEdge) -> NSRect {
        switch edge {
        case .left: return NSRect(x: frame.minX, y: frame.minY, width: 0, height: frame.height)
        case .right: return NSRect(x: frame.maxX, y: frame.minY, width: 0, height: frame.height)
        case .top: return NSRect(x: frame.minX, y: frame.minY, width: frame.width, height: 0)
        case .bottom: return NSRect(x: frame.minX, y: frame.maxY, width: frame.width, height: 0)
        }
    }

    // MARK: Dividers

    private func rebuildDividers() {
        dividers.forEach { $0.removeFromSuperview() }
        dividers = []
        guard let tree else { return }
        walk(tree, rect: bounds, path: []) { node, _, path in
            guard case .split(let vertical, _, _, _) = node else { return }
            let divider = DividerView(path: path, vertical: vertical)
            divider.owner = self
            dividers.append(divider)
            addSubview(divider)
        }
    }

    private func layoutDividers(for tree: PaneLayoutTree) {
        var rects: [[Int]: NSRect] = [:]
        walk(tree, rect: bounds, path: []) { node, rect, path in
            guard case .split(let vertical, let storedRatio, _, _) = node else { return }
            let ratio = dragRatioOverride.flatMap { $0.path == path ? $0.ratio : nil } ?? storedRatio
            let (a, _) = Self.divide(rect, vertical: vertical, ratio: ratio)
            rects[path] = vertical
                ? NSRect(x: a.maxX, y: rect.minY, width: Metrics.paneGap, height: rect.height)
                : NSRect(x: rect.minX, y: a.maxY, width: rect.width, height: Metrics.paneGap)
        }
        for divider in dividers {
            guard let line = rects[divider.path] else { continue }
            // A wider invisible grab area around the 1-pt line.
            divider.frame = divider.vertical ? line.insetBy(dx: -3, dy: 0) : line.insetBy(dx: 0, dy: -3)
        }
    }

    /// Rectangle a split occupies and its stored ratio.
    fileprivate func splitInfo(at path: [Int]) -> (rect: NSRect, vertical: Bool, ratio: Double)? {
        guard let tree else { return nil }
        var found: (NSRect, Bool, Double)?
        walk(tree, rect: bounds, path: []) { node, rect, nodePath in
            if nodePath == path, case .split(let vertical, let ratio, _, _) = node { found = (rect, vertical, ratio) }
        }
        return found
    }

    fileprivate func dividerDragged(path: [Int], ratio: Double) {
        dragRatioOverride = (path, ratio)
        needsLayout = true
        layoutSubtreeIfNeeded()
    }

    fileprivate func dividerReleased(path: [Int], ratio: Double, settle: Double) {
        dragRatioOverride = nil
        delegate?.paneLayoutView(self, didChangeRatio: settle, at: path, animated: abs(settle - ratio) > 0.0005)
    }

    /// Allowed ratio range so neither side gets smaller than the minimum pane size.
    fileprivate func ratioLimits(rect: NSRect, vertical: Bool) -> ClosedRange<Double> {
        let length = vertical ? rect.width : rect.height
        let minimum = vertical ? Self.minimumPaneSize.width : Self.minimumPaneSize.height
        guard length > 0 else { return 0.15...0.85 }
        let low = max(0.15, Double(minimum / length))
        let high = min(0.85, 1 - Double(minimum / length))
        return low <= high ? low...high : 0.5...0.5
    }

    // MARK: Tab drops (dock overlay)

    override func draggingEntered(_ sender: NSDraggingInfo) -> NSDragOperation { updateDrop(sender) }
    override func draggingUpdated(_ sender: NSDraggingInfo) -> NSDragOperation { updateDrop(sender) }
    override func draggingExited(_ sender: NSDraggingInfo?) { hideOverlay() }
    override func draggingEnded(_ sender: NSDraggingInfo) { hideOverlay() }

    override func performDragOperation(_ sender: NSDraggingInfo) -> Bool {
        defer { hideOverlay() }
        guard let (payload, pane, zone) = dropTarget(sender) else { return false }
        return delegate?.paneLayoutView(self, accept: payload, on: pane, zone: zone) ?? false
    }

    private func updateDrop(_ sender: NSDraggingInfo) -> NSDragOperation {
        guard let (_, pane, zone) = dropTarget(sender), let frame = containers[pane]?.frame else {
            hideOverlay()
            return []
        }
        addSubview(overlay, positioned: .above, relativeTo: nil)
        overlayPane = pane
        overlay.frame = frame
        overlay.isHidden = false
        overlay.show(zone, animated: true)
        return .move
    }

    private func hideOverlay() {
        guard overlayPane != nil else { return }
        overlay.hide(animated: true)
        overlayPane = nil
        // Take the overlay out of hit-testing once its fade has finished.
        DispatchQueue.main.asyncAfter(deadline: .now() + 0.3) { [weak self] in
            guard let self, self.overlayPane == nil else { return }
            self.overlay.isHidden = true
        }
    }

    private func dropTarget(_ sender: NSDraggingInfo) -> (TabDragPayload, UUID, DockZone)? {
        guard let data = sender.draggingPasteboard.data(forType: .folderSplitterTab),
              let payload = try? JSONDecoder().decode(TabDragPayload.self, from: data) else { return nil }
        let point = convert(sender.draggingLocation, from: nil)
        guard let (pane, frame) = containers.first(where: { $0.value.frame.contains(point) }).map({ ($0.key, $0.value.frame) })
        else { return nil }
        overlay.frame = frame
        var zone = overlay.zone(at: overlay.convert(point, from: self))
        if delegate?.paneLayoutView(self, canAccept: payload, on: pane, zone: zone) != true {
            zone = .center
            guard delegate?.paneLayoutView(self, canAccept: payload, on: pane, zone: zone) == true else { return nil }
        }
        return (payload, pane, zone)
    }
}

/// Clips its pane while it grows or shrinks.
private final class PaneContainer: NSView {
    override init(frame frameRect: NSRect) {
        super.init(frame: frameRect)
        wantsLayer = true
        layer?.masksToBounds = true
    }
    required init?(coder: NSCoder) { fatalError("init(coder:) is not used") }
    override var isFlipped: Bool { true }
}

/// A 1-pt separator with a 7-pt grab area. Drag tracks 1:1 from the grab offset, resists past
/// the minimum pane size, and springs back to the limit on release. Double-click evens the split.
private final class DividerView: NSView {
    let path: [Int]
    let vertical: Bool
    weak var owner: PaneLayoutView?
    private var grabOffset: CGFloat = 0
    private var liveRatio: Double = 0.5
    private var pressed = false { didSet { needsDisplay = true } }

    init(path: [Int], vertical: Bool) {
        self.path = path
        self.vertical = vertical
        super.init(frame: .zero)
        setAccessibilityElement(true)
        setAccessibilityRole(.splitter)
        setAccessibilityLabel(tr("Pane divider", "영역 구분선"))
    }

    required init?(coder: NSCoder) { fatalError("init(coder:) is not used") }

    override func draw(_ dirtyRect: NSRect) {
        let line = vertical
            ? NSRect(x: bounds.midX - Metrics.paneGap / 2, y: 0, width: Metrics.paneGap, height: bounds.height)
            : NSRect(x: 0, y: bounds.midY - Metrics.paneGap / 2, width: bounds.width, height: Metrics.paneGap)
        (pressed ? NSColor.controlAccentColor : Palette.separator).setFill()
        line.fill()
    }

    /// Dividers drag even in a background window, like other split views.
    override func acceptsFirstMouse(for event: NSEvent?) -> Bool { true }

    override func resetCursorRects() {
        addCursorRect(bounds, cursor: vertical ? .resizeLeftRight : .resizeUpDown)
    }

    override func mouseDown(with event: NSEvent) {
        guard let owner, let info = owner.splitInfo(at: path) else { return }
        if event.clickCount == 2 {
            owner.dividerReleased(path: path, ratio: info.ratio, settle: equalRatio())
            return
        }
        pressed = true
        let point = owner.convert(event.locationInWindow, from: nil)
        let center = vertical ? frame.midX : frame.midY
        grabOffset = (vertical ? point.x : point.y) - center
        liveRatio = info.ratio
    }

    override func mouseDragged(with event: NSEvent) {
        guard let owner, let info = owner.splitInfo(at: path) else { return }
        let point = owner.convert(event.locationInWindow, from: nil)
        let length = (vertical ? info.rect.width : info.rect.height) - Metrics.paneGap
        guard length > 0 else { return }
        let position = (vertical ? point.x - info.rect.minX : point.y - info.rect.minY) - grabOffset
        let raw = Double(position / length)
        let limits = owner.ratioLimits(rect: info.rect, vertical: vertical)
        let ratio: Double
        if raw < limits.lowerBound {
            let over = CGFloat(limits.lowerBound - raw) * length
            ratio = limits.lowerBound - Double(Motion.rubberband(over, dimension: length) / length)
        } else if raw > limits.upperBound {
            let over = CGFloat(raw - limits.upperBound) * length
            ratio = limits.upperBound + Double(Motion.rubberband(over, dimension: length) / length)
        } else {
            ratio = raw
        }
        liveRatio = ratio
        owner.dividerDragged(path: path, ratio: ratio)
    }

    override func mouseUp(with event: NSEvent) {
        guard pressed else { return }
        pressed = false
        guard let owner, let info = owner.splitInfo(at: path) else { return }
        let limits = owner.ratioLimits(rect: info.rect, vertical: vertical)
        let settled = min(max(liveRatio, limits.lowerBound), limits.upperBound)
        owner.dividerReleased(path: path, ratio: liveRatio, settle: settled)
    }

    /// Even split by leaf count, so three columns become thirds.
    private func equalRatio() -> Double {
        guard let owner, let tree = owner.tree else { return 0.5 }
        var node = tree
        for step in path {
            guard case .split(_, _, let first, let second) = node else { return 0.5 }
            node = step == 0 ? first : second
        }
        guard case .split(let vertical, _, let first, let second) = node else { return 0.5 }
        func leaves(_ n: PaneLayoutTree) -> Int {
            guard case .split(let v, _, let a, let b) = n else { return 1 }
            return v == vertical ? leaves(a) + leaves(b) : max(leaves(a), leaves(b))
        }
        let a = Double(leaves(first)), b = Double(leaves(second))
        return a / (a + b)
    }
}
