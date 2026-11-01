import AppKit
import Darwin
import UniformTypeIdentifiers

enum FinderTags {
    struct Tag: Sendable {
        let name: String
        let korean: String
        let color: Int
        var title: String { tr(name, korean) }
        var record: String { "\(title)\n\(color)" }
    }

    static let values = [
        Tag(name: "Red", korean: "빨간색", color: 6),
        Tag(name: "Orange", korean: "주황색", color: 7),
        Tag(name: "Yellow", korean: "노란색", color: 5),
        Tag(name: "Green", korean: "초록색", color: 2),
        Tag(name: "Blue", korean: "파란색", color: 4),
        Tag(name: "Purple", korean: "보라색", color: 3),
        Tag(name: "Gray", korean: "회색", color: 1)
    ]
    static let attribute = "com.apple.metadata:_kMDItemUserTags"

    static func matches(_ record: String, tag: Tag) -> Bool {
        record == tag.name || record == tag.korean || record == tag.record || record == "\(tag.korean)\n\(tag.color)"
    }

    static func read(_ url: URL) throws -> [String] {
        let size = url.withUnsafeFileSystemRepresentation { path in
            getxattr(path!, attribute, nil, 0, 0, XATTR_NOFOLLOW)
        }
        if size < 0 {
            if errno == ENOATTR { return [] }
            throw NSError(domain: NSPOSIXErrorDomain, code: Int(errno))
        }
        var data = Data(count: size)
        let count = data.withUnsafeMutableBytes { bytes in
            url.withUnsafeFileSystemRepresentation { path in
                getxattr(path!, attribute, bytes.baseAddress, bytes.count, 0, XATTR_NOFOLLOW)
            }
        }
        guard count >= 0 else { throw NSError(domain: NSPOSIXErrorDomain, code: Int(errno)) }
        data.count = count
        guard let tags = try PropertyListSerialization.propertyList(from: data, options: [], format: nil) as? [String] else {
            throw NSError(domain: NSCocoaErrorDomain, code: NSPropertyListReadCorruptError,
                          userInfo: [NSLocalizedDescriptionKey: tr("The existing Finder tags could not be read.", "기존 Finder 태그를 읽을 수 없습니다.")])
        }
        return tags
    }

    static func write(_ tags: [String], to url: URL) throws {
        let data = try PropertyListSerialization.data(fromPropertyList: tags, format: .binary, options: 0)
        let result = data.withUnsafeBytes { bytes in
            url.withUnsafeFileSystemRepresentation { path in
                setxattr(path!, attribute, bytes.baseAddress, bytes.count, 0, XATTR_NOFOLLOW)
            }
        }
        guard result == 0 else { throw NSError(domain: NSPOSIXErrorDomain, code: Int(errno)) }
    }

    @MainActor static func color(_ value: Int) -> NSColor {
        switch value {
        case 6: return .systemRed
        case 7: return .systemOrange
        case 5: return .systemYellow
        case 2: return .systemGreen
        case 4: return .systemBlue
        case 3: return .systemPurple
        default: return .systemGray
        }
    }
}

extension FileListViewController: NSMenuDelegate {
    func contextMenu(for item: FileItem?) -> NSMenu {
        host?.fileListDidActivate(self)
        view.window?.makeFirstResponder(isIconView ? collection : outline)
        if let item {
            if !selectedURLs.contains(item.url) { select([item.url]) }
        } else {
            select([])
        }
        let menu = NSMenu()
        menu.delegate = self
        if item != nil {
            addCommand(tr("Open", "열기"), #selector(openSelection(_:)), to: menu)
            if selectedItems.allSatisfy(\.isDirectory) {
                addCommand(tr("Open in New Tab", "새로운 탭에서 열기"), #selector(openSelectionInNewTab(_:)), to: menu)
                addCommand(tr("Open in Other Pane", "다른 영역에서 열기"), #selector(openSelectionInOtherPane(_:)), to: menu)
                let newPane = addCommand(tr("Open in New Pane", "새로운 영역에서 열기"), nil, to: menu)
                let paneMenu = NSMenu(title: newPane.title)
                addCommand(tr("Right", "오른쪽"), #selector(openInNewPaneRight(_:)), to: paneMenu)
                addCommand(tr("Below", "아래쪽"), #selector(openInNewPaneBelow(_:)), to: paneMenu)
                newPane.submenu = paneMenu
            } else {
                let openWith = addCommand(tr("Open With", "다음으로 열기"), nil, to: menu)
                openWith.submenu = openWithMenu()
            }
            if selectedURLs.contains(where: { ubiquitousDownloads[$0] == false }) {
                addCommand(tr("Download Now", "지금 다운로드"), #selector(downloadCloudSelection(_:)), to: menu)
            }
            if selectedURLs.contains(where: { ubiquitousDownloads[$0] == true }) {
                addCommand(tr("Remove Download", "다운로드 제거"), #selector(removeCloudDownload(_:)), to: menu)
            }
            menu.addItem(.separator())
            addCommand(tr("Move to Trash", "휴지통으로 이동"), #selector(moveToTrash(_:)), to: menu)
            menu.addItem(.separator())
            addCommand(tr("Get Info", "정보 가져오기"), #selector(getInfo(_:)), to: menu)
            addCommand(tr("Rename", "이름 변경"), #selector(renameSelection(_:)), to: menu)
            addCommand(tr("Compress", "압축"), #selector(compressSelection(_:)), to: menu)
            addCommand(tr("Duplicate", "복제"), #selector(duplicateSelection(_:)), to: menu)
            addCommand(tr("Make Alias", "가상본 만들기"), #selector(makeAlias(_:)), to: menu)
            if selectedItems.contains(where: { $0.isAlias || $0.isSymbolicLink }) {
                addCommand(tr("Show Original", "원본 보기"), #selector(showOriginal(_:)), to: menu)
            }
            addCommand(tr("Quick Look", "훑어보기"), #selector(toggleQuickLook(_:)), to: menu)
            let share = NSSharingServicePicker(items: selectedURLs).standardShareMenuItem
            share.title = tr("Share", "공유")
            menu.addItem(share)
            menu.addItem(.separator())
            addCommand(tr("Copy", "복사하기"), #selector(copy(_:)), to: menu)
            addCommand(tr("Cut", "오려두기"), #selector(cut(_:)), to: menu)
            addCommand(tr("Copy as Pathname", "경로 이름 복사"), #selector(copyPath(_:)), to: menu)
            addCommand(tr("Copy to Other Pane", "다른 영역으로 복사"), #selector(copyToOtherPane(_:)), to: menu)
            addCommand(tr("Move to Other Pane", "다른 영역으로 이동"), #selector(moveToOtherPane(_:)), to: menu)
            addCommand(tr("Paste Items", "항목 붙여넣기"), #selector(paste(_:)), to: menu)
            addCommand(tr("Move Items Here", "항목을 여기로 이동"), #selector(pasteAsMove(_:)), to: menu)
            menu.addItem(.separator())
            addCommand(tr("New Folder with Selection", "선택 항목을 포함한 새로운 폴더"), #selector(newFolderWithSelection(_:)), to: menu)
            let tags = addCommand(tr("Tags", "태그"), nil, to: menu)
            tags.submenu = finderTagsMenu()
        } else {
            addCommand(tr("New Folder", "새로운 폴더"), #selector(newFolder(_:)), to: menu)
            menu.addItem(.separator())
            addCommand(tr("Paste Items", "항목 붙여넣기"), #selector(paste(_:)), to: menu)
            addCommand(tr("Move Items Here", "항목을 여기로 이동"), #selector(pasteAsMove(_:)), to: menu)
            menu.addItem(.separator())
            addCommand(tr("Get Info", "정보 가져오기"), #selector(getInfo(_:)), to: menu)
            addCommand(tr("Copy as Pathname", "경로 이름 복사"), #selector(copyPath(_:)), to: menu)
            let viewAs = addCommand(tr("View As", "보기 방식"), nil, to: menu)
            let viewMenu = NSMenu(title: viewAs.title)
            addCommand(tr("as Icons", "아이콘으로 보기"), #selector(setViewModeIcons(_:)), to: viewMenu)
            addCommand(tr("as List", "목록으로 보기"), #selector(setViewModeList(_:)), to: viewMenu)
            viewAs.submenu = viewMenu
            let sort = addCommand(tr("Sort By", "정렬 기준"), nil, to: menu)
            let sortMenu = NSMenu(title: sort.title)
            for (index, key) in SortKey.allCases.enumerated() {
                addCommand(key.title, #selector(sortBy(_:)), to: sortMenu).tag = index
            }
            sortMenu.addItem(.separator())
            addCommand(tr("Reverse Sort Order", "정렬 순서 반대로"), #selector(toggleSortDirection(_:)), to: sortMenu)
            sort.submenu = sortMenu
            addCommand(tr("Refresh", "새로 고침"), #selector(refresh(_:)), to: menu)
        }
        menu.addItem(.separator())
        addCommand(tr("Show in Finder", "Finder에서 보기"), #selector(showInFinder(_:)), to: menu)
        addCommand(tr("Open in Terminal", "터미널에서 열기"), #selector(openTerminalHere(_:)), to: menu)
        if item != nil {
            let services = addCommand(tr("Services", "서비스"), nil, to: menu)
            let submenu = NSMenu(title: services.title)
            submenu.delegate = self
            services.submenu = submenu
        }
        return menu
    }
    @objc func downloadCloudSelection(_ sender: Any?) { changeCloudDownload(remove: false) }
    @objc func removeCloudDownload(_ sender: Any?) { changeCloudDownload(remove: true) }

    private func changeCloudDownload(remove: Bool) {
        guard !isTextEditing else { return }
        let urls = selectedURLs.filter { ubiquitousDownloads[$0] == remove }
        guard !urls.isEmpty else { return }
        Task { [weak self] in
            let failures = await Task.detached(priority: .userInitiated) {
                urls.compactMap { url -> FileOperationFailure? in
                    do {
                        if remove { try FileManager.default.evictUbiquitousItem(at: url) }
                        else { try FileManager.default.startDownloadingUbiquitousItem(at: url) }
                        return nil
                    } catch { return FileOperationFailure(url: url, message: error.localizedDescription) }
                }
            }.value
            guard let self else { return }
            self.reloadPreservingState()
            if !failures.isEmpty { self.presentError(failures.map { "\($0.url.lastPathComponent): \($0.message)" }.joined(separator: "\n")) }
        }
    }


    @discardableResult
    private func addCommand(_ title: String, _ action: Selector?, to menu: NSMenu) -> NSMenuItem {
        let item = NSMenuItem(title: title, action: action, keyEquivalent: "")
        item.target = self
        menu.addItem(item)
        return item
    }

    @objc func openInNewPaneRight(_ sender: Any?) { openItems(selectedURLs, disposition: .newPane(.right)) }
    @objc func openInNewPaneBelow(_ sender: Any?) { openItems(selectedURLs, disposition: .newPane(.bottom)) }

    private func openWithMenu() -> NSMenu {
        let menu = NSMenu(title: tr("Open With", "다음으로 열기"))
        let urls = selectedURLs
        addCommand(tr("Other…", "기타…"), #selector(chooseApplication(_:)), to: menu)
        Task { [weak self, weak menu] in
            let entries = await Task.detached(priority: .userInitiated) { () -> [(URL, String, Bool)] in
                guard let first = urls.first else { return [] }
                let workspace = NSWorkspace.shared
                let defaultApp = workspace.urlForApplication(toOpen: first)
                var applications = Set(workspace.urlsForApplications(toOpen: first))
                for url in urls.dropFirst() { applications.formIntersection(workspace.urlsForApplications(toOpen: url)) }
                let sharedDefault = defaultApp.flatMap { app in urls.allSatisfy { workspace.urlForApplication(toOpen: $0) == app } ? app : nil }
                if let sharedDefault { applications.insert(sharedDefault) }
                return applications.map { application in
                    let name = (FileManager.default.displayName(atPath: application.path) as NSString).deletingPathExtension
                    return (application, name, application == sharedDefault)
                }.sorted { lhs, rhs in
                    if lhs.2 != rhs.2 { return lhs.2 }
                    return lhs.1.localizedStandardCompare(rhs.1) == .orderedAscending
                }
            }.value
            guard let self, let menu else { return }
            menu.removeAllItems()
            for entry in entries {
                let title = entry.2 ? tr("\(entry.1) (default)", "\(entry.1)(기본)") : entry.1
                let item = self.addCommand(title, #selector(self.openUsingApplication(_:)), to: menu)
                item.representedObject = entry.0
                func setIcon(_ image: NSImage) {
                    guard let icon = image.copy() as? NSImage else { return }
                    icon.size = NSSize(width: 16, height: 16)
                    item.image = icon
                }
                if let image = FileListIconCache.shared.applicationImage(for: entry.0, completion: setIcon) { setIcon(image) }
            }
            if !entries.isEmpty { menu.addItem(.separator()) }
            self.addCommand(tr("Other…", "기타…"), #selector(self.chooseApplication(_:)), to: menu)
            menu.update()
        }
        return menu
    }

    @objc func openUsingApplication(_ sender: Any?) {
        guard let application = (sender as? NSMenuItem)?.representedObject as? URL else { return }
        openWithApplication(application, urls: selectedURLs)
    }

    @objc func chooseApplication(_ sender: Any?) {
        guard let window = view.window, !selectedURLs.isEmpty else { return }
        let urls = selectedURLs
        let panel = NSOpenPanel()
        panel.title = tr("Choose an Application", "응용 프로그램 선택")
        panel.prompt = tr("Open", "열기")
        panel.allowedContentTypes = [.application]
        panel.canChooseFiles = true
        panel.canChooseDirectories = false
        panel.allowsMultipleSelection = false
        panel.directoryURL = URL(fileURLWithPath: "/Applications", isDirectory: true)
        panel.beginSheetModal(for: window) { [weak self] response in
            guard response == .OK, let application = panel.url else { return }
            self?.openWithApplication(application, urls: urls)
        }
    }

    private func finderTagsMenu() -> NSMenu {
        let menu = NSMenu(title: tr("Tags", "태그"))
        for (index, tag) in FinderTags.values.enumerated() {
            let item = addCommand(tag.title, #selector(applyFinderTag(_:)), to: menu)
            item.tag = index
            let matches = selectedItems.filter { $0.tags.contains { FinderTags.matches($0, tag: tag) } }.count
            item.state = matches == selectedItems.count ? .on : matches > 0 ? .mixed : .off
            let color = FinderTags.color(tag.color)
            item.image = NSImage(size: NSSize(width: 14, height: 14), flipped: false) { bounds in
                color.setFill()
                NSBezierPath(ovalIn: bounds.insetBy(dx: 2, dy: 2)).fill()
                return true
            }
        }
        return menu
    }

    @objc func applyFinderTag(_ sender: Any?) {
        guard let menuItem = sender as? NSMenuItem, FinderTags.values.indices.contains(menuItem.tag), !selectedURLs.isEmpty else { return }
        let tag = FinderTags.values[menuItem.tag]
        let urls = selectedURLs
        Task { [weak self] in
            let failures = await Task.detached(priority: .userInitiated) { () -> [FileOperationFailure] in
                var records: [(URL, [String])] = []
                var failures: [FileOperationFailure] = []
                for url in urls {
                    do { records.append((url, try FinderTags.read(url))) }
                    catch { failures.append(FileOperationFailure(url: url, message: error.localizedDescription)) }
                }
                let remove = records.count == urls.count && records.allSatisfy { $0.1.contains { FinderTags.matches($0, tag: tag) } }
                for (url, original) in records {
                    var updated = original
                    if remove { updated.removeAll { FinderTags.matches($0, tag: tag) } }
                    else if !updated.contains(where: { FinderTags.matches($0, tag: tag) }) { updated.append(tag.record) }
                    guard updated != original else { continue }
                    do { try FinderTags.write(updated, to: url) }
                    catch { failures.append(FileOperationFailure(url: url, message: error.localizedDescription)) }
                }
                return failures
            }.value
            guard let self else { return }
            self.reloadPreservingState()
            if !failures.isEmpty { self.presentError(failures.map { "\($0.url.lastPathComponent): \($0.message)" }.joined(separator: "\n")) }
        }
    }

    func menuNeedsUpdate(_ menu: NSMenu) {
        guard menu.title == tr("Services", "서비스"), let services = NSApp.servicesMenu, services !== menu else { return }
        services.update()
        menu.removeAllItems()
        for item in services.items {
            if let copy = item.copy() as? NSMenuItem { menu.addItem(copy) }
        }
    }
}
