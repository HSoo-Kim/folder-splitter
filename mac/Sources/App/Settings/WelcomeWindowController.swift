import AppKit
import SwiftUI

@MainActor
struct WelcomeView: View {
    @ObservedObject var model: SettingsModel
    let onFinish: () -> Void
    @State private var makeDefault = false

    var body: some View {
        VStack(alignment: .leading, spacing: Metrics.xl) {
            HStack(spacing: Metrics.l) {
                Image(systemName: "rectangle.split.2x2")
                    .font(.system(size: 38, weight: .light))
                    .foregroundStyle(Color.accentColor)
                    .accessibilityHidden(true)
                VStack(alignment: .leading, spacing: Metrics.xs) {
                    Text(tr("Welcome to Folder Splitter", "Folder Splitter 시작하기"))
                        .font(Font(Typography.title(24))).tracking(Typography.titleKern)
                    Text(tr("Keep the folders you work with side by side.", "작업하는 폴더를 나란히 두고 사용하십시오."))
                        .foregroundStyle(.secondary)
                }
            }
            VStack(alignment: .leading, spacing: Metrics.m) {
                feature("rectangle.split.2x2", tr("Up to four panes in one window", "하나의 윈도우에서 최대 4개의 영역 사용"))
                feature("folder.badge.plus", tr("Separate tabs for each pane", "영역마다 독립적인 탭 사용"))
                feature("magnifyingglass", tr("Type a name in the address bar to search your disk", "주소 막대에 이름을 입력하여 디스크 검색"))
            }
            VStack(alignment: .leading, spacing: Metrics.m) {
                FullDiskAccessRow(granted: model.fullDiskAccess)
                Text(tr("Allow Full Disk Access for whole-disk search. Without it, macOS may ask separately for Desktop, Documents, and Downloads access, and some folders won’t appear in search.",
                        "디스크 전체를 검색하려면 전체 디스크 접근 권한을 허용하십시오. 권한이 없으면 macOS에서 데스크탑, 문서 및 다운로드 폴더에 대한 접근을 각각 요청할 수 있으며 일부 폴더는 검색되지 않습니다."))
                    .font(.callout).foregroundStyle(.secondary).fixedSize(horizontal: false, vertical: true)
            }
            .padding(Metrics.l)
            .background {
                if Motion.reduceTransparency { RoundedRectangle(cornerRadius: Metrics.largeRadius).fill(Color(nsColor: .controlBackgroundColor)) }
                else { RoundedRectangle(cornerRadius: Metrics.largeRadius).fill(.regularMaterial) }
            }
            .overlay(RoundedRectangle(cornerRadius: Metrics.largeRadius).stroke(Color(nsColor: Palette.separator), lineWidth: 1))

            VStack(alignment: .leading, spacing: Metrics.s) {
                Toggle(tr("Make Folder Splitter the default file viewer", "Folder Splitter를 기본 파일 뷰어로 설정"),
                       isOn: Binding(get: { makeDefault }, set: { enabled in
                           model.setDefaultViewer(enabled)
                           makeDefault = model.defaultViewer
                       }))
                Text(tr("“Show in Finder” requests will open here. Finder keeps the Desktop and Dock folders. You can undo this in Settings.",
                        "‘Finder에서 보기’ 요청을 이 앱에서 엽니다. 데스크탑과 Dock의 폴더는 Finder에서 관리합니다. 설정에서 언제든 되돌릴 수 있습니다."))
                    .font(.callout).foregroundStyle(.secondary).fixedSize(horizontal: false, vertical: true)
            }
            Label {
                if model.settings.globalHotKey == .off {
                    Text(tr("You can set a global show/hide shortcut in Settings.", "설정에서 앱 보기/가리기 전역 단축키를 지정할 수 있습니다."))
                } else {
                    Text(tr("Press \(model.settings.globalHotKey.displayName) from any app to show Folder Splitter. Press again to hide it.",
                            "어떤 앱에서든 \(model.settings.globalHotKey.displayName)을 눌러 Folder Splitter를 앞으로 가져오십시오. 다시 누르면 가립니다."))
                }
            } icon: { Image(systemName: "keyboard") }
            .foregroundStyle(.secondary).fixedSize(horizontal: false, vertical: true)
            HStack {
                Spacer()
                Button(tr("Get Started", "시작하기"), action: onFinish)
                    .buttonStyle(.borderedProminent).controlSize(.large)
                    .keyboardShortcut(.defaultAction)
            }
        }
        .padding(32)
        .font(Font(Typography.list(model.settings.textSize)))
        .frame(width: 650)
        .background(Color(nsColor: .windowBackgroundColor))
        .preferredColorScheme(model.settings.appearance.colorScheme)
        .alert(tr("The setting couldn’t be changed.", "설정을 변경할 수 없습니다."),
               isPresented: Binding(get: { model.errorMessage != nil }, set: { if !$0 { model.errorMessage = nil } })) {
            Button(tr("OK", "확인"), role: .cancel) { model.errorMessage = nil }
        } message: { Text(model.errorMessage ?? "") }
    }

    private func feature(_ symbol: String, _ title: String) -> some View {
        Label { Text(title) } icon: {
            Image(systemName: symbol).frame(width: 24).foregroundStyle(.secondary)
        }
    }
}

@MainActor
final class WelcomeWindowController: NSWindowController, NSWindowDelegate {
    private static var active: WelcomeWindowController?
    private var onFinish: (() -> Void)?
    private let model = SettingsModel()

    private init(onFinish: @escaping () -> Void) {
        self.onFinish = onFinish
        let window = NSWindow(contentRect: NSRect(x: 0, y: 0, width: 650, height: 660),
                              styleMask: [.titled, .closable], backing: .buffered, defer: false)
        super.init(window: window)
        window.title = tr("Welcome to Folder Splitter", "Folder Splitter 시작하기")
        window.contentViewController = NSHostingController(rootView: WelcomeView(model: model, onFinish: { [weak self] in
            self?.close()
        }))
        window.isReleasedWhenClosed = false
        window.delegate = self
        window.center()
    }

    required init?(coder: NSCoder) { nil }

    /// The launch coordinator checks hasShownWelcome before calling and persists it in
    /// onFinish. Closing the welcome window also completes onboarding exactly once.
    static func present(onFinish: @escaping () -> Void) {
        if let active {
            active.showWindow(nil)
            active.window?.makeKeyAndOrderFront(nil)
            return
        }
        let controller = WelcomeWindowController(onFinish: onFinish)
        active = controller
        controller.showWindow(nil)
        controller.window?.makeKeyAndOrderFront(nil)
        NSApp.bringForward()
    }

    func windowDidBecomeKey(_ notification: Notification) { model.refreshSystemState() }

    func windowWillClose(_ notification: Notification) {
        let finish = onFinish
        onFinish = nil
        Self.active = nil
        finish?()
    }
}
