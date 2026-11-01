import Foundation

/// Turns any URL the user asked to open into "show this folder, select these items".
/// Folders and volumes open as themselves; files, packages and apps open their parent with the
/// item selected; aliases and symbolic links to folders open the target folder.
enum LocationResolver {
    struct Target: Sendable, Equatable {
        let folder: URL
        let selection: [URL]
    }

    static func resolve(_ url: URL) async -> Target {
        await Task.detached(priority: .userInitiated) { resolveNow(url) }.value
    }

    /// Synchronous variant for background contexts. Touches the disk.
    static func resolveNow(_ url: URL) -> Target {
        let standardized = url.standardizedFileURL
        let keys: Set<URLResourceKey> = [.isDirectoryKey, .isPackageKey, .isAliasFileKey, .isSymbolicLinkKey]
        guard let values = try? standardized.resourceValues(forKeys: keys) else {
            // Missing item: open it anyway so the list can explain what is wrong.
            return Target(folder: standardized, selection: [])
        }
        if values.isAliasFile == true || values.isSymbolicLink == true {
            let resolved = values.isSymbolicLink == true
                ? standardized.resolvingSymlinksInPath()
                : (try? URL(resolvingAliasFileAt: standardized, options: [.withoutUI, .withoutMounting]))
            if let resolved,
               let target = try? resolved.resourceValues(forKeys: [.isDirectoryKey, .isPackageKey]),
               target.isDirectory == true, target.isPackage != true {
                return Target(folder: resolved.standardizedFileURL, selection: [])
            }
        }
        if values.isDirectory == true && values.isPackage != true {
            return Target(folder: standardized, selection: [])
        }
        return Target(folder: standardized.deletingLastPathComponent(), selection: [standardized])
    }
}
