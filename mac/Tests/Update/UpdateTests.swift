import Foundation
import CryptoKit
import Testing

private let testDigest = String(repeating: "a", count: 64)

private func releaseEntry(_ tag: String, mac: Bool = true, draft: Bool = false, prerelease: Bool = false,
                          page: String = "https://github.com/HSoo-Kim/folder-splitter/releases/tag/test",
                          package: String = "https://github.com/HSoo-Kim/folder-splitter/releases/download/test/folder-splitter-mac.zip",
                          digest: String? = "sha256:" + String(repeating: "a", count: 64)) -> [String: Any] {
    var asset: [String: Any] = ["name": mac ? UpdateFeed.packageName : "folder-splitter-win-x64.zip", "browser_download_url": package]
    if let digest { asset["digest"] = digest }
    return ["tag_name": tag, "html_url": page, "draft": draft, "prerelease": prerelease, "assets": [asset]]
}

private func feedData(_ entries: [[String: Any]]) throws -> Data {
    try JSONSerialization.data(withJSONObject: entries, options: [.sortedKeys])
}

private final class UpdateSandbox {
    let root: URL
    let applications: URL
    let bundle: URL
    let identifier = "test.folder-splitter.update.\(UUID().uuidString)"
    private let fm = FileManager.default

    init() throws {
        root = fm.temporaryDirectory.appendingPathComponent("UpdateTests-\(UUID().uuidString)", isDirectory: true)
        applications = root.appendingPathComponent("Applications", isDirectory: true)
        bundle = applications.appendingPathComponent("Folder Splitter.app", isDirectory: true)
        try fm.createDirectory(at: applications, withIntermediateDirectories: true)
        try makeApp(at: bundle, identifier: identifier, version: "1.0.0", contents: "original executable\n")
    }

    deinit {
        try? fm.setAttributes([.posixPermissions: 0o755], ofItemAtPath: applications.path)
        try? fm.removeItem(at: root)
        // Trash chooses the final name (including a collision suffix); locate only this fixture's app.
        let trash = fm.homeDirectoryForCurrentUser.appendingPathComponent(".Trash", isDirectory: true)
        for item in (try? fm.contentsOfDirectory(at: trash, includingPropertiesForKeys: nil)) ?? [] {
            if (try? plist(item)["CFBundleIdentifier"] as? String) == identifier { try? fm.removeItem(at: item) }
        }
    }

    func makeApp(at url: URL, identifier: String, version: String, contents: String = "new executable\n") throws {
        let executable = url.appendingPathComponent("Contents/MacOS/FolderSplitter")
        try fm.createDirectory(at: executable.deletingLastPathComponent(), withIntermediateDirectories: true)
        let info: [String: Any] = ["CFBundleIdentifier": identifier, "CFBundleShortVersionString": version,
                                   "CFBundleExecutable": "FolderSplitter", "CFBundlePackageType": "APPL"]
        try PropertyListSerialization.data(fromPropertyList: info, format: .xml, options: 0)
            .write(to: url.appendingPathComponent("Contents/Info.plist"))
        try Data(contents.utf8).write(to: executable)
        try fm.setAttributes([.posixPermissions: 0o755], ofItemAtPath: executable.path)
    }

    func package(identifier: String? = nil, version: String = "2.0.0", twoApps: Bool = false) throws -> ReleaseInfo {
        let source = root.appendingPathComponent("package-source", isDirectory: true)
        let app = source.appendingPathComponent("Folder Splitter.app", isDirectory: true)
        try makeApp(at: app, identifier: identifier ?? self.identifier, version: version)
        let archive = root.appendingPathComponent(UpdateFeed.packageName)
        if twoApps {
            try makeApp(at: source.appendingPathComponent("Other.app"), identifier: self.identifier, version: version)
            try command("/usr/bin/ditto", ["-c", "-k", "--sequesterRsrc", source.path, archive.path])
        } else {
            try command("/usr/bin/ditto", ["-c", "-k", "--sequesterRsrc", "--keepParent", app.path, archive.path])
        }
        return ReleaseInfo(version: AppVersion("2.0.0")!, tag: "mac-v2.0.0", pageURL: root,
                           packageURL: archive, packageSHA256: digest(try Data(contentsOf: archive)))
    }

    func snapshot() throws -> [String: Data] {
        var result: [String: Data] = [:]
        let enumerator = try #require(fm.enumerator(at: bundle, includingPropertiesForKeys: [.isRegularFileKey]))
        for case let item as URL in enumerator {
            if try item.resourceValues(forKeys: [.isRegularFileKey]).isRegularFile == true {
                result[String(item.path.dropFirst(bundle.path.count))] = try Data(contentsOf: item)
            }
        }
        return result
    }

    func assertNoTemporaryFolders() throws {
        #expect(try fm.contentsOfDirectory(atPath: applications.path) == ["Folder Splitter.app"])
    }
}

private func plist(_ app: URL) throws -> [String: Any] {
    let data = try Data(contentsOf: app.appendingPathComponent("Contents/Info.plist"))
    return try #require(try PropertyListSerialization.propertyList(from: data, format: nil) as? [String: Any])
}

private func digest(_ data: Data) -> String {
    SHA256.hash(data: data).map { String(format: "%02x", $0) }.joined()
}

@discardableResult
private func command(_ executable: String, _ arguments: [String]) throws -> Int32 {
    let task = Process()
    task.executableURL = URL(fileURLWithPath: executable)
    task.arguments = arguments
    task.standardOutput = FileHandle.nullDevice
    task.standardError = FileHandle.nullDevice
    try task.run()
    task.waitUntilExit()
    #expect(task.terminationStatus == 0)
    return task.terminationStatus
}

private final class UpdateProgressCapture: @unchecked Sendable {
    private let lock = NSLock()
    private var storage: [Double?] = []
    func append(_ fraction: Double?) { lock.lock(); defer { lock.unlock() }; storage.append(fraction) }
    var values: [Double?] { lock.lock(); defer { lock.unlock() }; return storage }
}

private final class FeedStub: URLProtocol, @unchecked Sendable {
    struct Reply {
        let status: Int
        let headers: [String: String]
        let data: Data
    }
    nonisolated(unsafe) static var handler: ((URLRequest) throws -> Reply)?
    override class func canInit(with request: URLRequest) -> Bool { true }
    override class func canonicalRequest(for request: URLRequest) -> URLRequest { request }
    override func startLoading() {
        do {
            let handler = try #require(Self.handler)
            let reply = try handler(request)
            let url = try #require(request.url)
            let response = try #require(HTTPURLResponse(url: url, statusCode: reply.status,
                                                       httpVersion: "HTTP/1.1", headerFields: reply.headers))
            client?.urlProtocol(self, didReceive: response, cacheStoragePolicy: .notAllowed)
            if !reply.data.isEmpty { client?.urlProtocol(self, didLoad: reply.data) }
            client?.urlProtocolDidFinishLoading(self)
        } catch { client?.urlProtocol(self, didFailWithError: error) }
    }
    override func stopLoading() {}
}

@Suite(.serialized)
struct UpdateTests {
    @Test func versionNormalizationAndOrdering() throws {
        #expect(AppVersion("1.10")! > AppVersion("1.9")!)
        #expect(AppVersion("1.0") == AppVersion("1.0.0"))
        #expect(AppVersion("mac-v1.2.3") == AppVersion("v1.2.3"))
        #expect(AppVersion("V1.2.3") == AppVersion("1.2.3"))
        #expect(AppVersion("1")?.description == "1.0.0")
        #expect(AppVersion("v1.2")?.description == "1.2.0")
        #expect(AppVersion("01.002.3.0")?.description == "1.2.3")
        #expect(AppVersion("1.2.3.4")?.description == "1.2.3.4")
        #expect(AppVersion("1.2.3.4")! > AppVersion("1.2.3")!)
        #expect(AppVersion("2")! > AppVersion("1.999.999.999")!)
        #expect(Set([AppVersion("1")!, AppVersion("1.0.0.0")!]).count == 1)
        #expect(AppVersion("18446744073709551615.0.0") != nil)
    }

    @Test(arguments: ["", "v", "mac-v", "mac-1.2", "vv1.2", "1..2", ".1", "1.", "1.2.3.4.5",
                      " 1.2", "1.2\n", "+1.2", "-1.2", "1.2beta", "1.2.3-rc1", "١.2", "18446744073709551616"])
    func rejectsInvalidVersions(_ text: String) { #expect(AppVersion(text) == nil) }

    @Test func selectsHighestMacReleaseInAnyOrder() throws {
        let entries = [releaseEntry("v20.0", mac: false), releaseEntry("mac-v1.9"),
                       releaseEntry("mac-v9.0", draft: true), releaseEntry("v1.10"),
                       releaseEntry("mac-v10.0", prerelease: true), releaseEntry("junk")]
        for values in [entries, entries.reversed().map { $0 }] {
            let release = try #require(try UpdateFeed.parse(feedData(values), githubOnly: true))
            #expect(release.version == AppVersion("1.10"))
            #expect(release.tag == "v1.10")
            #expect(release.packageSHA256 == testDigest)
            #expect(release.isInstallable)
        }
        #expect(try UpdateFeed.parse(feedData([releaseEntry("v8", mac: false)]), githubOnly: true) == nil)
    }

    @Test func rejectsUntrustedReleaseAndPackageURLs() throws {
        for bad in ["http://github.com/a", "https://github.com.evil.test/a", "https://example.com/a",
                    "https://github.com@evil.test/a", "https://github.com:443/a", "file:///tmp/package.zip"] {
            #expect(try UpdateFeed.parse(feedData([releaseEntry("v2", page: bad)]), githubOnly: true) == nil)
            let release = try #require(try UpdateFeed.parse(feedData([releaseEntry("v2", package: bad)]), githubOnly: true))
            #expect(release.packageURL == nil)
            #expect(!release.isInstallable)
        }
        let local = try #require(try UpdateFeed.parse(feedData([releaseEntry("v2", page: "file:///tmp/release", package: "file:///tmp/update.zip")]), githubOnly: false))
        #expect(local.isInstallable)
        #expect(try UpdateFeed.parse(feedData([releaseEntry("v2", page: "http://localhost/release")]), githubOnly: false) == nil)
    }

    @Test func digestMustBeExactlySHA256Hex() throws {
        for hash in [nil, "", "sha256:abc", "sha512:" + testDigest, "sha256:" + String(repeating: "g", count: 64),
                     "sha256:" + testDigest + "0", "SHA256:" + testDigest] as [String?] {
            let release = try #require(try UpdateFeed.parse(feedData([releaseEntry("v2", digest: hash)]), githubOnly: true))
            #expect(release.packageSHA256 == nil)
            #expect(!release.isInstallable)
        }
        let release = try #require(try UpdateFeed.parse(feedData([releaseEntry("v2", digest: "sha256:" + testDigest.uppercased())]), githubOnly: true))
        #expect(release.packageSHA256 == testDigest)
    }

    @Test func fileFeedOnlyReturnsNewerVersions() async throws {
        let box = try UpdateSandbox()
        let feed = box.root.appendingPathComponent("feed.json")
        try feedData([releaseEntry("mac-v2.0.0")]).write(to: feed)
        #expect(try await UpdateFeed.fetchNewer(than: AppVersion("1.0")!, feed: feed)?.version == AppVersion("2"))
        #expect(try await UpdateFeed.fetchNewer(than: AppVersion("2")!, feed: feed) == nil)
        #expect(try await UpdateFeed.fetchNewer(than: AppVersion("3")!, feed: feed) == nil)
    }

    @Test func etag304ReusesParsedReleaseAndRechecksCurrentVersion() async throws {
        let configuration = URLSessionConfiguration.ephemeral
        configuration.protocolClasses = [FeedStub.self]
        let previous = UpdateFeed.session
        let stubSession = URLSession(configuration: configuration)
        UpdateFeed.session = stubSession
        defer { UpdateFeed.session = previous; stubSession.invalidateAndCancel(); FeedStub.handler = nil }
        let feed = URL(string: "https://api.github.com/repos/test/\(UUID().uuidString)/releases")!
        let body = try feedData([releaseEntry("mac-v2.0")])
        FeedStub.handler = { request in
            #expect(request.value(forHTTPHeaderField: "If-None-Match") == nil)
            #expect(request.value(forHTTPHeaderField: "User-Agent") == "FolderSplitter-macOS/1.0.0")
            #expect(request.value(forHTTPHeaderField: "Accept") == "application/vnd.github+json")
            #expect(request.timeoutInterval == 10)
            return .init(status: 200, headers: ["ETag": "\"release-v2\""], data: body)
        }
        let first = try await UpdateFeed.fetchNewer(than: AppVersion("1")!, feed: feed)
        #expect(first?.version == AppVersion("2"))
        FeedStub.handler = { request in
            #expect(request.value(forHTTPHeaderField: "If-None-Match") == "\"release-v2\"")
            return .init(status: 304, headers: [:], data: Data())
        }
        #expect(try await UpdateFeed.fetchNewer(than: AppVersion("1")!, feed: feed) == first)
        #expect(try await UpdateFeed.fetchNewer(than: AppVersion("2")!, feed: feed) == nil)
        #expect(try await UpdateFeed.fetchNewer(than: AppVersion("3")!, feed: feed) == nil)
    }

    @Test func findsMacReleaseBeyondFirstPageAndContinuesAfter304() async throws {
        let configuration = URLSessionConfiguration.ephemeral
        configuration.protocolClasses = [FeedStub.self]
        let previous = UpdateFeed.session
        let stubSession = URLSession(configuration: configuration)
        UpdateFeed.session = stubSession
        defer { UpdateFeed.session = previous; stubSession.invalidateAndCancel(); FeedStub.handler = nil }
        #expect(URLComponents(url: UpdateFeed.defaultURL, resolvingAgainstBaseURL: false)?
            .queryItems?.contains(URLQueryItem(name: "per_page", value: "100")) == true)
        let feed = URL(string: "https://api.github.com/repos/test/\(UUID().uuidString)/releases?per_page=100")!
        let second = URL(string: feed.absoluteString + "&page=2")!
        let third = URL(string: feed.absoluteString + "&page=3")!
        let windows = try feedData((1...100).map { releaseEntry("v\($0)", mac: false) })
        let mac = try feedData([releaseEntry("mac-v2.0")])
        var requests: [URL] = []
        var rechecking = false
        FeedStub.handler = { request in
            let url = try #require(request.url)
            requests.append(url)
            if url == feed {
                #expect(request.value(forHTTPHeaderField: "If-None-Match") == (rechecking ? "\"windows\"" : nil))
                return .init(status: rechecking ? 304 : 200,
                             headers: ["ETag": "\"windows\"", "Link": "<\(second)>; rel=\"next\", <\(third)>; rel=\"last\""],
                             data: rechecking ? Data() : windows)
            }
            #expect(request.value(forHTTPHeaderField: "If-None-Match") == nil)
            if url == second {
                return .init(status: 200, headers: ["Link": "<\(third)>; rel=\"next\""], data: windows)
            }
            #expect(url == third)
            return .init(status: 200, headers: [:], data: mac)
        }
        #expect(try await UpdateFeed.fetchNewer(than: AppVersion("1")!, feed: feed)?.version == AppVersion("2"))
        #expect(requests == [feed, second, third])
        requests.removeAll()
        rechecking = true
        #expect(try await UpdateFeed.fetchNewer(than: AppVersion("2")!, feed: feed) == nil)
        #expect(requests == [feed, second, third])
    }

    @Test func paginationStopsAtLastPageWithoutMacRelease() async throws {
        let configuration = URLSessionConfiguration.ephemeral
        configuration.protocolClasses = [FeedStub.self]
        let previous = UpdateFeed.session
        let stubSession = URLSession(configuration: configuration)
        UpdateFeed.session = stubSession
        defer { UpdateFeed.session = previous; stubSession.invalidateAndCancel(); FeedStub.handler = nil }
        let feed = URL(string: "https://api.github.com/repos/test/\(UUID().uuidString)/releases?per_page=100")!
        let second = URL(string: feed.absoluteString + "&page=2")!
        let windows = try feedData([releaseEntry("v9", mac: false)])
        var requests: [URL] = []
        FeedStub.handler = { request in
            let url = try #require(request.url)
            requests.append(url)
            return .init(status: 200, headers: url == feed ? ["Link": "<\(second)>; rel=\"next\""] : [:],
                         data: url == feed ? windows : Data("[]".utf8))
        }
        #expect(try await UpdateFeed.fetchNewer(than: AppVersion("1")!, feed: feed) == nil)
        #expect(requests == [feed, second])
    }

    @Test func cachedEmptyFeedAndRateLimitErrors() async throws {
        let configuration = URLSessionConfiguration.ephemeral
        configuration.protocolClasses = [FeedStub.self]
        let previous = UpdateFeed.session
        let stubSession = URLSession(configuration: configuration)
        UpdateFeed.session = stubSession
        defer { UpdateFeed.session = previous; stubSession.invalidateAndCancel(); FeedStub.handler = nil }
        let feed = URL(string: "https://api.github.com/repos/test/\(UUID().uuidString)/releases")!
        FeedStub.handler = { _ in .init(status: 200, headers: ["ETag": "empty"], data: Data("[]".utf8)) }
        #expect(try await UpdateFeed.fetchNewer(than: AppVersion("1")!, feed: feed) == nil)
        FeedStub.handler = { request in
            #expect(request.value(forHTTPHeaderField: "If-None-Match") == "empty")
            return .init(status: 304, headers: [:], data: Data())
        }
        #expect(try await UpdateFeed.fetchNewer(than: AppVersion("1")!, feed: feed) == nil)
        for (status, headers, limited) in [(403, ["x-ratelimit-remaining": "0"], true), (429, [:], true),
                                           (403, ["x-ratelimit-remaining": "1"], false), (500, [:], false)] {
            FeedStub.handler = { _ in .init(status: status, headers: headers, data: Data()) }
            do {
                _ = try await UpdateFeed.fetchNewer(than: AppVersion("1")!, feed: feed)
                Issue.record("Expected HTTP error \(status)")
            } catch let error as UpdateError {
                switch error {
                case .rateLimited: #expect(limited)
                case .http(let code): #expect(!limited); #expect(code == status)
                default: Issue.record("Unexpected error: \(error)")
                }
                #expect(error.errorDescription?.isEmpty == false)
            }
        }
    }

    @Test func installsVerifiedFilePackageAndCleansStaging() async throws {
        let box = try UpdateSandbox()
        let release = try box.package()
        let progress = UpdateProgressCapture()
        #expect(UpdateInstaller.canInstall(release, replacing: box.bundle))
        try await UpdateInstaller.install(release, replacing: box.bundle, bundleIdentifier: box.identifier, progress: { progress.append($0) })
        #expect(try plist(box.bundle)["CFBundleShortVersionString"] as? String == "2.0.0")
        #expect(try String(contentsOf: box.bundle.appendingPathComponent("Contents/MacOS/FolderSplitter"), encoding: .utf8) == "new executable\n")
        try box.assertNoTemporaryFolders()
        #expect(progress.values.last! == 1)
        #expect(progress.values.compactMap { $0 }.allSatisfy { (0...1).contains($0) })
    }

    @Test(arguments: ["checksum", "identifier", "version", "two-apps"])
    func failedVerificationPreservesOriginal(_ kind: String) async throws {
        let box = try UpdateSandbox()
        let before = try box.snapshot()
        let original = try box.package(identifier: kind == "identifier" ? "wrong.identifier" : nil,
                                       version: kind == "version" ? "3.0.0" : "2.0.0", twoApps: kind == "two-apps")
        let release = ReleaseInfo(version: original.version, tag: original.tag, pageURL: original.pageURL,
                                  packageURL: original.packageURL, packageSHA256: kind == "checksum" ? String(repeating: "0", count: 64) : original.packageSHA256)
        do {
            try await UpdateInstaller.install(release, replacing: box.bundle, bundleIdentifier: box.identifier)
            Issue.record("Expected verification failure for \(kind)")
        } catch let error as UpdateError {
            switch (kind, error) {
            case ("checksum", .checksum), ("identifier", .wrongApp), ("version", .versionMismatch), ("two-apps", .wrongApp): break
            default: Issue.record("Unexpected verification error: \(error)")
            }
            #expect(error.errorDescription?.isEmpty == false)
        }
        #expect(try box.snapshot() == before)
        try box.assertNoTemporaryFolders()
    }

    @Test func cannotInstallWithoutDigestOrWritableParent() throws {
        let box = try UpdateSandbox()
        let release = try box.package()
        let noDigest = ReleaseInfo(version: release.version, tag: release.tag, pageURL: release.pageURL,
                                   packageURL: release.packageURL, packageSHA256: nil)
        #expect(!UpdateInstaller.canInstall(noDigest, replacing: box.bundle))
        try FileManager.default.setAttributes([.posixPermissions: 0o555], ofItemAtPath: box.applications.path)
        defer { try? FileManager.default.setAttributes([.posixPermissions: 0o755], ofItemAtPath: box.applications.path) }
        #expect(!UpdateInstaller.canInstall(release, replacing: box.bundle))
    }
}
