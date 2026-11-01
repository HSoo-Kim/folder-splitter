import Foundation
import Darwin

/// Reads only metadata supplied by the listing or legacy Dropbox attribute names.
/// Root aliases belong to the listing's off-main root preparation, not per-item I/O.
enum CloudStatusReader {
    /// Test/listing override; mutations must not overlap readers.
    nonisolated(unsafe) static var dropboxRootsOverride: [URL]? {
        didSet { overrideRootPaths = dropboxRootsOverride.map { roots in roots.filter(\.isFileURL).map(RootPath.init) } }
    }
    nonisolated(unsafe) private static var overrideRootPaths: [RootPath]?

    private static let configuredRoots = readDropboxRoots(at: FileManager.default.homeDirectoryForCurrentUser
        .appendingPathComponent(".dropbox/info.json"))
    private static let configuredRootPaths = configuredRoots.map(RootPath.init)

    private struct RootPath: Sendable {
        let path: String
        let prefix: String
        init(_ url: URL) {
            path = url.standardizedFileURL.path
            prefix = path == "/" ? path : path + "/"
        }
    }

    static func dropboxRoots() -> [URL] { dropboxRootsOverride ?? configuredDropboxRoots() }

    /// The actual Dropbox configuration, parsed once; never affected by the override.
    static func configuredDropboxRoots() -> [URL] { configuredRoots }

    /// Personal precedes business; all account entries with an absolute path are supported.
    static func readDropboxRoots(at url: URL) -> [URL] {
        guard let data = try? Data(contentsOf: url),
              let object = try? JSONSerialization.jsonObject(with: data) as? [String: Any] else { return [] }
        var roots: [URL] = []
        var seen: Set<String> = []
        let accounts = ["personal", "business"] + object.keys.filter { $0 != "personal" && $0 != "business" }.sorted()
        for account in accounts {
            guard let entry = object[account] as? [String: Any], let path = entry["path"] as? String,
                  path.hasPrefix("/"), !path.contains("\0") else { continue }
            let root = URL(fileURLWithPath: path, isDirectory: true).standardizedFileURL
            if seen.insert(root.path).inserted { roots.append(root) }
        }
        return roots
    }

    static func status(for url: URL, values: URLResourceValues?, insideDropbox: Bool) -> CloudStatus {
        // No resource query here: the listing opts into prefetching only for ubiquitous folders.
        if let values, let status = ubiquitousStatus(
            isUbiquitous: values.isUbiquitousItem == true,
            downloadingStatus: values.ubiquitousItemDownloadingStatus,
            isDownloading: values.ubiquitousItemIsDownloading == true,
            isUploading: values.ubiquitousItemIsUploading == true,
            hasUnresolvedConflicts: values.ubiquitousItemHasUnresolvedConflicts == true
        ) { return status }
        guard insideDropbox, url.isFileURL else { return .none }
        let path = url.standardizedFileURL.path
        let roots = overrideRootPaths ?? configuredRootPaths
        guard roots.contains(where: { root in
            path == root.path || path.hasPrefix(root.prefix)
        }) else { return .none }
        return dropboxStatus(for: url)
    }
    /// Nil permits legacy Dropbox classification; .none means a downloaded ubiquitous item.
    static func ubiquitousStatus(isUbiquitous: Bool, downloadingStatus: URLUbiquitousItemDownloadingStatus?,
                                 isDownloading: Bool, isUploading: Bool,
                                 hasUnresolvedConflicts: Bool) -> CloudStatus? {
        guard isUbiquitous else { return nil }
        if hasUnresolvedConflicts { return .cloudConflict }
        if isDownloading { return .cloudDownloading }
        if isUploading { return .cloudUploading }
        if downloadingStatus == .notDownloaded { return .cloudNotDownloaded }
        return CloudStatus.none
    }


    private static func dropboxStatus(for url: URL) -> CloudStatus {
        url.withUnsafeFileSystemRepresentation { path in
            guard let path else { return .none }
            // Names alone are authoritative. Never read attribute payloads or follow a row's symlink.
            let count = listxattr(path, nil, 0, XATTR_NOFOLLOW)
            guard count > 0 else { return .none }
            return withUnsafeTemporaryAllocation(of: CChar.self, capacity: count) { names in
                let length = listxattr(path, names.baseAddress, names.count, XATTR_NOFOLLOW)
                guard length > 0, let start = names.baseAddress else { return .none }
                var ignored = false
                var synced = false
                var offset = 0
                while offset < length {
                    let name = start.advanced(by: offset)
                    if strcmp(name, "com.dropbox.placeholder") == 0 { return .dropboxOnlineOnly }
                    if strcmp(name, "com.dropbox.ignored") == 0 { ignored = true }
                    if strcmp(name, "com.dropbox.attrs") == 0 { synced = true }
                    offset += strlen(name) + 1
                }
                if ignored { return .dropboxIgnored }
                return synced ? .dropboxSynced : .none
            }
        }
    }
}
