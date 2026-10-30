import AppKit

/// One tab: its folder, its own back/forward history and (once shown) its file list.
@MainActor
final class TabState {
    static let historyLimit = 50

    let id: UUID
    private(set) var folder: URL?
    private(set) var back: [URL]
    private(set) var forward: [URL]
    /// Created the first time the tab is shown, then kept so scroll and selection survive tab switches.
    var list: FileListViewController?
    /// Small folder icon for the tab, loaded off the main thread.
    var icon: NSImage?
    /// Finder display name of `folder`, resolved off the main thread (see `quickName` until then).
    var resolvedTitle: String?

    init(id: UUID = UUID(), folder: URL?) {
        self.id = id
        self.folder = folder
        back = []
        forward = []
    }

    init(snapshot: TabSnapshot) {
        id = snapshot.id
        folder = snapshot.path.map { URL(fileURLWithPath: $0, isDirectory: true) }
        back = snapshot.back.suffix(Self.historyLimit).map { URL(fileURLWithPath: $0, isDirectory: true) }
        forward = snapshot.forward.suffix(Self.historyLimit).map { URL(fileURLWithPath: $0, isDirectory: true) }
    }

    var snapshot: TabSnapshot {
        TabSnapshot(id: id, path: folder?.path, back: back.map(\.path), forward: forward.map(\.path))
    }

    var isBlank: Bool { folder == nil }

    var title: String {
        guard let folder else { return tr("New Tab", "새로운 탭") }
        return resolvedTitle ?? TabState.quickName(of: folder)
    }

    var canGoBack: Bool { !back.isEmpty }
    var canGoForward: Bool { !forward.isEmpty }

    /// Moves to `url`, recording the current folder in the back history.
    func navigate(to url: URL) {
        let target = url.standardizedFileURL
        guard target != folder else { return }
        if let folder {
            back.append(folder)
            if back.count > Self.historyLimit { back.removeFirst(back.count - Self.historyLimit) }
        }
        forward.removeAll()
        folder = target
        icon = nil
        resolvedTitle = nil
    }

    /// Steps `steps` entries back (1 = previous folder). Returns the folder to show.
    func goBack(_ steps: Int = 1) -> URL? {
        guard steps >= 1, steps <= back.count, let current = folder else { return nil }
        var moved = [current]
        for _ in 1..<steps { moved.append(back.removeLast()) }
        let target = back.removeLast()
        forward.append(contentsOf: moved)
        folder = target
        icon = nil
        resolvedTitle = nil
        return target
    }

    func goForward(_ steps: Int = 1) -> URL? {
        guard steps >= 1, steps <= forward.count, let current = folder else { return nil }
        var moved = [current]
        for _ in 1..<steps { moved.append(forward.removeLast()) }
        let target = forward.removeLast()
        back.append(contentsOf: moved)
        folder = target
        icon = nil
        resolvedTitle = nil
        return target
    }

    /// Finder's display name: localized for standard folders, volume name for "/".
    /// Touches the disk — call it off the main thread for anything that may be a network volume.
    nonisolated static func displayName(of url: URL) -> String {
        let name = FileManager.default.displayName(atPath: url.path)
        return name.isEmpty ? url.lastPathComponent : PathText.nfc(name)
    }

    /// A name that needs no disk access: localized titles for the standard folders, the file
    /// name otherwise. Used on the main thread for menus and until `displayName` is resolved.
    static func quickName(of url: URL) -> String {
        let path = url.standardizedFileURL.path
        if let place = StandardFolder.allCases.first(where: { $0.url?.standardizedFileURL.path == path }) { return place.title }
        if path == "/" { return "Macintosh HD" }
        return PathText.nfc(url.lastPathComponent)
    }
}
