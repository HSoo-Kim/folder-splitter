import AppKit

extension Notification.Name {
    /// Posted on the main thread whenever `UpdateController.shared` changes what it shows.
    static let updateStateDidChange = Notification.Name("FolderSplitter.updateStateDidChange")
}

/// Watches GitHub for a newer Mac release, like the Windows app: at launch, every hour while
/// running, and when the app comes back after 15 minutes or more. Only release information is
/// read; nothing about the user or their files is sent. Installing happens only when asked.
@MainActor
final class UpdateController {
    static let shared = UpdateController()

    enum Phase: Equatable {
        case idle
        case installing(progress: Double?)
        /// The new bundle is in place, even if restarting must wait for file operations.
        case installed
    }

    static let checkInterval: TimeInterval = 60 * 60
    static let recheckAfter: TimeInterval = 15 * 60

    let current: AppVersion = AppVersion(Bundle.main.infoDictionary?["CFBundleShortVersionString"] as? String ?? "")
        ?? AppVersion("0.0.0")!
    private(set) var available: ReleaseInfo?
    private(set) var phase: Phase = .idle
    /// The version whose notice the user closed with Later (the footer link stays).
    private var dismissedVersion: AppVersion?
    private var lastCheck = Date.distantPast
    private var timer: Timer?
    private var checking = false
    private var started = false
    private var feed = UpdateFeed.defaultURL
    /// QA sets this to false so installing doesn't restart the app under test.
    var relaunchAfterInstall = true

    var isEnabled: Bool { SettingsStore.shared.settings.checkForUpdates }
    var canInstallAvailable: Bool { available.map { UpdateInstaller.canInstall($0, replacing: Bundle.main.bundleURL) } ?? false }

    /// Whether a window should show the update notice now.
    var wantsNotice: Bool {
        guard let available, phase == .idle else { return false }
        return dismissedVersion != available.version
    }

    func start(feed: URL? = nil) {
        guard !started else { return }
        started = true
        if let feed { self.feed = feed }
        NotificationCenter.default.addObserver(forName: .settingsDidChange, object: nil, queue: .main) { [weak self] note in
            MainActor.assumeIsolated {
                guard let self, let old = note.userInfo?["old"] as? AppSettings,
                      old.checkForUpdates != self.isEnabled else { return }
                self.applyEnabled()
            }
        }
        NotificationCenter.default.addObserver(forName: NSApplication.didBecomeActiveNotification, object: nil, queue: .main) { [weak self] _ in
            MainActor.assumeIsolated { self?.checkIfStale() }
        }
        applyEnabled()
    }

    private func applyEnabled() {
        timer?.invalidate()
        timer = nil
        guard isEnabled else {
            available = nil
            post()
            return
        }
        timer = Timer.scheduledTimer(withTimeInterval: Self.checkInterval, repeats: true) { [weak self] _ in
            MainActor.assumeIsolated { self?.checkInBackground() }
        }
        timer?.tolerance = 60
        // Let the first window settle before touching the network.
        DispatchQueue.main.asyncAfter(deadline: .now() + 3) { [weak self] in self?.checkInBackground() }
    }

    private func checkIfStale() {
        guard started, isEnabled, Date().timeIntervalSince(lastCheck) >= Self.recheckAfter else { return }
        checkInBackground()
    }

    private func checkInBackground() {
        Task { await check(userInitiated: false) }
    }

    /// Automatic checks fail silently; a check the user asked for reports the outcome.
    func check(userInitiated: Bool) async {
        guard !checking, phase == .idle else { return }
        guard isEnabled || userInitiated else { return }
        checking = true
        defer { checking = false }
        lastCheck = Date()
        do {
            let newer = try await UpdateFeed.fetchNewer(than: current, feed: feed)
            available = newer
            // Asking again brings the notice back even if it was put off with Later.
            if userInitiated && newer != nil { dismissedVersion = nil }
            post()
            if userInitiated && newer == nil {
                inform(tr("Folder Splitter is up to date.", "Folder Splitter가 최신 버전입니다."),
                       tr("Version \(current) is the newest version available.", "버전 \(current)이(가) 사용 가능한 최신 버전입니다."))
            }
        } catch {
            if userInitiated {
                inform(tr("Couldn’t check for updates.", "업데이트를 확인할 수 없습니다."), error.localizedDescription)
            }
        }
    }

    /// Later: hide the notice for this version; the footer link stays.
    func dismissNotice() {
        dismissedVersion = available?.version
        post()
    }

    /// Update: install in place and relaunch when this copy can be replaced, otherwise open the
    /// release page so the user can download it.
    func install() async {
        guard let release = available, phase == .idle else { return }
        guard AppServices.shared?.fileOperations.hasRunningOperations != true else {
            inform(tr("Wait for file operations to finish before updating.",
                      "파일 작업이 완료된 다음 업데이트하십시오."), "")
            return
        }
        guard canInstallAvailable else {
            NSWorkspace.shared.open(release.pageURL)
            dismissedVersion = release.version
            post()
            return
        }
        phase = .installing(progress: nil)
        post()
        do {
            try await UpdateInstaller.install(release, replacing: Bundle.main.bundleURL,
                                              bundleIdentifier: Bundle.main.bundleIdentifier ?? "",
                                              progress: { fraction in
                Task { @MainActor in
                    guard case .installing = UpdateController.shared.phase else { return }
                    UpdateController.shared.phase = .installing(progress: fraction)
                    UpdateController.shared.post()
                }
            })
        } catch {
            phase = .idle
            post()
            let alert = NSAlert()
            alert.alertStyle = .warning
            alert.messageText = tr("The update couldn’t be installed.", "업데이트를 설치할 수 없습니다.")
            alert.informativeText = error.localizedDescription + "\n\n"
                + tr("Folder Splitter wasn’t changed. You can download the new version from the release page.",
                     "Folder Splitter는 변경되지 않았습니다. 릴리스 페이지에서 새로운 버전을 다운로드할 수 있습니다.")
            alert.addButton(withTitle: tr("Open Release Page", "릴리스 페이지 열기"))
            alert.addButton(withTitle: tr("Cancel", "취소"))
            if alert.runModal() == .alertFirstButtonReturn { NSWorkspace.shared.open(release.pageURL) }
            return
        }
        phase = .installed
        post()
        if relaunchAfterInstall { AppRelauncher.relaunch() }
    }

    private func inform(_ message: String, _ detail: String) {
        let alert = NSAlert()
        alert.messageText = message
        alert.informativeText = detail
        alert.addButton(withTitle: tr("OK", "확인"))
        alert.runModal()
    }

    private func post() {
        NotificationCenter.default.post(name: .updateStateDidChange, object: self)
    }
}
