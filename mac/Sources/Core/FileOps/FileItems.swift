import Foundation
import Darwin

enum FileName {
    static func display(_ name: String) -> String { name.replacingOccurrences(of: ":", with: "/") }
    static func disk(_ name: String) -> String { name.replacingOccurrences(of: "/", with: ":") }
}

enum OnlineOnlyItems {
    static func isStub(_ url: URL) -> Bool {
        url.path.withCString { path in
            var info = stat()
            guard lstat(path, &info) == 0, (info.st_mode & S_IFMT) == S_IFREG, info.st_size == 0 else { return false }
            return getxattr(path, "com.dropbox.placeholder", nil, 0, 0, XATTR_NOFOLLOW) >= 0
        }
    }

    /// FileManager's enumerator does not descend into symbolic links.
    static func firstStub(in url: URL) -> URL? {
        if isStub(url) { return url }
        var info = stat()
        guard url.path.withCString({ lstat($0, &info) }) == 0, (info.st_mode & S_IFMT) == S_IFDIR,
              let walk = FileManager.default.enumerator(at: url, includingPropertiesForKeys: nil) else { return nil }
        for case let child as URL in walk where isStub(child) { return child }
        return nil
    }
}

/// A pre-copy inventory, not a recursive deletion instruction. Entries created or changed
/// during a move are left in place, and directories are removed only with rmdir.
struct CopiedSourceInventory {
    private struct Entry {
        let url: URL
        let relative: String
        let info: stat
    }
    private let entries: [Entry]

    init(_ source: URL) throws {
        var entries: [Entry] = []
        func append(_ url: URL, relative: String) throws {
            var info = stat()
            guard url.path.withCString({ lstat($0, &info) }) == 0 else { throw POSIXError(POSIXErrorCode(rawValue: errno) ?? .EIO) }
            entries.append(Entry(url: url, relative: relative, info: info))
        }
        try append(source, relative: "")
        if (entries[0].info.st_mode & S_IFMT) == S_IFDIR {
            var walkError: Error?
            guard let walk = FileManager.default.enumerator(at: source, includingPropertiesForKeys: nil, errorHandler: { _, error in walkError = error; return false }) else { throw POSIXError(.EIO) }
            for case let child as URL in walk {
                // The enumerator may canonicalize /var to /private/var; its depth is
                // a reliable relative path even when its URL prefix differs.
                try append(child, relative: child.pathComponents.suffix(walk.level).joined(separator: "/"))
            }
            if let walkError { throw walkError }
        }
        self.entries = entries
    }

    func removeCopiedEntries(destination: URL) throws {
        var firstError: Error?
        for entry in entries.reversed() {
            var current = stat(), copied = stat()
            let target = entry.relative.isEmpty ? destination : destination.appendingPathComponent(entry.relative)
            guard entry.url.path.withCString({ lstat($0, &current) }) == 0 else {
                if errno != ENOENT { firstError = firstError ?? POSIXError(POSIXErrorCode(rawValue: errno) ?? .EIO) }
                continue
            }
            let original = entry.info
            let directory = (original.st_mode & S_IFMT) == S_IFDIR
            guard current.st_dev == original.st_dev, current.st_ino == original.st_ino,
                  (current.st_mode & S_IFMT) == (original.st_mode & S_IFMT),
                  target.path.withCString({ lstat($0, &copied) }) == 0,
                  (copied.st_mode & S_IFMT) == (original.st_mode & S_IFMT),
                  directory || (current.st_size == original.st_size && copied.st_size == original.st_size &&
                    current.st_mtimespec.tv_sec == original.st_mtimespec.tv_sec && current.st_mtimespec.tv_nsec == original.st_mtimespec.tv_nsec) else {
                firstError = firstError ?? POSIXError(.EBUSY)
                continue
            }
            let result = entry.url.path.withCString { directory ? rmdir($0) : unlink($0) }
            if result != 0 { firstError = firstError ?? POSIXError(POSIXErrorCode(rawValue: errno) ?? .EIO) }
        }
        if let firstError { throw firstError }
    }
}
