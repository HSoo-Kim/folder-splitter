import AppKit

@MainActor
private enum ModelSheetUI {
    static func label(_ text: String, secondary: Bool = false) -> NSTextField {
        let view = NSTextField(labelWithString: text)
        view.font = Typography.list(.medium)
        view.textColor = secondary ? .secondaryLabelColor : .labelColor
        view.lineBreakMode = .byTruncatingMiddle
        return view
    }

    static func button(_ title: String, target: AnyObject, action: Selector) -> NSButton {
        let button = NSButton(title: title, target: target, action: action)
        button.bezelStyle = .rounded
        button.font = Typography.list(.medium)
        button.setAccessibilityLabel(title)
        return button
    }

    static func stack(_ views: [NSView], vertical: Bool = false) -> NSStackView {
        let stack = NSStackView(views: views)
        stack.orientation = vertical ? .vertical : .horizontal
        stack.alignment = vertical ? .leading : .centerY
        stack.spacing = Metrics.s
        return stack
    }

    static func window(title: String, size: NSSize) -> NSWindow {
        let window = NSWindow(contentRect: NSRect(origin: .zero, size: size),
                              styleMask: [.titled, .resizable], backing: .buffered, defer: false)
        window.title = title
        window.minSize = size
        window.isReleasedWhenClosed = false
        return window
    }

    static func install(_ content: NSView, in window: NSWindow) {
        guard let root = window.contentView else { return }
        content.translatesAutoresizingMaskIntoConstraints = false
        root.addSubview(content)
        NSLayoutConstraint.activate([
            content.leadingAnchor.constraint(equalTo: root.leadingAnchor, constant: Metrics.xl),
            content.trailingAnchor.constraint(equalTo: root.trailingAnchor, constant: -Metrics.xl),
            content.topAnchor.constraint(equalTo: root.topAnchor, constant: Metrics.xl),
            content.bottomAnchor.constraint(equalTo: root.bottomAnchor, constant: -Metrics.l)
        ])
    }

    static func table(_ columns: [(String, String, CGFloat)], label: String) -> (NSTableView, NSScrollView) {
        let table = NSTableView()
        table.rowHeight = 28
        table.usesAlternatingRowBackgroundColors = true
        table.columnAutoresizingStyle = .lastColumnOnlyAutoresizingStyle
        table.setAccessibilityLabel(label)
        for (identifier, title, width) in columns {
            let column = NSTableColumn(identifier: NSUserInterfaceItemIdentifier(identifier))
            column.title = title
            column.width = width
            column.minWidth = 80
            table.addTableColumn(column)
        }
        let scroll = NSScrollView()
        scroll.hasVerticalScroller = true
        scroll.hasHorizontalScroller = true
        scroll.borderType = .bezelBorder
        scroll.documentView = table
        return (table, scroll)
    }

    static func close(_ window: NSWindow?, response: NSApplication.ModalResponse = .cancel) {
        guard let window else { return }
        if let parent = window.sheetParent { parent.endSheet(window, returnCode: response) }
        else { window.orderOut(nil) }
    }

    static func error(_ error: Error, in window: NSWindow) {
        let alert = NSAlert(error: error)
        alert.beginSheetModal(for: window)
    }
}

/// Preview generation, including directory enumeration and file metadata, never runs on the UI actor.
private func modelRenamePreview(items: [URL], options: BulkRenameOptions) throws -> [BulkRenamePreview] {
    let manager = FileManager.default
    func key(_ name: String) -> String { PathText.nfc(PathText.nfc(name).lowercased()) }
    var existing: [URL: Set<String>] = [:]
    for item in items {
        try Task.checkCancellation()
        let parent = item.deletingLastPathComponent().standardizedFileURL
        if existing[parent] == nil {
            existing[parent] = Set(try manager.contentsOfDirectory(atPath: parent.path).map(key))
        }
    }
    if existing.count == 1 {
        return BulkRenamePlanner.preview(items, options: options, existingNames: existing.values.first ?? [])
    }
    // Preserve global numbering across parents, then account for each parent's occupied names.
    var previews = BulkRenamePlanner.preview(items, options: options, existingNames: [])
    var blocked = true
    while blocked {
        blocked = false
        var vacated: [URL: Set<String>] = [:]
        for preview in previews where preview.problem == nil && preview.changes {
            vacated[preview.item.deletingLastPathComponent().standardizedFileURL, default: []].insert(key(preview.oldName))
        }
        for index in previews.indices {
            let preview = previews[index]
            guard preview.problem == nil, preview.changes else { continue }
            let parent = preview.item.deletingLastPathComponent().standardizedFileURL
            let name = key(preview.newName)
            guard existing[parent]?.contains(name) == true, vacated[parent]?.contains(name) != true,
                  name != key(preview.oldName) else { continue }
            previews[index] = BulkRenamePreview(item: preview.item, oldName: preview.oldName,
                                                newName: preview.newName, problem: .exists)
            blocked = true
        }
    }
    return previews
}

@MainActor
final class BulkRenameSheetController: NSWindowController, NSTextFieldDelegate, NSTableViewDataSource, NSTableViewDelegate {
    private let items: [URL]
    let findField = NSTextField(string: "")
    let replaceField = NSTextField(string: "")
    let startField = NSTextField(string: "1")
    let regexCheckbox = NSButton(checkboxWithTitle: tr("Regular expression", "정규 표현식"), target: nil, action: nil)
    let matchCaseCheckbox = NSButton(checkboxWithTitle: tr("Match case", "대소문자 구분"), target: nil, action: nil)
    private(set) var previews: [BulkRenamePreview] = []
    private(set) var previewTable: NSTableView!
    private(set) var renameButton: NSButton!
    private let summary = ModelSheetUI.label("", secondary: true)
    private let spinner = NSProgressIndicator()
    private var previewTask: Task<Void, Never>?
    private var previewWork: Task<[BulkRenamePreview], Error>?
    private var generation = 0

    static func present(for items: [URL], in window: NSWindow) {
        let controller = BulkRenameSheetController(items: items)
        guard let sheet = controller.window else { return }
        window.beginSheet(sheet) { [controller] _ in controller.cancelPreview() }
    }

    init(items: [URL], startLoading: Bool = true) {
        self.items = items
        super.init(window: ModelSheetUI.window(title: tr("Rename Items", "항목 이름 변경"), size: NSSize(width: 760, height: 510)))
        buildContent()
        if startLoading { refreshPreview() }
    }

    required init?(coder: NSCoder) { fatalError("init(coder:) is unavailable") }

    private func buildContent() {
        guard let window else { return }
        let title = ModelSheetUI.label(tr("Rename \(items.count) Items", "항목 \(items.count)개 이름 변경"))
        title.font = Typography.title()
        for (field, title) in [(findField, tr("Find", "찾기")), (replaceField, tr("Replace With", "대치할 내용")),
                               (startField, tr("Start Number", "시작 번호"))] {
            field.font = Typography.list(.medium)
            field.delegate = self
            field.setAccessibilityLabel(title)
        }
        findField.placeholderString = tr("Text to find", "찾을 텍스트")
        replaceField.placeholderString = tr("New text or name", "새 텍스트 또는 이름")
        startField.widthAnchor.constraint(equalToConstant: 80).isActive = true
        for checkbox in [regexCheckbox, matchCaseCheckbox] {
            checkbox.font = Typography.list(.medium)
            checkbox.target = self
            checkbox.action = #selector(optionsChanged(_:))
            checkbox.setAccessibilityLabel(checkbox.title)
        }
        let fields = NSGridView(views: [
            [ModelSheetUI.label(tr("Find:", "찾기:")), findField],
            [ModelSheetUI.label(tr("Replace with:", "대치할 내용:")), replaceField]
        ])
        fields.rowSpacing = Metrics.s
        fields.columnSpacing = Metrics.m
        fields.column(at: 1).xPlacement = .fill
        let options = ModelSheetUI.stack([regexCheckbox, matchCaseCheckbox, NSView(),
                                          ModelSheetUI.label(tr("Start number:", "시작 번호:")), startField])
        let hint = ModelSheetUI.label(tr("Use {n} for numbering or {n:3} for padded numbers. File extensions are kept.",
                                        "{n}은 일련번호, {n:3}은 세 자리 번호입니다. 파일 확장자는 유지됩니다."), secondary: true)
        hint.font = Typography.list(.small)
        let (table, scroll) = ModelSheetUI.table([
            ("old", tr("Current Name", "현재 이름"), 225),
            ("new", tr("New Name", "새 이름"), 225),
            ("problem", tr("Problem", "문제"), 250)
        ], label: tr("Rename preview", "이름 변경 미리보기"))
        previewTable = table
        table.dataSource = self
        table.delegate = self
        spinner.style = .spinning
        spinner.controlSize = .small
        spinner.isDisplayedWhenStopped = false
        renameButton = ModelSheetUI.button(tr("Rename", "이름 변경"), target: self, action: #selector(applyRename(_:)))
        renameButton.keyEquivalent = "\r"
        renameButton.isEnabled = false
        let cancel = ModelSheetUI.button(tr("Cancel", "취소"), target: self, action: #selector(cancel(_:)))
        cancel.keyEquivalent = "\u{1b}"
        let footer = ModelSheetUI.stack([spinner, summary, NSView(), cancel, renameButton])
        let content = ModelSheetUI.stack([title, fields, options, hint, scroll, footer], vertical: true)
        content.spacing = Metrics.m
        ModelSheetUI.install(content, in: window)
        for view in [fields, options, scroll, footer] {
            view.widthAnchor.constraint(equalTo: content.widthAnchor).isActive = true
        }
        scroll.heightAnchor.constraint(greaterThanOrEqualToConstant: 220).isActive = true
        summary.setAccessibilityLabel(tr("Rename status", "이름 변경 상태"))
        window.initialFirstResponder = findField
    }

    func controlTextDidChange(_ obj: Notification) { refreshPreview() }
    @objc private func optionsChanged(_ sender: Any?) { refreshPreview() }

    func refreshPreview() {
        cancelPreview()
        generation += 1
        let currentGeneration = generation
        renameButton.isEnabled = false
        guard let start = Int(startField.stringValue), start >= 0 else {
            previews = []
            previewTable.reloadData()
            summary.stringValue = tr("Type a valid nonnegative start number.", "0 이상의 유효한 시작 번호를 입력하십시오.")
            summary.textColor = .systemRed
            return
        }
        var options = BulkRenameOptions()
        options.find = FileName.disk(findField.stringValue)
        options.replace = FileName.disk(replaceField.stringValue)
        options.useRegex = regexCheckbox.state == .on
        options.caseSensitive = matchCaseCheckbox.state == .on
        options.startNumber = start
        summary.stringValue = tr("Updating preview…", "미리보기 업데이트 중…")
        summary.textColor = .secondaryLabelColor
        spinner.startAnimation(nil)
        let items = self.items
        let work = Task.detached(priority: .userInitiated) { try modelRenamePreview(items: items, options: options) }
        previewWork = work
        previewTask = Task { [weak self] in
            do {
                let previews = try await work.value
                guard let self, !Task.isCancelled, self.generation == currentGeneration else { return }
                self.setPreviews(previews)
            } catch {
                guard let self, !Task.isCancelled, self.generation == currentGeneration else { return }
                self.spinner.stopAnimation(nil)
                self.previews = []
                self.previewTable.reloadData()
                self.summary.stringValue = tr("Cannot preview names: \(error.localizedDescription)", "이름을 미리 볼 수 없습니다: \(error.localizedDescription)")
                self.summary.textColor = .systemRed
            }
        }
    }

    func setPreviews(_ previews: [BulkRenamePreview]) {
        self.previews = previews
        spinner.stopAnimation(nil)
        previewTable.reloadData()
        let problems = previews.filter { $0.problem != nil }
        let changes = previews.filter { $0.changes }.count
        if let problem = problems.first?.problem {
            summary.stringValue = tr("\(problems.count) problems: \(Self.reason(problem))", "문제 \(problems.count)개: \(Self.reason(problem))")
            summary.textColor = .systemRed
        } else {
            summary.stringValue = tr("\(changes) of \(previews.count) names change", "\(previews.count)개 중 \(changes)개의 이름 변경")
            summary.textColor = .secondaryLabelColor
        }
        renameButton.isEnabled = problems.isEmpty && changes > 0
    }

    private static func reason(_ problem: BulkRenamePreview.Problem) -> String {
        switch problem {
        case .empty: return tr("A name would be empty.", "이름이 비어 있습니다.")
        case .invalidCharacter: return tr("A name contains a character that is not allowed.", "이름에 사용할 수 없는 문자가 있습니다.")
        case .duplicate: return tr("Two items would get the same name.", "두 항목의 이름이 같아집니다.")
        case .exists: return tr("An item with that name already exists.", "같은 이름의 항목이 이미 있습니다.")
        case .invalidPattern: return tr("The regular expression is not valid.", "정규 표현식이 올바르지 않습니다.")
        }
    }

    private func cancelPreview() {
        previewTask?.cancel()
        previewWork?.cancel()
        spinner.stopAnimation(nil)
    }

    @objc private func cancel(_ sender: Any?) {
        cancelPreview()
        ModelSheetUI.close(window)
    }

    @objc private func applyRename(_ sender: Any?) {
        guard renameButton.isEnabled, let window else { return }
        renameButton.isEnabled = false
        let changes = previews.filter { $0.changes && $0.problem == nil }.map { FileRename(item: $0.item, newName: $0.newName) }
        do {
            _ = try AppServices.shared.fileOperations.rename(changes)
            cancelPreview()
            ModelSheetUI.close(window, response: .OK)
        } catch {
            ModelSheetUI.error(error, in: window)
            refreshPreview()
        }
    }

    func numberOfRows(in tableView: NSTableView) -> Int { previews.count }

    func tableView(_ tableView: NSTableView, viewFor tableColumn: NSTableColumn?, row: Int) -> NSView? {
        let preview = previews[row]
        let value: String
        switch tableColumn?.identifier.rawValue {
        case "old": value = FileName.display(preview.oldName)
        case "new": value = FileName.display(preview.newName)
        default: value = preview.problem.map(Self.reason) ?? ""
        }
        let label = ModelSheetUI.label(value)
        label.toolTip = value
        if preview.problem != nil, tableColumn?.identifier.rawValue != "old" { label.textColor = .systemRed }
        else if !preview.changes { label.textColor = .secondaryLabelColor }
        return label
    }
}

private struct ModelComparisonData: @unchecked Sendable {
    let result: FolderComparisonResult
    let icons: [URL: NSImage]
}

private func modelCompareFolders(left: URL, right: URL) throws -> ModelComparisonData {
    let result = try FolderComparison.compare(left: left, right: right)
    let urls = Set(result.onlyLeft + result.onlyRight + result.leftNewer + result.rightNewer + result.leftSizeDiffers + result.rightSizeDiffers)
    var icons: [URL: NSImage] = [:]
    for url in urls {
        try Task.checkCancellation()
        icons[url] = NSWorkspace.shared.icon(forFile: url.path)
    }
    return ModelComparisonData(result: result, icons: icons)
}

@MainActor
final class CompareSheetController: NSWindowController, NSTableViewDataSource, NSTableViewDelegate {
    private enum Category: Int, CaseIterable {
        case onlyLeft, onlyRight, leftNewer, rightNewer, leftSizeDiffers, rightSizeDiffers
        var title: String {
            switch self {
            case .onlyLeft: return tr("Only in left", "왼쪽에만 있음")
            case .onlyRight: return tr("Only in right", "오른쪽에만 있음")
            case .leftNewer: return tr("Left is newer", "왼쪽이 더 최신")
            case .rightNewer: return tr("Right is newer", "오른쪽이 더 최신")
            case .leftSizeDiffers: return tr("Left size differs", "왼쪽 크기 다름")
            case .rightSizeDiffers: return tr("Right size differs", "오른쪽 크기 다름")
            }
        }
        func urls(in result: FolderComparisonResult) -> [URL] {
            switch self {
            case .onlyLeft: return result.onlyLeft
            case .onlyRight: return result.onlyRight
            case .leftNewer: return result.leftNewer
            case .rightNewer: return result.rightNewer
            case .leftSizeDiffers: return result.leftSizeDiffers
            case .rightSizeDiffers: return result.rightSizeDiffers
            }
        }
        var isLeft: Bool { self == .onlyLeft || self == .leftNewer || self == .leftSizeDiffers }
    }

    private let left: URL
    private let right: URL
    private let selection: ([URL], [URL]) -> Void
    private let spinner = NSProgressIndicator()
    private let status = ModelSheetUI.label(tr("Comparing folders…", "폴더 비교 중…"), secondary: true)
    private var checkboxes: [Category: NSButton] = [:]
    private var result: FolderComparisonResult?
    private var icons: [URL: NSImage] = [:]
    private var rows: [(url: URL, category: Category)] = []
    private(set) var differenceTable: NSTableView!
    private var selectLeftButton: NSButton!
    private var selectRightButton: NSButton!
    private var compareTask: Task<Void, Never>?
    private var compareWork: Task<ModelComparisonData, Error>?

    static func present(left: URL, right: URL, in window: NSWindow, select: @escaping ([URL], [URL]) -> Void) {
        let controller = CompareSheetController(left: left, right: right, select: select)
        guard let sheet = controller.window else { return }
        window.beginSheet(sheet) { [controller] _ in controller.cancelWork() }
    }

    init(left: URL, right: URL, startLoading: Bool = true, select: @escaping ([URL], [URL]) -> Void) {
        self.left = left
        self.right = right
        selection = select
        super.init(window: ModelSheetUI.window(title: tr("Compare Folders", "폴더 비교"), size: NSSize(width: 760, height: 560)))
        buildContent()
        if startLoading { startComparison() }
    }

    required init?(coder: NSCoder) { fatalError("init(coder:) is unavailable") }

    private func buildContent() {
        guard let window else { return }
        let title = ModelSheetUI.label(tr("Compare Folders", "폴더 비교"))
        title.font = Typography.title()
        let leftLabel = ModelSheetUI.label(tr("Left: \(left.path)", "왼쪽: \(left.path)"), secondary: true)
        let rightLabel = ModelSheetUI.label(tr("Right: \(right.path)", "오른쪽: \(right.path)"), secondary: true)
        leftLabel.toolTip = left.path
        rightLabel.toolTip = right.path
        let categories = NSGridView()
        categories.columnSpacing = Metrics.xl
        categories.rowSpacing = Metrics.s
        for pair in [[Category.onlyLeft, .onlyRight], [.leftNewer, .rightNewer], [.leftSizeDiffers, .rightSizeDiffers]] {
            let buttons = pair.map { category -> NSButton in
                let button = NSButton(checkboxWithTitle: tr("\(category.title): 0", "\(category.title): 0"), target: self, action: #selector(selectionChanged(_:)))
                button.tag = category.rawValue
                button.font = Typography.list(.medium)
                button.state = .on
                button.isEnabled = false
                button.setAccessibilityLabel(category.title)
                checkboxes[category] = button
                return button
            }
            categories.addRow(with: buttons)
        }
        let (table, scroll) = ModelSheetUI.table([
            ("name", tr("Differing Item", "다른 항목"), 320),
            ("category", tr("Difference", "차이"), 250)
        ], label: tr("Folder differences", "폴더 차이"))
        differenceTable = table
        table.dataSource = self
        table.delegate = self
        spinner.style = .spinning
        spinner.controlSize = .small
        spinner.isDisplayedWhenStopped = false
        selectLeftButton = ModelSheetUI.button(tr("Select in Left Pane", "왼쪽 영역에서 선택"), target: self, action: #selector(selectLeft(_:)))
        selectRightButton = ModelSheetUI.button(tr("Select in Right Pane", "오른쪽 영역에서 선택"), target: self, action: #selector(selectRight(_:)))
        selectLeftButton.isEnabled = false
        selectRightButton.isEnabled = false
        let done = ModelSheetUI.button(tr("Done", "완료"), target: self, action: #selector(done(_:)))
        done.keyEquivalent = "\r"
        let footer = ModelSheetUI.stack([selectLeftButton, selectRightButton, NSView(), done])
        let activity = ModelSheetUI.stack([spinner, status])
        let content = ModelSheetUI.stack([title, leftLabel, rightLabel, categories, activity, scroll, footer], vertical: true)
        content.spacing = Metrics.m
        ModelSheetUI.install(content, in: window)
        for view in [leftLabel, rightLabel, scroll, footer] {
            view.widthAnchor.constraint(equalTo: content.widthAnchor).isActive = true
        }
        scroll.heightAnchor.constraint(greaterThanOrEqualToConstant: 220).isActive = true
    }

    private func startComparison() {
        spinner.startAnimation(nil)
        let left = self.left
        let right = self.right
        let work = Task.detached(priority: .userInitiated) { try modelCompareFolders(left: left, right: right) }
        compareWork = work
        compareTask = Task { [weak self] in
            do {
                let data = try await work.value
                guard let self, !Task.isCancelled else { return }
                self.setComparison(data.result, icons: data.icons)
            } catch {
                guard let self, !Task.isCancelled else { return }
                self.spinner.stopAnimation(nil)
                self.status.stringValue = tr("Cannot compare folders: \(error.localizedDescription)", "폴더를 비교할 수 없습니다: \(error.localizedDescription)")
                self.status.textColor = .systemRed
            }
        }
    }

    /// Also useful for rendering a deterministic, offscreen sheet preview.
    func setComparison(_ result: FolderComparisonResult, icons: [URL: NSImage] = [:]) {
        self.result = result
        self.icons = icons
        spinner.stopAnimation(nil)
        rows = Category.allCases.flatMap { category in category.urls(in: result).map { ($0, category) } }
        for category in Category.allCases {
            let count = category.urls(in: result).count
            checkboxes[category]?.title = tr("\(category.title): \(count)", "\(category.title): \(count)")
            checkboxes[category]?.isEnabled = count > 0
        }
        status.stringValue = rows.isEmpty ? tr("The folders have no differences.", "두 폴더에 차이가 없습니다.")
            : tr("\(Set(rows.map(\.url)).count) differing items", "다른 항목 \(Set(rows.map(\.url)).count)개")
        status.textColor = .secondaryLabelColor
        differenceTable.reloadData()
        updateSelectionButtons()
    }

    private func selectedURLs(left: Bool) -> [URL] {
        guard let result else { return [] }
        var seen = Set<URL>()
        return Category.allCases.filter { $0.isLeft == left && checkboxes[$0]?.state == .on }
            .flatMap { $0.urls(in: result) }.filter { seen.insert($0).inserted }
    }

    private func updateSelectionButtons() {
        selectLeftButton.isEnabled = !selectedURLs(left: true).isEmpty
        selectRightButton.isEnabled = !selectedURLs(left: false).isEmpty
    }

    @objc private func selectionChanged(_ sender: Any?) { updateSelectionButtons() }
    @objc private func selectLeft(_ sender: Any?) { selection(selectedURLs(left: true), []) }
    @objc private func selectRight(_ sender: Any?) { selection([], selectedURLs(left: false)) }
    @objc private func done(_ sender: Any?) {
        cancelWork()
        ModelSheetUI.close(window, response: .OK)
    }
    private func cancelWork() {
        compareTask?.cancel()
        compareWork?.cancel()
    }

    func numberOfRows(in tableView: NSTableView) -> Int { rows.count }

    func tableView(_ tableView: NSTableView, viewFor tableColumn: NSTableColumn?, row: Int) -> NSView? {
        let item = rows[row]
        if tableColumn?.identifier.rawValue == "category" { return ModelSheetUI.label(item.category.title) }
        let image = NSImageView()
        image.image = icons[item.url]
        image.imageScaling = .scaleProportionallyDown
        image.widthAnchor.constraint(equalToConstant: 18).isActive = true
        image.heightAnchor.constraint(equalToConstant: 18).isActive = true
        image.setAccessibilityElement(false)
        let name = ModelSheetUI.label(item.url.lastPathComponent)
        let view = ModelSheetUI.stack([image, name])
        view.toolTip = item.url.path
        return view
    }
}

@MainActor
enum WorkspaceSheets {
    static func askName(in window: NSWindow, suggested: String, existing: [String], completion: @escaping (String?) -> Void) {
        let controller = WorkspaceNameSheetController(suggested: suggested, existing: existing, completion: completion)
        guard let sheet = controller.window else { return }
        window.beginSheet(sheet) { [controller] response in controller.finish(response: response) }
    }

    static func manage(in window: NSWindow, store: WorkspaceStore, open: @escaping (Workspace) -> Void) {
        let controller = WorkspaceManagerSheetController(store: store, open: open)
        guard let sheet = controller.window else { return }
        window.beginSheet(sheet) { [controller] _ in controller.stopObserving() }
    }

    fileprivate static func deleteWorkspace(_ workspace: Workspace, store: WorkspaceStore, undoManager: UndoManager?) {
        WorkspaceDeletion(workspace: workspace, store: store, undoManager: undoManager).delete()
    }
}

/// Undo handlers target this main-actor object; no AppKit object crosses an executor boundary.
@MainActor
private final class WorkspaceDeletion {
    private let workspace: Workspace
    private let store: WorkspaceStore
    private weak var undoManager: UndoManager?

    init(workspace: Workspace, store: WorkspaceStore, undoManager: UndoManager?) {
        self.workspace = workspace
        self.store = store
        self.undoManager = undoManager
    }

    func delete() {
        store.delete(named: workspace.name)
        let grouping = undoManager?.isUndoing == false && undoManager?.isRedoing == false
        if grouping { undoManager?.beginUndoGrouping() }
        undoManager?.registerUndo(withTarget: self) { action in
            MainActor.assumeIsolated { action.restore() }
        }
        undoManager?.setActionName(tr("Delete Workspace", "작업 구성 삭제"))
        if grouping { undoManager?.endUndoGrouping() }
    }

    private func restore() {
        store.save(workspace)
        undoManager?.registerUndo(withTarget: self) { action in
            MainActor.assumeIsolated { action.delete() }
        }
        undoManager?.setActionName(tr("Delete Workspace", "작업 구성 삭제"))
    }
}

@MainActor
final class WorkspaceNameSheetController: NSWindowController, NSTextFieldDelegate {
    let nameField: NSTextField
    private let existing: [String]
    private let completion: (String?) -> Void
    private let warning = ModelSheetUI.label("")
    private var saveButton: NSButton!
    private var completed = false
    var proposedName: String { nameField.stringValue.trimmingCharacters(in: .whitespacesAndNewlines) }

    init(suggested: String, existing: [String], completion: @escaping (String?) -> Void) {
        self.existing = existing
        self.completion = completion
        nameField = NSTextField(string: suggested)
        super.init(window: ModelSheetUI.window(title: tr("Workspace Name", "작업 구성 이름"), size: NSSize(width: 470, height: 220)))
        guard let window else { return }
        let heading = ModelSheetUI.label(tr("Name this workspace", "작업 구성의 이름을 입력하십시오."))
        heading.font = Typography.title()
        nameField.font = Typography.list(.medium)
        nameField.delegate = self
        nameField.setAccessibilityLabel(tr("Workspace name", "작업 구성 이름"))
        warning.font = Typography.list(.small)
        warning.lineBreakMode = .byWordWrapping
        warning.maximumNumberOfLines = 2
        saveButton = ModelSheetUI.button(tr("Save", "저장"), target: self, action: #selector(save(_:)))
        saveButton.keyEquivalent = "\r"
        let cancel = ModelSheetUI.button(tr("Cancel", "취소"), target: self, action: #selector(cancel(_:)))
        cancel.keyEquivalent = "\u{1b}"
        let footer = ModelSheetUI.stack([NSView(), cancel, saveButton])
        let content = ModelSheetUI.stack([heading, nameField, warning, NSView(), footer], vertical: true)
        content.spacing = Metrics.m
        ModelSheetUI.install(content, in: window)
        for view in [nameField, warning, footer] { view.widthAnchor.constraint(equalTo: content.widthAnchor).isActive = true }
        window.initialFirstResponder = nameField
        validateName()
    }

    required init?(coder: NSCoder) { fatalError("init(coder:) is unavailable") }

    func controlTextDidChange(_ obj: Notification) { validateName() }

    private func validateName() {
        let name = proposedName
        saveButton.isEnabled = !name.isEmpty
        if name.isEmpty {
            warning.stringValue = tr("Type a workspace name.", "작업 구성 이름을 입력하십시오.")
            warning.textColor = .systemRed
            saveButton.title = tr("Save", "저장")
        } else if existing.contains(where: { $0.precomposedStringWithCanonicalMapping.caseInsensitiveCompare(name.precomposedStringWithCanonicalMapping) == .orderedSame }) {
            warning.stringValue = tr("A workspace with this name already exists and will be replaced.", "같은 이름의 작업 구성이 이미 있으며 대치됩니다.")
            warning.textColor = .systemOrange
            saveButton.title = tr("Replace", "대치")
        } else {
            warning.stringValue = ""
            saveButton.title = tr("Save", "저장")
        }
    }

    @objc private func save(_ sender: Any?) {
        guard !proposedName.isEmpty else { return }
        ModelSheetUI.close(window, response: .OK)
    }
    @objc private func cancel(_ sender: Any?) { ModelSheetUI.close(window) }

    func finish(response: NSApplication.ModalResponse) {
        guard !completed else { return }
        completed = true
        completion(response == .OK ? proposedName : nil)
    }
}

@MainActor
final class WorkspaceManagerSheetController: NSWindowController, NSTableViewDataSource, NSTableViewDelegate {
    private let store: WorkspaceStore
    private let openWorkspace: (Workspace) -> Void
    private var workspaces: [Workspace] = []
    private(set) var workspaceTable: NSTableView!
    private var renameButton: NSButton!
    private var deleteButton: NSButton!
    private var openButton: NSButton!
    private let emptyLabel = ModelSheetUI.label("", secondary: true)
    private var observer: NSObjectProtocol?
    private let savedDateFormatter: DateFormatter = {
        let formatter = DateFormatter()
        formatter.locale = Loc.locale
        formatter.dateStyle = .medium
        formatter.timeStyle = .short
        return formatter
    }()

    init(store: WorkspaceStore, open: @escaping (Workspace) -> Void) {
        self.store = store
        openWorkspace = open
        super.init(window: ModelSheetUI.window(title: tr("Manage Workspaces", "작업 구성 관리"), size: NSSize(width: 600, height: 410)))
        buildContent()
        reloadWorkspaces()
        // Observe the public notification without replacing the store's persistence onChange hook.
        observer = NotificationCenter.default.addObserver(forName: .workspacesDidChange, object: nil, queue: .main) { [weak self] _ in
            Task { @MainActor [weak self] in self?.reloadWorkspaces() }
        }
    }

    required init?(coder: NSCoder) { fatalError("init(coder:) is unavailable") }

    private func buildContent() {
        guard let window else { return }
        let heading = ModelSheetUI.label(tr("Saved Workspaces", "저장된 작업 구성"))
        heading.font = Typography.title()
        let (table, scroll) = ModelSheetUI.table([
            ("name", tr("Name", "이름"), 320), ("saved", tr("Date Saved", "저장일"), 210)
        ], label: tr("Saved workspaces", "저장된 작업 구성"))
        workspaceTable = table
        table.dataSource = self
        table.delegate = self
        table.target = self
        table.doubleAction = #selector(openSelected(_:))
        renameButton = ModelSheetUI.button(tr("Rename…", "이름 변경…"), target: self, action: #selector(renameSelected(_:)))
        deleteButton = ModelSheetUI.button(tr("Delete", "삭제"), target: self, action: #selector(deleteSelected(_:)))
        openButton = ModelSheetUI.button(tr("Open", "열기"), target: self, action: #selector(openSelected(_:)))
        openButton.keyEquivalent = "\r"
        let done = ModelSheetUI.button(tr("Done", "완료"), target: self, action: #selector(done(_:)))
        done.keyEquivalent = "\u{1b}"
        let footer = ModelSheetUI.stack([renameButton, deleteButton, NSView(), done, openButton])
        let content = ModelSheetUI.stack([heading, scroll, emptyLabel, footer], vertical: true)
        content.spacing = Metrics.m
        ModelSheetUI.install(content, in: window)
        for view in [scroll, emptyLabel, footer] { view.widthAnchor.constraint(equalTo: content.widthAnchor).isActive = true }
        scroll.heightAnchor.constraint(greaterThanOrEqualToConstant: 230).isActive = true
    }

    private var selectedWorkspace: Workspace? {
        let row = workspaceTable.selectedRow
        return workspaces.indices.contains(row) ? workspaces[row] : nil
    }

    private func reloadWorkspaces(selecting name: String? = nil) {
        let selectedName = name ?? selectedWorkspace?.name
        workspaces = store.workspaces
        workspaceTable.reloadData()
        if let selectedName, let index = workspaces.firstIndex(where: { $0.name == selectedName }) {
            workspaceTable.selectRowIndexes(IndexSet(integer: index), byExtendingSelection: false)
        }
        emptyLabel.stringValue = workspaces.isEmpty
            ? tr("No saved workspaces. Use Save Workspace to keep your current layout and tabs.", "저장된 작업 구성이 없습니다. ‘작업 구성 저장’을 선택하여 현재 레이아웃과 탭을 저장하십시오.")
            : tr("\(workspaces.count) saved workspaces", "저장된 작업 구성 \(workspaces.count)개")
        updateButtons()
    }

    private func updateButtons() {
        let hasSelection = selectedWorkspace != nil
        renameButton.isEnabled = hasSelection
        deleteButton.isEnabled = hasSelection
        openButton.isEnabled = hasSelection
    }

    func tableViewSelectionDidChange(_ notification: Notification) { updateButtons() }
    func numberOfRows(in tableView: NSTableView) -> Int { workspaces.count }
    func tableView(_ tableView: NSTableView, viewFor tableColumn: NSTableColumn?, row: Int) -> NSView? {
        let workspace = workspaces[row]
        if tableColumn?.identifier.rawValue == "saved" {
            let label = ModelSheetUI.label(savedDateFormatter.string(from: workspace.savedAt), secondary: true)
            label.font = Typography.listSecondary(.medium)
            return label
        }
        return ModelSheetUI.label(workspace.name)
    }

    @objc private func renameSelected(_ sender: Any?) {
        guard let workspace = selectedWorkspace, let window else { return }
        WorkspaceSheets.askName(in: window, suggested: workspace.name,
                               existing: workspaces.filter { $0.name != workspace.name }.map(\.name)) { [weak self] name in
            guard let self, let name else { return }
            let replaced = self.store.workspaces.first {
                $0.name.caseInsensitiveCompare(name) == .orderedSame && $0.name.caseInsensitiveCompare(workspace.name) != .orderedSame
            }
            do {
                // The name sheet explicitly confirms replacement, while rename itself rejects collisions.
                guard self.store.workspaces.contains(where: { $0.name == workspace.name }) else {
                    throw WorkspaceStore.WorkspaceError.notFound
                }
                if let replaced { self.store.delete(named: replaced.name) }
                try self.store.rename(workspace.name, to: name)
                self.reloadWorkspaces(selecting: name)
            } catch {
                if let replaced { self.store.save(replaced) }
                ModelSheetUI.error(error, in: window)
            }
        }
    }

    @objc private func deleteSelected(_ sender: Any?) {
        guard let workspace = selectedWorkspace, let window else { return }
        WorkspaceSheets.deleteWorkspace(workspace, store: store, undoManager: window.sheetParent?.undoManager ?? window.undoManager)
        reloadWorkspaces()
    }

    @objc private func openSelected(_ sender: Any?) {
        guard let workspace = selectedWorkspace else { return }
        stopObserving()
        ModelSheetUI.close(window, response: .OK)
        openWorkspace(workspace)
    }

    @objc private func done(_ sender: Any?) {
        stopObserving()
        ModelSheetUI.close(window, response: .OK)
    }

    func stopObserving() {
        if let observer { NotificationCenter.default.removeObserver(observer) }
        observer = nil
    }
}
