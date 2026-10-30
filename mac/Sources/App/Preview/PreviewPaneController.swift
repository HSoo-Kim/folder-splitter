import AppKit
import Quartz

private final class PreviewPosterView: NSImageView {
    override var intrinsicContentSize: NSSize { NSSize(width: NSView.noIntrinsicMetric, height: NSView.noIntrinsicMetric) }
}

/// Selection preview. Initialize AppServices before loading the view; selections are retained
/// across view loading and pinning freezes the displayed selection until the pin is released.
@MainActor final class PreviewPaneController: NSViewController {
    private(set) var isPinned = false
    private var urls: [URL] = []
    private var latestSelection: [URL] = []
    private var generation = 0
    private var metadataTask: Task<[PreviewMetadata], Never>?
    private var observer: NSObjectProtocol?
    private var watcher: PreviewFileWatcher?
    private let titleLabel = NSTextField(labelWithString: "")
    private let pin = NSButton()
    private let detach = NSButton()
    private let openButton = NSButton()
    private let content = NSView()
    private let details = PreviewDetailGrid()
    private let detailsScroll = NSScrollView()
    private var quickLook: QLPreviewView?
    private var posterView: NSImageView?
    private let summary = NSStackView()
    private let progress = NSProgressIndicator()
    private var detailsHeight: NSLayoutConstraint!
    private var collapsedDetailsHeight: NSLayoutConstraint!

    override func loadView() {
        view = NSView(); view.wantsLayer = true; view.setAccessibilityIdentifier(AccessibilityID.preview)
        titleLabel.font = Typography.tabTitleSelected; titleLabel.lineBreakMode = .byTruncatingMiddle
        titleLabel.setContentHuggingPriority(.defaultLow, for: .horizontal)
        titleLabel.setContentCompressionResistancePriority(.defaultLow, for: .horizontal)
        pin.image = NSImage(systemSymbolName: "pin", accessibilityDescription: tr("Pin Preview", "미리보기 고정")); pin.setButtonType(.toggle)
        pin.target = self; pin.action = #selector(togglePin)
        detach.image = NSImage(systemSymbolName: "arrow.up.left.and.arrow.down.right", accessibilityDescription: tr("Open Preview in New Window", "새로운 윈도우에서 미리보기 열기"))
        detach.target = self; detach.action = #selector(openWindow)
        openButton.image = NSImage(systemSymbolName: "arrow.up.forward.app", accessibilityDescription: tr("Open with Default Application", "기본 응용 프로그램으로 열기")); openButton.target = self; openButton.action = #selector(openDefault)
        for button in [pin, detach, openButton] {
            button.bezelStyle = .inline; button.isBordered = false
            button.toolTip = button.image?.accessibilityDescription
            button.widthAnchor.constraint(equalToConstant: 25).isActive = true
            button.heightAnchor.constraint(equalToConstant: 26).isActive = true
        }
        let header = NSStackView(views: [titleLabel, pin, detach, openButton]); header.spacing = 4; header.distribution = .fill
        let separator = NSBox(); separator.boxType = .separator
        detailsScroll.drawsBackground = false; detailsScroll.hasVerticalScroller = true; detailsScroll.autohidesScrollers = true; detailsScroll.documentView = details
        details.translatesAutoresizingMaskIntoConstraints = false
        summary.orientation = .vertical; summary.alignment = .centerX; summary.spacing = 12
        progress.style = .spinning; progress.controlSize = .small; progress.isDisplayedWhenStopped = false
        for child in [header, separator, content, detailsScroll] { child.translatesAutoresizingMaskIntoConstraints = false; view.addSubview(child) }
        for child in [summary, progress] { child.translatesAutoresizingMaskIntoConstraints = false; content.addSubview(child) }
        detailsHeight = detailsScroll.heightAnchor.constraint(equalTo: view.heightAnchor, multiplier: 0.38)
        collapsedDetailsHeight = detailsScroll.heightAnchor.constraint(equalToConstant: 0)
        NSLayoutConstraint.activate([
            header.leadingAnchor.constraint(equalTo: view.leadingAnchor, constant: 12), header.trailingAnchor.constraint(equalTo: view.trailingAnchor, constant: -8), header.topAnchor.constraint(equalTo: view.safeAreaLayoutGuide.topAnchor, constant: 8), header.heightAnchor.constraint(equalToConstant: 28),
            separator.topAnchor.constraint(equalTo: header.bottomAnchor, constant: 6), separator.leadingAnchor.constraint(equalTo: view.leadingAnchor), separator.trailingAnchor.constraint(equalTo: view.trailingAnchor),
            content.topAnchor.constraint(equalTo: separator.bottomAnchor), content.leadingAnchor.constraint(equalTo: view.leadingAnchor), content.trailingAnchor.constraint(equalTo: view.trailingAnchor), content.bottomAnchor.constraint(equalTo: detailsScroll.topAnchor, constant: -8),
            detailsScroll.leadingAnchor.constraint(equalTo: view.leadingAnchor, constant: 12), detailsScroll.trailingAnchor.constraint(equalTo: view.trailingAnchor, constant: -12), detailsScroll.bottomAnchor.constraint(equalTo: view.bottomAnchor, constant: -12), detailsHeight,
            details.leadingAnchor.constraint(equalTo: detailsScroll.contentView.leadingAnchor), details.topAnchor.constraint(equalTo: detailsScroll.contentView.topAnchor), details.widthAnchor.constraint(equalTo: detailsScroll.contentView.widthAnchor),
            summary.centerXAnchor.constraint(equalTo: content.centerXAnchor), summary.centerYAnchor.constraint(equalTo: content.centerYAnchor), summary.widthAnchor.constraint(lessThanOrEqualTo: content.widthAnchor, constant: -32),
            progress.centerXAnchor.constraint(equalTo: content.centerXAnchor), progress.centerYAnchor.constraint(equalTo: content.centerYAnchor)
        ])
        observer = NotificationCenter.default.addObserver(forName: .settingsDidChange, object: nil, queue: .main) { [weak self] _ in
            MainActor.assumeIsolated { self?.renderSelection() }
        }
        renderSelection()
    }
    deinit { metadataTask?.cancel(); if let observer { NotificationCenter.default.removeObserver(observer) } }
    func show(_ urls: [URL]) {
        latestSelection = urls
        guard !isPinned else { return }
        guard self.urls != urls else { return }
        self.urls = urls
        if isViewLoaded { renderSelection() }
    }
    @objc private func togglePin() {
        isPinned.toggle(); pin.state = isPinned ? .on : .off
        pin.image = NSImage(systemSymbolName: isPinned ? "pin.fill" : "pin", accessibilityDescription: isPinned ? tr("Unpin Preview", "미리보기 고정 해제") : tr("Pin Preview", "미리보기 고정"))
        pin.contentTintColor = isPinned ? .controlAccentColor : nil
        pin.toolTip = pin.image?.accessibilityDescription
        if !isPinned { urls = latestSelection; renderSelection() }
    }
    @objc private func openWindow() { for url in urls { PreviewWindows.open(url) } }
    @objc private func openDefault() {
        let selection = urls
        Task {
            let applications = await Task.detached { selection.map { ($0, NSWorkspace.shared.urlForApplication(toOpen: $0)) } }.value
            for (url, application) in applications {
                guard let application else {
                    let alert = NSAlert(); alert.messageText = tr("No application can open this item.", "이 항목을 열 수 있는 응용 프로그램이 없습니다."); alert.informativeText = url.lastPathComponent; alert.runModal(); continue
                }
                NSWorkspace.shared.open([url], withApplicationAt: application, configuration: .init()) { _, error in
                    if let error { Task { @MainActor in NSAlert(error: error).runModal() } }
                }
            }
        }
    }
    private func renderSelection() {
        generation += 1; let currentGeneration = generation
        metadataTask?.cancel(); watcher = nil
        quickLook?.close(); quickLook?.removeFromSuperview(); quickLook = nil
        posterView?.removeFromSuperview(); posterView = nil
        for child in summary.arrangedSubviews { summary.removeArrangedSubview(child); child.removeFromSuperview() }
        details.setRows([]); progress.stopAnimation(nil)
        titleLabel.stringValue = urls.count == 1 ? urls[0].lastPathComponent : (urls.isEmpty ? tr("Preview", "미리보기") : Format.items(urls.count))
        detach.isEnabled = !urls.isEmpty; openButton.isEnabled = !urls.isEmpty; pin.isEnabled = !urls.isEmpty || isPinned
        detailsScroll.isHidden = !SettingsStore.shared.settings.showPreviewDetails || urls.isEmpty
        NSLayoutConstraint.deactivate([detailsHeight, collapsedDetailsHeight])
        (detailsScroll.isHidden ? collapsedDetailsHeight : detailsHeight)?.isActive = true
        summary.isHidden = false
        guard !urls.isEmpty else {
            let icon = NSImageView(image: NSImage(systemSymbolName: "doc.viewfinder", accessibilityDescription: nil) ?? NSImage()); icon.contentTintColor = .tertiaryLabelColor
            icon.imageScaling = .scaleProportionallyUpOrDown
            icon.widthAnchor.constraint(equalToConstant: 56).isActive = true; icon.heightAnchor.constraint(equalToConstant: 56).isActive = true
            summary.addArrangedSubview(icon)
            let label = NSTextField(labelWithString: tr("No selection", "선택 항목 없음")); label.font = Typography.title(14); label.textColor = .secondaryLabelColor; summary.addArrangedSubview(label)
            return
        }
        progress.startAnimation(nil)
        let selection = urls
        let task = Task.detached(priority: .userInitiated) { () -> [PreviewMetadata] in
            var result: [PreviewMetadata] = []
            for url in selection {
                if Task.isCancelled { break }
                result.append(await PreviewMetadata.read(url, richDetails: selection.count == 1, includePoster: selection.count == 1))
            }
            return result
        }
        metadataTask = task
        Task { [weak self] in
            let metadata = await task.value
            guard let self, currentGeneration == self.generation, !task.isCancelled else { return }
            self.progress.stopAnimation(nil)
            guard !metadata.isEmpty else { return }
            if metadata.count == 1 {
                let item = metadata[0]; self.titleLabel.stringValue = item.name
                self.details.setRows(item.detailRows)
                self.watcher = PreviewFileWatcher(item.url) { [weak self] in self?.renderSelection() }
                if item.isFolder || item.error != nil { self.showSummary(metadata) }
                else { self.showQuickLook(item) }
            } else {
                self.showSummary(metadata)
                let total = metadata.reduce(Int64(0)) { $0 + ($1.isFolder ? 0 : $1.size) }
                self.details.setRows([(tr("Selection", "선택 항목"), Format.items(metadata.count)), (tr("File size", "파일 크기"), Format.bytes(total))])
            }
            if !Motion.reduceMotion { self.content.wantsLayer = true; if let layer = self.content.layer { layer.opacity = 0; Motion.fade(layer, to: 1) } }
        }
    }
    private func showQuickLook(_ item: PreviewMetadata) {
        summary.isHidden = true
        // The local poster paints immediately while Quick Look's out-of-process surface loads.
        // Quick Look remains above it and owns zoom, media controls and PDF paging.
        if let image = item.poster {
            let poster = PreviewPosterView(image: image); poster.imageScaling = .scaleProportionallyUpOrDown
            poster.translatesAutoresizingMaskIntoConstraints = false; content.addSubview(poster)
            NSLayoutConstraint.activate([poster.leadingAnchor.constraint(equalTo: content.leadingAnchor, constant: 12), poster.trailingAnchor.constraint(equalTo: content.trailingAnchor, constant: -12), poster.topAnchor.constraint(equalTo: content.topAnchor, constant: 12), poster.bottomAnchor.constraint(equalTo: content.bottomAnchor, constant: -12)])
            posterView = poster
        }
        guard let preview = QLPreviewView(frame: content.bounds, style: .normal) else {
            if item.poster == nil {
                summary.isHidden = false; showSummary([item])
                let label = NSTextField(wrappingLabelWithString: tr("Preview unavailable", "미리보기를 사용할 수 없음"))
                label.textColor = .secondaryLabelColor; summary.addArrangedSubview(label)
            }
            return
        }
        preview.autostarts = false; preview.shouldCloseWithWindow = true
        preview.translatesAutoresizingMaskIntoConstraints = false; content.addSubview(preview)
        NSLayoutConstraint.activate([preview.leadingAnchor.constraint(equalTo: content.leadingAnchor), preview.trailingAnchor.constraint(equalTo: content.trailingAnchor), preview.topAnchor.constraint(equalTo: content.topAnchor), preview.bottomAnchor.constraint(equalTo: content.bottomAnchor)])
        preview.previewItem = item.url as NSURL; quickLook = preview
    }
    private func showSummary(_ metadata: [PreviewMetadata]) {
        let fan = NSView(); fan.translatesAutoresizingMaskIntoConstraints = false
        fan.widthAnchor.constraint(equalToConstant: 150).isActive = true; fan.heightAnchor.constraint(equalToConstant: 120).isActive = true
        for (index, item) in metadata.prefix(3).enumerated().reversed() {
            let icon = NSImageView(image: item.icon); icon.imageScaling = .scaleProportionallyUpOrDown
            icon.frame = NSRect(x: 17 + index * 12, y: 8 + index * 7, width: 100, height: 100); fan.addSubview(icon)
        }
        summary.addArrangedSubview(fan)
        let name = metadata.count == 1 ? metadata[0].name : Format.items(metadata.count)
        let title = NSTextField(wrappingLabelWithString: name); title.font = Typography.title(16); title.alignment = .center; summary.addArrangedSubview(title)
        let subtitle: String
        if metadata.count == 1, let count = metadata[0].count { subtitle = Format.items(count) }
        else { subtitle = Format.bytes(metadata.reduce(Int64(0)) { $0 + ($1.isFolder ? 0 : $1.size) }) }
        let secondary = NSTextField(labelWithString: subtitle); secondary.textColor = .secondaryLabelColor; secondary.font = .systemFont(ofSize: 12); summary.addArrangedSubview(secondary)
    }
}
