import AppKit

@MainActor
final class FileListBackgroundView: NSView {
    override var isOpaque: Bool { true }
    // NSView doesn't clip to bounds since the macOS 14 SDK; filling dirtyRect would paint
    // over neighbouring views (the pane's tab bar and address row).
    override func draw(_ dirtyRect: NSRect) { NSColor.controlBackgroundColor.setFill(); bounds.intersection(dirtyRect).fill() }
    override func viewDidChangeEffectiveAppearance() { super.viewDidChangeEffectiveAppearance(); needsDisplay = true }
}

@MainActor
final class FileOutlineView: NSOutlineView {
    weak var owner: FileListViewController?
    override func mouseDown(with event: NSEvent) { owner?.activate(); super.mouseDown(with: event) }
    /// Like Finder, a click into a background window selects (or starts dragging) right away.
    override func acceptsFirstMouse(for event: NSEvent?) -> Bool { true }
    override func draggingExited(_ sender: NSDraggingInfo?) { owner?.cancelSpringLoad(); super.draggingExited(sender) }
    override func becomeFirstResponder() -> Bool {
        let result = super.becomeFirstResponder()
        owner?.activate()
        return result
    }
    override func resignFirstResponder() -> Bool {
        let result = super.resignFirstResponder()
        Task { @MainActor [weak self] in self?.owner?.refreshSelectionAppearance() }
        return result
    }
    override func keyDown(with event: NSEvent) { owner?.activate(); if owner?.handleKey(event) != true { super.keyDown(with: event) } }
    override func otherMouseDown(with event: NSEvent) {
        guard event.buttonNumber == 2 else { super.otherMouseDown(with: event); return }
        let row = row(at: convert(event.locationInWindow, from: nil))
        if let node = item(atRow: row) as? FileNode { owner?.openItems([node.item.url], disposition: .newTab(activate: false)) }
    }
    override func menu(for event: NSEvent) -> NSMenu? {
        owner?.activate()
        let row = row(at: convert(event.locationInWindow, from: nil))
        if row >= 0, !selectedRowIndexes.contains(row) { selectRowIndexes(IndexSet(integer: row), byExtendingSelection: false) }
        if row < 0 { deselectAll(nil) }
        return owner?.contextMenu(for: (item(atRow: row) as? FileNode)?.item)
    }
}

@MainActor
final class FileCollectionView: NSCollectionView {
    override func draggingExited(_ sender: NSDraggingInfo?) { owner?.cancelSpringLoad(); super.draggingExited(sender) }
    weak var owner: FileListViewController?
    override func acceptsFirstMouse(for event: NSEvent?) -> Bool { true }
    override func mouseDown(with event: NSEvent) {
        owner?.activate()
        super.mouseDown(with: event)
        if event.clickCount == 2 { owner?.openItems(owner?.selectedURLs ?? [], disposition: event.modifierFlags.contains(.command) ? .newTab(activate: true) : .current) }
    }
    override func becomeFirstResponder() -> Bool {
        let result = super.becomeFirstResponder()
        owner?.activate()
        visibleItems().forEach { ($0 as? FileIconItem)?.updateSelection() }
        return result
    }
    override func resignFirstResponder() -> Bool {
        let result = super.resignFirstResponder()
        Task { @MainActor [weak self] in self?.visibleItems().forEach { ($0 as? FileIconItem)?.updateSelection() } }
        return result
    }
    override func keyDown(with event: NSEvent) { owner?.activate(); if owner?.handleKey(event) != true { super.keyDown(with: event) } }
    override func otherMouseDown(with event: NSEvent) {
        guard event.buttonNumber == 2 else { super.otherMouseDown(with: event); return }
        if let path = indexPathForItem(at: convert(event.locationInWindow, from: nil)), let owner, owner.roots.indices.contains(path.item) { owner.openItems([owner.roots[path.item].item.url], disposition: .newTab(activate: false)) }
    }
    override func menu(for event: NSEvent) -> NSMenu? {
        owner?.activate()
        let path = indexPathForItem(at: convert(event.locationInWindow, from: nil))
        if let path, !selectionIndexPaths.contains(path) { selectionIndexPaths = [path]; owner?.selectionDidChange() }
        if path == nil { selectionIndexPaths = []; owner?.selectionDidChange() }
        return owner?.contextMenu(for: path.flatMap { owner?.roots[$0.item].item })
    }
}

@MainActor
final class FileNameCell: NSTableCellView {
    let icon = NSImageView()
    let name = NSTextField(labelWithString: "")
    let tags = NSStackView()
    var representedURL: URL?
    override init(frame frameRect: NSRect) {
        super.init(frame: frameRect)
        icon.imageScaling = .scaleProportionallyUpOrDown
        name.lineBreakMode = .byTruncatingTail
        name.isSelectable = false
        tags.orientation = .horizontal
        tags.spacing = 3
        let stack = NSStackView(views: [icon, name, tags])
        stack.spacing = Metrics.s
        stack.alignment = .centerY
        stack.translatesAutoresizingMaskIntoConstraints = false
        addSubview(stack)
        NSLayoutConstraint.activate([stack.leadingAnchor.constraint(equalTo: leadingAnchor, constant: 2), stack.trailingAnchor.constraint(equalTo: trailingAnchor, constant: -Metrics.s), stack.topAnchor.constraint(equalTo: topAnchor), stack.bottomAnchor.constraint(equalTo: bottomAnchor), icon.widthAnchor.constraint(equalToConstant: 20), icon.heightAnchor.constraint(equalToConstant: 20)])
        name.setContentCompressionResistancePriority(.defaultLow, for: .horizontal)
        imageView = icon
        textField = name
    }
    required init?(coder: NSCoder) { fatalError("init(frame:) required") }
    func configure(_ item: FileItem, owner: FileListViewController) {
        representedURL = item.url
        name.stringValue = item.displayName(showExtension: SettingsStore.shared.settings.showAllExtensions)
        name.font = Typography.list(SettingsStore.shared.settings.textSize)
        name.isEditable = false
        name.isSelectable = false
        name.delegate = owner
        setAccessibilityLabel(name.stringValue)
        tags.arrangedSubviews.forEach { tags.removeArrangedSubview($0); $0.removeFromSuperview() }
        for tag in item.tags.prefix(7) {
            let dot = NSImageView(image: NSImage(systemSymbolName: "circle.fill", accessibilityDescription: tag.components(separatedBy: "\n").first)!)
            dot.contentTintColor = FileTagColors.color(for: tag)
            dot.translatesAutoresizingMaskIntoConstraints = false
            NSLayoutConstraint.activate([dot.widthAnchor.constraint(equalToConstant: 7), dot.heightAnchor.constraint(equalToConstant: 7)])
            tags.addArrangedSubview(dot)
        }
        icon.image = NSImage(systemSymbolName: item.isDirectory ? "folder" : "doc", accessibilityDescription: nil)
        let image = FileListIconCache.shared.image(for: item, size: 20, thumbnail: false) { [weak self] image in
            guard self?.representedURL == item.url else { return }
            self?.icon.image = image
        }
        if let image { icon.image = image }
    }
}

@MainActor
final class FileIconItem: NSCollectionViewItem {
    static let identifier = NSUserInterfaceItemIdentifier("FileIconItem")
    let icon = NSImageView()
    let name = NSTextField(wrappingLabelWithString: "")
    weak var owner: FileListViewController?
    var representedURL: URL?
    private var iconWidth: NSLayoutConstraint?
    private var iconHeight: NSLayoutConstraint?
    override var isSelected: Bool { didSet { updateSelection() } }
    override func loadView() {
        view = NSView()
        view.wantsLayer = true
        view.layer?.cornerRadius = Metrics.smallRadius
        icon.imageScaling = .scaleProportionallyUpOrDown
        icon.imageAlignment = .alignCenter
        icon.imageFrameStyle = .none
        name.alignment = .center
        name.maximumNumberOfLines = 2
        name.lineBreakMode = .byWordWrapping
        icon.translatesAutoresizingMaskIntoConstraints = false
        name.translatesAutoresizingMaskIntoConstraints = false
        view.addSubview(icon)
        view.addSubview(name)
        let width = icon.widthAnchor.constraint(equalToConstant: 72)
        let height = icon.heightAnchor.constraint(equalToConstant: 72)
        iconWidth = width
        iconHeight = height
        NSLayoutConstraint.activate([
            icon.topAnchor.constraint(equalTo: view.topAnchor, constant: Metrics.xs),
            icon.centerXAnchor.constraint(equalTo: view.centerXAnchor),
            width, height,
            name.topAnchor.constraint(equalTo: icon.bottomAnchor, constant: Metrics.xs),
            name.leadingAnchor.constraint(equalTo: view.leadingAnchor, constant: Metrics.xs),
            name.trailingAnchor.constraint(equalTo: view.trailingAnchor, constant: -Metrics.xs),
            name.bottomAnchor.constraint(lessThanOrEqualTo: view.bottomAnchor, constant: -Metrics.xs),
            name.heightAnchor.constraint(equalToConstant: 36)
        ])
        imageView = icon
        textField = name
    }
    func configure(_ item: FileItem, owner: FileListViewController) {
        self.owner = owner
        representedURL = item.url
        iconWidth?.constant = CGFloat(owner.viewSettings.iconSize)
        iconHeight?.constant = CGFloat(owner.viewSettings.iconSize)
        name.stringValue = item.displayName(showExtension: SettingsStore.shared.settings.showAllExtensions)
        name.font = Typography.list(SettingsStore.shared.settings.textSize)
        name.isEditable = false
        name.isSelectable = false
        name.delegate = owner
        icon.image = NSImage(systemSymbolName: item.isDirectory ? "folder" : "doc", accessibilityDescription: nil)
        let scale = view.window?.backingScaleFactor ?? owner.view.window?.backingScaleFactor ?? NSScreen.main?.backingScaleFactor ?? 2
        let cached = FileListIconCache.shared.image(for: item, size: CGFloat(owner.viewSettings.iconSize), thumbnail: true, scale: scale) { [weak self] image in
            guard self?.representedURL == item.url else { return }
            self?.icon.image = image
        }
        if let cached { icon.image = cached }
        view.setAccessibilityElement(true)
        view.setAccessibilityRole(.button)
        view.setAccessibilityLabel(name.stringValue)
        updateSelection()
    }
    func updateSelection() {
        guard isViewLoaded else { return }
        let emphasized = owner?.isActive == true && view.window?.firstResponder === owner?.collection
        view.effectiveAppearance.performAsCurrentDrawingAppearance {
            let isTarget = owner?.dragTarget == representedURL && representedURL != nil
            view.layer?.backgroundColor = (isTarget ? Palette.dropHighlight : (isSelected ? (emphasized ? NSColor.selectedContentBackgroundColor : .unemphasizedSelectedContentBackgroundColor) : .clear)).cgColor
        }
        name.textColor = isSelected && emphasized ? .alternateSelectedControlTextColor : .labelColor
        view.setAccessibilitySelected(isSelected)
    }
}

@MainActor
enum FileTagColors {
    static func color(for tag: String) -> NSColor {
        switch Int(tag.components(separatedBy: "\n").last ?? "") ?? 0 {
        case 1: return .systemGray
        case 2: return .systemGreen
        case 3: return .systemPurple
        case 4: return .systemBlue
        case 5: return .systemYellow
        case 6: return .systemRed
        case 7: return .systemOrange
        default: return .secondaryLabelColor
        }
    }
}

extension FileListViewController: NSOutlineViewDataSource, NSOutlineViewDelegate, NSCollectionViewDataSource, NSCollectionViewDelegate, NSTextFieldDelegate {
@MainActor
final class FileSelectionRow: NSTableRowView {
    weak var owner: FileListViewController?
    var targetURL: URL?
    override func drawBackground(in dirtyRect: NSRect) {
        super.drawBackground(in: dirtyRect)
        if let targetURL, owner?.dragTarget == targetURL {
            Palette.dropHighlight.setFill()
            bounds.intersection(dirtyRect).fill()
        }
    }
    override var isEmphasized: Bool {
        get { owner?.isActive == true && window?.firstResponder === owner?.outline }
        set { }
    }
}

    func outlineView(_ outlineView: NSOutlineView, rowViewForItem item: Any) -> NSTableRowView? {
        let row = FileSelectionRow()
        row.owner = self
        row.targetURL = (item as? FileNode)?.item.url
        return row
    }
    func outlineView(_ outlineView: NSOutlineView, numberOfChildrenOfItem item: Any?) -> Int { (item as? FileNode)?.children?.count ?? (item == nil ? roots.count : 0) }
    func outlineView(_ outlineView: NSOutlineView, child index: Int, ofItem item: Any?) -> Any { (item as? FileNode)?.children?[index] ?? roots[index] }
    func outlineView(_ outlineView: NSOutlineView, isItemExpandable item: Any) -> Bool {
        guard let node = item as? FileNode else { return false }
        return node.item.isDirectory && !node.isExpansionBlocked
    }
    func outlineView(_ outlineView: NSOutlineView, shouldExpandItem item: Any) -> Bool {
        guard let node = item as? FileNode, !node.isExpansionBlocked else { return false }
        loadChildren(node)
        return true
    }
    func outlineViewItemDidExpand(_ notification: Notification) { if let node = notification.userInfo?["NSObject"] as? FileNode { rememberExpansion(node, expanded: true) } }
    func outlineViewItemDidCollapse(_ notification: Notification) { if let node = notification.userInfo?["NSObject"] as? FileNode { rememberExpansion(node, expanded: false) } }
    func outlineViewSelectionDidChange(_ notification: Notification) { selectionDidChange() }
    func outlineView(_ outlineView: NSOutlineView, typeSelectStringFor tableColumn: NSTableColumn?, item: Any) -> String? { (item as? FileNode)?.item.name }
    func outlineView(_ outlineView: NSOutlineView, viewFor tableColumn: NSTableColumn?, item: Any) -> NSView? {
        guard let node = item as? FileNode, let column = tableColumn, let key = SortKey(rawValue: column.identifier.rawValue) else { return nil }
        if key == .name {
            let id = NSUserInterfaceItemIdentifier("FileName")
            let cell = (outlineView.makeView(withIdentifier: id, owner: self) as? FileNameCell) ?? FileNameCell()
            cell.identifier = id
            cell.configure(node.item, owner: self)
            styleCell(cell, for: node)
            return cell
        }
        let id = NSUserInterfaceItemIdentifier("FileSecondary")
        let cell = (outlineView.makeView(withIdentifier: id, owner: self) as? NSTableCellView) ?? NSTableCellView()
        cell.identifier = id
        let label: NSTextField
        if let existing = cell.textField { label = existing }
        else {
            label = NSTextField(labelWithString: "")
            label.translatesAutoresizingMaskIntoConstraints = false
            cell.addSubview(label)
            cell.textField = label
            NSLayoutConstraint.activate([label.leadingAnchor.constraint(equalTo: cell.leadingAnchor, constant: 4), label.trailingAnchor.constraint(equalTo: cell.trailingAnchor, constant: -8), label.centerYAnchor.constraint(equalTo: cell.centerYAnchor)])
        }
        label.font = Typography.listSecondary(SettingsStore.shared.settings.textSize)
        label.textColor = .secondaryLabelColor
        label.lineBreakMode = .byTruncatingTail
        label.alignment = key == .size ? .right : .left
        switch key {
        case .modified: label.stringValue = node.item.modified.map(Format.date) ?? "—"
        case .created: label.stringValue = node.item.created.map(Format.date) ?? "—"
        case .added: label.stringValue = node.item.added.map(Format.date) ?? "—"
        case .size: label.stringValue = node.item.size.map(Format.bytes) ?? "—"
        case .kind: label.stringValue = node.item.kind
        case .name: break
        }
        styleCell(cell, for: node)
        return cell
    }
    func outlineView(_ outlineView: NSOutlineView, sortDescriptorsDidChange oldDescriptors: [NSSortDescriptor]) {
        guard let descriptor = outlineView.sortDescriptors.first, let raw = descriptor.key, let key = SortKey(rawValue: raw) else { return }
        guard viewSettings.sortKey != key || viewSettings.ascending != descriptor.ascending else { return }
        var settings = viewSettings
        settings.sortKey = key
        settings.ascending = descriptor.ascending
        applyViewSettings(settings)
    }
    func collectionView(_ collectionView: NSCollectionView, numberOfItemsInSection section: Int) -> Int { roots.count }
    func collectionView(_ collectionView: NSCollectionView, itemForRepresentedObjectAt indexPath: IndexPath) -> NSCollectionViewItem {
        let item = collectionView.makeItem(withIdentifier: FileIconItem.identifier, for: indexPath) as! FileIconItem
        item.configure(roots[indexPath.item].item, owner: self)
        return item
    }
    func collectionView(_ collectionView: NSCollectionView, didSelectItemsAt indexPaths: Set<IndexPath>) { selectionDidChange() }
    func collectionView(_ collectionView: NSCollectionView, didDeselectItemsAt indexPaths: Set<IndexPath>) { selectionDidChange() }
    func beginInlineRename(expectedURL: URL? = nil) {
        guard renameURL == nil else { return }
        if loadState == .loading {
            renameAfterLoadURL = expectedURL ?? (selectedURLs.count == 1 ? selectedURLs.first : nil)
            return
        }
        guard let url = selectedURLs.first, selectedURLs.count == 1,
              expectedURL == nil || expectedURL?.standardizedFileURL == url.standardizedFileURL else { return }
        let field: NSTextField?
        if isIconView, let index = roots.firstIndex(where: { $0.item.url == url }) {
            field = (collection.item(at: IndexPath(item: index, section: 0)) as? FileIconItem)?.name
        } else {
            let column = outline.column(withIdentifier: NSUserInterfaceItemIdentifier(SortKey.name.rawValue))
            guard column >= 0 else { return }
            field = (outline.view(atColumn: column, row: outline.selectedRow, makeIfNecessary: true) as? FileNameCell)?.name
        }
        guard let field, inlineRenameField(field, represents: url) else { return }
        renameURL = url
        renameField = field
        field.stringValue = PathText.nfc(url.lastPathComponent)
        field.isEditable = true
        field.isSelectable = true
        // One way in: making the field first responder starts editing. Calling selectText first
        // would start a session that this call then ends (and ending disables the field).
        view.window?.makeFirstResponder(field)
        guard let editor = field.currentEditor() else {
            completeInlineRename(endingField: field, commit: false)
            return
        }
        let name = url.lastPathComponent as NSString
        let stem = (nodes[url]?.item.isDirectory == true ? name as String : name.deletingPathExtension) as NSString
        editor.selectedRange = NSRange(location: 0, length: stem.length)
    }

    /// Check both the editor identity and its live item binding, never just the selected row.
    private func inlineRenameField(_ field: NSTextField, represents url: URL) -> Bool {
        let row = outline.row(for: field)
        let column = outline.column(withIdentifier: NSUserInterfaceItemIdentifier(SortKey.name.rawValue))
        if row >= 0, column >= 0,
           let cell = outline.view(atColumn: column, row: row, makeIfNecessary: false) as? FileNameCell {
            return cell.name === field && cell.representedURL == url
                && (outline.item(atRow: row) as? FileNode)?.item.url == url
        }
        for case let item as FileIconItem in collection.visibleItems() where item.name === field {
            guard item.representedURL == url, let path = collection.indexPath(for: item),
                  roots.indices.contains(path.item) else { return false }
            return roots[path.item].item.url == url
        }
        return false
    }

    func control(_ control: NSControl, textView: NSTextView, doCommandBy commandSelector: Selector) -> Bool {
        guard control === renameField else { return false }
        if commandSelector == #selector(NSResponder.cancelOperation(_:)) {
            completeInlineRename(endingField: renameField, commit: false)
            focus()
            return true
        }
        // AppKit ends editing for Return, Tab and focus changes, then sends the end notification.
        return false
    }

    func controlTextDidEndEditing(_ obj: Notification) {
        completeInlineRename(endingField: obj.object as? NSTextField, commit: true)
    }

    private func completeInlineRename(endingField: NSTextField?, commit: Bool) {
        guard let url = renameURL else { return }
        let editorField = renameField
        let validBinding = editorField != nil && endingField === editorField
            && editorField.map { inlineRenameField($0, represents: url) } == true
        let currentName = PathText.nfc(nodes[url]?.item.name ?? url.lastPathComponent)
        let newName = validBinding ? editorField.map { PathText.nfc($0.stringValue) } : nil
        // Clear the session before aborting/disabling the field; AppKit may notify reentrantly.
        renameURL = nil
        renameField = nil
        if !commit || !validBinding { _ = editorField?.abortEditing() }
        if let editorField {
            editorField.isEditable = false
            editorField.isSelectable = false
            if inlineRenameField(editorField, represents: url) {
                editorField.stringValue = nodes[url]?.item.displayName(showExtension: SettingsStore.shared.settings.showAllExtensions) ?? currentName
            }
        }
        var renamed: URL?
        if commit, validBinding, let newName, newName != currentName {
            do { renamed = try AppServices.shared.fileOperations.rename(url, to: newName) }
            catch { presentError(error.localizedDescription) }
        }
        finishDeferredInlineRenameRefresh(selecting: renamed)
    }
}
