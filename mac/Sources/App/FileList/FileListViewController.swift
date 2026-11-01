import AppKit
import Quartz

@MainActor
/// One tab's asynchronous Finder-style listing. Install AppServices before initialization;
/// the host owns navigation and receives activation, selection, load, and failure callbacks.
final class FileListViewController: NSViewController {
    enum LoadState: Equatable { case idle, loading, loaded, failed(String) }
    weak var host: FileListHost?
    var paneNumber = 1 { didSet { updateAccessibility() } }
    private(set) var folder: URL?
    private(set) var loadState: LoadState = .idle
    private(set) var selectedURLs: [URL] = []
    var filterText = "" { didSet { if filterText != oldValue { rebuildPresentation() } } }
    var isActive = false { didSet { refreshSelectionAppearance() } }
    private(set) var viewSettings = FolderViewSettings.default
    var visibleItemCount: Int { roots.count }
    var totalItemCount: Int { snapshots[folder ?? URL(fileURLWithPath: "/")]?.count ?? 0 }
    var selectionByteCount: Int64? {
        let sizes = selectedItems.compactMap(\.size)
        guard !sizes.isEmpty else { return nil }
        return sizes.reduce(0) { total, size in
            let (sum, overflow) = total.addingReportingOverflow(size)
            return overflow ? Int64.max : sum
        }
    }
    private(set) var folderFileBytes: Int64 = 0
    var selectedItems: [FileItem] { selectedURLs.compactMap { nodes[$0]?.item } }
    var allDisplayedItems: [FileItem] { isIconView ? roots.map(\.item) : (0..<outline.numberOfRows).compactMap { (outline.item(atRow: $0) as? FileNode)?.item } }
    var isIconView: Bool { viewSettings.viewMode == .icons }
    let outline = FileOutlineView()
    let collection = FileCollectionView()
    let scroll = NSScrollView()
    let iconScroll = NSScrollView()
    private let message = NSTextField(wrappingLabelWithString: "")
    private let privacy = NSButton(title: tr("Open Privacy & Security Settings", "개인정보 보호 및 보안 설정 열기"), target: nil, action: nil)
    private let stateStack = NSStackView()
    var roots: [FileNode] = []
    var nodes: [URL: FileNode] = [:]
    private var snapshots: [URL: [FileItem]] = [:]
    private var itemIdentities: [URL: String] = [:]
    private var pendingSelection: [URL]?
    private var selectionRemapping: [URL: URL] = [:]
    private var directoryLocations: [URL: FileListDirectoryLocation] = [:]
    private var generation = 0
    private var presentationGeneration = 0
    private var loadingTask: Task<Void, Never>?
    private var presentationTask: Task<Void, Never>?
    private var loadingWorker: Task<Result<(FileListSnapshot, [URL: FileListSnapshot]), Error>, Never>?
    private var refreshPending = false
    private var childLoadTasks: [URL: Task<Void, Never>] = [:]
    private var watcher: FileListWatcher?
    private var watchedFolders = Set<URL>()
    private var expanded = Set<URL>()
    private var recursivelyExpanding = Set<URL>()
    private var applyingSelection = false
    var renameURL: URL?
    weak var renameField: NSTextField?
    // Never recycle an inline editor's row. Coalesce all presentation changes until it ends.
    var inlineRenameNeedsRefresh = false
    private var inlineRenameNeedsAppearance = false
    private var inlineRenamePendingShow: (folder: URL, selection: [URL])?
    var renameAfterLoadURL: URL?
    var typeSelectionText = ""
    var typeSelectionTime: TimeInterval = 0
    var quickLookURLs: [URL] = []
    var springTimer: Timer?
    var springURL: URL?
    var dragTarget: URL?
    var lastSettings = SettingsStore.shared.settings

    init(host: FileListHost) {
        self.host = host
        super.init(nibName: nil, bundle: nil)
    }
    required init?(coder: NSCoder) { fatalError("init(host:) required") }
    deinit {
        loadingTask?.cancel()
        loadingWorker?.cancel()
        childLoadTasks.values.forEach { $0.cancel() }
        presentationTask?.cancel()
        watcher?.stop()
        NotificationCenter.default.removeObserver(self)
    }
    override func validRequestor(forSendType sendType: NSPasteboard.PasteboardType?, returnType: NSPasteboard.PasteboardType?) -> Any? {
        servicesRequestor(forSendType: sendType, returnType: returnType) ?? super.validRequestor(forSendType: sendType, returnType: returnType)
    }
    override func acceptsPreviewPanelControl(_ panel: QLPreviewPanel!) -> Bool { true }
    override func beginPreviewPanelControl(_ panel: QLPreviewPanel!) { panel.dataSource = self; panel.delegate = self; quickLookURLs = selectedURLs }
    override func endPreviewPanelControl(_ panel: QLPreviewPanel!) { panel.dataSource = nil; panel.delegate = nil }

    override func loadView() {
        view = FileListBackgroundView(frame: NSRect(x: 0, y: 0, width: 640, height: 400))
        view.wantsLayer = true
        outline.owner = self
        outline.delegate = self
        outline.dataSource = self
        outline.allowsMultipleSelection = true
        outline.allowsEmptySelection = true
        outline.usesAlternatingRowBackgroundColors = true
        outline.columnAutoresizingStyle = .lastColumnOnlyAutoresizingStyle
        outline.autosaveTableColumns = true
        outline.autosaveName = "FileListColumns"
        outline.target = self
        outline.doubleAction = #selector(doubleClick(_:))
        for key in [SortKey.name, .modified, .size, .kind, .created, .added] {
            let column = NSTableColumn(identifier: NSUserInterfaceItemIdentifier(key.rawValue))
            column.title = key.title
            column.width = key == .name ? 290 : (key == .size ? 90 : 155)
            column.minWidth = key == .name ? 120 : 65
            column.isHidden = key == .created || key == .added
            column.sortDescriptorPrototype = NSSortDescriptor(key: key.rawValue, ascending: key == .name || key == .kind)
            outline.addTableColumn(column)
        }
        outline.outlineTableColumn = outline.tableColumns.first
        outline.headerView?.menu = makeColumnMenu()
        scroll.documentView = outline
        scroll.hasVerticalScroller = true
        scroll.hasHorizontalScroller = true
        scroll.borderType = .noBorder
        collection.owner = self
        collection.frame = view.bounds
        collection.autoresizingMask = [.width]
        collection.dataSource = self
        collection.delegate = self
        collection.isSelectable = true
        collection.allowsMultipleSelection = true
        collection.backgroundColors = [.controlBackgroundColor]
        collection.collectionViewLayout = NSCollectionViewFlowLayout()
        collection.register(FileIconItem.self, forItemWithIdentifier: FileIconItem.identifier)
        iconScroll.documentView = collection
        iconScroll.hasVerticalScroller = true
        iconScroll.borderType = .noBorder
        for s in [scroll, iconScroll] {
            // Automatic insets keep the first row clear of the outline's floating column header.
            s.automaticallyAdjustsContentInsets = true
            s.translatesAutoresizingMaskIntoConstraints = false
            view.addSubview(s)
            NSLayoutConstraint.activate([s.leadingAnchor.constraint(equalTo: view.leadingAnchor), s.trailingAnchor.constraint(equalTo: view.trailingAnchor), s.topAnchor.constraint(equalTo: view.topAnchor), s.bottomAnchor.constraint(equalTo: view.bottomAnchor)])
        }
        message.textColor = .secondaryLabelColor
        message.alignment = .center
        message.maximumNumberOfLines = 5
        privacy.target = self
        privacy.action = #selector(openPrivacy(_:))
        privacy.bezelStyle = .rounded
        stateStack.orientation = .vertical
        stateStack.spacing = Metrics.m
        stateStack.addArrangedSubview(message)
        stateStack.addArrangedSubview(privacy)
        stateStack.translatesAutoresizingMaskIntoConstraints = false
        view.addSubview(stateStack)
        NSLayoutConstraint.activate([stateStack.centerXAnchor.constraint(equalTo: view.centerXAnchor), stateStack.centerYAnchor.constraint(equalTo: view.centerYAnchor), stateStack.widthAnchor.constraint(lessThanOrEqualTo: view.widthAnchor, constant: -48)])
        outline.registerForDraggedTypes([.fileURL])
        outline.setDraggingSourceOperationMask([.copy, .move], forLocal: false)
        outline.setDraggingSourceOperationMask([.copy, .move], forLocal: true)
        collection.registerForDraggedTypes([.fileURL])
        collection.setDraggingSourceOperationMask([.copy, .move], forLocal: false)
        collection.setDraggingSourceOperationMask([.copy, .move], forLocal: true)
        NotificationCenter.default.addObserver(self, selector: #selector(settingsChanged(_:)), name: .settingsDidChange, object: nil)
        NotificationCenter.default.addObserver(self, selector: #selector(itemsProduced(_:)), name: .fileListItemsProduced, object: nil)
        NSApp.registerServicesMenuSendTypes([.fileURL, .URL, .string], returnTypes: [])
        updateAccessibility()
        updateAppearance()
        updateEmptyState()
    }

    func show(_ folder: URL, selecting: [URL] = []) {
        guard renameURL == nil else {
            inlineRenamePendingShow = (folder, selecting)
            inlineRenameNeedsRefresh = true
            return
        }
        loadViewIfNeeded()
        presentationGeneration += 1
        presentationTask?.cancel()
        let folder = folder.standardizedFileURL
        let changed = self.folder != folder
        self.folder = folder
        pendingSelection = selecting
        if let expected = renameAfterLoadURL, expected.deletingLastPathComponent().standardizedFileURL != folder { renameAfterLoadURL = nil }
        if changed {
            watcher?.stop()
            watcher = nil
            watchedFolders.removeAll()
            filterText = ""
            expanded.removeAll()
            recursivelyExpanding.removeAll()
            viewSettings = AppServices.shared.viewMemory.viewSettings(for: folder) ?? SettingsStore.shared.settings.defaultView
            updateAppearance()
        }
        if !changed, loadingTask != nil {
            // Explicit navigation/selection supersedes a scan that may predate the requested item.
            loadingTask?.cancel()
            loadingWorker?.cancel()
        }
        load(changedFolder: changed, revealSelection: !selecting.isEmpty)
    }

    private func load(changedFolder: Bool, revealSelection: Bool = false) {
        guard renameURL == nil else { inlineRenameNeedsRefresh = true; return }
        guard let folder else { return }
        if loadingTask != nil {
            guard changedFolder else { refreshPending = true; return }
            loadingTask?.cancel()
            loadingWorker?.cancel()
            refreshPending = false
        }
        generation += 1
        let token = generation
        for (url, task) in childLoadTasks { task.cancel(); nodes[url]?.isLoading = false }
        childLoadTasks.removeAll()
        // Background refreshes retain the loaded state and the visible content.
        if changedFolder || loadState != .loaded {
            loadState = .loading
            host?.fileListContentDidChange(self)
        }
        let hidden = SettingsStore.shared.settings.showHiddenFiles
        let expandedFolders = changedFolder ? [] : Array(expanded)
        let previousIdentities = itemIdentities
        loadingTask = Task { [weak self] in
            defer { self?.finishLoading(generation: token) }
            guard !Task.isCancelled else { return }
            let worker = Task.detached(priority: .userInitiated) { () -> Result<(FileListSnapshot, [URL: FileListSnapshot]), Error> in
                do {
                    let root = try FileListReader.read(folder, showHidden: hidden)
                    var children: [URL: FileListSnapshot] = [:]
                    var locations = Dictionary(root.identities.map { ($0.value, $0.key) }, uniquingKeysWith: { first, _ in first })
                    for url in expandedFolders.sorted(by: { $0.path.count < $1.path.count }) {
                        try Task.checkCancellation()
                        let actualURL = previousIdentities[url].flatMap { locations[$0] } ?? url
                        if let listing = try? FileListReader.read(actualURL, showHidden: hidden) {
                            children[actualURL] = listing
                            for (child, id) in listing.identities { locations[id] = child }
                        }
                    }
                    try Task.checkCancellation()
                    return .success((root, children))
                } catch { return .failure(error) }
            }
            self?.loadingWorker = worker
            let result = await withTaskCancellationHandler(operation: { await worker.value }, onCancel: { worker.cancel() })
            guard let self, !Task.isCancelled, token == self.generation else { return }
            guard self.renameURL == nil else { self.inlineRenameNeedsRefresh = true; return }
            switch result {
            case .success(let (snapshot, children)):
                self.snapshots = [folder: snapshot.items]
                self.directoryLocations = [folder: FileListDirectoryLocation(snapshot)]
                var identities = snapshot.identities
                for (url, listing) in children {
                    self.snapshots[url] = listing.items
                    self.directoryLocations[url] = FileListDirectoryLocation(listing)
                    identities.merge(listing.identities, uniquingKeysWith: { _, new in new })
                }
                let locations = Dictionary(identities.map { ($0.value, $0.key) }, uniquingKeysWith: { first, second in
                    previousIdentities[first] != nil && previousIdentities[second] == nil ? second : first
                })
                func newLocation(_ url: URL) -> URL {
                    identities[url] != nil ? url : previousIdentities[url].flatMap { locations[$0] } ?? url
                }
                self.selectionRemapping = previousIdentities.keys.reduce(into: [:]) { remapping, url in
                    let mapped = newLocation(url)
                    if mapped != url { remapping[url] = mapped }
                }
                self.expanded = Set(self.expanded.map(newLocation))
                self.itemIdentities = identities
                self.folderFileBytes = snapshot.fileBytes
                self.loadState = .loaded
                self.rebuildPresentation(resetScroll: changedFolder, revealSelection: revealSelection || self.pendingSelection?.isEmpty == false)
                if let presentation = self.presentationTask { await presentation.value }
                guard token == self.generation else { return }
                self.installWatcher()
            case .failure(let error):
                if error is CancellationError { return }
                self.loadState = .failed(FileListReader.failureMessage(error, folder: folder))
                self.roots.removeAll()
                self.nodes.removeAll()
                self.snapshots.removeAll()
                self.directoryLocations.removeAll()
                self.itemIdentities.removeAll()
                self.pendingSelection = nil
                self.selectionRemapping.removeAll()
                self.folderFileBytes = 0
                self.renameAfterLoadURL = nil
                self.outline.reloadData()
                self.collection.reloadData()
                self.applySelection([])
                self.updateEmptyState()
                self.host?.fileListContentDidChange(self)
            }
        }
    }

    private func finishLoading(generation token: Int) {
        guard token == generation else { return }
        loadingTask = nil
        loadingWorker = nil
        guard refreshPending else { return }
        refreshPending = false
        if renameURL != nil { inlineRenameNeedsRefresh = true }
        else { reloadPreservingState() }
    }

    func reloadPreservingState() { load(changedFolder: false) }

    private func rebuildPresentation(resetScroll: Bool = false, revealSelection: Bool = false) {
        guard renameURL == nil else { inlineRenameNeedsRefresh = true; return }
        guard let folder, snapshots[folder] != nil else { return }
        presentationGeneration += 1
        let token = presentationGeneration
        presentationTask?.cancel()
        let source = snapshots
        let filter = filterText
        let settings = viewSettings
        let foldersFirst = SettingsStore.shared.settings.foldersFirst
        presentationTask = Task { [weak self] in
            let arranged = await Task.detached(priority: .userInitiated) {
                source.mapValues { FileListReader.arrange($0, filter: filter, settings: settings, foldersFirst: foldersFirst) }
            }.value
            guard let self, !Task.isCancelled, token == self.presentationGeneration else { return }
            guard self.renameURL == nil else { self.inlineRenameNeedsRefresh = true; return }
            let scrollView = self.isIconView ? self.iconScroll : self.scroll
            let origin = scrollView.contentView.bounds.origin
            var nextNodes: [URL: FileNode] = [:]
            var ancestorPaths: Set<String> = [folder.standardizedFileURL.path]
            var ancestorIdentities = Set<String>()
            if let location = self.directoryLocations[folder] {
                ancestorPaths.insert(location.path)
                if let identity = location.identity { ancestorIdentities.insert(identity) }
            }
            @MainActor func makeNodes(_ items: [FileItem], parent: FileNode?, paths: Set<String>, identities: Set<String>) -> [FileNode] {
                items.map { item in
                    let node = nextNodes[item.url] == nil ? (self.nodes[item.url] ?? FileNode(item)) : FileNode(item)
                    node.item = item
                    node.parent = parent
                    if nextNodes[item.url] == nil { nextNodes[item.url] = node }
                    let path = item.url.standardizedFileURL.path
                    let identity = self.itemIdentities[item.url]
                    let location = self.directoryLocations[item.url]
                    let repeatsPath = paths.contains(path) || location.map { paths.contains($0.path) } == true
                    let repeatsIdentity = identity.map { identities.contains($0) } == true
                        || location?.identity.map { identities.contains($0) } == true
                    node.isExpansionBlocked = item.isDirectory && (repeatsPath || repeatsIdentity)
                    if node.isExpansionBlocked {
                        node.children = []
                        self.expanded.remove(item.url)
                    } else if let children = arranged[item.url] {
                        var childPaths = paths
                        var childIdentities = identities
                        childPaths.insert(path)
                        if let identity { childIdentities.insert(identity) }
                        if let location {
                            childPaths.insert(location.path)
                            if let identity = location.identity { childIdentities.insert(identity) }
                        }
                        node.children = makeNodes(children, parent: node, paths: childPaths, identities: childIdentities)
                    } else { node.children = nil }
                    return node
                }
            }
            let nextRoots = makeNodes(arranged[folder] ?? [], parent: nil, paths: ancestorPaths, identities: ancestorIdentities)
            let oldRoots = self.roots
            self.nodes = nextNodes
            self.applyingSelection = true
            self.reconcileRoots(old: oldRoots, next: nextRoots)
            self.collection.reloadData()
            for url in self.expanded { if let node = self.nodes[url] { self.outline.expandItem(node) } }
            self.outline.reloadData()
            if resetScroll {
                // Content insets (the floating column header) aren't settled on a fresh list,
                // so scroll by row geometry instead of to a computed origin.
                scrollView.layoutSubtreeIfNeeded()
                if self.isIconView {
                    self.collection.scroll(.zero)
                } else {
                    scrollView.contentView.scroll(to: NSPoint(x: -scrollView.contentInsets.left, y: -scrollView.contentInsets.top))
                    if self.outline.numberOfRows > 0 { self.outline.scrollRowToVisible(0) }
                }
            } else {
                scrollView.contentView.scroll(to: origin)
            }
            scrollView.reflectScrolledClipView(scrollView.contentView)
            self.applyingSelection = false
            let chosen = (self.pendingSelection ?? self.selectedURLs).map { self.selectionRemapping[$0] ?? $0 }
            self.pendingSelection = nil
            self.selectionRemapping.removeAll()
            self.applySelection(chosen)
            if revealSelection, !chosen.isEmpty { self.scrollSelectionToVisible() }
            self.updateEmptyState()
            self.host?.fileListContentDidChange(self)
            if let expected = self.renameAfterLoadURL {
                let presented = self.nodes.keys.contains { $0.standardizedFileURL == expected.standardizedFileURL }
                if presented || !self.refreshPending {
                    self.renameAfterLoadURL = nil
                    if self.selectedURLs.count == 1, self.selectedURLs[0].standardizedFileURL == expected.standardizedFileURL {
                        self.beginInlineRename(expectedURL: expected)
                    }
                }
            }
        }
    }
    /// Run after the field-editor callback unwinds; a new edit keeps the pending work deferred.
    func finishDeferredInlineRenameRefresh(selecting renamed: URL? = nil) {
        if let renamed {
            pendingSelection = [renamed]
            inlineRenameNeedsRefresh = true
        }
        Task { @MainActor [weak self] in
            guard let self, self.renameURL == nil else { return }
            let navigation = self.inlineRenamePendingShow
            let refresh = self.inlineRenameNeedsRefresh
            let appearance = self.inlineRenameNeedsAppearance
            self.inlineRenamePendingShow = nil
            self.inlineRenameNeedsRefresh = false
            self.inlineRenameNeedsAppearance = false
            if appearance { self.updateAppearance() }
            if let navigation {
                self.show(navigation.folder, selecting: navigation.selection)
            } else {
                if refresh { self.reloadPreservingState() }
                else { self.refreshSelectionAppearance() }
            }
        }
    }

    private func reconcileRoots(old: [FileNode], next: [FileNode]) {
        let nextURLs = Set(next.map { $0.item.url })
        let oldURLs = Set(old.map { $0.item.url })
        let oldSurvivors = old.lazy.filter { nextURLs.contains($0.item.url) }.map { $0.item.url }
        let nextSurvivors = next.lazy.filter { oldURLs.contains($0.item.url) }.map { $0.item.url }
        let changedCount = nextURLs.symmetricDifference(oldURLs).count
        // Re-sorts and bulk replacements must remain linear rather than moving 50,000 rows individually.
        if old.isEmpty || next.isEmpty || changedCount > 128 || !oldSurvivors.elementsEqual(nextSurvivors) {
            roots = next
            outline.reloadData()
            return
        }
        // AppKit updates only changed root rows. Child contents are refreshed while the expansion set is retained.
        outline.beginUpdates()
        roots = old
        for index in roots.indices.reversed() where !nextURLs.contains(roots[index].item.url) {
            roots.remove(at: index)
            outline.removeItems(at: IndexSet(integer: index), inParent: nil, withAnimation: Motion.reduceMotion ? [] : .effectFade)
        }
        for (index, node) in next.enumerated() {
            if !oldURLs.contains(node.item.url) {
                roots.insert(node, at: index)
                outline.insertItems(at: IndexSet(integer: index), inParent: nil, withAnimation: Motion.reduceMotion ? [] : .effectFade)
            }
        }
        roots = next
        outline.endUpdates()
    }

    func select(_ urls: [URL]) {
        pendingSelection = urls
        applySelection(urls)
    }

    private func applySelection(_ urls: [URL]) {
        let wanted = Set(urls.map(\.standardizedFileURL))
        applyingSelection = true
        outline.selectRowIndexes(IndexSet((0..<outline.numberOfRows).filter { row in
            guard let node = outline.item(atRow: row) as? FileNode else { return false }
            return wanted.contains(node.item.url.standardizedFileURL)
        }), byExtendingSelection: false)
        collection.selectionIndexPaths = Set(roots.enumerated().compactMap { wanted.contains($0.element.item.url.standardizedFileURL) ? IndexPath(item: $0.offset, section: 0) : nil })
        applyingSelection = false
        recordSelectionChange(clearPendingSelection: false)
    }

    func selectionDidChange() {
        recordSelectionChange(clearPendingSelection: true)
    }

    private func recordSelectionChange(clearPendingSelection: Bool) {
        guard !applyingSelection else { return }
        if clearPendingSelection { pendingSelection = nil }
        let chosen = isIconView ? collection.selectionIndexPaths.sorted().compactMap { roots.indices.contains($0.item) ? roots[$0.item].item.url : nil } : outline.selectedRowIndexes.compactMap { (outline.item(atRow: $0) as? FileNode)?.item.url }
        var seen = Set<URL>()
        selectedURLs = chosen.filter { seen.insert($0).inserted }
        refreshSelectionAppearance()
        host?.fileListSelectionDidChange(self)
        if QLPreviewPanel.sharedPreviewPanelExists(), QLPreviewPanel.shared()?.isVisible == true {
            quickLookURLs = selectedURLs
            QLPreviewPanel.shared()?.reloadData()
        }
    }

    func focus() { loadViewIfNeeded(); view.window?.makeFirstResponder(isIconView ? collection : outline); activate() }
    func activate() { host?.fileListDidActivate(self); refreshSelectionAppearance() }
    func refreshSelectionAppearance() {
        outline.needsDisplay = true
        outline.enumerateAvailableRowViews { rowView, row in
            let emphasized = self.isActive && self.outline.window?.firstResponder === self.outline && self.outline.isRowSelected(row)
            for cell in rowView.subviews.compactMap({ $0 as? NSTableCellView }) {
                cell.textField?.textColor = emphasized ? .alternateSelectedControlTextColor : (cell is FileNameCell ? .labelColor : .secondaryLabelColor)
            }
        }
        collection.visibleItems().forEach { ($0 as? FileIconItem)?.updateSelection() }
    }
    func styleCell(_ cell: NSTableCellView, for node: FileNode) {
        let row = outline.row(forItem: node)
        let emphasized = isActive && outline.window?.firstResponder === outline && row >= 0 && outline.isRowSelected(row)
        cell.textField?.textColor = emphasized ? .alternateSelectedControlTextColor : (cell is FileNameCell ? .labelColor : .secondaryLabelColor)
    }
    func scrollSelectionToVisible() {
        if isIconView, let path = collection.selectionIndexPaths.sorted().first { collection.scrollToItems(at: [path], scrollPosition: [.nearestHorizontalEdge, .nearestVerticalEdge]) }
        else if outline.selectedRow >= 0 { outline.scrollRowToVisible(outline.selectedRow) }
    }

    func applyViewSettings(_ settings: FolderViewSettings) {
        viewSettings = settings
        if let folder { AppServices.shared.viewMemory.setViewSettings(settings, for: folder) }
        updateAppearance()
        rebuildPresentation()
    }
    override func viewDidLayout() {
        super.viewDidLayout()
        let width = max(1, iconScroll.contentSize.width)
        if collection.frame.width != width { collection.setFrameSize(NSSize(width: width, height: max(collection.frame.height, iconScroll.contentSize.height))) }
        if let layout = collection.collectionViewLayout as? NSCollectionViewFlowLayout {
            let edge = CGFloat(viewSettings.iconSize)
            let itemWidth = min(max(100, edge + 32), max(1, width - 2 * Metrics.l))
            if layout.itemSize.width != itemWidth { layout.itemSize = NSSize(width: itemWidth, height: edge + 52) }
        }
    }

    private func updateAppearance() {
        guard isViewLoaded else { return }
        guard renameURL == nil else {
            inlineRenameNeedsAppearance = true
            inlineRenameNeedsRefresh = true
            return
        }
        let settings = SettingsStore.shared.settings
        outline.rowHeight = Metrics.rowHeight(settings.textSize, settings.rowDensity)
        let descriptor = NSSortDescriptor(key: viewSettings.sortKey.rawValue, ascending: viewSettings.ascending)
        if outline.sortDescriptors != [descriptor] { outline.sortDescriptors = [descriptor] }
        scroll.isHidden = isIconView
        iconScroll.isHidden = !isIconView
        if let layout = collection.collectionViewLayout as? NSCollectionViewFlowLayout {
            let edge = CGFloat(viewSettings.iconSize)
            layout.itemSize = NSSize(width: min(max(100, edge + 32), max(1, collection.frame.width - 2 * Metrics.l)), height: edge + 52)
            layout.minimumInteritemSpacing = Metrics.s
            layout.minimumLineSpacing = Metrics.m
            layout.sectionInset = NSEdgeInsets(top: Metrics.l, left: Metrics.l, bottom: Metrics.l, right: Metrics.l)
        }
        for column in outline.tableColumns { if let key = SortKey(rawValue: column.identifier.rawValue) { column.title = key.title } }
        outline.needsDisplay = true
        let selection = selectedURLs
        applyingSelection = true
        collection.reloadData()
        applyingSelection = false
        applySelection(selection)
    }

    private func updateAccessibility() {
        guard isViewLoaded else { return }
        outline.setAccessibilityIdentifier(AccessibilityID.fileList(paneNumber))
        collection.setAccessibilityIdentifier(AccessibilityID.fileList(paneNumber))
        outline.setAccessibilityLabel(tr("Files", "파일"))
        collection.setAccessibilityLabel(tr("Files", "파일"))
    }

    private func updateEmptyState() {
        guard isViewLoaded else { return }
        privacy.isHidden = true
        scroll.isHidden = isIconView
        iconScroll.isHidden = !isIconView
        switch loadState {
        case .failed(let reason):
            scroll.isHidden = true
            iconScroll.isHidden = true
            message.stringValue = reason
            privacy.isHidden = !(reason.contains(tr("Privacy", "개인정보 보호")))
            stateStack.isHidden = false
        case .loaded where roots.isEmpty:
            message.stringValue = filterText.isEmpty ? tr("This folder is empty", "폴더가 비어 있음") : tr("No matching items", "일치하는 항목 없음")
            stateStack.isHidden = false
        default: stateStack.isHidden = true
        }
    }

    @objc private func openPrivacy(_ sender: Any?) {
        if let url = URL(string: "x-apple.systempreferences:com.apple.preference.security?Privacy_AllFiles") { NSWorkspace.shared.open(url) }
    }
    @objc private func settingsChanged(_ note: Notification) {
        let settings = SettingsStore.shared.settings
        let reload = lastSettings.showHiddenFiles != settings.showHiddenFiles
        if settings.defaultView.iconSize != lastSettings.defaultView.iconSize {
            viewSettings.iconSize = settings.defaultView.iconSize
        }
        lastSettings = settings
        updateAppearance()
        if reload { reloadPreservingState() } else { rebuildPresentation() }
    }
    @objc private func itemsProduced(_ notification: Notification) {
        guard let target = notification.userInfo?["folder"] as? URL, target.standardizedFileURL == folder?.standardizedFileURL,
              let items = notification.userInfo?["items"] as? [URL], !items.isEmpty else { return }
        show(target, selecting: items)
    }
    func presentError(_ message: String) {
        let alert = NSAlert()
        alert.messageText = tr("The operation couldn’t be completed", "작업을 완료할 수 없습니다")
        alert.informativeText = message
        if let window = view.window { alert.beginSheetModal(for: window) } else { alert.runModal() }
    }
    private func installWatcher() {
        guard let folder else { return }
        let folders = Set(([folder] + Array(expanded)).map(\.standardizedFileURL))
        guard folders != watchedFolders else { return }
        watcher?.stop()
        watchedFolders = folders
        watcher = FileListWatcher(folders: Array(folders)) { [weak self] in
            Task { @MainActor in self?.reloadPreservingState() }
        }
    }

    func loadChildren(_ node: FileNode, recursive: Bool = false) {
        guard renameURL == nil else { inlineRenameNeedsRefresh = true; return }
        guard !node.isExpansionBlocked else { return }
        let url = node.item.url
        if recursive { recursivelyExpanding.insert(url) }
        if recursive, let children = node.children {
            for child in children where child.item.isDirectory && !child.isExpansionBlocked && !child.item.isSymbolicLink && !child.item.isAlias {
                loadChildren(child, recursive: true)
                outline.expandItem(child)
            }
        }
        guard node.children == nil, !node.isLoading else { return }
        node.isLoading = true
        let token = generation
        let hidden = SettingsStore.shared.settings.showHiddenFiles
        let settings = viewSettings
        let foldersFirst = SettingsStore.shared.settings.foldersFirst
        let filter = filterText
        var ancestorPaths = Set<String>()
        var ancestorIdentities = Set<String>()
        if let folder {
            ancestorPaths.insert(folder.standardizedFileURL.path)
            if let location = directoryLocations[folder] {
                ancestorPaths.insert(location.path)
                if let identity = location.identity { ancestorIdentities.insert(identity) }
            }
        }
        var ancestor = node.parent
        while let parent = ancestor {
            ancestorPaths.insert(parent.item.url.standardizedFileURL.path)
            if let identity = itemIdentities[parent.item.url] { ancestorIdentities.insert(identity) }
            if let location = directoryLocations[parent.item.url] {
                ancestorPaths.insert(location.path)
                if let identity = location.identity { ancestorIdentities.insert(identity) }
            }
            ancestor = parent.parent
        }
        let paths = ancestorPaths
        let identities = ancestorIdentities
        childLoadTasks[url] = Task { [weak self, weak node] in
            defer {
                if let self, self.generation == token { self.childLoadTasks.removeValue(forKey: url) }
            }
            guard !Task.isCancelled else { return }
            let worker = Task.detached(priority: .userInitiated) { () -> Result<(source: FileListSnapshot?, displayed: [FileItem]), Error> in
                do {
                    let location = try FileListReader.directoryLocation(url)
                    if paths.contains(location.url.standardizedFileURL.path)
                        || location.identity.map({ identities.contains($0) }) == true {
                        return .success((nil, []))
                    }
                    let source = try FileListReader.read(url, showHidden: hidden)
                    return .success((source, FileListReader.arrange(source.items, filter: filter, settings: settings, foldersFirst: foldersFirst)))
                } catch { return .failure(error) }
            }
            let result = await withTaskCancellationHandler(operation: { await worker.value }, onCancel: { worker.cancel() })
            guard let self, let node, !Task.isCancelled, token == self.generation else { return }
            node.isLoading = false
            guard self.renameURL == nil else { self.inlineRenameNeedsRefresh = true; return }
            switch result {
            case .success(let listing):
                guard let source = listing.source else {
                    node.isExpansionBlocked = true
                    node.children = []
                    self.expanded.remove(url)
                    self.recursivelyExpanding.remove(url)
                    self.outline.collapseItem(node)
                    self.outline.reloadItem(node)
                    self.installWatcher()
                    return
                }
                self.snapshots[url] = source.items
                self.directoryLocations[url] = FileListDirectoryLocation(source)
                self.itemIdentities.merge(source.identities, uniquingKeysWith: { _, new in new })
                node.children = listing.displayed.map {
                    let child = FileNode($0)
                    child.parent = node
                    if self.nodes[$0.url] == nil { self.nodes[$0.url] = child }
                    return child
                }
                self.outline.reloadItem(node, reloadChildren: true)
                self.outline.expandItem(node)
                self.expanded.insert(url)
                self.installWatcher()
                if self.recursivelyExpanding.contains(url) {
                    for child in node.children ?? [] where child.item.isDirectory && !child.item.isSymbolicLink && !child.item.isAlias { self.loadChildren(child, recursive: true) }
                }
            case .failure(let error):
                if !(error is CancellationError) { self.presentError(FileListReader.failureMessage(error, folder: url)) }
            }
        }
    }
    func rememberExpansion(_ node: FileNode, expanded shouldExpand: Bool) {
        guard !shouldExpand || !node.isExpansionBlocked else { return }
        if shouldExpand { expanded.insert(node.item.url); loadChildren(node) }
        else { expanded.remove(node.item.url); recursivelyExpanding.remove(node.item.url) }
        installWatcher()
    }
    func makeColumnMenu() -> NSMenu {
        let menu = NSMenu()
        for column in outline.tableColumns where column.identifier.rawValue != SortKey.name.rawValue {
            let item = NSMenuItem(title: column.title, action: #selector(toggleColumn(_:)), keyEquivalent: "")
            item.target = self
            item.representedObject = column.identifier.rawValue
            item.state = column.isHidden ? .off : .on
            menu.addItem(item)
        }
        return menu
    }
    @objc func toggleColumn(_ sender: NSMenuItem) {
        guard let id = sender.representedObject as? String, let column = outline.tableColumns.first(where: { $0.identifier.rawValue == id }) else { return }
        column.isHidden.toggle()
        sender.state = column.isHidden ? .off : .on
    }
}

final class FileNode: NSObject {
    var item: FileItem
    var children: [FileNode]?
    weak var parent: FileNode?
    var isExpansionBlocked = false
    var isLoading = false
    init(_ item: FileItem) { self.item = item }
}

private struct FileListDirectoryLocation {
    let path: String
    let identity: String?
    init(_ snapshot: FileListSnapshot) {
        path = snapshot.directory.standardizedFileURL.path
        identity = snapshot.directoryIdentity
    }
}
