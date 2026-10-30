import AppKit

/// One pane: its tabs, the header (back/forward, address/search, filter), the selected tab's
/// file list and the status line.
@MainActor
final class PaneController: NSViewController, PaneCommands, FileListHost, NSMenuItemValidation, NSSearchFieldDelegate {
    static let tabLimit = 32

    let id: UUID
    weak var windowController: MainWindowController?
    var number = 1 { didSet { applyNumber() } }
    private(set) var tabs: [TabState] = []
    private(set) var selectedTabID: UUID?
    var isActive = false { didSet { if isActive != oldValue { applyActive() } } }

    private let tabBar = TabBarView(frame: .zero)
    private let navigation = NavigationButtonsView(frame: .zero)
    let addressBar = AddressBarView(frame: .zero)
    private let filterField = NSSearchField()
    private let content = NSView()
    private let statusBar = StatusBarView(frame: .zero)
    private let emptyState = PaneEmptyStateView()
    private var statusHeight: NSLayoutConstraint?
    private var availableBytes: [String: Int64] = [:]
    private var iconLoads: Set<UUID> = []

    init(id: UUID = UUID(), snapshot: PaneSnapshot? = nil) {
        self.id = id
        super.init(nibName: nil, bundle: nil)
        if let snapshot {
            tabs = snapshot.tabs.prefix(Self.tabLimit).map(TabState.init(snapshot:))
            let index = min(max(snapshot.selectedTab, 0), max(tabs.count - 1, 0))
            selectedTabID = tabs.isEmpty ? nil : tabs[index].id
        }
    }

    required init?(coder: NSCoder) { fatalError("init(coder:) is not used") }

    var snapshot: PaneSnapshot {
        PaneSnapshot(id: id, tabs: tabs.map(\.snapshot), selectedTab: selectedIndex ?? 0)
    }

    var selectedTab: TabState? { tabs.first { $0.id == selectedTabID } }
    private var selectedIndex: Int? { tabs.firstIndex { $0.id == selectedTabID } }
    var currentFolder: URL? { selectedTab?.folder }
    var currentList: FileListViewController? { selectedTab?.list }
    var selectedURLs: [URL] { currentList?.selectedURLs ?? [] }
    var openFolders: [URL] { tabs.compactMap(\.folder) }

    // MARK: View

    override func loadView() {
        let root = PaneRootView()
        root.onMouseDown = { [weak self] in self?.activate() }
        view = root

        tabBar.paneID = id
        tabBar.delegate = self
        navigation.onBack = { [weak self] in self?.goBack(nil) }
        navigation.onForward = { [weak self] in self?.goForward(nil) }
        navigation.backMenu = { [weak self] in self?.historyMenu(forward: false) }
        navigation.forwardMenu = { [weak self] in self?.historyMenu(forward: true) }
        addressBar.delegate = self
        addressBar.search = AppServices.shared.search
        filterField.placeholderString = tr("Filter", "필터")
        filterField.controlSize = .regular
        filterField.font = Typography.address
        filterField.delegate = self
        filterField.sendsSearchStringImmediately = true
        filterField.target = self
        filterField.action = #selector(filterChanged(_:))
        filterField.setAccessibilityLabel(tr("Filter items in this folder", "이 폴더의 항목 필터"))
        emptyState.onOpen = { [weak self] url in self?.open(url, disposition: .current) }

        let header = PaneHeaderView()
        // Content first so the chrome stays above it: scroll views paint their background
        // beyond their frame toward a transparent titlebar region.
        for sub in [content, statusBar, header, tabBar] as [NSView] {
            sub.translatesAutoresizingMaskIntoConstraints = false
            root.addSubview(sub)
        }
        for sub in [navigation, addressBar, filterField] as [NSView] {
            sub.translatesAutoresizingMaskIntoConstraints = false
            header.addSubview(sub)
        }
        emptyState.translatesAutoresizingMaskIntoConstraints = false
        content.addSubview(emptyState)

        let filterWidth = filterField.widthAnchor.constraint(equalToConstant: 168)
        filterWidth.priority = .defaultHigh
        let addressMin = addressBar.widthAnchor.constraint(greaterThanOrEqualToConstant: 120)
        addressMin.priority = .defaultHigh
        let status = statusBar.heightAnchor.constraint(equalToConstant: Metrics.statusBarHeight)
        statusHeight = status

        NSLayoutConstraint.activate([
            tabBar.topAnchor.constraint(equalTo: root.topAnchor),
            tabBar.leadingAnchor.constraint(equalTo: root.leadingAnchor),
            tabBar.trailingAnchor.constraint(equalTo: root.trailingAnchor),
            tabBar.heightAnchor.constraint(equalToConstant: Metrics.tabBarHeight),

            header.topAnchor.constraint(equalTo: tabBar.bottomAnchor),
            header.leadingAnchor.constraint(equalTo: root.leadingAnchor),
            header.trailingAnchor.constraint(equalTo: root.trailingAnchor),
            header.heightAnchor.constraint(equalToConstant: Metrics.addressBarHeight + Metrics.s),

            navigation.leadingAnchor.constraint(equalTo: header.leadingAnchor, constant: Metrics.s),
            navigation.centerYAnchor.constraint(equalTo: header.centerYAnchor),
            addressBar.leadingAnchor.constraint(equalTo: navigation.trailingAnchor, constant: Metrics.xs),
            addressBar.centerYAnchor.constraint(equalTo: header.centerYAnchor),
            addressBar.heightAnchor.constraint(equalToConstant: Metrics.addressBarHeight),
            addressMin,
            filterField.leadingAnchor.constraint(equalTo: addressBar.trailingAnchor, constant: Metrics.s),
            yielding(filterField.trailingAnchor.constraint(equalTo: header.trailingAnchor, constant: -Metrics.s)),
            filterField.centerYAnchor.constraint(equalTo: header.centerYAnchor),
            filterWidth,
            filterField.widthAnchor.constraint(greaterThanOrEqualToConstant: 60),

            content.topAnchor.constraint(equalTo: header.bottomAnchor),
            content.leadingAnchor.constraint(equalTo: root.leadingAnchor),
            content.trailingAnchor.constraint(equalTo: root.trailingAnchor),
            content.bottomAnchor.constraint(equalTo: statusBar.topAnchor),

            emptyState.topAnchor.constraint(equalTo: content.topAnchor),
            emptyState.leadingAnchor.constraint(equalTo: content.leadingAnchor),
            emptyState.trailingAnchor.constraint(equalTo: content.trailingAnchor),
            emptyState.bottomAnchor.constraint(equalTo: content.bottomAnchor),

            statusBar.leadingAnchor.constraint(equalTo: root.leadingAnchor),
            statusBar.trailingAnchor.constraint(equalTo: root.trailingAnchor),
            yielding(statusBar.bottomAnchor.constraint(equalTo: root.bottomAnchor)),
            status,
        ])
        root.setAccessibilityElement(true)
        root.setAccessibilityRole(.group)
        applyNumber()
        applySettings()
        NotificationCenter.default.addObserver(self, selector: #selector(settingsChanged(_:)),
                                               name: .settingsDidChange, object: nil)
    }

    override func viewDidLoad() {
        super.viewDidLoad()
        refreshTabs(animated: false)
        showSelectedTab()
    }

    /// Outer edges give way (999) so a pane that is momentarily zero-sized — before the first
    /// layout or while it grows in — never makes the constraints unsatisfiable.
    private func yielding(_ constraint: NSLayoutConstraint) -> NSLayoutConstraint {
        constraint.priority = NSLayoutConstraint.Priority(999)
        return constraint
    }

    private func applyNumber() {
        guard isViewLoaded else { return }
        view.setAccessibilityIdentifier(AccessibilityID.pane(number))
        view.setAccessibilityLabel(tr("Pane \(number)", "영역 \(number)"))
        tabBar.paneNumber = number
        addressBar.paneNumber = number
        statusBar.paneNumber = number
        filterField.setAccessibilityIdentifier(AccessibilityID.filter(number))
        for tab in tabs { tab.list?.paneNumber = number }
    }

    private func applyActive() {
        guard isViewLoaded else { return }
        tabBar.isActivePane = isActive
        for tab in tabs { tab.list?.isActive = isActive && tab.id == selectedTabID }
    }

    private func applySettings() {
        let settings = SettingsStore.shared.settings
        statusBar.isHidden = !settings.showStatusBar
        statusHeight?.constant = settings.showStatusBar ? Metrics.statusBarHeight : 0
    }

    @objc private func settingsChanged(_ note: Notification) {
        applySettings()
        updateStatus()
        refreshTabs(animated: false)
    }

    // MARK: Tabs

    @discardableResult
    func addTab(folder: URL?, selecting: [URL] = [], activate: Bool = true, at index: Int? = nil,
                id: UUID = UUID(), history: TabSnapshot? = nil) -> TabState? {
        guard tabs.count < Self.tabLimit else {
            windowController?.showNotice(tr("A pane can hold up to 32 tabs.", "한 영역에서 탭을 최대 32개까지 열 수 있습니다."))
            NSSound.beep()
            return nil
        }
        let tab = history.map(TabState.init(snapshot:)) ?? TabState(id: id, folder: folder)
        let position = min(max(index ?? (selectedIndex.map { $0 + 1 } ?? tabs.count), 0), tabs.count)
        tabs.insert(tab, at: position)
        if activate || selectedTabID == nil {
            selectedTabID = tab.id
            refreshTabs(animated: true)
            showSelectedTab(selecting: selecting)
            if tab.isBlank { addressBar.beginEditing() }
        } else {
            refreshTabs(animated: true)
        }
        if let folder = tab.folder, history == nil { AppServices.shared.history.recordVisit(folder) }
        windowController?.paneDidChange(self)
        return tab
    }

    /// Removes and returns a tab (for moving it to another pane or window).
    func detachTab(_ tabID: UUID) -> (tab: TabState, index: Int)? {
        guard let index = tabs.firstIndex(where: { $0.id == tabID }) else { return nil }
        let tab = tabs.remove(at: index)
        // Release the list (and its folder watcher) with the tab; a moved tab gets a new one.
        tab.list?.view.removeFromSuperview()
        tab.list?.removeFromParent()
        tab.list = nil
        if selectedTabID == tabID {
            selectedTabID = tabs.isEmpty ? nil : tabs[min(index, tabs.count - 1)].id
            showSelectedTab()
        }
        refreshTabs(animated: true)
        windowController?.paneDidChange(self)
        return (tab, index)
    }

    /// Inserts an existing tab (moved from elsewhere) and selects it.
    func insert(_ tab: TabState, at index: Int?) {
        guard tabs.count < Self.tabLimit else { NSSound.beep(); return }
        tab.list?.view.removeFromSuperview()
        tab.list?.removeFromParent()
        tab.list = nil   // rebuilt with this pane as host
        let position = min(max(index ?? tabs.count, 0), tabs.count)
        tabs.insert(tab, at: position)
        selectedTabID = tab.id
        refreshTabs(animated: true)
        showSelectedTab()
        windowController?.paneDidChange(self)
    }

    func selectTab(_ tabID: UUID) {
        guard tabs.contains(where: { $0.id == tabID }), selectedTabID != tabID else { return }
        selectedTabID = tabID
        refreshTabs(animated: false)
        showSelectedTab()
        windowController?.paneDidChange(self)
    }

    func closeTab(id tabID: UUID) {
        guard let index = tabs.firstIndex(where: { $0.id == tabID }) else { return }
        let tab = tabs[index]
        windowController?.recordClosedTab(tab.snapshot, pane: id, index: index)
        _ = detachTab(tabID)
        if tabs.isEmpty { view.window?.makeFirstResponder(view) }
    }

    private func refreshTabs(animated: Bool) {
        guard isViewLoaded else { return }
        let items = tabs.map { tab in
            TabBarItem(id: tab.id, title: tab.title, icon: tab.icon,
                       toolTip: tab.folder.map { PathText.abbreviated($0) }, isBlank: tab.isBlank)
        }
        tabBar.setItems(items, selected: selectedTabID, animated: animated)
        tabBar.canReopenClosedTab = windowController?.canReopenTab(in: id) ?? false
        for tab in tabs where tab.icon == nil || tab.resolvedTitle == nil { loadIcon(for: tab) }
    }

    func refreshReopenState() {
        tabBar.canReopenClosedTab = windowController?.canReopenTab(in: id) ?? false
    }

    /// Icon and Finder display name come from the disk (a network share can stall), so they are
    /// resolved off the main thread; the tab shows the folder's file name until then.
    private func loadIcon(for tab: TabState) {
        guard let folder = tab.folder, !iconLoads.contains(tab.id) else { return }
        iconLoads.insert(tab.id)
        let path = folder.path
        let tabID = tab.id
        Task.detached(priority: .utility) {
            let icon = SendableImage(NSWorkspace.shared.icon(forFile: path))
            let title = TabState.displayName(of: URL(fileURLWithPath: path, isDirectory: true))
            await MainActor.run { [weak self] in
                guard let self else { return }
                self.iconLoads.remove(tabID)
                guard let tab = self.tabs.first(where: { $0.id == tabID }), tab.folder?.path == path else { return }
                icon.image.size = NSSize(width: 16, height: 16)
                tab.icon = icon.image
                tab.resolvedTitle = title
                self.refreshTabs(animated: false)
                if tab.id == self.selectedTabID { self.windowController?.paneContentDidChange(self) }
            }
        }
    }

    /// Shows the selected tab's list (creating it on first show) or the empty state.
    private func showSelectedTab(selecting: [URL] = []) {
        guard isViewLoaded else { return }
        for tab in tabs where tab.id != selectedTabID { tab.list?.view.isHidden = true }
        guard let tab = selectedTab, let folder = tab.folder else {
            emptyState.isHidden = false
            emptyState.reload(blankTab: selectedTab != nil)
            addressBar.url = nil
            filterField.stringValue = ""
            updateStatus()
            windowController?.paneContentDidChange(self)
            return
        }
        emptyState.isHidden = true
        let list: FileListViewController
        if let existing = tab.list {
            list = existing
            list.view.isHidden = false
            if list.folder != folder { list.show(folder, selecting: selecting) }
            else if !selecting.isEmpty { list.select(selecting) }
        } else {
            list = FileListViewController(host: self)
            list.paneNumber = number
            tab.list = list
            addChild(list)
            list.view.translatesAutoresizingMaskIntoConstraints = false
            content.addSubview(list.view, positioned: .below, relativeTo: emptyState)
            NSLayoutConstraint.activate([
                list.view.topAnchor.constraint(equalTo: content.topAnchor),
                list.view.leadingAnchor.constraint(equalTo: content.leadingAnchor),
                list.view.trailingAnchor.constraint(equalTo: content.trailingAnchor),
                list.view.bottomAnchor.constraint(equalTo: content.bottomAnchor),
            ])
            list.show(folder, selecting: selecting)
        }
        list.isActive = isActive
        addressBar.url = folder
        filterField.stringValue = list.filterText
        navigation.canGoBack = tab.canGoBack
        navigation.canGoForward = tab.canGoForward
        updateStatus()
        windowController?.paneContentDidChange(self)
        if isActive, view.window?.firstResponder is PaneRootView || view.window?.firstResponder === view.window {
            list.focus()
        }
    }

    // MARK: Navigation

    /// Shows `folder` in the selected tab (creating one in an empty pane).
    func navigate(to folder: URL, selecting: [URL] = []) {
        guard let tab = selectedTab else {
            addTab(folder: folder, selecting: selecting)
            return
        }
        tab.navigate(to: folder)
        AppServices.shared.history.recordVisit(folder)
        refreshTabs(animated: false)
        showSelectedTab(selecting: selecting)
        windowController?.paneDidChange(self)
    }

    /// "Open in other pane" semantics: select a tab already showing the folder, else fill the
    /// selected blank tab, else add a tab.
    func show(_ folder: URL, selecting: [URL]) {
        if let existing = tabs.first(where: { $0.folder == folder.standardizedFileURL }) {
            selectTab(existing.id)
            if !selecting.isEmpty { existing.list?.select(selecting) }
        } else if let tab = selectedTab, tab.isBlank {
            navigate(to: folder, selecting: selecting)
        } else {
            addTab(folder: folder, selecting: selecting)
        }
    }

    func open(_ url: URL, disposition: OpenDisposition) {
        windowController?.open(url, disposition: disposition, from: self)
    }

    func activate() {
        windowController?.activatePane(self)
    }

    func focusList() {
        if let list = currentList { list.focus() }
        else if selectedTab?.isBlank ?? true { addressBar.beginEditing() }
    }

    private func historyMenu(forward: Bool) -> NSMenu? {
        guard let tab = selectedTab else { return nil }
        let entries = forward ? Array(tab.forward.reversed()) : Array(tab.back.reversed())
        guard !entries.isEmpty else { return nil }
        let menu = NSMenu()
        for (offset, url) in entries.prefix(20).enumerated() {
            let item = NSMenuItem(title: TabState.quickName(of: url), action: #selector(jumpInHistory(_:)), keyEquivalent: "")
            item.target = self
            item.tag = (offset + 1) * (forward ? 1 : -1)
            item.toolTip = PathText.abbreviated(url)
            let icon = NSWorkspace.shared.icon(for: .folder)
            icon.size = NSSize(width: 16, height: 16)
            item.image = icon
            menu.addItem(item)
        }
        return menu
    }

    @objc private func jumpInHistory(_ sender: NSMenuItem) {
        guard let tab = selectedTab else { return }
        let previous = tab.folder
        let target = sender.tag > 0 ? tab.goForward(sender.tag) : tab.goBack(-sender.tag)
        guard let target else { return }
        refreshTabs(animated: false)
        showSelectedTab(selecting: selectionReturning(to: target, from: previous))
        windowController?.paneDidChange(self)
    }

    // MARK: Status line

    private func updateStatus() {
        guard isViewLoaded else { return }
        guard let list = currentList, let folder = currentFolder else {
            statusBar.update(primary: selectedTab == nil ? tr("No tabs", "탭 없음") : "", secondary: "")
            return
        }
        let primary: String
        switch list.loadState {
        case .idle, .loading:
            primary = tr("Loading…", "불러오는 중…")
        case .failed(let reason):
            primary = reason
        case .loaded:
            let selected = list.selectedURLs.count
            let visible = list.visibleItemCount
            let total = list.totalItemCount
            if selected > 0 {
                var text = tr("\(selected) of \(visible) selected", "\(visible)개 중 \(selected)개 선택")
                if let bytes = list.selectionByteCount { text += " (\(Format.bytes(bytes)))" }
                primary = text
            } else if visible != total {
                primary = tr("\(visible) of \(total) items", "항목 \(total)개 중 \(visible)개")
            } else {
                primary = Format.items(total)
            }
        }
        var secondary: [String] = []
        if case .loaded = list.loadState {
            secondary.append(tr("\(Format.bytes(list.folderFileBytes)) here", "이 폴더 \(Format.bytes(list.folderFileBytes))"))
        }
        if let available = availableBytes[folder.path] {
            secondary.append(tr("\(Format.bytes(available)) available", "\(Format.bytes(available)) 사용 가능"))
        } else {
            loadAvailableCapacity(for: folder)
        }
        statusBar.update(primary: primary, secondary: secondary.joined(separator: " · "))
    }

    private func loadAvailableCapacity(for folder: URL) {
        let path = folder.path
        Task.detached(priority: .utility) {
            let values = try? URL(fileURLWithPath: path).resourceValues(forKeys: [.volumeAvailableCapacityForImportantUsageKey, .volumeAvailableCapacityKey])
            let bytes = values?.volumeAvailableCapacityForImportantUsage ?? values?.volumeAvailableCapacity.map(Int64.init)
            guard let bytes else { return }
            await MainActor.run { [weak self] in
                guard let self else { return }
                self.availableBytes[path] = bytes
                if self.currentFolder?.path == path { self.updateStatus() }
            }
        }
    }

    // MARK: FileListHost

    func fileListDidActivate(_ list: NSViewController) { activate() }

    func fileListSelectionDidChange(_ list: NSViewController) {
        guard list === currentList else { return }
        updateStatus()
        windowController?.paneSelectionDidChange(self)
    }

    func fileListContentDidChange(_ list: NSViewController) {
        guard list === currentList, let fileList = currentList else { return }
        if filterField.stringValue != fileList.filterText { filterField.stringValue = fileList.filterText }
        availableBytes[currentFolder?.path ?? ""] = nil
        updateStatus()
        windowController?.paneContentDidChange(self)
    }

    var otherPaneFolder: URL? { windowController?.otherPane(of: self)?.currentFolder }

    // MARK: Filter

    @objc private func filterChanged(_ sender: NSSearchField) {
        currentList?.filterText = sender.stringValue
        updateStatus()
    }

    func control(_ control: NSControl, textView: NSTextView, doCommandBy selector: Selector) -> Bool {
        guard control === filterField else { return false }
        if selector == #selector(NSResponder.cancelOperation(_:)) {
            filterField.stringValue = ""
            currentList?.filterText = ""
            updateStatus()
            currentList?.focus()
            return true
        }
        if selector == #selector(NSResponder.insertNewline(_:)) || selector == #selector(NSResponder.moveDown(_:)) {
            currentList?.focus()
            return true
        }
        return false
    }

    // MARK: PaneCommands

    @objc func newTab(_ sender: Any?) {
        addTab(folder: nil)
    }

    @objc func closeTab(_ sender: Any?) {
        if let tabID = selectedTabID { closeTab(id: tabID) }
        else { windowController?.closeEmptyPane(self) }
    }

    @objc func goBack(_ sender: Any?) {
        guard let tab = selectedTab, let previous = tab.folder, let target = tab.goBack() else { return }
        refreshTabs(animated: false)
        showSelectedTab(selecting: selectionReturning(to: target, from: previous))
        windowController?.paneDidChange(self)
    }

    @objc func goForward(_ sender: Any?) {
        guard let tab = selectedTab, tab.goForward() != nil else { return }
        refreshTabs(animated: false)
        showSelectedTab()
        windowController?.paneDidChange(self)
    }

    @objc func goToEnclosingFolder(_ sender: Any?) {
        guard let folder = currentFolder, folder.path != "/" else { return }
        navigate(to: folder.deletingLastPathComponent(), selecting: [folder])
    }

    @objc func goToStandardFolder(_ sender: Any?) {
        guard let tag = (sender as? NSMenuItem)?.tag, let place = StandardFolder(rawValue: tag), let url = place.url else { return }
        open(url, disposition: .current)
    }

    @objc func focusAddressBar(_ sender: Any?) {
        activate()
        addressBar.beginEditing()
    }

    @objc func focusFilter(_ sender: Any?) {
        activate()
        view.window?.makeFirstResponder(filterField)
    }

    @objc func selectNextTab(_ sender: Any?) { cycleTab(1) }
    @objc func selectPreviousTab(_ sender: Any?) { cycleTab(-1) }

    private func cycleTab(_ step: Int) {
        guard let index = selectedIndex, tabs.count > 1 else { return }
        selectTab(tabs[(index + step + tabs.count) % tabs.count].id)
        currentList?.focus()
    }

    @objc func addToSidebar(_ sender: Any?) {
        let folders = selectedURLs.filter { (try? $0.resourceValues(forKeys: [.isDirectoryKey]).isDirectory) == true }
        let targets = folders.isEmpty ? (currentFolder.map { [$0] } ?? []) : folders
        for url in targets where !AppServices.shared.favorites.contains(url) {
            AppServices.shared.favorites.add(url, at: nil)
        }
    }

    @objc func openFolderInOtherPane(_ sender: Any?) {
        guard let folder = currentFolder else { return }
        windowController?.openInOtherPane(folder, selecting: [], from: self)
    }

    @objc func comparePanes(_ sender: Any?) {
        windowController?.compare(from: self)
    }

    func validateMenuItem(_ item: NSMenuItem) -> Bool {
        switch item.action {
        case #selector(goBack(_:)): return selectedTab?.canGoBack ?? false
        case #selector(goForward(_:)): return selectedTab?.canGoForward ?? false
        case #selector(goToEnclosingFolder(_:)): return currentFolder.map { $0.path != "/" } ?? false
        case #selector(goToStandardFolder(_:)):
            return StandardFolder(rawValue: item.tag)?.url != nil
        case #selector(selectNextTab(_:)), #selector(selectPreviousTab(_:)): return tabs.count > 1
        case #selector(addToSidebar(_:)): return currentFolder != nil
        case #selector(openFolderInOtherPane(_:)): return currentFolder != nil
        case #selector(comparePanes(_:)):
            return currentFolder != nil && otherPaneFolder != nil
        case #selector(closeTab(_:)):
            item.title = selectedTab == nil && (windowController?.visiblePaneCount ?? 1) > 1
                ? tr("Close Pane", "영역 닫기") : tr("Close Tab", "탭 닫기")
            return true
        case #selector(newTab(_:)): return tabs.count < Self.tabLimit
        case #selector(focusFilter(_:)): return currentList != nil
        default: return true
        }
    }

    /// Coming back up from a subfolder selects the folder you came out of, like Finder.
    private func selectionReturning(to target: URL, from previous: URL?) -> [URL] {
        guard let previous, previous != target, PathText.isWithin(previous, target) else { return [] }
        return [childOf(target, toward: previous)]
    }

    private func childOf(_ folder: URL, toward descendant: URL) -> URL {
        let base = folder.standardizedFileURL.pathComponents
        let parts = descendant.standardizedFileURL.pathComponents
        guard parts.count > base.count else { return descendant }
        return folder.appendingPathComponent(parts[base.count])
    }
}

// MARK: - Tab bar

extension PaneController: TabBarViewDelegate {
    func tabBar(_ bar: TabBarView, didSelect id: UUID) {
        activate()
        selectTab(id)
        currentList?.focus()
    }

    func tabBar(_ bar: TabBarView, didClose id: UUID) { closeTab(id: id) }

    func tabBarDidRequestNewTab(_ bar: TabBarView) {
        activate()
        addTab(folder: nil)
    }

    func tabBar(_ bar: TabBarView, didMove id: UUID, to index: Int) {
        guard let from = tabs.firstIndex(where: { $0.id == id }) else { return }
        let tab = tabs.remove(at: from)
        tabs.insert(tab, at: min(max(index, 0), tabs.count))
        refreshTabs(animated: false)
        windowController?.paneDidChange(self)
    }

    func tabBar(_ bar: TabBarView, menuFor id: UUID) -> NSMenu? {
        guard let tab = tabs.first(where: { $0.id == id }) else { return nil }
        let menu = NSMenu()
        func add(_ title: String, _ action: @escaping () -> Void) {
            let item = ClosureMenuItem(title: title, action: action)
            menu.addItem(item)
        }
        add(tr("Duplicate Tab", "탭 복제")) { [weak self] in
            guard let self else { return }
            self.addTab(folder: tab.folder, at: (self.tabs.firstIndex { $0.id == id } ?? 0) + 1)
        }
        if let folder = tab.folder {
            add(tr("Open in Other Pane", "다른 영역에서 열기")) { [weak self] in
                guard let self else { return }
                self.windowController?.openInOtherPane(folder, selecting: [], from: self)
            }
            add(tr("Move to New Pane", "새로운 영역으로 이동")) { [weak self] in
                guard let self else { return }
                self.windowController?.moveTab(id, from: self, toNewPaneAt: .right)
            }
            add(tr("Move to New Window", "새로운 윈도우로 이동")) { [weak self] in
                guard let self, let detached = self.detachTab(id) else { return }
                (NSApp.delegate as? AppDelegate)?.openWindow(with: detached.tab)
            }
            menu.addItem(.separator())
            add(tr("Copy as Pathname", "경로 이름 복사")) {
                NSPasteboard.general.clearContents()
                NSPasteboard.general.setString(folder.path, forType: .string)
            }
            add(tr("Show in Finder", "Finder에서 보기")) { FinderBridge.open(folder) }
        }
        menu.addItem(.separator())
        add(tr("Close Tab", "탭 닫기")) { [weak self] in self?.closeTab(id: id) }
        if tabs.count > 1 {
            add(tr("Close Other Tabs", "다른 탭 닫기")) { [weak self] in
                guard let self else { return }
                for other in self.tabs.map(\.id) where other != id { self.closeTab(id: other) }
            }
        }
        return menu
    }

    func tabBar(_ bar: TabBarView, acceptTab payload: TabDragPayload, at index: Int) -> Bool {
        windowController?.acceptTab(payload, into: self, at: index) ?? false
    }

    func tabBar(_ bar: TabBarView, acceptFiles urls: [URL], onto id: UUID, operation: NSDragOperation) -> Bool {
        guard let folder = tabs.first(where: { $0.id == id })?.folder else { return false }
        return FileDrop.perform(urls, into: folder, operation: operation)
    }

    func tabBar(_ bar: TabBarView, didTearOff id: UUID, at screenPoint: NSPoint) {
        guard let detached = detachTab(id) else { return }
        (NSApp.delegate as? AppDelegate)?.openWindow(with: detached.tab, at: screenPoint)
    }

    func tabBarDidRequestReopenClosedTab(_ bar: TabBarView) {
        windowController?.reopenClosedTab(in: self)
    }
}

// MARK: - Address bar

extension PaneController: AddressBarViewDelegate {
    func addressBar(_ bar: AddressBarView, open url: URL, disposition: OpenDisposition) {
        activate()
        open(url, disposition: disposition)
    }

    func addressBarPriorityRoots(_ bar: AddressBarView) -> [String] {
        windowController?.searchPriorityRoots(from: self) ?? []
    }

    func addressBarDidEndEditing(_ bar: AddressBarView) {
        currentList?.focus()
    }

    func addressBar(_ bar: AddressBarView, acceptFiles urls: [URL], into folder: URL, operation: NSDragOperation) -> Bool {
        FileDrop.perform(urls, into: folder, operation: operation)
    }
}

/// The address row: an opaque window-background strip so list content never shows through.
final class PaneHeaderView: NSView {
    override var isOpaque: Bool { true }

    override func draw(_ dirtyRect: NSRect) {
        NSColor.windowBackgroundColor.setFill()
        // Views don't clip to bounds since the macOS 14 SDK; never fill beyond our own frame.
        bounds.intersection(dirtyRect).fill()
    }
}

/// Root view of a pane: activates the pane on any click inside it before the click is handled.
final class PaneRootView: NSView {
    var onMouseDown: (() -> Void)?

    override var acceptsFirstResponder: Bool { true }

    override func hitTest(_ point: NSPoint) -> NSView? {
        let hit = super.hitTest(point)
        if hit != nil, let event = NSApp.currentEvent,
           [.leftMouseDown, .rightMouseDown, .otherMouseDown].contains(event.type) {
            onMouseDown?()
        }
        return hit
    }
}

/// NSImage is not Sendable; icons are created off-main and handed over once.
struct SendableImage: @unchecked Sendable {
    let image: NSImage
    init(_ image: NSImage) { self.image = image }
}

/// Menu item that runs a closure (for small context menus).
final class ClosureMenuItem: NSMenuItem {
    private let handler: () -> Void

    init(title: String, action handler: @escaping () -> Void) {
        self.handler = handler
        super.init(title: title, action: #selector(run), keyEquivalent: "")
        target = self
    }

    required init(coder: NSCoder) { fatalError("init(coder:) is not used") }

    @objc private func run() { handler() }
}
