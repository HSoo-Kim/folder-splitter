import AppKit

/// The menu bar. Shortcuts follow Finder wherever Finder has the command; pane commands use
/// ⌥⌘/⌃ combinations Finder leaves free.
@MainActor
enum MainMenu {
    static func build() -> NSMenu {
        let main = NSMenu()
        main.addItem(submenu(appMenu()))
        main.addItem(submenu(fileMenu()))
        main.addItem(submenu(editMenu()))
        main.addItem(submenu(viewMenu()))
        main.addItem(submenu(goMenu()))
        main.addItem(submenu(panesMenu()))
        let window = windowMenu()
        main.addItem(submenu(window))
        NSApp.windowsMenu = window
        let help = helpMenu()
        main.addItem(submenu(help))
        NSApp.helpMenu = help
        return main
    }

    private static func submenu(_ menu: NSMenu) -> NSMenuItem {
        let item = NSMenuItem(title: menu.title, action: nil, keyEquivalent: "")
        item.submenu = menu
        return item
    }

    private static func item(_ title: String, _ action: Selector?, _ key: String = "",
                             _ modifiers: NSEvent.ModifierFlags = .command, tag: Int = 0,
                             symbol: String? = nil) -> NSMenuItem {
        let item = NSMenuItem(title: title, action: action, keyEquivalent: key)
        item.keyEquivalentModifierMask = key.isEmpty ? [] : modifiers
        item.tag = tag
        if let symbol { item.image = NSImage(systemSymbolName: symbol, accessibilityDescription: nil) }
        return item
    }

    private static let appName = "Folder Splitter"

    private static func appMenu() -> NSMenu {
        let menu = NSMenu(title: appName)
        menu.addItem(item(tr("About \(appName)", "\(appName)에 관하여"), #selector(AppCommands.showAbout(_:))))
        menu.addItem(item(tr("Check for Updates…", "업데이트 확인…"), #selector(AppCommands.checkForUpdates(_:))))
        menu.addItem(.separator())
        menu.addItem(item(tr("Settings…", "설정…"), #selector(AppCommands.showSettings(_:)), ","))
        menu.addItem(.separator())
        let services = NSMenu(title: tr("Services", "서비스"))
        let servicesItem = NSMenuItem(title: services.title, action: nil, keyEquivalent: "")
        servicesItem.submenu = services
        NSApp.servicesMenu = services
        menu.addItem(servicesItem)
        menu.addItem(.separator())
        menu.addItem(item(tr("Hide \(appName)", "\(appName) 가리기"), #selector(NSApplication.hide(_:)), "h"))
        menu.addItem(item(tr("Hide Others", "기타 가리기"), #selector(NSApplication.hideOtherApplications(_:)), "h", [.command, .option]))
        menu.addItem(item(tr("Show All", "모두 보기"), #selector(NSApplication.unhideAllApplications(_:))))
        menu.addItem(.separator())
        menu.addItem(item(tr("Quit \(appName)", "\(appName) 종료"), #selector(NSApplication.terminate(_:)), "q"))
        return menu
    }

    private static func fileMenu() -> NSMenu {
        let menu = NSMenu(title: tr("File", "파일"))
        menu.addItem(item(tr("New Window", "새로운 윈도우"), #selector(AppCommands.newWindow(_:)), "n"))
        menu.addItem(item(tr("New Tab", "새로운 탭"), #selector(PaneCommands.newTab(_:)), "t"))
        menu.addItem(item(tr("New Folder", "새로운 폴더"), #selector(FileCommands.newFolder(_:)), "n", [.command, .shift]))
        menu.addItem(item(tr("New Folder with Selection", "선택 항목을 포함한 새로운 폴더"), #selector(FileCommands.newFolderWithSelection(_:)), "n", [.command, .control]))
        menu.addItem(.separator())
        menu.addItem(item(tr("Open", "열기"), #selector(FileCommands.openSelection(_:)), "o"))
        menu.addItem(item(tr("Open in New Tab", "새로운 탭에서 열기"), #selector(FileCommands.openSelectionInNewTab(_:)), "o", [.command, .control]))
        menu.addItem(item(tr("Open in Other Pane", "다른 영역에서 열기"), #selector(FileCommands.openSelectionInOtherPane(_:))))
        menu.addItem(.separator())
        menu.addItem(item(tr("Close Tab", "탭 닫기"), #selector(PaneCommands.closeTab(_:)), "w"))
        menu.addItem(item(tr("Close Window", "윈도우 닫기"), #selector(NSWindow.performClose(_:)), "w", [.command, .shift]))
        menu.addItem(item(tr("Reopen Closed Tab", "닫은 탭 다시 열기"), #selector(WindowCommands.reopenClosedTab(_:)), "t", [.command, .shift]))
        menu.addItem(.separator())
        menu.addItem(item(tr("Get Info", "정보 가져오기"), #selector(FileCommands.getInfo(_:)), "i"))
        menu.addItem(item(tr("Rename", "이름 변경"), #selector(FileCommands.renameSelection(_:))))
        menu.addItem(item(tr("Rename Items…", "항목 이름 변경…"), #selector(FileCommands.bulkRename(_:))))
        menu.addItem(item(tr("Compress", "압축"), #selector(FileCommands.compressSelection(_:))))
        menu.addItem(item(tr("Duplicate", "복제"), #selector(FileCommands.duplicateSelection(_:)), "d"))
        menu.addItem(item(tr("Make Alias", "가상본 만들기"), #selector(FileCommands.makeAlias(_:)), "a", [.command, .control]))
        menu.addItem(item(tr("Show Original", "원본 보기"), #selector(FileCommands.showOriginal(_:)), "r"))
        menu.addItem(item(tr("Quick Look", "훑어보기"), #selector(FileCommands.toggleQuickLook(_:)), "y"))
        menu.addItem(.separator())
        menu.addItem(item(tr("Copy as Pathname", "경로 이름 복사"), #selector(FileCommands.copyPath(_:)), "c", [.command, .option]))
        menu.addItem(item(tr("Show in Finder", "Finder에서 보기"), #selector(FileCommands.showInFinder(_:))))
        menu.addItem(item(tr("Open in Terminal", "터미널에서 열기"), #selector(FileCommands.openTerminalHere(_:))))
        menu.addItem(.separator())
        menu.addItem(item(tr("Move to Trash", "휴지통으로 이동"), #selector(FileCommands.moveToTrash(_:)), "\u{8}"))
        menu.addItem(.separator())
        menu.addItem(item(tr("Add to Sidebar", "사이드바에 추가"), #selector(PaneCommands.addToSidebar(_:)), "t", [.command, .control]))
        return menu
    }

    private static func editMenu() -> NSMenu {
        let menu = NSMenu(title: tr("Edit", "편집"))
        menu.addItem(item(tr("Undo", "실행 취소"), Selector(("undo:")), "z"))
        menu.addItem(item(tr("Redo", "실행 복귀"), Selector(("redo:")), "z", [.command, .shift]))
        menu.addItem(.separator())
        menu.addItem(item(tr("Cut", "오려두기"), #selector(NSText.cut(_:)), "x"))
        menu.addItem(item(tr("Copy", "복사하기"), #selector(NSText.copy(_:)), "c"))
        menu.addItem(item(tr("Paste", "붙여넣기"), #selector(NSText.paste(_:)), "v"))
        menu.addItem(item(tr("Move Item Here", "항목을 여기로 이동"), #selector(FileCommands.pasteAsMove(_:)), "v", [.command, .option]))
        menu.addItem(item(tr("Select All", "전체 선택"), #selector(NSText.selectAll(_:)), "a"))
        menu.addItem(item(tr("Deselect All", "전체 선택 해제"), #selector(NSTableView.deselectAll(_:)), "a", [.command, .option]))
        menu.addItem(.separator())
        menu.addItem(item(tr("Filter This Folder", "이 폴더 필터"), #selector(PaneCommands.focusFilter(_:)), "f"))
        menu.addItem(item(tr("Search Files by Name", "이름으로 파일 검색"), #selector(PaneCommands.focusAddressBar(_:)), "l"))
        return menu
    }

    private static func viewMenu() -> NSMenu {
        let menu = NSMenu(title: tr("View", "보기"))
        menu.addItem(item(tr("as Icons", "아이콘으로 보기"), #selector(FileCommands.setViewModeIcons(_:)), "1"))
        menu.addItem(item(tr("as List", "목록으로 보기"), #selector(FileCommands.setViewModeList(_:)), "2"))
        menu.addItem(.separator())
        let sort = NSMenu(title: tr("Sort By", "정렬 기준"))
        for (index, key) in SortKey.allCases.enumerated() {
            sort.addItem(item(key.title, #selector(FileCommands.sortBy(_:)), "\(index + 1)", [.command, .option, .control], tag: index))
        }
        sort.addItem(.separator())
        sort.addItem(item(tr("Reverse Order", "역순"), #selector(FileCommands.toggleSortDirection(_:))))
        let sortItem = NSMenuItem(title: sort.title, action: nil, keyEquivalent: "")
        sortItem.submenu = sort
        menu.addItem(sortItem)
        menu.addItem(.separator())
        menu.addItem(item(tr("Show Hidden Files", "숨김 파일 보기"), #selector(WindowCommands.toggleHiddenFiles(_:)), ".", [.command, .shift]))
        menu.addItem(item(tr("Show All Filename Extensions", "모든 파일 확장자 보기"), #selector(WindowCommands.toggleFilenameExtensions(_:))))
        menu.addItem(item(tr("Keep Folders on Top", "폴더를 상단에 유지"), #selector(WindowCommands.toggleFoldersFirst(_:))))
        menu.addItem(.separator())
        menu.addItem(item(tr("Hide Sidebar", "사이드바 가리기"), #selector(WindowCommands.toggleSidebar(_:)), "s", [.command, .control]))
        menu.addItem(item(tr("Show Folder Tree", "폴더 트리 보기"), #selector(WindowCommands.toggleFolderTree(_:))))
        menu.addItem(item(tr("Show Preview", "미리보기 보기"), #selector(WindowCommands.togglePreview(_:)), "p", [.command, .shift]))
        menu.addItem(item(tr("Hide Status Bar", "상태 막대 가리기"), #selector(WindowCommands.toggleStatusBar(_:)), "/"))
        menu.addItem(.separator())
        menu.addItem(item(tr("Make Text Bigger", "텍스트 크게"), #selector(WindowCommands.increaseTextSize(_:)), "+"))
        menu.addItem(item(tr("Make Text Smaller", "텍스트 작게"), #selector(WindowCommands.decreaseTextSize(_:)), "-"))
        menu.addItem(.separator())
        menu.addItem(item(tr("Refresh", "새로 고침"), #selector(FileCommands.refresh(_:)), "r", [.command, .option]))
        menu.addItem(item(tr("Enter Full Screen", "전체 화면 시작"), #selector(NSWindow.toggleFullScreen(_:)), "f", [.command, .control]))
        return menu
    }

    private static func goMenu() -> NSMenu {
        let menu = NSMenu(title: tr("Go", "이동"))
        menu.addItem(item(tr("Back", "뒤로"), #selector(PaneCommands.goBack(_:)), "["))
        menu.addItem(item(tr("Forward", "앞으로"), #selector(PaneCommands.goForward(_:)), "]"))
        menu.addItem(item(tr("Enclosing Folder", "상위 폴더"), #selector(PaneCommands.goToEnclosingFolder(_:)), String(UnicodeScalar(NSUpArrowFunctionKey)!)))
        menu.addItem(.separator())
        let shortcuts: [StandardFolder: (String, NSEvent.ModifierFlags)] = [
            .home: ("h", [.command, .shift]), .desktop: ("d", [.command, .shift]), .documents: ("o", [.command, .shift]),
            .downloads: ("l", [.command, .option]), .applications: ("a", [.command, .shift]), .utilities: ("u", [.command, .shift]),
            .iCloudDrive: ("i", [.command, .shift]), .computer: ("c", [.command, .shift]),
        ]
        for place in StandardFolder.allCases {
            let (key, modifiers) = shortcuts[place] ?? ("", [])
            menu.addItem(item(place.title, #selector(PaneCommands.goToStandardFolder(_:)), key, modifiers, tag: place.rawValue, symbol: place.symbolName))
        }
        menu.addItem(.separator())
        let recent = NSMenu(title: tr("Recent Folders", "최근 사용한 폴더"))
        recent.delegate = RecentFoldersMenu.shared
        let recentItem = NSMenuItem(title: recent.title, action: nil, keyEquivalent: "")
        recentItem.submenu = recent
        menu.addItem(recentItem)
        menu.addItem(.separator())
        menu.addItem(item(tr("Go to Folder…", "폴더로 이동…"), #selector(PaneCommands.focusAddressBar(_:)), "g", [.command, .shift]))
        return menu
    }

    private static func panesMenu() -> NSMenu {
        let menu = NSMenu(title: tr("Panes", "영역"))
        for (index, preset) in [PaneLayoutPreset.one, .two, .threeSideBySide, .four].enumerated() {
            let entry = item(preset.title, #selector(WindowCommands.setPaneLayout(_:)), "\(index + 1)", [.command, .option], tag: preset.rawValue)
            entry.image = LayoutGlyph.image(for: preset)
            menu.addItem(entry)
        }
        let more = NSMenu(title: tr("Other Layouts", "다른 레이아웃"))
        for preset in [PaneLayoutPreset.twoStacked, .threeColumns, .threeStacked] {
            let entry = item(preset.title, #selector(WindowCommands.setPaneLayout(_:)), tag: preset.rawValue)
            entry.image = LayoutGlyph.image(for: preset)
            more.addItem(entry)
        }
        let moreItem = NSMenuItem(title: more.title, action: nil, keyEquivalent: "")
        moreItem.submenu = more
        menu.addItem(moreItem)
        menu.addItem(item(tr("Make Panes Equal Size", "영역 크기 같게"), #selector(WindowCommands.equalizePanes(_:))))
        menu.addItem(.separator())
        menu.addItem(item(tr("Next Pane", "다음 영역"), #selector(WindowCommands.focusNextPane(_:)), "]", [.command, .option]))
        menu.addItem(item(tr("Previous Pane", "이전 영역"), #selector(WindowCommands.focusPreviousPane(_:)), "[", [.command, .option]))
        for number in 1...4 {
            menu.addItem(item(tr("Go to Pane \(number)", "영역 \(number)\(number == 3 ? "으로" : "로") 이동"), #selector(WindowCommands.focusPane(_:)), "\(number)", .control, tag: number))
        }
        menu.addItem(.separator())
        menu.addItem(item(tr("Show Next Tab", "다음 탭 보기"), #selector(PaneCommands.selectNextTab(_:)), "\t", .control))
        menu.addItem(item(tr("Show Previous Tab", "이전 탭 보기"), #selector(PaneCommands.selectPreviousTab(_:)), "\t", [.control, .shift]))
        menu.addItem(.separator())
        menu.addItem(item(tr("Open Folder in Other Pane", "다른 영역에서 폴더 열기"), #selector(PaneCommands.openFolderInOtherPane(_:)), "o", [.control, .option]))
        menu.addItem(item(tr("Copy to Other Pane", "다른 영역으로 복사"), #selector(FileCommands.copyToOtherPane(_:)), "c", [.control, .option]))
        menu.addItem(item(tr("Move to Other Pane", "다른 영역으로 이동"), #selector(FileCommands.moveToOtherPane(_:)), "x", [.control, .option]))
        menu.addItem(item(tr("Compare with Other Pane…", "다른 영역과 비교…"), #selector(PaneCommands.comparePanes(_:))))
        menu.addItem(.separator())
        menu.addItem(item(tr("Close Pane", "영역 닫기"), #selector(WindowCommands.closePane(_:))))
        menu.addItem(.separator())
        let workspaces = WorkspaceMenu.make()
        let workspacesItem = NSMenuItem(title: tr("Workspaces", "작업 구성"), action: nil, keyEquivalent: "")
        workspacesItem.submenu = workspaces
        menu.addItem(workspacesItem)
        return menu
    }

    private static func windowMenu() -> NSMenu {
        let menu = NSMenu(title: tr("Window", "윈도우"))
        menu.addItem(item(tr("Minimize", "최소화"), #selector(NSWindow.performMiniaturize(_:)), "m"))
        menu.addItem(item(tr("Zoom", "확대/축소"), #selector(NSWindow.performZoom(_:))))
        menu.addItem(.separator())
        menu.addItem(item(tr("Bring All to Front", "모두 앞으로 가져오기"), #selector(NSApplication.arrangeInFront(_:))))
        return menu
    }

    private static func helpMenu() -> NSMenu {
        let menu = NSMenu(title: tr("Help", "도움말"))
        menu.addItem(shortcutHelpItem())
        menu.delegate = HelpMenuGuard.shared
        return menu
    }

    static func shortcutHelpItem() -> NSMenuItem {
        item(tr("Keyboard Shortcuts", "키보드 단축키"), #selector(AppCommands.showShortcutHelp(_:)), "?")
    }
}

/// Keeps ⌘? working: the Help menu was occasionally seen without its item right after a
/// SwiftUI window closed (cause not found). The delegate answers ⌘? itself and puts the item
/// back whenever the menu is opened.
@MainActor
final class HelpMenuGuard: NSObject, NSMenuDelegate {
    static let shared = HelpMenuGuard()
    private let action = #selector(AppCommands.showShortcutHelp(_:))

    func menuNeedsUpdate(_ menu: NSMenu) {
        if !menu.items.contains(where: { $0.action == action }) { menu.addItem(MainMenu.shortcutHelpItem()) }
    }

    func menuHasKeyEquivalent(_ menu: NSMenu, for event: NSEvent, target: AutoreleasingUnsafeMutablePointer<AnyObject?>,
                              action: UnsafeMutablePointer<Selector?>) -> Bool {
        let flags = event.modifierFlags.intersection([.command, .option, .control, .shift])
        guard event.charactersIgnoringModifiers == "?", flags == [.command, .shift] || flags == .command else { return false }
        target.pointee = NSApp.delegate
        action.pointee = self.action
        return true
    }
}

/// Go ▸ Recent Folders, rebuilt each time it opens.
@MainActor
final class RecentFoldersMenu: NSObject, NSMenuDelegate {
    static let shared = RecentFoldersMenu()

    func menuNeedsUpdate(_ menu: NSMenu) {
        menu.removeAllItems()
        let folders = AppServices.shared?.history.recentFolders ?? []
        if folders.isEmpty {
            let empty = NSMenuItem(title: tr("No Recent Folders", "최근 사용한 폴더 없음"), action: nil, keyEquivalent: "")
            empty.isEnabled = false
            menu.addItem(empty)
            return
        }
        for url in folders {
            let entry = ClosureMenuItem(title: TabState.quickName(of: url)) {
                (NSApp.delegate as? AppDelegate)?.frontController?.open(url, disposition: .current)
            }
            entry.toolTip = PathText.abbreviated(url)
            let icon = NSWorkspace.shared.icon(for: .folder)
            icon.size = NSSize(width: 16, height: 16)
            entry.image = icon
            menu.addItem(entry)
        }
    }
}

/// Workspaces menu (toolbar and Panes menu): save, open by name, manage.
@MainActor
final class WorkspaceMenu: NSObject, NSMenuDelegate {
    private static let shared = WorkspaceMenu()

    static func make() -> NSMenu {
        let menu = NSMenu(title: tr("Workspaces", "작업 구성"))
        menu.delegate = shared
        shared.menuNeedsUpdate(menu)
        return menu
    }

    func menuNeedsUpdate(_ menu: NSMenu) {
        menu.removeAllItems()
        menu.addItem(NSMenuItem(title: tr("Save Workspace…", "작업 구성 저장…"), action: #selector(WindowCommands.saveWorkspace(_:)), keyEquivalent: ""))
        let saved = SessionCoordinator.shared?.workspaces.workspaces ?? []
        if !saved.isEmpty {
            menu.addItem(.separator())
            for workspace in saved {
                let entry = NSMenuItem(title: workspace.name, action: #selector(WindowCommands.openWorkspace(_:)), keyEquivalent: "")
                entry.representedObject = workspace.name
                menu.addItem(entry)
            }
        }
        menu.addItem(.separator())
        menu.addItem(NSMenuItem(title: tr("Manage Workspaces…", "작업 구성 관리…"), action: #selector(WindowCommands.manageWorkspaces(_:)), keyEquivalent: ""))
    }
}
