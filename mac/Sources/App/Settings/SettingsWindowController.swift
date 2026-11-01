import AppKit
import SwiftUI

@MainActor
final class SettingsWindowController: NSWindowController, NSToolbarDelegate, NSWindowDelegate {
    static let shared = SettingsWindowController()
    private let model = SettingsModel()

    private init() {
        let window = NSWindow(contentRect: NSRect(x: 0, y: 0, width: 640, height: 640),
                              styleMask: [.titled, .closable, .miniaturizable, .resizable],
                              backing: .buffered, defer: false)
        super.init(window: window)
        window.title = tr("Settings", "설정")
        window.contentViewController = NSHostingController(rootView: SettingsView(model: model))
        window.minSize = NSSize(width: 610, height: 640)
        window.isReleasedWhenClosed = false
        window.delegate = self
        window.setFrameAutosaveName("SettingsWindow")
        let toolbar = NSToolbar(identifier: "FolderSplitter.Settings")
        toolbar.delegate = self
        toolbar.allowsUserCustomization = false
        toolbar.displayMode = .iconAndLabel
        toolbar.selectedItemIdentifier = NSToolbarItem.Identifier(SettingsTab.general.rawValue)
        window.toolbar = toolbar
        window.toolbarStyle = .preference
        window.center()
    }

    required init?(coder: NSCoder) { nil }

    func show() {
        model.refreshSearch()
        model.refreshSystemState()
        window?.title = tr("Settings", "설정")
        showWindow(nil)
        window?.makeKeyAndOrderFront(nil)
        NSApp.bringForward()
    }

    func windowDidBecomeKey(_ notification: Notification) {
        model.refreshSystemState()
        model.refreshSearch()
    }

    func toolbarAllowedItemIdentifiers(_ toolbar: NSToolbar) -> [NSToolbarItem.Identifier] {
        SettingsTab.allCases.map { NSToolbarItem.Identifier($0.rawValue) }
    }

    func toolbarDefaultItemIdentifiers(_ toolbar: NSToolbar) -> [NSToolbarItem.Identifier] {
        toolbarAllowedItemIdentifiers(toolbar)
    }

    func toolbarSelectableItemIdentifiers(_ toolbar: NSToolbar) -> [NSToolbarItem.Identifier] {
        toolbarAllowedItemIdentifiers(toolbar)
    }

    func toolbar(_ toolbar: NSToolbar, itemForItemIdentifier identifier: NSToolbarItem.Identifier,
                 willBeInsertedIntoToolbar flag: Bool) -> NSToolbarItem? {
        guard let tab = SettingsTab(rawValue: identifier.rawValue) else { return nil }
        let item = NSToolbarItem(itemIdentifier: identifier)
        item.label = tab.title
        item.paletteLabel = tab.title
        item.image = NSImage(systemSymbolName: tab.symbol, accessibilityDescription: tab.title)
        item.target = self
        item.action = #selector(selectTab(_:))
        return item
    }

    @objc private func selectTab(_ sender: NSToolbarItem) {
        guard let tab = SettingsTab(rawValue: sender.itemIdentifier.rawValue) else { return }
        model.selectedTab = tab
    }
}
