import AppKit

@MainActor
final class NavigationButtonsView: NSView {
    var canGoBack = false { didSet { back.isEnabled = canGoBack } }
    var canGoForward = false { didSet { forward.isEnabled = canGoForward } }
    var onBack: (() -> Void)?
    var onForward: (() -> Void)?
    var backMenu: (() -> NSMenu?)?
    var forwardMenu: (() -> NSMenu?)?
    private let back = HistoryButton(symbol: "chevron.left", label: tr("Back", "뒤로"))
    private let forward = HistoryButton(symbol: "chevron.right", label: tr("Forward", "앞으로"))

    override init(frame frameRect: NSRect) {
        super.init(frame: frameRect)
        addSubview(back); addSubview(forward)
        back.isEnabled = false; forward.isEnabled = false
        back.onPress = { [weak self] in self?.onBack?() }
        forward.onPress = { [weak self] in self?.onForward?() }
        back.history = { [weak self] in self?.backMenu?() }
        forward.history = { [weak self] in self?.forwardMenu?() }
    }
    required init?(coder: NSCoder) { fatalError("init(coder:) is unavailable") }
    override var intrinsicContentSize: NSSize { NSSize(width: 56, height: Metrics.addressBarHeight) }
    override func layout() {
        super.layout()
        back.frame = NSRect(x: 0, y: 0, width: bounds.width / 2, height: bounds.height)
        forward.frame = NSRect(x: bounds.width / 2, y: 0, width: bounds.width / 2, height: bounds.height)
    }
}

@MainActor
private final class HistoryButton: NSButton {
    var onPress: (() -> Void)?
    var history: (() -> NSMenu?)?
    init(symbol: String, label: String) {
        super.init(frame: .zero)
        image = NSImage(systemSymbolName: symbol, accessibilityDescription: label)
        title = ""; bezelStyle = .texturedRounded; isBordered = false
        toolTip = label; setAccessibilityLabel(label)
        target = self; action = #selector(activate)
    }
    required init?(coder: NSCoder) { fatalError("init(coder:) is unavailable") }
    @objc private func activate() { onPress?() }
    override func rightMouseDown(with event: NSEvent) {
        guard isEnabled, let menu = history?() else { return }
        NSMenu.popUpContextMenu(menu, with: event, for: self)
    }
    override func mouseDown(with event: NSEvent) {
        guard isEnabled, let window else { return }
        highlight(true)
        let heldUntil = Date(timeIntervalSinceNow: 0.45)
        while let next = window.nextEvent(matching: [.leftMouseUp, .leftMouseDragged], until: heldUntil, inMode: .eventTracking, dequeue: true) {
            if next.type == .leftMouseUp {
                highlight(false)
                if bounds.contains(convert(next.locationInWindow, from: nil)) { onPress?() }
                return
            }
        }
        highlight(false)
        if let menu = history?() { menu.popUp(positioning: nil, at: NSPoint(x: 0, y: bounds.minY), in: self) }
    }
}

@MainActor
final class StatusBarView: NSView {
    var paneNumber = 1 { didSet { setAccessibilityIdentifier(AccessibilityID.status(paneNumber)) } }
    private let primaryLabel = NSTextField(labelWithString: "")
    private let secondaryLabel = NSTextField(labelWithString: "")
    override init(frame frameRect: NSRect) {
        super.init(frame: frameRect)
        for label in [primaryLabel, secondaryLabel] {
            label.font = Typography.status; label.textColor = .secondaryLabelColor
            label.lineBreakMode = .byTruncatingTail; addSubview(label)
        }
        secondaryLabel.alignment = .right
        setAccessibilityIdentifier(AccessibilityID.status(paneNumber))
    }
    required init?(coder: NSCoder) { fatalError("init(coder:) is unavailable") }
    override var intrinsicContentSize: NSSize { NSSize(width: NSView.noIntrinsicMetric, height: Metrics.statusBarHeight) }
    func update(primary: String, secondary: String) {
        primaryLabel.stringValue = primary; secondaryLabel.stringValue = secondary; needsLayout = true
    }
    override func layout() {
        super.layout()
        let available = max(0, bounds.width - 16)
        let measured = (secondaryLabel.stringValue as NSString).size(withAttributes: [.font: Typography.status]).width
        let right = secondaryLabel.stringValue.isEmpty ? 0 : min(ceil(measured)+8, available * 0.45)
        secondaryLabel.frame = NSRect(x: bounds.width - right - 8, y: (bounds.height - 14) / 2, width: right, height: 14)
        primaryLabel.frame = NSRect(x: 8, y: (bounds.height - 14) / 2, width: max(0, available - right - (right > 0 ? 12 : 0)), height: 14)
    }
    override func draw(_ dirtyRect: NSRect) {
        super.draw(dirtyRect)
        Palette.separator.setFill(); NSRect(x: 0, y: bounds.height - 1, width: bounds.width, height: 1).fill()
    }
}

enum DockZone: Equatable { case center, edge(SplitEdge) }

@MainActor
final class DockOverlayView: NSView {
    private let highlight = NSVisualEffectView()
    private let label = NSTextField(labelWithString: "")
    private var currentZone: DockZone = .center
    private var visibilityGeneration = UUID()
    override init(frame frameRect: NSRect) {
        super.init(frame: frameRect)
        wantsLayer = true; isHidden = true
        highlight.material = .popover; highlight.blendingMode = .withinWindow; highlight.state = .active
        highlight.wantsLayer = true; highlight.layer?.cornerRadius = Metrics.largeRadius
        highlight.layer?.borderWidth = Motion.increaseContrast ? 2 : 1
        addSubview(highlight)
        label.font = Typography.title(); label.alignment = .center; label.textColor = .labelColor
        highlight.addSubview(label)
        setAccessibilityIdentifier(AccessibilityID.dockOverlay)
        setAccessibilityRole(.group)
    }
    required init?(coder: NSCoder) { fatalError("init(coder:) is unavailable") }
    override func hitTest(_ point: NSPoint) -> NSView? { nil }
    func zone(at point: NSPoint) -> DockZone {
        guard bounds.width > 0, bounds.height > 0 else { return .center }
        let x = (point.x - bounds.minX) / bounds.width
        let y = (point.y - bounds.minY) / bounds.height
        let distances: [(CGFloat, SplitEdge)] = [(x, .left), (1-x, .right), (isFlipped ? y : 1-y, .top), (isFlipped ? 1-y : y, .bottom)]
        if let nearest = distances.min(by: { $0.0 < $1.0 }), nearest.0 < 0.28 { return .edge(nearest.1) }
        return .center
    }
    func show(_ zone: DockZone, animated: Bool) {
        let wasHidden = isHidden
        visibilityGeneration = UUID()
        currentZone = zone; isHidden = false
        switch zone {
        case .center: label.stringValue = tr("Move Here", "여기로 이동")
        case .edge(.left): label.stringValue = tr("Split Left", "왼쪽으로 분할")
        case .edge(.right): label.stringValue = tr("Split Right", "오른쪽으로 분할")
        case .edge(.top): label.stringValue = tr("Split Above", "위쪽으로 분할")
        case .edge(.bottom): label.stringValue = tr("Split Below", "아래쪽으로 분할")
        }
        setAccessibilityLabel(label.stringValue)
        effectiveAppearance.performAsCurrentDrawingAppearance {
            highlight.layer?.borderColor = Palette.dropOutline.cgColor
            highlight.layer?.backgroundColor = (Motion.reduceTransparency ? NSColor.windowBackgroundColor : Palette.dropHighlight).cgColor
        }
        let target = zoneRect(zone)
        if animated && !Motion.reduceMotion && !wasHidden {
            Motion.animate(response: Motion.quickResponse) { self.highlight.animator().frame = target }
        } else { highlight.frame = target }
        layoutLabel()
        if animated, let layer {
            if wasHidden { alphaValue = 0 }
            Motion.animate(response: Motion.quickResponse) { self.animator().alphaValue = 1 }
            if wasHidden && !Motion.reduceMotion {
                layer.setValue(0.98, forKeyPath: "transform.scale")
                Motion.spring(layer, keyPath: "transform.scale", to: 1, response: Motion.quickResponse)
            }
        } else { alphaValue = 1 }
    }
    func hide(animated: Bool) {
        guard !isHidden else { return }
        let generation = UUID(); visibilityGeneration = generation
        if animated {
            Motion.animate(response: Motion.quickResponse, { self.animator().alphaValue = 0 }, completion: { [weak self] in
                guard let self, self.visibilityGeneration == generation else { return }; self.isHidden = true
            })
            if let layer, !Motion.reduceMotion { Motion.spring(layer, keyPath: "transform.scale", to: 0.98) }
        } else { isHidden = true; alphaValue = 0 }
    }
    override func viewDidChangeEffectiveAppearance() {
        super.viewDidChangeEffectiveAppearance()
        if !isHidden { show(currentZone, animated: false) }
    }
    override func layout() { super.layout(); highlight.frame = zoneRect(currentZone); layoutLabel() }
    private func layoutLabel() { label.frame = NSRect(x: 8, y: (highlight.bounds.height - 24)/2, width: max(0, highlight.bounds.width-16), height: 24) }
    private func zoneRect(_ zone: DockZone) -> NSRect {
        var rect = bounds.insetBy(dx: 8, dy: 8)
        switch zone {
        case .center: rect = rect.insetBy(dx: rect.width * 0.18, dy: rect.height * 0.18)
        case .edge(.left): rect.size.width *= 0.5
        case .edge(.right): rect.origin.x += rect.width * 0.5; rect.size.width *= 0.5
        case .edge(.top): rect.origin.y += isFlipped ? 0 : rect.height * 0.5; rect.size.height *= 0.5
        case .edge(.bottom): rect.origin.y += isFlipped ? rect.height * 0.5 : 0; rect.size.height *= 0.5
        }
        return rect
    }
}
