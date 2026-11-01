import AppKit

/// Shown in a pane with no tabs or on a blank tab: says where you are and offers the places
/// you are most likely to want, so an empty pane is never a dead end.
@MainActor
final class PaneEmptyStateView: NSView {
    var onOpen: ((URL) -> Void)?

    private let symbol = NSImageView()
    private let title = NSTextField(labelWithString: "")
    private let subtitle = NSTextField(wrappingLabelWithString: "")
    private let places = NSStackView()

    override init(frame frameRect: NSRect) {
        super.init(frame: frameRect)
        symbol.symbolConfiguration = .init(pointSize: 30, weight: .light)
        symbol.contentTintColor = .tertiaryLabelColor
        title.font = Typography.title(15)
        title.textColor = .secondaryLabelColor
        title.alignment = .center
        subtitle.font = .systemFont(ofSize: 12)
        subtitle.textColor = .tertiaryLabelColor
        subtitle.alignment = .center
        places.orientation = .vertical
        places.alignment = .leading
        places.spacing = 2

        let stack = NSStackView(views: [symbol, title, subtitle, places])
        stack.orientation = .vertical
        stack.alignment = .centerX
        stack.spacing = Metrics.s
        stack.setCustomSpacing(Metrics.l, after: subtitle)
        stack.translatesAutoresizingMaskIntoConstraints = false
        // In a short pane the stack drops the place list first, then the symbol, instead of
        // spilling over the address row.
        stack.setVisibilityPriority(NSStackView.VisibilityPriority(rawValue: 500), for: places)
        stack.setVisibilityPriority(NSStackView.VisibilityPriority(rawValue: 700), for: symbol)
        stack.setVisibilityPriority(NSStackView.VisibilityPriority(rawValue: 800), for: subtitle)
        stack.setClippingResistancePriority(.defaultLow, for: .vertical)
        addSubview(stack)
        let centered = stack.centerYAnchor.constraint(equalTo: centerYAnchor, constant: -Metrics.l)
        centered.priority = .defaultLow
        NSLayoutConstraint.activate([
            stack.centerXAnchor.constraint(equalTo: centerXAnchor),
            centered,
            stack.topAnchor.constraint(greaterThanOrEqualTo: topAnchor, constant: Metrics.m),
            stack.bottomAnchor.constraint(lessThanOrEqualTo: bottomAnchor, constant: -Metrics.m),
            stack.widthAnchor.constraint(lessThanOrEqualTo: widthAnchor, constant: -Metrics.xl * 2),
            subtitle.widthAnchor.constraint(lessThanOrEqualToConstant: 320),
        ])
    }

    required init?(coder: NSCoder) { fatalError("init(coder:) is not used") }

    func reload(blankTab: Bool) {
        symbol.image = NSImage(systemSymbolName: blankTab ? "magnifyingglass" : "rectangle.dashed", accessibilityDescription: nil)
        title.stringValue = blankTab ? tr("New Tab", "새로운 탭") : tr("Empty Pane", "빈 영역")
        subtitle.stringValue = blankTab
            ? tr("Type a folder path or part of a name above, or choose a place below.",
                 "위에 폴더 경로나 이름 일부를 입력하거나 아래에서 위치를 선택하십시오.")
            : tr("Drag a tab here, type a path above, or choose a place below.",
                 "탭을 여기로 드래그하거나, 위에 경로를 입력하거나, 아래에서 위치를 선택하십시오.")
        places.arrangedSubviews.forEach { $0.removeFromSuperview() }
        var seen = Set<String>()
        let recent = AppServices.shared.history.recentFolders
        let favorites = AppServices.shared.favorites.favorites
        for url in (recent + favorites) where seen.insert(url.standardizedFileURL.path).inserted {
            places.addArrangedSubview(PlaceButton(url: url) { [weak self] in self?.onOpen?(url) })
            if places.arrangedSubviews.count == 6 { break }
        }
        places.isHidden = places.arrangedSubviews.isEmpty
    }
}

/// A borderless row button: folder icon + name, highlights on press immediately.
private final class PlaceButton: NSButton {
    private let handler: () -> Void

    init(url: URL, handler: @escaping () -> Void) {
        self.handler = handler
        super.init(frame: .zero)
        title = TabState.quickName(of: url)
        toolTip = PathText.abbreviated(url)
        let icon = NSWorkspace.shared.icon(for: .folder)
        icon.size = NSSize(width: 16, height: 16)
        image = icon
        imagePosition = .imageLeading
        isBordered = false
        bezelStyle = .recessed
        showsBorderOnlyWhileMouseInside = true
        font = .systemFont(ofSize: 13)
        contentTintColor = .labelColor
        target = self
        action = #selector(open)
        setButtonType(.momentaryPushIn)
    }

    required init?(coder: NSCoder) { fatalError("init(coder:) is not used") }

    @objc private func open() { handler() }
}
