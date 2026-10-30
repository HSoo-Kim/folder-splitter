import AppKit
import SwiftUI

@MainActor
struct SettingsView: View {
    @ObservedObject var model: SettingsModel
    @State private var confirmForgetHistory = false

    var body: some View {
        Form {
            switch model.selectedTab {
            case .general: general
            case .appearance: appearance
            case .search: search
            }
        }
        .formStyle(.grouped)
        .font(Font(Typography.list(model.settings.textSize)))
        .frame(minWidth: 590, minHeight: 560)
        .preferredColorScheme(model.settings.appearance.colorScheme)
        .alert(tr("The setting couldn’t be changed.", "설정을 변경할 수 없습니다."),
               isPresented: Binding(get: { model.errorMessage != nil }, set: { if !$0 { model.errorMessage = nil } })) {
            Button(tr("OK", "확인"), role: .cancel) { model.errorMessage = nil }
        } message: { Text(model.errorMessage ?? "") }
        .confirmationDialog(tr("Forget Frequently Opened Folders?", "자주 여는 폴더를 지우겠습니까?"),
                            isPresented: $confirmForgetHistory, titleVisibility: .visible) {
            Button(tr("Forget Folders", "폴더 기록 지우기"), role: .destructive) {
                AppServices.shared?.history.forgetAll()
            }
            Button(tr("Cancel", "취소"), role: .cancel) {}
        } message: {
            Text(tr("Recent folders and search ranking history will be cleared. Your files and favorites won’t change.",
                    "최근 사용한 폴더와 검색 순위 기록을 지웁니다. 파일 및 즐겨찾기는 변경되지 않습니다."))
        }
    }

    private var general: some View {
        Group {
            Section {
                Picker(tr("Language", "언어"), selection: model.binding(\.language)) {
                    Text(tr("System", "시스템 설정 사용")).tag(AppLanguage.system)
                    Text(tr("English", "English")).tag(AppLanguage.english)
                    Text(tr("한국어", "한국어")).tag(AppLanguage.korean)
                }
                if model.settings.language != model.initialLanguage {
                    HStack {
                        Text(tr("Restart to apply the language everywhere.", "앱 전체에 언어를 적용하려면 재시동하십시오."))
                            .foregroundStyle(.secondary)
                        Spacer()
                        Button(tr("Restart", "재시동")) { AppRelauncher.relaunch() }
                    }
                }
                Toggle(tr("Open at login", "로그인 시 열기"), isOn: Binding(get: { model.loginEnabled }, set: { model.setLoginEnabled($0) }))
                if model.loginNeedsApproval {
                    HStack(alignment: .top) {
                        Text(tr("Allow Folder Splitter in System Settings → General → Login Items & Extensions.",
                                "시스템 설정 → 일반 → 로그인 항목 및 확장 프로그램에서 Folder Splitter를 허용하십시오."))
                            .font(.callout).foregroundStyle(.secondary)
                        Button(tr("Open Settings", "설정 열기")) { SMLoginSettings.open() }
                    }
                }
                Toggle(tr("Restore windows on launch", "실행 시 윈도우 복원"), isOn: model.binding(\.restoreSessionOnLaunch))
            }
            Section {
                Toggle(tr("Use as default file viewer", "기본 파일 뷰어로 사용"),
                       isOn: Binding(get: { model.defaultViewer }, set: { model.setDefaultViewer($0) }))
            } footer: {
                Text(tr("“Show in Finder” from other apps opens Folder Splitter. The Desktop and folders in the Dock still belong to Finder. Turn this off to restore Finder as the default viewer.",
                        "다른 앱에서 ‘Finder에서 보기’를 선택하면 Folder Splitter가 열립니다. 데스크탑과 Dock의 폴더는 계속 Finder에서 관리합니다. 이 설정을 끄면 Finder가 기본 뷰어로 복원됩니다."))
                    .multilineTextAlignment(.leading).frame(maxWidth: .infinity, alignment: .leading)
            }
            Section {
                Picker(tr("Global shortcut", "전역 단축키"), selection: model.binding(\.globalHotKey)) {
                    ForEach(GlobalHotKeyChoice.allCases, id: \.self) { choice in
                        Text(choice.displayName).tag(choice)
                    }
                }
                if GlobalHotKey.shared.lastRegistrationStatus != noErr {
                    Text(tr("Couldn’t register this shortcut. It may be used by another app. Choose a different shortcut.",
                            "이 단축키를 등록할 수 없습니다. 다른 앱에서 사용 중일 수 있습니다. 다른 단축키를 선택하십시오."))
                        .foregroundStyle(.red).font(.callout)
                }
            } footer: {
                Text(tr("Bring Folder Splitter to the front from any app. Press again to hide it. No Accessibility permission is needed.",
                        "어떤 앱에서든 Folder Splitter를 앞으로 가져옵니다. 다시 누르면 가립니다. 손쉬운 사용 권한은 필요하지 않습니다."))
                    .multilineTextAlignment(.leading).frame(maxWidth: .infinity, alignment: .leading)
            }
            Section {
                FullDiskAccessRow(granted: model.fullDiskAccess)
            } footer: {
                Text(tr("Full Disk Access lets whole-disk search include protected folders without separate folder-access prompts. After granting access, restart Folder Splitter and rebuild the index.",
                        "전체 디스크 접근 권한을 허용하면 폴더별 접근 요청 없이 보호된 폴더까지 검색할 수 있습니다. 권한을 허용한 후 Folder Splitter를 재시동하고 색인을 재구성하십시오."))
                    .multilineTextAlignment(.leading).frame(maxWidth: .infinity, alignment: .leading)
            }
        }
    }

    private var appearance: some View {
        Group {
            Section {
                Picker(tr("Appearance", "모양"), selection: model.binding(\.appearance)) {
                    Text(tr("System", "시스템 설정 사용")).tag(AppearanceMode.system)
                    Text(tr("Light", "라이트 모드")).tag(AppearanceMode.light)
                    Text(tr("Dark", "다크 모드")).tag(AppearanceMode.dark)
                }
                Picker(tr("Text size", "텍스트 크기"), selection: model.binding(\.textSize)) {
                    Text(tr("Small", "작게")).tag(TextSize.small)
                    Text(tr("Medium", "중간")).tag(TextSize.medium)
                    Text(tr("Large", "크게")).tag(TextSize.large)
                }
                Picker(tr("Row spacing", "행 간격"), selection: model.binding(\.rowDensity)) {
                    Text(tr("Compact", "좁게")).tag(RowDensity.compact)
                    Text(tr("Comfortable", "넓게")).tag(RowDensity.comfortable)
                }
                VStack(alignment: .leading, spacing: Metrics.s) {
                    Text(tr("Active pane color", "활성 영역 색상"))
                    HStack(spacing: Metrics.s) {
                        ForEach(PaneHighlight.allCases, id: \.self) { choice in
                            Button {
                                SettingsStore.shared.update { $0.paneHighlight = choice }
                            } label: {
                                HStack(spacing: Metrics.xs) {
                                    Circle().fill(Color(nsColor: Palette.paneHighlight(choice)))
                                        .frame(width: 12, height: 12)
                                    Text(choice.title)
                                    if model.settings.paneHighlight == choice {
                                        Image(systemName: "checkmark").font(.caption.weight(.semibold))
                                    }
                                }
                                .padding(.horizontal, Metrics.xs)
                                .frame(minHeight: 22)
                            }
                            .buttonStyle(.bordered)
                            .accessibilityLabel(choice.title)
                            .accessibilityAddTraits(model.settings.paneHighlight == choice ? .isSelected : [])
                        }
                    }
                }
            }
            Section(tr("Files and Folders", "파일 및 폴더")) {
                Toggle(tr("Show hidden files", "숨김 파일 보기"), isOn: model.binding(\.showHiddenFiles))
                Toggle(tr("Show all filename extensions", "모든 파일 확장자 보기"), isOn: model.binding(\.showAllExtensions))
                Toggle(tr("Keep folders on top", "폴더를 상단에 유지"), isOn: model.binding(\.foldersFirst))
            }
            Section(tr("Window", "윈도우")) {
                Toggle(tr("Show folder tree", "폴더 트리 보기"), isOn: model.binding(\.showFolderTree))
                Toggle(tr("Show status bar", "상태 막대 보기"), isOn: model.binding(\.showStatusBar))
                Toggle(tr("Show details under preview", "미리보기 아래에 세부사항 보기"), isOn: model.binding(\.showPreviewDetails))
            }
        }
    }

    private var search: some View {
        Group {
            Section {
                Toggle(tr("Enable search index", "검색 색인 활성화"), isOn: model.binding(\.searchIndexEnabled))
                HStack {
                    VStack(alignment: .leading, spacing: Metrics.xs) {
                        Text(tr("Search Index", "검색 색인"))
                        Text(indexDescription).foregroundStyle(.secondary).textSelection(.enabled)
                    }
                    Spacer()
                    if isIndexBusy { ProgressView().controlSize(.small) }
                    Button(tr("Rebuild Index", "색인 재구성")) { SettingsHooks.rebuildSearchIndex?() }
                        .disabled(!model.settings.searchIndexEnabled || SettingsHooks.rebuildSearchIndex == nil)
                }
            } footer: {
                Text(tr("Search uses file and folder names, never file contents.", "파일 내용이 아닌 파일 및 폴더 이름만 검색합니다."))
                    .multilineTextAlignment(.leading).frame(maxWidth: .infinity, alignment: .leading)
            }
            Section(tr("Excluded Folders", "제외된 폴더")) {
                ForEach(model.settings.searchExcludedFolders, id: \.self) { path in
                    HStack {
                        Label(path, systemImage: "folder").textSelection(.enabled).lineLimit(2)
                        Spacer()
                        Button { model.removeExcludedFolder(path) } label: { Image(systemName: "minus.circle") }
                            .buttonStyle(.borderless)
                            .accessibilityLabel(tr("Remove exclusion: \(path)", "제외 항목 제거: \(path)"))
                    }
                }
                Button(tr("Add Folder…", "폴더 추가…")) { model.addExcludedFolder() }
                if !model.defaultExclusions.isEmpty {
                    DisclosureGroup(tr("Always Excluded", "항상 제외됨")) {
                        ForEach(model.defaultExclusions, id: \.self) { path in
                            Text(path).font(.callout).foregroundStyle(.secondary).textSelection(.enabled)
                        }
                    }
                }
            }
            if !model.deniedPaths.isEmpty {
                Section(tr("Folders macOS Denied", "macOS에서 접근을 거부한 폴더")) {
                    ForEach(model.deniedPaths, id: \.self) { path in
                        Label(path, systemImage: "lock").textSelection(.enabled).lineLimit(2)
                    }
                    Text(tr("Grant Full Disk Access, or allow these folders in Files and Folders, then rebuild the index.",
                            "전체 디스크 접근 권한을 부여하거나 파일 및 폴더에서 이 폴더에 대한 접근을 허용한 다음 색인을 재구성하십시오."))
                        .font(.callout).foregroundStyle(.secondary)
                    Button(tr("Open Files and Folders Settings", "파일 및 폴더 설정 열기")) { PrivacyFolders.openFilesAndFoldersSettings() }
                }
            }
            Section {
                Button(tr("Forget Frequently Opened Folders…", "자주 여는 폴더 기록 지우기…")) { confirmForgetHistory = true }
            } footer: {
                Text(tr("Folder history helps rank search results. Forgetting it does not delete any files.",
                        "폴더 기록은 검색 결과의 순위를 정하는 데 사용됩니다. 기록을 지워도 파일은 삭제되지 않습니다."))
                    .multilineTextAlignment(.leading).frame(maxWidth: .infinity, alignment: .leading)
            }
        }
    }

    private var isIndexBusy: Bool {
        switch model.indexState {
        case .loading, .building: return true
        default: return false
        }
    }

    private var indexDescription: String {
        switch model.indexState {
        case .disabled: return tr("Indexing is off", "색인 꺼짐")
        case .loading: return tr("Loading index…", "색인 불러오는 중…")
        case .building(let scanned):
            return tr("Building index · \(scanned.formatted()) items scanned", "색인 구성 중 · 항목 \(scanned.formatted())개 확인됨")
        case .ready(let entries):
            return tr("\(entries.formatted()) items indexed", "항목 \(entries.formatted())개 색인됨")
        case .failed(let message): return tr("Index unavailable: \(message)", "색인을 사용할 수 없음: \(message)")
        }
    }
}

@MainActor
struct FullDiskAccessRow: View {
    let granted: Bool?
    var body: some View {
        HStack {
            VStack(alignment: .leading, spacing: Metrics.xs) {
                Text(tr("Full Disk Access", "전체 디스크 접근 권한"))
                Label(status, systemImage: granted == true ? "checkmark.circle.fill" : "lock.shield")
                    .foregroundStyle(granted == true ? Color(nsColor: .systemGreen) : .secondary)
                    .font(.callout)
            }
            Spacer()
            Button(tr("Open Privacy & Security Settings", "개인정보 보호 및 보안 설정 열기")) { FullDiskAccess.openPrivacySettings() }
        }
    }
    private var status: String {
        guard let granted else { return tr("Checking access…", "접근 권한 확인 중…") }
        return granted ? tr("Allowed", "허용됨") : tr("Not granted", "허용되지 않음")
    }
}

extension AppearanceMode {
    var colorScheme: ColorScheme? {
        switch self {
        case .system: return nil
        case .light: return .light
        case .dark: return .dark
        }
    }
}

private extension PaneHighlight {
    var title: String {
        switch self {
        case .accent: return tr("Accent", "강조 색상")
        case .blue: return tr("Blue", "파란색")
        case .green: return tr("Green", "초록색")
        case .orange: return tr("Orange", "주황색")
        }
    }
}

private enum SMLoginSettings {
    @MainActor static func open() {
        if let url = URL(string: "x-apple.systempreferences:com.apple.LoginItems-Settings.extension") {
            NSWorkspace.shared.open(url)
        }
    }
}
