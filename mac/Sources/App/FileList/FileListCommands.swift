import AppKit

extension Notification.Name {
    static let fileListItemsProduced = Notification.Name("FolderSplitter.fileListItemsProduced")
}

private enum FileListClipboard {
    static let cutType = NSPasteboard.PasteboardType.folderSplitterCut

    @MainActor static var urls: [URL] {
        (NSPasteboard.general.readObjects(forClasses: [NSURL.self], options: [.urlReadingFileURLsOnly: true]) as? [URL]) ?? []
    }
}

extension FileListViewController: FileCommands, NSMenuItemValidation, @preconcurrency NSServicesMenuRequestor {
    // A field editor owns standard editing commands; list actions must not touch its files.
    var isTextEditing: Bool { view.window?.firstResponder is NSText }

    @objc func openSelection(_ sender: Any?) { openItems(selectedURLs, disposition: .current) }
    @objc func openSelectionInNewTab(_ sender: Any?) { openItems(selectedURLs, disposition: .newTab(activate: true)) }
    @objc func openSelectionInOtherPane(_ sender: Any?) { openItems(selectedURLs, disposition: .otherPane) }
    @objc func toggleQuickLook(_ sender: Any?) { togglePreviewPanel() }

    @objc func getInfo(_ sender: Any?) {
        let urls = selectedURLs.isEmpty ? folder.map { [$0] } ?? [] : selectedURLs
        if !urls.isEmpty { UIHooks.showInfo?(urls) }
    }

    @objc func renameSelection(_ sender: Any?) {
        guard !isTextEditing else { return }
        if selectedURLs.count > 1 { bulkRename(sender); return }
        guard selectedURLs.count == 1 else { return }
        guard sender is NSMenuItem else { beginInlineRename(); return }
        // A menu sends its action while it is still closing; editing started now would lose
        // focus to the closing menu. Start once the run loop is back in its default mode.
        let url = selectedURLs[0]
        RunLoop.main.perform(inModes: [.default]) { [weak self] in
            MainActor.assumeIsolated { self?.beginInlineRename(expectedURL: url) }
        }
    }

    @objc func bulkRename(_ sender: Any?) {
        guard !isTextEditing, !selectedURLs.isEmpty, let window = view.window else { return }
        UIHooks.presentBulkRename?(selectedURLs, window)
    }

    @objc func duplicateSelection(_ sender: Any?) {
        let urls = selectedURLs
        guard !urls.isEmpty else { return }
        performFileOperation { await AppServices.shared.fileOperations.duplicate(urls) }
    }

    @objc func makeAlias(_ sender: Any?) {
        guard !isTextEditing, !selectedURLs.isEmpty else { return }
        finishFileOperation(AppServices.shared.fileOperations.makeAlias(selectedURLs))
    }

    @objc func showOriginal(_ sender: Any?) {
        let items = selectedItems.filter { $0.isAlias || $0.isSymbolicLink }
        guard !items.isEmpty else { return }
        Task { [weak self] in
            let result = await Task.detached(priority: .userInitiated) { () -> ([URL], [String]) in
                var urls: [URL] = []
                var errors: [String] = []
                for item in items {
                    do {
                        let url = item.isAlias
                            ? try URL(resolvingAliasFileAt: item.url, options: [.withoutUI, .withoutMounting])
                            : item.url.resolvingSymlinksInPath()
                        urls.append(url)
                    } catch { errors.append("\(item.name): \(error.localizedDescription)") }
                }
                return (urls, errors)
            }.value
            guard let self else { return }
            for url in result.0 { self.host?.open(url, disposition: .current) }
            if !result.1.isEmpty { self.presentError(result.1.joined(separator: "\n")) }
        }
    }

    @objc func compressSelection(_ sender: Any?) {
        let urls = selectedURLs
        guard !urls.isEmpty else { return }
        performFileOperation { await AppServices.shared.fileOperations.compress(urls) }
    }

    @objc func newFolder(_ sender: Any?) {
        guard !isTextEditing, let folder else { return }
        do {
            let created = try AppServices.shared.fileOperations.newFolder(in: folder)
            renameAfterLoadURL = created
            show(folder, selecting: [created])
        } catch { presentError(error.localizedDescription) }
    }

    @objc func newFolderWithSelection(_ sender: Any?) {
        let urls = selectedURLs
        guard !urls.isEmpty else { return }
        performFileOperation { await AppServices.shared.fileOperations.newFolder(containing: urls) }
    }

    @objc func moveToTrash(_ sender: Any?) {
        let urls = selectedURLs
        guard !urls.isEmpty else { return }
        performFileOperation { await AppServices.shared.fileOperations.moveToTrash(urls) }
    }

    @objc func copy(_ sender: Any?) { writeClipboard(cut: false) }
    @objc func cut(_ sender: Any?) { writeClipboard(cut: true) }

    private func writeClipboard(cut: Bool) {
        guard !isTextEditing, !selectedURLs.isEmpty else { return }
        let pasteboard = NSPasteboard.general
        pasteboard.clearContents()
        pasteboard.writeObjects(selectedURLs as [NSURL])
        if cut { pasteboard.setString("1", forType: FileListClipboard.cutType) }
    }

    @objc func paste(_ sender: Any?) {
        pasteFiles(moving: NSPasteboard.general.string(forType: FileListClipboard.cutType) == "1")
    }

    @objc func pasteAsMove(_ sender: Any?) { pasteFiles(moving: true) }

    private func pasteFiles(moving: Bool) {
        guard !isTextEditing, let folder else { return }
        let urls = FileListClipboard.urls
        guard !urls.isEmpty else { return }
        let changeCount = NSPasteboard.general.changeCount
        Task { [weak self] in
            let outcome = await AppServices.shared.fileOperations.transfer(urls, to: folder, mode: moving ? .move : .copy)
            if moving, !outcome.produced.isEmpty, NSPasteboard.general.changeCount == changeCount {
                // Retain skipped or failed cut items so a partial move can still be completed.
                let remaining = await Task.detached(priority: .utility) { urls.filter { FileManager.default.fileExists(atPath: $0.path) } }.value
                guard NSPasteboard.general.changeCount == changeCount else {
                    self?.finishFileOperation(outcome, destination: folder)
                    return
                }
                NSPasteboard.general.clearContents()
                if !remaining.isEmpty {
                    NSPasteboard.general.writeObjects(remaining as [NSURL])
                    NSPasteboard.general.setString("1", forType: FileListClipboard.cutType)
                }
            }
            self?.finishFileOperation(outcome, destination: folder)
        }
    }

    @objc override func selectAll(_ sender: Any?) {
        guard !isTextEditing else { return }
        select(allDisplayedItems.map(\.url))
    }
    @objc func deselectAll(_ sender: Any?) {
        guard !isTextEditing else { return }
        select([])
    }

    @objc func copyPath(_ sender: Any?) {
        let urls = selectedURLs.isEmpty ? folder.map { [$0] } ?? [] : selectedURLs
        guard !urls.isEmpty else { return }
        NSPasteboard.general.clearContents()
        NSPasteboard.general.setString(urls.map(\.path).joined(separator: "\n"), forType: .string)
    }

    @objc func copyToOtherPane(_ sender: Any?) { transferToOtherPane(mode: .copy) }
    @objc func moveToOtherPane(_ sender: Any?) { transferToOtherPane(mode: .move) }

    private func transferToOtherPane(mode: TransferMode) {
        guard !isTextEditing, let destination = host?.otherPaneFolder, !selectedURLs.isEmpty else { return }
        let urls = selectedURLs
        performFileOperation(destination: destination) {
            await AppServices.shared.fileOperations.transfer(urls, to: destination, mode: mode)
        }
    }

    @objc func showInFinder(_ sender: Any?) {
        let urls = selectedURLs.isEmpty ? folder.map { [$0] } ?? [] : selectedURLs
        FinderBridge.reveal(urls)
    }

    @objc func openTerminalHere(_ sender: Any?) {
        let destination = selectedItems.count == 1 && selectedItems[0].isDirectory ? selectedItems[0].url : folder
        guard let destination else { return }
        guard let terminal = NSWorkspace.shared.urlForApplication(withBundleIdentifier: "com.apple.Terminal") else {
            presentError(tr("Terminal could not be found.", "터미널을 찾을 수 없습니다.")); return
        }
        openWithApplication(terminal, urls: [destination])
    }

    func openWithApplication(_ application: URL, urls: [URL]) {
        guard !urls.isEmpty else { return }
        let configuration = NSWorkspace.OpenConfiguration()
        configuration.activates = true
        NSWorkspace.shared.open(urls, withApplicationAt: application, configuration: configuration) { [weak self] _, error in
            guard let error else { return }
            Task { @MainActor in self?.presentError(error.localizedDescription) }
        }
    }

    @objc func setViewModeIcons(_ sender: Any?) {
        var settings = viewSettings
        settings.viewMode = .icons
        applyViewSettings(settings)
    }

    @objc func setViewModeList(_ sender: Any?) {
        var settings = viewSettings
        settings.viewMode = .list
        applyViewSettings(settings)
    }

    @objc func sortBy(_ sender: Any?) {
        guard let item = sender as? NSMenuItem, SortKey.allCases.indices.contains(item.tag) else { return }
        var settings = viewSettings
        settings.sortKey = SortKey.allCases[item.tag]
        applyViewSettings(settings)
    }

    @objc func toggleSortDirection(_ sender: Any?) {
        var settings = viewSettings
        settings.ascending.toggle()
        applyViewSettings(settings)
    }

    @objc func refresh(_ sender: Any?) { reloadPreservingState() }

    func performFileOperation(destination: URL? = nil, _ operation: @escaping @MainActor () async -> FileOperationOutcome) {
        guard !isTextEditing, !selectedURLs.isEmpty else { return }
        Task { [weak self] in
            let outcome = await operation()
            self?.finishFileOperation(outcome, destination: destination)
        }
    }

    func finishFileOperation(_ outcome: FileOperationOutcome, destination: URL? = nil) {
        let producedHere = outcome.produced.filter { $0.deletingLastPathComponent().standardizedFileURL == folder?.standardizedFileURL }
        if let folder, !producedHere.isEmpty { show(folder, selecting: producedHere) }
        else { reloadPreservingState() }
        if let destination, !outcome.produced.isEmpty {
            NotificationCenter.default.post(name: .fileListItemsProduced, object: self,
                                            userInfo: ["folder": destination, "items": outcome.produced])
        }
    }

    func servicesRequestor(forSendType sendType: NSPasteboard.PasteboardType?, returnType: NSPasteboard.PasteboardType?) -> Any? {
        guard returnType == nil, !selectedURLs.isEmpty,
              sendType == .fileURL || sendType == .URL || sendType == .string else { return nil }
        return self
    }

    @objc func writeSelection(to pasteboard: NSPasteboard, types: [NSPasteboard.PasteboardType]) -> Bool {
        guard !selectedURLs.isEmpty else { return false }
        pasteboard.clearContents()
        var written = false
        if types.contains(.fileURL) || types.contains(.URL) { written = pasteboard.writeObjects(selectedURLs as [NSURL]) }
        if types.contains(.string) {
            written = pasteboard.setString(selectedURLs.map(\.path).joined(separator: "\n"), forType: .string) || written
        }
        return written
    }

    func validateMenuItem(_ menuItem: NSMenuItem) -> Bool {
        if isTextEditing {
            switch menuItem.action {
            case #selector(moveToTrash(_:)), #selector(renameSelection(_:)), #selector(bulkRename(_:)),
                 #selector(duplicateSelection(_:)), #selector(compressSelection(_:)), #selector(makeAlias(_:)),
                 #selector(newFolder(_:)), #selector(newFolderWithSelection(_:)),
                 #selector(copyToOtherPane(_:)), #selector(moveToOtherPane(_:)),
                 #selector(copy(_:)), #selector(cut(_:)), #selector(paste(_:)), #selector(pasteAsMove(_:)),
                 #selector(selectAll(_:)), #selector(deselectAll(_:)),
                 #selector(downloadCloudSelection(_:)), #selector(removeCloudDownload(_:)):
                return false
            default:
                break
            }
        }
        let hasSelection = !selectedURLs.isEmpty
        let count = selectedURLs.count
        switch menuItem.action {
        case #selector(downloadCloudSelection(_:)):
            return selectedURLs.contains { ubiquitousDownloads[$0] == false }
        case #selector(removeCloudDownload(_:)):
            return selectedURLs.contains { ubiquitousDownloads[$0] == true }
        case #selector(setViewModeIcons(_:)):
            menuItem.state = viewSettings.viewMode == .icons ? .on : .off
            return folder != nil
        case #selector(setViewModeList(_:)):
            menuItem.state = viewSettings.viewMode == .list ? .on : .off
            return folder != nil
        case #selector(sortBy(_:)):
            guard SortKey.allCases.indices.contains(menuItem.tag) else { return false }
            menuItem.state = viewSettings.sortKey == SortKey.allCases[menuItem.tag] ? .on : .off
            return folder != nil
        case #selector(toggleSortDirection(_:)):
            menuItem.title = viewSettings.ascending ? tr("Sort Descending", "내림차순 정렬") : tr("Sort Ascending", "오름차순 정렬")
            return folder != nil
        case #selector(renameSelection(_:)), #selector(bulkRename(_:)):
            menuItem.title = count > 1 ? tr("Rename \(count) Items…", "항목 \(count)개 이름 변경…") : tr("Rename…", "이름 변경…")
            return hasSelection
        case #selector(moveToTrash(_:)):
            menuItem.title = count > 1 ? tr("Move \(count) Items to Trash", "항목 \(count)개를 휴지통으로 이동") : tr("Move to Trash", "휴지통으로 이동")
            return hasSelection
        case #selector(compressSelection(_:)):
            menuItem.title = count == 1 ? tr("Compress “\(selectedURLs[0].lastPathComponent)”", "‘\(selectedURLs[0].lastPathComponent)’ 압축") : tr("Compress \(count) Items", "항목 \(count)개 압축")
            return hasSelection
        case #selector(newFolderWithSelection(_:)):
            menuItem.title = tr("New Folder with Selection (\(count) Items)", "선택 항목을 포함한 새로운 폴더(항목 \(count)개)")
            return hasSelection && folder != nil
        case #selector(paste(_:)):
            let urls = FileListClipboard.urls
            menuItem.title = NSPasteboard.general.string(forType: FileListClipboard.cutType) == "1" ? tr("Move Items Here", "항목을 여기로 이동") : tr("Paste Items", "항목 붙여넣기")
            return folder != nil && !urls.isEmpty
        case #selector(pasteAsMove(_:)):
            return folder != nil && !FileListClipboard.urls.isEmpty
        case #selector(copyToOtherPane(_:)), #selector(moveToOtherPane(_:)):
            return hasSelection && host?.otherPaneFolder != nil
        case #selector(openSelectionInNewTab(_:)), #selector(openSelectionInOtherPane(_:)), #selector(openInNewPaneRight(_:)), #selector(openInNewPaneBelow(_:)):
            return hasSelection && selectedItems.allSatisfy(\.isDirectory)
        case #selector(showOriginal(_:)):
            return selectedItems.contains { $0.isAlias || $0.isSymbolicLink }
        case #selector(newFolder(_:)), #selector(refresh(_:)), #selector(openTerminalHere(_:)):
            return folder != nil
        case #selector(showInFinder(_:)), #selector(getInfo(_:)), #selector(copyPath(_:)):
            return hasSelection || folder != nil
        case #selector(selectAll(_:)):
            return !allDisplayedItems.isEmpty
        case #selector(deselectAll(_:)):
            return hasSelection
        case #selector(toggleColumn(_:)):
            guard let identifier = menuItem.representedObject as? String,
                  let column = outline.tableColumns.first(where: { $0.identifier.rawValue == identifier }) else { return false }
            menuItem.state = column.isHidden ? .off : .on
            return column !== outline.tableColumns.first
        case #selector(applyFinderTag(_:)), #selector(openUsingApplication(_:)), #selector(chooseApplication(_:)),
             #selector(openSelection(_:)), #selector(toggleQuickLook(_:)), #selector(duplicateSelection(_:)),
             #selector(makeAlias(_:)), #selector(copy(_:)), #selector(cut(_:)):
            return hasSelection
        default:
            return true
        }
    }
}
