import AppKit
import Combine
import SwiftUI

struct ShortcutEntry: Identifiable {
    let id: Int
    let category: String
    let keys: String
    let title: String
}

@MainActor
final class ShortcutHelpModel: ObservableObject {
    @Published private(set) var entries: [ShortcutEntry] = []
    @Published private(set) var settings = SettingsStore.shared.settings
    private var observer: AnyCancellable?

    init() {
        observer = NotificationCenter.default.publisher(for: .settingsDidChange)
            .receive(on: RunLoop.main)
            .sink { [weak self] _ in
                self?.settings = SettingsStore.shared.settings
                self?.reload()
            }
        reload()
    }

    /// Read the actual menu each time help opens, so renamed commands and custom
    /// equivalents cannot drift from the list shown to the user.
    func reload() {
        var result: [ShortcutEntry] = []
        func append(_ category: String, _ keys: String, _ title: String) {
            result.append(ShortcutEntry(id: result.count, category: category, keys: keys, title: title))
        }
        func visit(_ menu: NSMenu, category: String) {
            for item in menu.items where !item.isSeparatorItem {
                if !item.keyEquivalent.isEmpty {
                    append(category, Self.keyLabel(item), item.title)
                }
                if let submenu = item.submenu { visit(submenu, category: category) }
            }
        }
        if let menu = NSApp.mainMenu {
            for item in menu.items {
                if let submenu = item.submenu {
                    visit(submenu, category: item.title.isEmpty ? submenu.title : item.title)
                }
            }
        }
        let category = tr("File List and Panes", "파일 목록 및 영역")
        let listOnly: [(String, String)] = [
            (tr("Space", "스페이스"), tr("Quick Look", "훑어보기")),
            ("↩", tr("Rename", "이름 변경")),
            ("⌘↓", tr("Open selected item", "선택한 항목 열기")),
            (tr("⌘-click", "⌘-클릭"), tr("Open folder in a new tab", "폴더를 새로운 탭에서 열기")),
            (tr("Middle-click", "가운데 클릭"), tr("Open folder in a background tab", "폴더를 백그라운드 탭에서 열기")),
            ("F6", tr("Next Pane", "다음 영역")),
            ("⇧F6", tr("Previous Pane", "이전 영역")),
            ("⌃⇥", tr("Show Next Tab", "다음 탭 보기")),
            (tr("Type a name", "이름 입력"), tr("Select a matching item", "일치하는 항목 선택"))
        ]
        for (keys, title) in listOnly { append(category, keys, title) }
        entries = result
    }

    static func keyLabel(_ item: NSMenuItem) -> String {
        let flags = item.keyEquivalentModifierMask
        var result = ""
        for (flag, glyph): (NSEvent.ModifierFlags, String) in [(.control, "⌃"), (.option, "⌥"), (.shift, "⇧"), (.command, "⌘")] {
            if flags.contains(flag) { result += glyph }
        }
        let key = item.keyEquivalent
        let names: [String: String] = [
            " ": tr("Space", "스페이스"), "\r": "↩", "\n": "↩", "\t": "⇥", "\u{19}": "⇤",
            "\u{1b}": "⎋", "\u{8}": "⌫", "\u{7f}": "⌫",
            String(UnicodeScalar(NSUpArrowFunctionKey)!): "↑",
            String(UnicodeScalar(NSDownArrowFunctionKey)!): "↓",
            String(UnicodeScalar(NSLeftArrowFunctionKey)!): "←",
            String(UnicodeScalar(NSRightArrowFunctionKey)!): "→",
            String(UnicodeScalar(NSHomeFunctionKey)!): "↖",
            String(UnicodeScalar(NSEndFunctionKey)!): "↘",
            String(UnicodeScalar(NSPageUpFunctionKey)!): "⇞",
            String(UnicodeScalar(NSPageDownFunctionKey)!): "⇟",
            String(UnicodeScalar(NSDeleteFunctionKey)!): "⌦"
        ]
        if let name = names[key] { return result + name }
        if let scalar = key.unicodeScalars.first, key.unicodeScalars.count == 1,
           scalar.value >= UInt32(NSF1FunctionKey), scalar.value <= UInt32(NSF35FunctionKey) {
            return result + "F\(scalar.value - UInt32(NSF1FunctionKey) + 1)"
        }
        return result + key.uppercased(with: Loc.locale)
    }
}

@MainActor
struct ShortcutHelpView: View {
    @ObservedObject var model: ShortcutHelpModel
    @State private var query = ""

    private var filtered: [ShortcutEntry] {
        let text = query.trimmingCharacters(in: .whitespacesAndNewlines)
        guard !text.isEmpty else { return model.entries }
        return model.entries.filter {
            $0.title.localizedStandardContains(text) || $0.keys.localizedStandardContains(text) || $0.category.localizedStandardContains(text)
        }
    }

    private var categories: [String] {
        var seen = Set<String>()
        return filtered.compactMap { seen.insert($0.category).inserted ? $0.category : nil }
    }

    var body: some View {
        VStack(spacing: 0) {
            HStack(spacing: Metrics.s) {
                Image(systemName: "magnifyingglass").foregroundStyle(.secondary)
                TextField(tr("Search keyboard shortcuts", "키보드 단축키 검색"), text: $query)
                    .textFieldStyle(.plain)
                    .accessibilityLabel(tr("Search keyboard shortcuts", "키보드 단축키 검색"))
                if !query.isEmpty {
                    Button { query = "" } label: { Image(systemName: "xmark.circle.fill") }
                        .buttonStyle(.plain).foregroundStyle(.secondary)
                        .accessibilityLabel(tr("Clear search", "검색 지우기"))
                }
            }
            .padding(Metrics.m)
            .background(Color(nsColor: .controlBackgroundColor), in: RoundedRectangle(cornerRadius: Metrics.mediumRadius))
            .padding(Metrics.l)
            Divider()
            ScrollView {
                LazyVStack(alignment: .leading, spacing: Metrics.l, pinnedViews: []) {
                    if filtered.isEmpty {
                        Label(tr("No shortcuts found", "단축키를 찾을 수 없음"), systemImage: "magnifyingglass")
                            .foregroundStyle(.secondary)
                            .frame(maxWidth: .infinity).padding(.top, Metrics.xl)
                    }
                    ForEach(categories, id: \.self) { category in
                        VStack(alignment: .leading, spacing: Metrics.s) {
                            Text(category).font(Font(Typography.title())).padding(.bottom, Metrics.xs)
                            ForEach(filtered.filter { $0.category == category }) { entry in
                                HStack(alignment: .firstTextBaseline, spacing: Metrics.l) {
                                    Text(entry.keys)
                                        .font(.system(size: Typography.list(model.settings.textSize).pointSize, weight: .medium))
                                        .padding(.horizontal, Metrics.s).padding(.vertical, Metrics.xs)
                                        .background(Color(nsColor: .controlBackgroundColor), in: RoundedRectangle(cornerRadius: Metrics.smallRadius))
                                        .overlay(RoundedRectangle(cornerRadius: Metrics.smallRadius).stroke(Color(nsColor: Palette.separator), lineWidth: 1))
                                        .frame(width: 148, alignment: .trailing)
                                    Text(entry.title).frame(maxWidth: .infinity, alignment: .leading)
                                }
                                .accessibilityElement(children: .combine)
                            }
                        }
                    }
                }
                .padding(Metrics.xl)
            }
        }
        .font(Font(Typography.list(model.settings.textSize)))
        .background(Color(nsColor: .windowBackgroundColor))
        .preferredColorScheme(model.settings.appearance.colorScheme)
        .frame(minWidth: 570, minHeight: 450)
    }
}

@MainActor
final class ShortcutHelpWindowController: NSWindowController {
    static let shared = ShortcutHelpWindowController()
    private let model = ShortcutHelpModel()

    private init() {
        let window = NSWindow(contentRect: NSRect(x: 0, y: 0, width: 660, height: 680),
                              styleMask: [.titled, .closable, .miniaturizable, .resizable], backing: .buffered, defer: false)
        super.init(window: window)
        window.title = tr("Keyboard Shortcuts", "키보드 단축키")
        window.contentViewController = NSHostingController(rootView: ShortcutHelpView(model: model))
        window.minSize = NSSize(width: 590, height: 490)
        window.isReleasedWhenClosed = false
        window.setFrameAutosaveName("ShortcutHelpWindow")
        window.center()
    }

    required init?(coder: NSCoder) { nil }

    func show() {
        model.reload()
        window?.title = tr("Keyboard Shortcuts", "키보드 단축키")
        showWindow(nil)
        window?.makeKeyAndOrderFront(nil)
        NSApp.activate(ignoringOtherApps: true)
    }
}
