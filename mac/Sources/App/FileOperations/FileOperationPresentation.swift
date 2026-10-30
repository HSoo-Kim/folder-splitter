import AppKit

@MainActor
enum FileConflictPresentation {
    static func alert(for conflict: FileConflict) -> NSAlert {
        let alert = NSAlert()
        alert.alertStyle = .warning
        let name = conflict.destination.lastPathComponent
        let folder = conflict.destination.deletingLastPathComponent().lastPathComponent
        alert.messageText = tr("An item named “\(name)” already exists in “\(folder)”.", "‘\(folder)’에 ‘\(name)’ 이름의 항목이 이미 있습니다.")
        alert.informativeText = tr("Choose whether to keep both items, replace the existing item, or skip this item. Replaced items are moved to the Trash.", "두 항목을 모두 유지하거나, 기존 항목을 대치하거나, 이 항목을 건너뛰도록 선택하십시오. 대치된 항목은 휴지통으로 이동됩니다.")
        alert.addButton(withTitle: tr("Keep Both", "모두 유지"))
        alert.addButton(withTitle: tr("Stop", "정지"))
        alert.addButton(withTitle: tr("Replace", "대치"))
        alert.addButton(withTitle: tr("Skip", "건너뛰기"))
        alert.buttons[1].keyEquivalent = "\u{1b}"
        alert.showsSuppressionButton = conflict.remainingItems > 0
        alert.suppressionButton?.title = tr("Apply to All", "모두에 적용")
        let details = NSStackView()
        details.orientation = .vertical
        details.alignment = .leading
        details.spacing = Metrics.s
        func row(_ title: String, bytes: Int64, modified: Date?) -> NSView {
            let heading = NSTextField(labelWithString: title)
            heading.font = Typography.sectionHeader
            let size = Format.bytes(bytes)
            let date = modified?.formatted(.dateTime.year().month(.abbreviated).day().hour().minute().locale(Loc.locale)) ?? tr("Unknown date", "알 수 없는 날짜")
            let subtitle = NSTextField(labelWithString: "\(size) · \(date)")
            subtitle.font = Typography.listSecondary(.small)
            subtitle.textColor = .secondaryLabelColor
            let stack = NSStackView(views: [heading, subtitle])
            stack.orientation = .vertical; stack.alignment = .leading; stack.spacing = Metrics.xs
            return stack
        }
        details.addArrangedSubview(row(tr("Incoming item", "새 항목"), bytes: conflict.sourceBytes, modified: conflict.sourceModified))
        details.addArrangedSubview(row(tr("Existing item", "기존 항목"), bytes: conflict.destinationBytes, modified: conflict.destinationModified))
        details.setFrameSize(NSSize(width: 360, height: 100))
        alert.accessoryView = details
        return alert
    }
}

final class FileProgressSnapshot: @unchecked Sendable {
    private let lock = NSLock()
    private var current = FileOperationProgress(completedBytes: 0, totalBytes: 0, item: nil)
    func set(_ value: FileOperationProgress) { lock.lock(); current = value; lock.unlock() }
    func get() -> FileOperationProgress { lock.lock(); defer { lock.unlock() }; return current }
}

@MainActor
final class FileProgressRow: NSView {
    private let heading = NSTextField(labelWithString: "")
    private let detail = NSTextField(labelWithString: "")
    private let bar = NSProgressIndicator()
    private let cancelButton = NSButton()
    let cancellation: FileOperationCancellation
    let snapshot: FileProgressSnapshot
    let started = Date()
    var isVisible = false

    init(title: String, cancellation: FileOperationCancellation, snapshot: FileProgressSnapshot) {
        self.cancellation = cancellation; self.snapshot = snapshot
        super.init(frame: NSRect(x: 0, y: 0, width: 420, height: 94))
        heading.stringValue = title; heading.font = Typography.list(.medium)
        heading.lineBreakMode = .byTruncatingMiddle
        detail.font = Typography.status; detail.textColor = .secondaryLabelColor
        bar.style = .bar; bar.isIndeterminate = true; bar.startAnimation(nil)
        cancelButton.image = NSImage(systemSymbolName: "xmark.circle.fill", accessibilityDescription: tr("Cancel Operation", "작업 취소"))
        cancelButton.isBordered = false; cancelButton.target = self; cancelButton.action = #selector(cancel)
        cancelButton.setAccessibilityLabel(tr("Cancel Operation", "작업 취소"))
        for view in [heading, detail, bar, cancelButton] { view.translatesAutoresizingMaskIntoConstraints = false; addSubview(view) }
        NSLayoutConstraint.activate([
            widthAnchor.constraint(equalToConstant: 420), heightAnchor.constraint(equalToConstant: 94),
            heading.leadingAnchor.constraint(equalTo: leadingAnchor, constant: Metrics.l), heading.topAnchor.constraint(equalTo: topAnchor, constant: Metrics.m),
            heading.trailingAnchor.constraint(equalTo: cancelButton.leadingAnchor, constant: -Metrics.s),
            cancelButton.trailingAnchor.constraint(equalTo: trailingAnchor, constant: -Metrics.m), cancelButton.centerYAnchor.constraint(equalTo: heading.centerYAnchor),
            cancelButton.widthAnchor.constraint(equalToConstant: 22), cancelButton.heightAnchor.constraint(equalToConstant: 22),
            bar.topAnchor.constraint(equalTo: heading.bottomAnchor, constant: Metrics.s), bar.leadingAnchor.constraint(equalTo: heading.leadingAnchor), bar.trailingAnchor.constraint(equalTo: trailingAnchor, constant: -Metrics.l),
            detail.topAnchor.constraint(equalTo: bar.bottomAnchor, constant: Metrics.s), detail.leadingAnchor.constraint(equalTo: heading.leadingAnchor), detail.trailingAnchor.constraint(equalTo: bar.trailingAnchor)
        ])
        setAccessibilityRole(.group); setAccessibilityLabel(title)
        refresh()
    }
    required init?(coder: NSCoder) { fatalError("init(coder:) is unavailable") }
    @objc private func cancel() {
        cancellation.cancel(); cancelButton.isEnabled = false
        detail.stringValue = tr("Stopping…", "정지하는 중…")
    }
    func refresh() {
        if cancellation.isCancelled { return }
        let value = snapshot.get()
        bar.isIndeterminate = value.totalBytes == 0
        if value.totalBytes > 0 { bar.maxValue = Double(value.totalBytes); bar.doubleValue = Double(value.completedBytes) }
        let elapsed = max(0.1, Date().timeIntervalSince(started))
        let speed = Double(value.completedBytes) / elapsed
        let completed = Format.bytes(value.completedBytes)
        let total = Format.bytes(value.totalBytes)
        if value.totalBytes == 0 { detail.stringValue = tr("Working…", "작업 중…"); return }
        let rate = Format.bytes(Int64(speed))
        let remaining = speed > 0 ? Int(ceil(Double(max(0, value.totalBytes - value.completedBytes)) / speed)) : 0
        let duration = remaining >= 60 ? tr("\(remaining / 60) min remaining", "\(remaining / 60)분 남음") : tr("\(remaining) sec remaining", "\(remaining)초 남음")
        detail.stringValue = tr("\(completed) / \(total) · \(rate)/s · \(duration)", "\(completed) / \(total) · \(rate)/초 · \(duration)")
    }
}

@MainActor
final class FileProgressPanel: NSPanel {
    private let stack = NSStackView()
    init() {
        super.init(contentRect: NSRect(x: 0, y: 0, width: 420, height: 94), styleMask: [.titled, .utilityWindow, .nonactivatingPanel], backing: .buffered, defer: false)
        title = tr("File Operations", "파일 작업")
        isFloatingPanel = true; hidesOnDeactivate = false; isReleasedWhenClosed = false
        level = .floating
        let material = NSVisualEffectView()
        material.material = .hudWindow; material.blendingMode = .behindWindow; material.state = .active
        material.wantsLayer = true
        if Motion.reduceTransparency { material.state = .inactive; material.layer?.backgroundColor = NSColor.windowBackgroundColor.cgColor }
        stack.orientation = .vertical; stack.spacing = 0; stack.alignment = .leading
        stack.translatesAutoresizingMaskIntoConstraints = false
        material.addSubview(stack)
        NSLayoutConstraint.activate([stack.leadingAnchor.constraint(equalTo: material.leadingAnchor), stack.trailingAnchor.constraint(equalTo: material.trailingAnchor), stack.topAnchor.constraint(equalTo: material.topAnchor), stack.bottomAnchor.constraint(equalTo: material.bottomAnchor)])
        contentView = material
    }
    func setRows(_ rows: [FileProgressRow]) {
        for view in stack.arrangedSubviews { stack.removeArrangedSubview(view); view.removeFromSuperview() }
        for row in rows { stack.addArrangedSubview(row) }
        setContentSize(NSSize(width: 420, height: max(1, rows.count) * 94))
    }
    func show(_ rows: [FileProgressRow]) {
        setRows(rows)
        if !isVisible { center(); alphaValue = 0; orderFront(nil) }
        Motion.animate { self.animator().alphaValue = 1 }
    }
    func dismiss() {
        Motion.animate({ self.animator().alphaValue = 0 }, completion: { [weak self] in
            guard let self, self.alphaValue == 0 else { return }
            self.orderOut(nil)
        })
    }
}
