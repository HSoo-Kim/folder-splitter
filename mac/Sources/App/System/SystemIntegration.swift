import AppKit
import Carbon
import CoreServices
import ServiceManagement
import Darwin

enum AppInstallLocation {
    static var isTranslocated: Bool { Bundle.main.bundleURL.path.contains("/AppTranslocation/") }

    static var moveToApplicationsMessage: String {
        tr("Move Folder Splitter to the Applications folder, then open it again.",
           "Folder Splitter를 응용 프로그램 폴더로 옮긴 다음 다시 여십시오.")
    }

    @MainActor static func showNoticeIfNeeded() {
        let path = Bundle.main.bundleURL.resolvingSymlinksInPath().standardizedFileURL.path
        guard isTranslocated || !path.hasPrefix("/Applications/") else { return }
        let key = "FolderSplitter.installLocationNoticePaths"
        var shown = UserDefaults.standard.stringArray(forKey: key) ?? []
        guard !shown.contains(path) else { return }
        shown.append(path)
        UserDefaults.standard.set(shown, forKey: key)
        let alert = NSAlert()
        alert.messageText = moveToApplicationsMessage
        alert.addButton(withTitle: tr("OK", "확인"))
        alert.runModal()
    }
}

/// Finder's reveal-service preference is global, not in the app's defaults domain.
/// Disabling always restores Finder; this does not replace Finder's Desktop or Dock.
enum DefaultFileViewer {
    private static let key = "NSFileViewer" as CFString
    private(set) static var lastError: String?

    static var isFolderSplitterDefault: Bool {
        guard let identifier = Bundle.main.bundleIdentifier else { return false }
        return preferredViewer == identifier
    }

    static var preferredViewer: String? {
        CFPreferencesCopyValue(key, kCFPreferencesAnyApplication,
                               kCFPreferencesCurrentUser, kCFPreferencesAnyHost) as? String
    }

    static func setEnabled(_ enabled: Bool) {
        lastError = nil
        guard !enabled || !AppInstallLocation.isTranslocated else {
            lastError = AppInstallLocation.moveToApplicationsMessage
            return
        }
        guard let identifier = Bundle.main.bundleIdentifier else {
            lastError = tr("This copy of Folder Splitter has no app identifier. Open the installed app and try again.",
                           "이 Folder Splitter에는 앱 식별자가 없습니다. 설치된 앱을 열고 다시 시도하십시오.")
            return
        }
        let previous = preferredViewer
        CFPreferencesSetValue(key, enabled ? identifier as CFString : nil,
                              kCFPreferencesAnyApplication, kCFPreferencesCurrentUser, kCFPreferencesAnyHost)
        guard CFPreferencesSynchronize(kCFPreferencesAnyApplication, kCFPreferencesCurrentUser, kCFPreferencesAnyHost) else {
            CFPreferencesSetValue(key, previous as CFString?, kCFPreferencesAnyApplication,
                                  kCFPreferencesCurrentUser, kCFPreferencesAnyHost)
            CFPreferencesSynchronize(kCFPreferencesAnyApplication, kCFPreferencesCurrentUser, kCFPreferencesAnyHost)
            lastError = tr("The default file viewer preference could not be saved.", "기본 파일 뷰어 설정을 저장할 수 없습니다.")
            return
        }
        let status = LSSetDefaultRoleHandlerForContentType("public.folder" as CFString, .viewer,
                                                          (enabled ? identifier : "com.apple.finder") as CFString)
        if status != noErr {
            CFPreferencesSetValue(key, previous as CFString?, kCFPreferencesAnyApplication,
                                  kCFPreferencesCurrentUser, kCFPreferencesAnyHost)
            CFPreferencesSynchronize(kCFPreferencesAnyApplication, kCFPreferencesCurrentUser, kCFPreferencesAnyHost)
            lastError = tr("macOS could not change the default folder viewer (error \(status)).",
                           "macOS에서 기본 폴더 뷰어를 변경할 수 없습니다(오류 \(status)).")
        }
    }
}

@MainActor
final class GlobalHotKey {
    static let shared = GlobalHotKey()
    private var hotKey: EventHotKeyRef?
    private var eventHandler: EventHandlerRef?
    private var action: (() -> Void)?
    private let signature: OSType = 0x4653504C // FSPL
    private(set) var lastRegistrationStatus: OSStatus = noErr
    var isRegistered: Bool { hotKey != nil }

    private init() {}

    /// Carbon receives this chord without Accessibility or Input Monitoring permission.
    /// The owner supplies show/hide behavior and calls this again when settings change.
    func apply(_ choice: GlobalHotKeyChoice, action: @escaping () -> Void) {
        if let hotKey { UnregisterEventHotKey(hotKey) }
        hotKey = nil
        self.action = action
        lastRegistrationStatus = noErr
        guard choice != .off else {
            if let eventHandler { RemoveEventHandler(eventHandler) }
            eventHandler = nil
            self.action = nil
            return
        }
        if eventHandler == nil {
            var type = EventTypeSpec(eventClass: OSType(kEventClassKeyboard), eventKind: UInt32(kEventHotKeyPressed))
            lastRegistrationStatus = InstallEventHandler(GetApplicationEventTarget(), { _, event, context in
                guard let event, let context else { return OSStatus(eventNotHandledErr) }
                var identifier = EventHotKeyID()
                let result = GetEventParameter(event, EventParamName(kEventParamDirectObject),
                                               EventParamType(typeEventHotKeyID), nil,
                                               MemoryLayout<EventHotKeyID>.size, nil, &identifier)
                guard result == noErr else { return result }
                return MainActor.assumeIsolated {
                    let owner = Unmanaged<GlobalHotKey>.fromOpaque(context).takeUnretainedValue()
                    guard identifier.signature == owner.signature, identifier.id == 1 else {
                        return OSStatus(eventNotHandledErr)
                    }
                    owner.action?()
                    return noErr
                }
            }, 1, &type, Unmanaged.passUnretained(self).toOpaque(), &eventHandler)
            guard lastRegistrationStatus == noErr else { return }
        }
        let keyCode = choice == .controlOptionCommandF ? kVK_ANSI_F : kVK_ANSI_E
        lastRegistrationStatus = RegisterEventHotKey(UInt32(keyCode), UInt32(controlKey | optionKey | cmdKey),
                                                     EventHotKeyID(signature: signature, id: 1),
                                                     GetApplicationEventTarget(), 0, &hotKey)
    }
}

enum FullDiskAccess {
    /// Read-only TCC probes: these locations are Full Disk Access protected, rather than
    /// Desktop/Documents consent locations. A denial never asks for per-folder consent.
    /// A missing probe is not evidence that permission was granted.
    static var isGranted: Bool {
        let home = FileManager.default.homeDirectoryForCurrentUser
        for relative in ["Library/Safari", "Library/Mail", "Library/Messages"] {
            let path = home.appendingPathComponent(relative).path
            guard let directory = opendir(path) else { continue }
            errno = 0
            _ = readdir(directory)
            let readable = errno == 0
            closedir(directory)
            if readable { return true }
        }
        return false
    }

    @MainActor static func openPrivacySettings() {
        if let url = URL(string: "x-apple.systempreferences:com.apple.preference.security?Privacy_AllFiles") {
            NSWorkspace.shared.open(url)
        }
    }
}

enum PrivacyFolders {
    @MainActor static func openFilesAndFoldersSettings() {
        if let url = URL(string: "x-apple.systempreferences:com.apple.preference.security?Privacy_FilesAndFolders") {
            NSWorkspace.shared.open(url)
        }
    }
}

enum LoginItem {
    static var isEnabled: Bool {
        SMAppService.mainApp.status == .enabled || SMAppService.mainApp.status == .requiresApproval
    }
    static var needsApproval: Bool { SMAppService.mainApp.status == .requiresApproval }

    static func setEnabled(_ enabled: Bool) throws {
        guard !enabled || !AppInstallLocation.isTranslocated else {
            throw NSError(domain: "FolderSplitter.InstallLocation", code: 1,
                          userInfo: [NSLocalizedDescriptionKey: AppInstallLocation.moveToApplicationsMessage])
        }
        if enabled { try SMAppService.mainApp.register() }
        else { try SMAppService.mainApp.unregister() }
    }
}

enum AppRelauncher {
    /// Single-quote shell escaping is essential: application paths can contain apostrophes.
    static func command(bundlePath: String, processID: Int32) -> String {
        let quoted = "'" + bundlePath.replacingOccurrences(of: "'", with: "'\\''") + "'"
        return "while /bin/kill -0 \(processID) 2>/dev/null; do /bin/sleep 0.1; done; exec /usr/bin/open -n \(quoted)"
    }

    @MainActor static func relaunch() {
        guard AppServices.shared?.fileOperations.hasRunningOperations != true else {
            let alert = NSAlert()
            alert.messageText = tr("Wait for file operations to finish before restarting.",
                                   "파일 작업이 완료된 다음 재시동하십시오.")
            alert.addButton(withTitle: tr("OK", "확인"))
            alert.runModal()
            return
        }
        let task = Process()
        task.executableURL = URL(fileURLWithPath: "/usr/bin/nohup")
        task.arguments = ["/bin/sh", "-c", command(bundlePath: Bundle.main.bundlePath,
                                        processID: ProcessInfo.processInfo.processIdentifier)]
        task.standardInput = FileHandle.nullDevice
        task.standardOutput = FileHandle.nullDevice
        task.standardError = FileHandle.nullDevice
        do {
            try task.run()
            NSApp.terminate(nil)
        } catch {
            let alert = NSAlert()
            alert.messageText = tr("Couldn’t Restart Folder Splitter", "Folder Splitter를 재시동할 수 없음")
            alert.informativeText = error.localizedDescription
            alert.runModal()
        }
    }
}
