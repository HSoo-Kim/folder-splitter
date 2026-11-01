import AppKit
import Quartz

extension FileListViewController {
    private static let favoriteDragType = NSPasteboard.PasteboardType("io.github.hsoo-kim.FolderSplitter.favorite")

    @objc func doubleClick(_ sender: Any?) {
        guard outline.clickedRow >= 0 else { return }
        openItems(selectedURLs, disposition: NSApp.currentEvent?.modifierFlags.contains(.command) == true ? .newTab(activate: true) : .current)
    }
    func openItems(_ urls: [URL], disposition: OpenDisposition) {
        let items = urls.compactMap { nodes[$0]?.item }
        Task { [weak self] in
            guard let self else { return }
            for item in items {
                if let target = await FileListReader.resolvedFolder(item) { self.host?.open(target, disposition: disposition) }
                else if !NSWorkspace.shared.open(item.url) {
                    self.presentError(tr("“\(item.name)” couldn’t be opened. Choose an application using Open With.", "“\(item.name)”을(를) 열 수 없습니다. ‘다음으로 열기’에서 응용 프로그램을 선택하십시오."))
                }
            }
        }
    }
    func handleKey(_ event: NSEvent) -> Bool {
        guard !isTextEditing else { return false }
        let flags = event.modifierFlags.intersection(.deviceIndependentFlagsMask)
        let command = flags.contains(.command)
        let option = flags.contains(.option)
        let characters = event.charactersIgnoringModifiers ?? ""
        if event.keyCode == 96 { refresh(nil); return true }
        if event.keyCode == 53 { if !filterText.isEmpty { filterText = ""; return true }; return false }
        if command {
            switch characters.lowercased() {
            case "o": if flags.contains(.control) { openSelectionInNewTab(nil) } else { openSelection(nil) }; return true
            case "y": toggleQuickLook(nil); return true
            case "c": if option { copyPath(nil) } else { copy(nil) }; return true
            case "x": cut(nil); return true
            case "v": if option { pasteAsMove(nil) } else { paste(nil) }; return true
            case "d": duplicateSelection(nil); return true
            case "a": if option { deselectAll(nil) } else { selectAll(nil) }; return true
            default: break
            }
            if event.keyCode == 51 { moveToTrash(nil); return true }
            if event.keyCode == 125 { openSelection(nil); return true }
        }
        if !command, event.keyCode == 36 || event.keyCode == 76 { renameSelection(nil); return true }
        if !command, characters == " " { toggleQuickLook(nil); return true }
        if !isIconView, !command, event.keyCode == 124 || event.keyCode == 123 {
            let expanding = event.keyCode == 124
            for row in outline.selectedRowIndexes {
                guard let node = outline.item(atRow: row) as? FileNode else { continue }
                if expanding, node.item.isDirectory {
                    if option { loadChildren(node, recursive: true); outline.expandItem(node, expandChildren: true) }
                    else { outline.expandItem(node) }
                } else if !expanding {
                    let wasExpanded = outline.isItemExpanded(node)
                    outline.collapseItem(node, collapseChildren: option)
                    if !wasExpanded, let parent = outline.parent(forItem: node) as? FileNode { select([parent.item.url]) }
                }
            }
            return true
        }
        if isIconView, !command, !flags.contains(.control), characters.unicodeScalars.allSatisfy({ !CharacterSet.controlCharacters.contains($0) }), !characters.isEmpty {
            if event.timestamp - typeSelectionTime > 1 { typeSelectionText = "" }
            typeSelectionTime = event.timestamp
            typeSelectionText += characters.precomposedStringWithCanonicalMapping
            let query = typeSelectionText
            let items = roots.map(\.item)
            Task { [weak self] in
                let match = await Task.detached(priority: .userInitiated) { items.first { $0.name.range(of: query, options: [.anchored, .caseInsensitive]) != nil }?.url }.value
                guard let self, self.typeSelectionText == query, let match else { return }
                self.select([match])
                self.scrollSelectionToVisible()
            }
            return true
        }
        return false
    }
    func togglePreviewPanel() {
        guard !selectedURLs.isEmpty else { return }
        if let panel = QLPreviewPanel.shared(), panel.isVisible { panel.orderOut(nil) }
        else {
            quickLookURLs = selectedURLs
            let panel = QLPreviewPanel.shared()
            panel?.dataSource = self
            panel?.delegate = self
            panel?.makeKeyAndOrderFront(nil)
        }
    }
    func iconFrameOnScreen(for url: URL) -> NSRect {
        guard let window = view.window else { return .zero }
        if isIconView, let index = roots.firstIndex(where: { $0.item.url == url }), let item = collection.item(at: IndexPath(item: index, section: 0)) as? FileIconItem {
            return window.convertToScreen(item.icon.convert(item.icon.bounds, to: nil))
        }
        guard let node = nodes[url] else { return .zero }
        let row = outline.row(forItem: node)
        if row >= 0, let cell = outline.view(atColumn: 0, row: row, makeIfNecessary: false) as? FileNameCell { return window.convertToScreen(cell.icon.convert(cell.icon.bounds, to: nil)) }
        return .zero
    }

    func dragOperation(_ info: NSDraggingInfo, target: URL?) -> NSDragOperation {
        guard info.draggingPasteboard.availableType(from: [Self.favoriteDragType]) == nil,
              let target,
              let urls = info.draggingPasteboard.readObjects(forClasses: [NSURL.self], options: [.urlReadingFileURLsOnly: true]) as? [URL],
              !urls.isEmpty else { cancelSpringLoad(); return [] }
        let targetPath = target.standardizedFileURL.path
        guard !urls.contains(where: { $0.standardizedFileURL == target.standardizedFileURL || $0.deletingLastPathComponent().standardizedFileURL == target.standardizedFileURL || targetPath.hasPrefix($0.standardizedFileURL.path + "/") }) else { cancelSpringLoad(); return [] }
        guard let mode = FileDrop.mode(for: urls, into: target, operation: info.draggingSourceOperationMask) else { cancelSpringLoad(); return [] }
        updateSpringLoad(target)
        return mode == .copy ? .copy : .move
    }
    func performDrop(_ info: NSDraggingInfo, target: URL?) -> Bool {
        guard info.draggingPasteboard.availableType(from: [Self.favoriteDragType]) == nil,
              let target,
              let urls = info.draggingPasteboard.readObjects(forClasses: [NSURL.self], options: [.urlReadingFileURLsOnly: true]) as? [URL],
              !urls.isEmpty else { cancelSpringLoad(); return false }
        let operation = info.draggingSourceOperationMask
        let validatedOperation = dragOperation(info, target: target)
        cancelSpringLoad()
        guard !validatedOperation.isEmpty else { return false }
        let targetItem = nodes[target]?.item
        Task { [weak self] in
            let destination: URL
            if let item = targetItem, item.isAlias || item.isSymbolicLink {
                guard let resolved = await FileListReader.resolvedFolder(item) else { return }
                destination = resolved
            } else { destination = target }
            guard let mode = FileDrop.mode(for: urls, into: destination, operation: operation) else { return }
            let result = await AppServices.shared.fileOperations.transfer(urls, to: destination, mode: mode)
            guard let self else { return }
            if target == self.folder { self.show(target, selecting: result.produced) }
            else if let current = self.folder {
                if let node = self.nodes[target] { self.outline.expandItem(node); self.rememberExpansion(node, expanded: true) }
                self.show(current, selecting: result.produced)
            }
            NotificationCenter.default.post(name: .fileListItemsProduced, object: self, userInfo: ["folder": destination, "items": result.produced])
        }
        return true
    }
    func updateSpringLoad(_ target: URL) {
        guard target != folder else { cancelSpringLoad(); return }
        guard springURL != target else { return }
        cancelSpringLoad()
        springURL = target
        dragTarget = target
        outline.needsDisplay = true
        collection.visibleItems().forEach { ($0 as? FileIconItem)?.updateSelection() }
        springTimer = Timer.scheduledTimer(withTimeInterval: 0.6, repeats: false) { [weak self] _ in
            Task { @MainActor in
                guard let self, self.springURL == target else { return }
                if !Motion.reduceMotion, let node = self.nodes[target] {
                    let row = self.outline.row(forItem: node)
                    if row >= 0, let rowView = self.outline.rowView(atRow: row, makeIfNecessary: false) {
                        Motion.animate(response: Motion.quickResponse, { rowView.animator().alphaValue = 0.5 }, completion: { rowView.alphaValue = 1 })
                    }
                }
                self.openItems([target], disposition: .current)
                self.springTimer = nil
            }
        }
    }
    func cancelSpringLoad() {
        springTimer?.invalidate()
        springTimer = nil
        springURL = nil
        dragTarget = nil
        outline.needsDisplay = true
        collection.visibleItems().forEach { ($0 as? FileIconItem)?.updateSelection() }
    }
}

extension FileListViewController: @preconcurrency QLPreviewPanelDataSource, QLPreviewPanelDelegate {
    func numberOfPreviewItems(in panel: QLPreviewPanel!) -> Int { quickLookURLs.count }
    func previewPanel(_ panel: QLPreviewPanel!, previewItemAt index: Int) -> QLPreviewItem! { quickLookURLs[index] as NSURL }
    nonisolated func previewPanel(_ panel: QLPreviewPanel!, sourceFrameOnScreenFor item: QLPreviewItem!) -> NSRect {
        MainActor.assumeIsolated { guard let url = item.previewItemURL else { return .zero }; return iconFrameOnScreen(for: url) }
    }
    nonisolated func previewPanel(_ panel: QLPreviewPanel!, transitionImageFor item: QLPreviewItem!, contentRect: UnsafeMutablePointer<NSRect>!) -> Any! {
        MainActor.assumeIsolated {
            guard let url = item.previewItemURL, let node = nodes[url] else { return nil }
            return FileListIconCache.shared.image(for: node.item, size: CGFloat(viewSettings.iconSize), thumbnail: isIconView) { _ in }
        }
    }
    nonisolated func previewPanel(_ panel: QLPreviewPanel!, handle event: NSEvent!) -> Bool {
        MainActor.assumeIsolated { handlePreviewKey(panel, event: event) }
    }
    private func handlePreviewKey(_ panel: QLPreviewPanel, event: NSEvent) -> Bool {
        guard event.type == .keyDown else { return false }
        if [123, 124, 125, 126].contains(event.keyCode) {
            let items = allDisplayedItems
            guard !items.isEmpty else { return true }
            let current = items.firstIndex(where: { $0.url == selectedURLs.first }) ?? 0
            let next = min(max(0, current + ([123, 126].contains(event.keyCode) ? -1 : 1)), items.count - 1)
            select([items[next].url])
            scrollSelectionToVisible()
            return true
        }
        if event.charactersIgnoringModifiers == " " { panel.orderOut(nil); return true }
        return handleKey(event)
    }
}

extension FileListViewController {
    func outlineView(_ outlineView: NSOutlineView, pasteboardWriterForItem item: Any) -> NSPasteboardWriting? {
        guard let node = item as? FileNode else { return nil }
        return node.item.url as NSURL
    }
    func outlineView(_ outlineView: NSOutlineView, draggingSession session: NSDraggingSession, willBeginAt screenPoint: NSPoint, forItems draggedItems: [Any]) { cancelSpringLoad() }
    func outlineView(_ outlineView: NSOutlineView, draggingSession session: NSDraggingSession, endedAt screenPoint: NSPoint, operation: NSDragOperation) { cancelSpringLoad() }
    func outlineView(_ outlineView: NSOutlineView, validateDrop info: NSDraggingInfo, proposedItem item: Any?, proposedChildIndex index: Int) -> NSDragOperation {
        let node = item as? FileNode
        if let node, !node.item.isDirectory { outlineView.setDropItem(nil, dropChildIndex: NSOutlineViewDropOnItemIndex); return dragOperation(info, target: folder) }
        outlineView.setDropItem(node, dropChildIndex: NSOutlineViewDropOnItemIndex)
        return dragOperation(info, target: node?.item.url ?? folder)
    }
    func outlineView(_ outlineView: NSOutlineView, acceptDrop info: NSDraggingInfo, item: Any?, childIndex index: Int) -> Bool { performDrop(info, target: (item as? FileNode)?.item.url ?? folder) }
    func collectionView(_ collectionView: NSCollectionView, pasteboardWriterForItemAt indexPath: IndexPath) -> NSPasteboardWriting? {
        let url = roots[indexPath.item].item.url
        return url as NSURL
    }
    func collectionView(_ collectionView: NSCollectionView, validateDrop draggingInfo: NSDraggingInfo, proposedIndexPath proposedDropIndexPath: AutoreleasingUnsafeMutablePointer<NSIndexPath>, dropOperation proposedDropOperation: UnsafeMutablePointer<NSCollectionView.DropOperation>) -> NSDragOperation {
        let path = proposedDropIndexPath.pointee as IndexPath
        let target = roots.indices.contains(path.item) && roots[path.item].item.isDirectory ? roots[path.item].item.url : folder
        proposedDropOperation.pointee = target == folder ? .before : .on
        return dragOperation(draggingInfo, target: target)
    }
    func collectionView(_ collectionView: NSCollectionView, acceptDrop draggingInfo: NSDraggingInfo, indexPath: IndexPath, dropOperation: NSCollectionView.DropOperation) -> Bool {
        let target = dropOperation == .on && roots.indices.contains(indexPath.item) && roots[indexPath.item].item.isDirectory ? roots[indexPath.item].item.url : folder
        return performDrop(draggingInfo, target: target)
    }
    func collectionView(_ collectionView: NSCollectionView, draggingSession session: NSDraggingSession, endedAt screenPoint: NSPoint, dragOperation operation: NSDragOperation) { cancelSpringLoad() }
}

