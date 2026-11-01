import AppKit
import CoreServices
import UniformTypeIdentifiers

/// Install `PreviewWindows.open` as `UIHooks.openPreviewWindow` after AppServices is ready.
/// Keeps at most eight independently pageable previews, reusing a window for an existing URL.
@MainActor enum PreviewWindows {
    private static var windows: [DetachedPreviewController] = []
    static func open(_ url: URL) {
        let url = url.standardizedFileURL
        if let existing = windows.first(where: { $0.url == url }) { existing.showWindow(nil); existing.window?.makeKeyAndOrderFront(nil); return }
        while windows.count >= 8 { windows.removeFirst().close() }
        let controller = DetachedPreviewController(url)
        controller.onClose = { [weak controller] in windows.removeAll { $0 === controller } }
        windows.append(controller); controller.showWindow(nil); controller.window?.makeKeyAndOrderFront(nil)
    }
}

@MainActor private final class DetachedPreviewController: NSWindowController, NSWindowDelegate {
    let url: URL
    var onClose: (() -> Void)?
    private var sizing: Task<PreviewMetadata, Never>?
    init(_ url: URL) {
        self.url = url
        let preview = PreviewPaneController(); preview.show([url])
        let window = NSWindow(contentRect: NSRect(x: 0, y: 0, width: 640, height: 620), styleMask: [.titled, .closable, .miniaturizable, .resizable], backing: .buffered, defer: false)
        window.contentViewController = preview; window.title = FileName.display(url.lastPathComponent); window.minSize = NSSize(width: 300, height: 300)
        window.isReleasedWhenClosed = false
        super.init(window: window); window.delegate = self
        let key = NSWindow.FrameAutosaveName("FolderSplitter.Preview." + url.path)
        let restored = window.setFrameUsingName(key); window.setFrameAutosaveName(key)
        if !restored {
            window.center()
            let task = Task.detached(priority: .userInitiated) { await PreviewMetadata.read(url) }; sizing = task
            Task { [weak self] in
                let metadata = await task.value
                guard let self, !task.isCancelled, let window = self.window else { return }
                let screen = window.screen?.visibleFrame ?? NSScreen.main?.visibleFrame ?? NSRect(x: 0, y: 0, width: 1200, height: 900)
                let maximum = NSSize(width: screen.width * 0.8, height: screen.height * 0.8)
                var target = metadata.dimensions ?? CGSize(width: 700, height: 660)
                let ratio = min(1, maximum.width / max(1, target.width), (maximum.height - 160) / max(1, target.height))
                target = CGSize(width: max(340, min(maximum.width, target.width * ratio)), height: max(380, min(maximum.height, target.height * ratio + 160)))
                window.setContentSize(target); window.center()
            }
        }
    }
    required init?(coder: NSCoder) { fatalError("init(coder:) has not been implemented") }
    func windowWillClose(_ notification: Notification) { sizing?.cancel(); onClose?() }
}

/// Install `InfoWindows.show(for:)` as `UIHooks.showInfo`. Closed windows cancel metadata
/// and recursive folder-size work; no more than ten per-item windows are retained.
@MainActor enum InfoWindows {
    private static var windows: [InfoWindowController] = []
    static func show(for urls: [URL]) {
        for url in urls.map(\.standardizedFileURL) {
            if let existing = windows.first(where: { $0.url == url }) { existing.showWindow(nil); existing.window?.makeKeyAndOrderFront(nil); continue }
            while windows.count >= 10 { windows.removeFirst().close() }
            let controller = InfoWindowController(url)
            controller.onClose = { [weak controller] in windows.removeAll { $0 === controller } }
            windows.append(controller); controller.showWindow(nil); controller.window?.makeKeyAndOrderFront(nil)
        }
    }
}

@MainActor final class InfoWindowController: NSWindowController, NSWindowDelegate, NSTextFieldDelegate {
    private(set) var url: URL
    var onClose: (() -> Void)?
    private let icon = NSImageView()
    private let name = NSTextField(string: "")
    private let grid = PreviewDetailGrid()
    private let size = NSTextField(wrappingLabelWithString: "")
    private let hiddenExtension = NSButton(checkboxWithTitle: tr("Hide extension", "확장자 가리기"), target: nil, action: nil)
    private let locked = NSButton(checkboxWithTitle: tr("Locked", "잠김"), target: nil, action: nil)
    private let applications = NSPopUpButton()
    private let changeAll = NSButton(title: tr("Change All…", "모두 변경…"), target: nil, action: nil)
    private let permissions = NSTextField(wrappingLabelWithString: "")
    private var metadata: PreviewMetadata?
    private var metadataTask: Task<PreviewMetadata, Never>?
    private var folderTask: Task<PreviewMetadata.FolderSize, Never>?
    private var folderSizePending = false
    private var watcher: PreviewFileWatcher?
    private var generation = 0
    private var closed = false

    init(_ url: URL) {
        self.url = url
        let window = NSWindow(contentRect: NSRect(x: 0, y: 0, width: 370, height: 650), styleMask: [.titled, .closable, .resizable, .miniaturizable], backing: .buffered, defer: false)
        window.isReleasedWhenClosed = false; window.minSize = NSSize(width: 340, height: 380)
        window.title = tr("\(FileName.display(url.lastPathComponent)) Info", "\(FileName.display(url.lastPathComponent)) 정보")
        super.init(window: window); window.delegate = self
        buildView(); window.center(); reload()
    }
    required init?(coder: NSCoder) { fatalError("init(coder:) has not been implemented") }
    private func buildView() {
        guard let window else { return }
        let scroll = NSScrollView(); scroll.hasVerticalScroller = true; scroll.autohidesScrollers = true; scroll.drawsBackground = false
        let stack = NSStackView(); stack.orientation = .vertical; stack.alignment = .leading; stack.spacing = 12; stack.edgeInsets = NSEdgeInsets(top: 20, left: 20, bottom: 20, right: 20)
        stack.translatesAutoresizingMaskIntoConstraints = false; scroll.documentView = stack; window.contentView = scroll
        NSLayoutConstraint.activate([stack.leadingAnchor.constraint(equalTo: scroll.contentView.leadingAnchor), stack.trailingAnchor.constraint(equalTo: scroll.contentView.trailingAnchor), stack.topAnchor.constraint(equalTo: scroll.contentView.topAnchor)])
        icon.imageScaling = .scaleProportionallyDown; icon.widthAnchor.constraint(equalToConstant: 64).isActive = true; icon.heightAnchor.constraint(equalToConstant: 64).isActive = true
        name.font = Typography.title(15); name.delegate = self; name.setAccessibilityLabel(tr("Name", "이름")); name.stringValue = FileName.display(url.lastPathComponent)
        let header = NSStackView(views: [icon, name]); header.spacing = 12; header.alignment = .centerY
        header.heightAnchor.constraint(equalToConstant: 64).isActive = true
        stack.addView(header, in: .top); header.widthAnchor.constraint(equalTo: stack.widthAnchor, constant: -40).isActive = true
        addHeading(tr("General", "일반"), to: stack)
        stack.addView(grid, in: .top); grid.widthAnchor.constraint(equalTo: stack.widthAnchor, constant: -40).isActive = true
        size.font = .systemFont(ofSize: 12); size.setAccessibilityLabel(tr("Size", "크기")); stack.addView(size, in: .top)
        for checkbox in [hiddenExtension, locked] { checkbox.target = self; checkbox.action = #selector(changeFlags(_:)); stack.addView(checkbox, in: .top) }
        addHeading(tr("Open With", "다음으로 열기"), to: stack)
        applications.target = self; applications.action = #selector(applicationChanged); applications.setAccessibilityLabel(tr("Open With", "다음으로 열기"))
        stack.addView(applications, in: .top); applications.widthAnchor.constraint(equalTo: stack.widthAnchor, constant: -40).isActive = true
        changeAll.target = self; changeAll.action = #selector(changeAllApplications); changeAll.bezelStyle = .rounded; stack.addView(changeAll, in: .top)
        addHeading(tr("Sharing & Permissions", "공유 및 사용 권한"), to: stack)
        permissions.font = .systemFont(ofSize: 12); permissions.isSelectable = true; stack.addView(permissions, in: .top)
        permissions.widthAnchor.constraint(equalTo: stack.widthAnchor, constant: -40).isActive = true
    }
    private func addHeading(_ title: String, to stack: NSStackView) {
        let separator = NSBox(); separator.boxType = .separator; stack.addView(separator, in: .top); separator.widthAnchor.constraint(equalTo: stack.widthAnchor, constant: -40).isActive = true
        let label = NSTextField(labelWithString: title); label.font = Typography.sectionHeader; stack.addView(label, in: .top)
    }
    private func reload() {
        guard !closed else { return }
        generation += 1; let currentGeneration = generation
        metadataTask?.cancel()
        let url = self.url
        if watcher == nil {
            watcher = PreviewFileWatcher(url) { [weak self] in
                guard let self, self.url == url else { return }
                self.reload()
            }
        }
        let task = Task.detached(priority: .userInitiated) { await PreviewMetadata.read(url, includeApplications: true) }; metadataTask = task
        Task { [weak self] in
            let value = await task.value
            guard let self, !self.closed, self.generation == currentGeneration, !task.isCancelled else { return }
            self.metadata = value; self.icon.image = value.icon
            if self.window?.firstResponder !== self.name.currentEditor() { self.name.stringValue = FileName.display(url.lastPathComponent) }
            self.window?.title = tr("\(value.name) Info", "\(value.name) 정보")
            self.grid.setRows(value.detailRows.filter { $0.0 != tr("Size", "크기") && $0.0 != tr("Contains", "포함 항목") })
            self.hiddenExtension.state = value.hiddenExtension ? .on : .off; self.hiddenExtension.isEnabled = !value.isFolder
            self.locked.state = value.locked ? .on : .off
            let access = value.readable ? (value.writable ? tr("Read & Write", "읽기 및 쓰기") : tr("Read Only", "읽기 전용")) : (value.writable ? tr("Write Only", "쓰기 전용") : tr("No Access", "접근 불가"))
            self.permissions.stringValue = tr("Owner: \(value.owner)\nGroup: \(value.group)\nYou can: \(access)", "소유자: \(value.owner)\n그룹: \(value.group)\n사용자 권한: \(access)")
            self.populateApplications(value)
            if value.isFolder {
                if self.folderTask != nil { self.folderSizePending = true }
                else { self.calculateFolderSize(url) }
            } else {
                self.folderTask?.cancel(); self.folderTask = nil; self.folderSizePending = false
                self.size.stringValue = tr("Size: \(Format.bytes(value.size))", "크기: \(Format.bytes(value.size))")
            }
        }
    }
    private func calculateFolderSize(_ url: URL) {
        if size.stringValue.isEmpty { size.stringValue = tr("Size: Calculating…", "크기: 계산 중…") }
        let task = Task.detached(priority: .utility) { PreviewMetadata.recursiveSize(url) }; folderTask = task
        Task { [weak self] in
            let value = await task.value
            guard let self, self.url == url, !self.closed, !task.isCancelled else { return }
            self.folderTask = nil
            let result = "\(Format.bytes(value.bytes)) · \(Format.items(value.count))"
            self.size.stringValue = tr("Size: \(result)", "크기: \(result)") + (value.error.map { "\n" + tr("Some items could not be read: \($0)", "일부 항목을 읽을 수 없습니다: \($0)") } ?? "")
            // Finish a size traversal before starting another; a busy folder cannot cancel
            // every traversal from zero and leave Get Info calculating indefinitely.
            if self.folderSizePending { self.folderSizePending = false; self.calculateFolderSize(url) }
        }
    }
    func controlTextDidEndEditing(_ notification: Notification) {
        guard name.stringValue != FileName.display(url.lastPathComponent) else { return }
        do {
            watcher = nil
            folderTask?.cancel(); folderTask = nil; folderSizePending = false; size.stringValue = ""
            url = try AppServices.shared.fileOperations.rename(url, to: FileName.disk(name.stringValue))
            reload()
        } catch { name.stringValue = FileName.display(url.lastPathComponent); NSAlert(error: error).runModal(); reload() }
    }
    @objc private func changeFlags(_ sender: NSButton) {
        let url = self.url; let state = sender.state == .on; let isHidden = sender === hiddenExtension
        Task {
            let error = await Task.detached { () -> String? in
                do {
                    var mutableURL = url; var values = URLResourceValues()
                    if isHidden { values.hasHiddenExtension = state } else { values.isUserImmutable = state }
                    try mutableURL.setResourceValues(values); return nil
                } catch { return error.localizedDescription }
            }.value
            if let error { let alert = NSAlert(); alert.messageText = tr("The item could not be changed.", "항목을 변경할 수 없습니다."); alert.informativeText = error; alert.runModal() }
            reload()
        }
    }
    private func populateApplications(_ value: PreviewMetadata) {
        applications.removeAllItems()
        var apps = value.applications
        if let app = value.defaultApplication, !apps.contains(app) { apps.insert(app, at: 0) }
        for app in apps {
            let isDefault = app == value.defaultApplication
            let title = app.deletingPathExtension().lastPathComponent + (isDefault ? tr(" (default)", " (기본)") : "")
            applications.addItem(withTitle: title); applications.lastItem?.representedObject = app
        }
        if let selection = value.defaultApplication, let index = apps.firstIndex(of: selection) { applications.selectItem(at: index) }
        applications.isEnabled = !value.isFolder && !apps.isEmpty
        changeAll.isEnabled = applications.isEnabled && value.typeIdentifier != nil
    }
    @objc private func applicationChanged() {
        guard let app = applications.selectedItem?.representedObject as? URL else { return }
        NSWorkspace.shared.setDefaultApplication(at: app, toOpenFileAt: url) { [weak self] error in
            Task { @MainActor in if let error { NSAlert(error: error).runModal() }; self?.reload() }
        }
    }
    @objc private func changeAllApplications() {
        guard let type = metadata?.typeIdentifier, let app = applications.selectedItem?.representedObject as? URL else { return }
        let alert = NSAlert(); alert.messageText = tr("Change the default application?", "기본 응용 프로그램을 변경하겠습니까?")
        alert.informativeText = tr("All documents of this type will open with \(app.deletingPathExtension().lastPathComponent).", "이 유형의 모든 문서가 \(app.deletingPathExtension().lastPathComponent)(으)로 열립니다.")
        alert.addButton(withTitle: tr("Continue", "계속")); alert.addButton(withTitle: tr("Cancel", "취소"))
        guard alert.runModal() == .alertFirstButtonReturn else { return }
        Task {
            let status = await Task.detached { () -> OSStatus in
                guard let identifier = Bundle(url: app)?.bundleIdentifier else { return OSStatus(kLSApplicationNotFoundErr) }
                return LSSetDefaultRoleHandlerForContentType(type as CFString, .all, identifier as CFString)
            }.value
            if status != noErr { NSAlert(error: NSError(domain: NSOSStatusErrorDomain, code: Int(status))).runModal() }
            reload()
        }
    }
    func windowWillClose(_ notification: Notification) {
        closed = true; metadataTask?.cancel(); folderTask?.cancel(); watcher = nil; onClose?()
    }
}
