import AppKit

/// The window's bottom line, as in the Windows app: the version on the left (with the update
/// link when a newer release exists) and the author on the right.
@MainActor
final class WindowFooterView: NSView {
    static let height: CGFloat = 22
    static let authorURL = URL(string: "https://github.com/HSoo-Kim")!

    private let version = FooterLinkButton()
    private let author = FooterLinkButton()

    override init(frame frameRect: NSRect) {
        super.init(frame: frameRect)
        author.setText("Made by HSoo-Kim (github.com/HSoo-Kim)", clickable: true)
        author.toolTip = "github.com/HSoo-Kim"
        author.setAccessibilityLabel("Made by HSoo-Kim")
        author.onPress = { NSWorkspace.shared.open(Self.authorURL) }
        version.onPress = { Task { await UpdateController.shared.install() } }
        for view in [version, author] {
            view.translatesAutoresizingMaskIntoConstraints = false
            addSubview(view)
        }
        version.setContentCompressionResistancePriority(.defaultLow, for: .horizontal)
        author.setContentCompressionResistancePriority(.defaultLow - 1, for: .horizontal)
        NSLayoutConstraint.activate([
            version.leadingAnchor.constraint(equalTo: leadingAnchor, constant: Metrics.m),
            version.centerYAnchor.constraint(equalTo: centerYAnchor),
            author.trailingAnchor.constraint(equalTo: trailingAnchor, constant: -Metrics.m),
            author.centerYAnchor.constraint(equalTo: centerYAnchor),
            author.leadingAnchor.constraint(greaterThanOrEqualTo: version.trailingAnchor, constant: Metrics.l),
        ])
        setAccessibilityElement(false)
        render()
        NotificationCenter.default.addObserver(self, selector: #selector(stateChanged), name: .updateStateDidChange, object: nil)
    }

    required init?(coder: NSCoder) { fatalError("init(coder:) is not used") }

    override var isOpaque: Bool { true }

    override func draw(_ dirtyRect: NSRect) {
        NSColor.windowBackgroundColor.setFill()
        bounds.intersection(dirtyRect).fill()
        Palette.separator.setFill()
        NSRect(x: 0, y: bounds.maxY - 1, width: bounds.width, height: 1).intersection(dirtyRect).fill()
    }

    @objc private func stateChanged() { render() }

    private func render() {
        let updates = UpdateController.shared
        let current = "v\(updates.current)"
        switch (updates.phase, updates.available) {
        case (.installing(let progress), let release?):
            let percent = progress.map { " \(Int(($0 * 100).rounded()))%" } ?? ""
            version.setText(current, extra: tr("Updating to \(release.version)…", "\(release.version)(으)로 업데이트하는 중…") + percent, clickable: false)
        case (.installed, let release?):
            version.setText(current, extra: tr("Updated to \(release.version)", "\(release.version)(으)로 업데이트됨"), clickable: false)
        case (.idle, let release?):
            let action = updates.canInstallAvailable ? tr("Update", "업데이트") : tr("Download", "다운로드")
            version.setText(current, extra: tr("New version \(release.version) available · \(action)",
                                               "새로운 버전 \(release.version) 사용 가능 · \(action)"), clickable: true)
        default:
            version.setText(current, clickable: false)
        }
    }
}

/// A borderless text control in secondary text. When clickable it shows the pointing hand, and
/// an `extra` part is drawn in the link colour (the author line is clickable as a whole).
@MainActor
private final class FooterLinkButton: NSButton {
    var onPress: (() -> Void)?
    private var clickable = false

    init() {
        super.init(frame: .zero)
        isBordered = false
        setButtonType(.momentaryChange)
        target = self
        action = #selector(pressed)
        lineBreakMode = .byTruncatingTail
    }

    required init?(coder: NSCoder) { fatalError("init(coder:) is not used") }

    func setText(_ text: String, extra: String? = nil, clickable: Bool) {
        let font = Typography.status
        let result = NSMutableAttributedString(string: text, attributes: [.font: font, .foregroundColor: NSColor.secondaryLabelColor])
        if let extra {
            result.append(NSAttributedString(string: "   ", attributes: [.font: font]))
            result.append(NSAttributedString(string: extra, attributes: [
                .font: font, .foregroundColor: clickable ? NSColor.linkColor : NSColor.secondaryLabelColor,
            ]))
        }
        attributedTitle = result
        self.clickable = clickable
        setAccessibilityLabel(result.string)
        window?.invalidateCursorRects(for: self)
    }

    override func resetCursorRects() {
        if clickable { addCursorRect(bounds, cursor: .pointingHand) }
    }

    @objc private func pressed() {
        if clickable { onPress?() }
    }
}

/// Non-blocking card in the bottom-right of the window offering the new version, like the
/// Windows app's notice. It rises from the footer it belongs to and leaves the same way.
@MainActor
final class UpdateNoticeView: NSVisualEffectView {
    private let title = NSTextField(labelWithString: "")
    private let detail = NSTextField(labelWithString: "")
    private let later = NSButton()
    private let update = NSButton()

    override init(frame frameRect: NSRect) {
        super.init(frame: frameRect)
        material = Motion.reduceTransparency ? .windowBackground : .popover
        blendingMode = .withinWindow
        state = .active
        wantsLayer = true
        layer?.cornerRadius = Metrics.largeRadius
        layer?.cornerCurve = .continuous
        layer?.borderWidth = Motion.increaseContrast ? 1 : 0.5
        layer?.borderColor = NSColor.separatorColor.cgColor
        shadow = {
            let shadow = NSShadow()
            shadow.shadowBlurRadius = 12
            shadow.shadowOffset = NSSize(width: 0, height: -3)
            shadow.shadowColor = NSColor.black.withAlphaComponent(0.18)
            return shadow
        }()

        let icon = NSImageView(image: NSApp.applicationIconImage ?? NSImage())
        icon.imageScaling = .scaleProportionallyUpOrDown
        title.font = .systemFont(ofSize: 13, weight: .semibold)
        title.lineBreakMode = .byTruncatingTail
        detail.font = .systemFont(ofSize: 12)
        detail.textColor = .secondaryLabelColor
        later.title = tr("Later", "나중에")
        later.bezelStyle = .push
        later.target = self
        later.action = #selector(laterPressed)
        update.bezelStyle = .push
        update.target = self
        update.action = #selector(updatePressed)
        update.keyEquivalent = ""   // non-modal: Return stays with the file list
        update.bezelColor = .controlAccentColor
        setAccessibilityElement(true)
        setAccessibilityRole(.group)

        let text = NSStackView(views: [title, detail])
        text.orientation = .vertical
        text.alignment = .leading
        text.spacing = 2
        let buttons = NSStackView(views: [later, update])
        buttons.spacing = Metrics.s
        for view in [icon, text, buttons] as [NSView] {
            view.translatesAutoresizingMaskIntoConstraints = false
            addSubview(view)
        }
        NSLayoutConstraint.activate([
            widthAnchor.constraint(equalToConstant: 340),
            icon.leadingAnchor.constraint(equalTo: leadingAnchor, constant: Metrics.m),
            icon.topAnchor.constraint(equalTo: topAnchor, constant: Metrics.m),
            icon.widthAnchor.constraint(equalToConstant: 40),
            icon.heightAnchor.constraint(equalToConstant: 40),
            text.leadingAnchor.constraint(equalTo: icon.trailingAnchor, constant: Metrics.m),
            text.trailingAnchor.constraint(lessThanOrEqualTo: trailingAnchor, constant: -Metrics.m),
            text.centerYAnchor.constraint(equalTo: icon.centerYAnchor),
            buttons.topAnchor.constraint(equalTo: icon.bottomAnchor, constant: Metrics.m),
            buttons.trailingAnchor.constraint(equalTo: trailingAnchor, constant: -Metrics.m),
            buttons.bottomAnchor.constraint(equalTo: bottomAnchor, constant: -Metrics.m),
        ])
    }

    required init?(coder: NSCoder) { fatalError("init(coder:) is not used") }

    func configure(_ release: ReleaseInfo, current: AppVersion, installable: Bool) {
        title.stringValue = tr("Folder Splitter \(release.version) is available", "Folder Splitter \(release.version)을(를) 사용할 수 있습니다")
        detail.stringValue = tr("You have \(current).", "현재 버전은 \(current)입니다.")
        update.title = installable ? tr("Update", "업데이트") : tr("Download", "다운로드")
        update.toolTip = installable
            ? tr("Download, verify and install, then reopen Folder Splitter", "다운로드하고 검증한 뒤 설치하고 Folder Splitter를 다시 엽니다")
            : tr("Open the release page", "릴리스 페이지를 엽니다")
        setAccessibilityLabel(title.stringValue)
    }

    @objc private func laterPressed() { UpdateController.shared.dismissNotice() }
    @objc private func updatePressed() { Task { await UpdateController.shared.install() } }
}
