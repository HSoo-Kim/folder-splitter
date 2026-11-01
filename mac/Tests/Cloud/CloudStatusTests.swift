import Foundation
import Testing
import Darwin

private final class CloudSandbox {
    let root: URL
    init() throws {
        root = FileManager.default.temporaryDirectory.appendingPathComponent("CloudTests-\(UUID().uuidString)", isDirectory: true)
        try FileManager.default.createDirectory(at: root, withIntermediateDirectories: false)
    }
    deinit { try? FileManager.default.removeItem(at: root) }

    func folder(_ path: String) throws -> URL {
        let url = root.appendingPathComponent(path, isDirectory: true)
        try FileManager.default.createDirectory(at: url, withIntermediateDirectories: true)
        return url
    }

    func file(_ path: String, contents: Data = Data()) throws -> URL {
        let url = root.appendingPathComponent(path)
        try FileManager.default.createDirectory(at: url.deletingLastPathComponent(), withIntermediateDirectories: true)
        try contents.write(to: url)
        return url
    }

    func attribute(_ name: String, on url: URL, contents: Data = Data("ONLINEONLY".utf8)) throws {
        let result = contents.withUnsafeBytes { bytes in
            url.withUnsafeFileSystemRepresentation { path in
                setxattr(path!, name, bytes.baseAddress, bytes.count, 0, XATTR_NOFOLLOW)
            }
        }
        if result != 0 { throw NSError(domain: NSPOSIXErrorDomain, code: Int(errno)) }
    }

    func info(_ object: [String: Any]) throws -> URL {
        try file("info.json", contents: JSONSerialization.data(withJSONObject: object, options: [.sortedKeys]))
    }

}

@Suite("Cloud metadata", .serialized)
struct CloudStatusTests {
    @Test func legacyAttributeNamesAndPrecedence() throws {
        let box = try CloudSandbox()
        let root = try box.folder("Dropbox 한글")
        let previous = CloudStatusReader.dropboxRootsOverride
        CloudStatusReader.dropboxRootsOverride = [root]
        defer { CloudStatusReader.dropboxRootsOverride = previous }

        let combinations: [([String], CloudStatus)] = [
            ([], .none),
            (["com.dropbox.attrs"], .dropboxSynced),
            (["com.dropbox.ignored"], .dropboxIgnored),
            (["com.dropbox.placeholder"], .dropboxOnlineOnly),
            (["com.dropbox.attrs", "com.dropbox.ignored"], .dropboxIgnored),
            (["com.dropbox.attrs", "com.dropbox.placeholder"], .dropboxOnlineOnly),
            (["com.dropbox.ignored", "com.dropbox.placeholder"], .dropboxOnlineOnly),
            (["com.dropbox.attrs", "com.dropbox.ignored", "com.dropbox.placeholder"], .dropboxOnlineOnly),
            (["com.example.syncing"], .none)
        ]
        for (index, combination) in combinations.enumerated() {
            let file = try box.file("Dropbox 한글/é-\(index)")
            for name in combination.0 { try box.attribute(name, on: file) }
            #expect(CloudStatusReader.status(for: file, values: nil, insideDropbox: true) == combination.1)
        }
        // Attribute names, not payloads, identify legacy metadata, including zero-length payloads.
        let emptyAttribute = try box.file("Dropbox 한글/빈 속성")
        try box.attribute("com.dropbox.placeholder", on: emptyAttribute, contents: Data())
        #expect(CloudStatusReader.status(for: emptyAttribute, values: nil, insideDropbox: true) == .dropboxOnlineOnly)
        let populated = try box.file("Dropbox 한글/downloaded", contents: Data("local contents".utf8))
        try box.attribute("com.dropbox.attrs", on: populated)
        #expect(CloudStatusReader.status(for: populated, values: nil, insideDropbox: true) == .dropboxSynced)
    }

    @Test func strictRootBoundariesAndCallerHint() throws {
        let box = try CloudSandbox()
        let root = try box.folder("Dropbox")
        let previous = CloudStatusReader.dropboxRootsOverride
        CloudStatusReader.dropboxRootsOverride = [root]
        defer { CloudStatusReader.dropboxRootsOverride = previous }

        let inside = try box.file("Dropbox/한글.txt")
        let sibling = try box.file("Dropbox-other/한글.txt")
        let outside = try box.file("outside/한글.txt")
        for file in [inside, sibling, outside] { try box.attribute("com.dropbox.placeholder", on: file) }
        #expect(CloudStatusReader.status(for: inside, values: nil, insideDropbox: false) == .none)
        #expect(CloudStatusReader.status(for: sibling, values: nil, insideDropbox: true) == .none)
        #expect(CloudStatusReader.status(for: outside, values: nil, insideDropbox: true) == .none)
        #expect(CloudStatusReader.status(for: outside, values: nil, insideDropbox: false) == .none)
        let escaped = root.appendingPathComponent("../outside/한글.txt")
        #expect(CloudStatusReader.status(for: escaped, values: nil, insideDropbox: true) == .none)
        let lexical = root.appendingPathComponent("unused/../한글.txt")
        #expect(CloudStatusReader.status(for: lexical.standardizedFileURL, values: nil, insideDropbox: true) == .dropboxOnlineOnly)
        try box.attribute("com.dropbox.attrs", on: root)
        #expect(CloudStatusReader.status(for: root, values: nil, insideDropbox: true) == .dropboxSynced)
        #expect(CloudStatusReader.status(for: box.root.appendingPathComponent("missing"), values: nil, insideDropbox: false) == .none)
        #expect(CloudStatusReader.status(for: URL(string: "https://example.com/Dropbox")!, values: nil, insideDropbox: true) == .none)
        CloudStatusReader.dropboxRootsOverride = []
        #expect(CloudStatusReader.status(for: inside, values: nil, insideDropbox: true) == .none)
    }

    @Test func symbolicLinkDoesNotReadOutsideTargetAttributes() throws {
        let box = try CloudSandbox()
        let root = try box.folder("Dropbox")
        let previous = CloudStatusReader.dropboxRootsOverride
        CloudStatusReader.dropboxRootsOverride = [root]
        defer { CloudStatusReader.dropboxRootsOverride = previous }
        let target = try box.file("outside/placeholder")
        try box.attribute("com.dropbox.placeholder", on: target)
        let link = root.appendingPathComponent("link")
        try FileManager.default.createSymbolicLink(at: link, withDestinationURL: target)
        #expect(CloudStatusReader.status(for: link, values: nil, insideDropbox: true) == .none)
    }

    @Test func configuredRootsIgnoreOverride() {
        let configured = CloudStatusReader.configuredDropboxRoots()
        let previous = CloudStatusReader.dropboxRootsOverride
        let fake = URL(fileURLWithPath: "/not-a-configured-dropbox-\(UUID().uuidString)", isDirectory: true)
        CloudStatusReader.dropboxRootsOverride = [fake]
        defer { CloudStatusReader.dropboxRootsOverride = previous }
        #expect(CloudStatusReader.dropboxRoots() == [fake])
        #expect(CloudStatusReader.configuredDropboxRoots() == configured)
        CloudStatusReader.dropboxRootsOverride = nil
        #expect(CloudStatusReader.dropboxRoots() == configured)
    }

    @Test func infoPersonalBusinessOrderAndLexicalDeduplication() throws {
        let box = try CloudSandbox()
        let personal = box.root.appendingPathComponent("없는 개인/한글 é", isDirectory: true)
        let business = box.root.appendingPathComponent("없는 회사/Dropbox", isDirectory: true)
        let info = try box.info([
            "business": ["path": business.path],
            "personal": ["path": personal.path],
            "unknown": ["path": "/ignored"]
        ])
        #expect(CloudStatusReader.readDropboxRoots(at: info) == [personal, business, URL(fileURLWithPath: "/ignored", isDirectory: true)])
        _ = try box.info([
            "personal": ["path": personal.path + "/subfolder/.."],
            "business": ["path": personal.path + "/"]
        ])
        #expect(CloudStatusReader.readDropboxRoots(at: info) == [personal])
        // Parsing the helper is independent of the once-cached real configuration.
        _ = try box.info(["business": ["path": business.path]])
        #expect(CloudStatusReader.readDropboxRoots(at: info) == [business])
    }

    @Test func infoMissingMalformedAndInvalidEntries() throws {
        let box = try CloudSandbox()
        let missing = box.root.appendingPathComponent("missing.json")
        #expect(CloudStatusReader.readDropboxRoots(at: missing).isEmpty)
        let malformed = try box.file("malformed.json", contents: Data("{broken".utf8))
        #expect(CloudStatusReader.readDropboxRoots(at: malformed).isEmpty)
        let array = try box.file("array.json", contents: Data("[]".utf8))
        #expect(CloudStatusReader.readDropboxRoots(at: array).isEmpty)
        let invalid: [Any] = ["", "relative/path", "~/Dropbox", "file:///Dropbox", "/invalid\0suffix", 42, NSNull()]
        for path in invalid {
            let info = try box.info(["personal": ["path": path], "business": ["path": "/valid/한글"]])
            #expect(CloudStatusReader.readDropboxRoots(at: info).map(\.path) == ["/valid/한글"])
        }
        let info = try box.info(["personal": ["notPath": "/ignored"], "business": "not a dictionary"])
        #expect(CloudStatusReader.readDropboxRoots(at: info).isEmpty)
        _ = try box.info(["personal": ["path": "/"]])
        #expect(CloudStatusReader.readDropboxRoots(at: info).map(\.path) == ["/"])
    }

    @Test func ubiquitousFlagPrecedenceAndDownloadedStates() throws {
        let box = try CloudSandbox()
        let url = box.root.appendingPathComponent("cloud-item")
        let cases: [([URLResourceKey: Bool], URLUbiquitousItemDownloadingStatus?, CloudStatus)] = [
            ([.isUbiquitousItemKey: true, .ubiquitousItemHasUnresolvedConflictsKey: true, .ubiquitousItemIsDownloadingKey: true, .ubiquitousItemIsUploadingKey: true], .notDownloaded, .cloudConflict),
            ([.isUbiquitousItemKey: true, .ubiquitousItemIsDownloadingKey: true, .ubiquitousItemIsUploadingKey: true], .notDownloaded, .cloudDownloading),
            ([.isUbiquitousItemKey: true, .ubiquitousItemIsUploadingKey: true], .notDownloaded, .cloudUploading),
            ([.isUbiquitousItemKey: true], .notDownloaded, .cloudNotDownloaded),
            ([.isUbiquitousItemKey: true], .downloaded, .none),
            ([.isUbiquitousItemKey: true], .current, .none),
            ([.isUbiquitousItemKey: true], nil, .none),
            ([.isUbiquitousItemKey: false, .ubiquitousItemHasUnresolvedConflictsKey: true, .ubiquitousItemIsDownloadingKey: true, .ubiquitousItemIsUploadingKey: true], .notDownloaded, .none),
            ([.ubiquitousItemIsDownloadingKey: true], .notDownloaded, .none)
        ]
        for (flags, state, expected) in cases {
            let status = CloudStatusReader.ubiquitousStatus(
                isUbiquitous: flags[.isUbiquitousItemKey] == true,
                downloadingStatus: state,
                isDownloading: flags[.ubiquitousItemIsDownloadingKey] == true,
                isUploading: flags[.ubiquitousItemIsUploadingKey] == true,
                hasUnresolvedConflicts: flags[.ubiquitousItemHasUnresolvedConflictsKey] == true
            )
            #expect((status ?? .none) == expected)
        }
        #expect(CloudStatusReader.status(for: url, values: URLResourceValues(), insideDropbox: false) == .none)
        #expect(CloudStatusReader.status(for: url, values: nil, insideDropbox: false) == .none)
        #expect(CloudStatusReader.ubiquitousStatus(isUbiquitous: true, downloadingStatus: .current,
                                                 isDownloading: false, isUploading: false,
                                                 hasUnresolvedConflicts: false) == CloudStatus.none)
        #expect(CloudStatusReader.ubiquitousStatus(isUbiquitous: false, downloadingStatus: .current,
                                                 isDownloading: false, isUploading: false,
                                                 hasUnresolvedConflicts: false) == nil)
    }
}
