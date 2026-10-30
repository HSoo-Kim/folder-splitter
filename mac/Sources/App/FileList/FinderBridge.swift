import AppKit

/// Explicitly targets Finder rather than a file or folder's registered default application.
@MainActor
enum FinderBridge {
    static func reveal(_ urls: [URL]) {
        guard !urls.isEmpty else { return }
        guard let finder = finderURL else { reportMissingFinder(); return }
        // Finder's Open Documents event opens files; reveal instead selects their icons.
        let files = urls.map { "POSIX file " + quoted($0.path) }.joined(separator: ", ")
        let source = "tell application \(quoted(finder.path))\nreveal {\(files)}\nactivate\nend tell"
        var error: NSDictionary?
        if let script = NSAppleScript(source: source) {
            script.executeAndReturnError(&error)
            if error == nil { return }
        }
        // Automation can be denied. Opening the containing folders still provides a real
        // Finder destination, without passing the original files to their default handlers.
        var seen = Set<URL>()
        let parents = urls.map { $0.deletingLastPathComponent().standardizedFileURL }.filter { seen.insert($0).inserted }
        open(parents, using: finder)
    }

    static func open(_ folder: URL) {
        guard let finder = finderURL else { reportMissingFinder(); return }
        open([folder], using: finder)
    }

    private static var finderURL: URL? {
        NSWorkspace.shared.urlForApplication(withBundleIdentifier: "com.apple.finder")
    }

    private static func open(_ folders: [URL], using finder: URL) {
        let configuration = NSWorkspace.OpenConfiguration()
        configuration.activates = true
        NSWorkspace.shared.open(folders, withApplicationAt: finder, configuration: configuration) { _, error in
            guard let error else { return }
            Task { @MainActor in NSApp.presentError(error) }
        }
    }

    private static func quoted(_ value: String) -> String {
        "\"" + value.replacingOccurrences(of: "\\", with: "\\\\").replacingOccurrences(of: "\"", with: "\\\"") + "\""
    }

    private static func reportMissingFinder() {
        NSApp.presentError(NSError(domain: "FolderSplitter.Finder", code: 1,
                                   userInfo: [NSLocalizedDescriptionKey: tr("Finder could not be found.", "Finder를 찾을 수 없습니다.")]))
    }
}
