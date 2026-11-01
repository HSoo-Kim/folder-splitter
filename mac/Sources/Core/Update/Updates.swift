import Foundation
import CryptoKit

struct AppVersion: Comparable, Hashable, Sendable, CustomStringConvertible {
    private let major: UInt64
    private let minor: UInt64
    private let patch: UInt64
    private let revision: UInt64

    init?(_ text: String) {
        var number = text[...]
        if number.hasPrefix("mac-v") { number.removeFirst(5) }
        else if number.hasPrefix("v") || number.hasPrefix("V") { number.removeFirst() }
        let parts = number.split(separator: ".", omittingEmptySubsequences: false)
        guard (1...4).contains(parts.count), parts.allSatisfy({ !$0.isEmpty && $0.utf8.allSatisfy { (48...57).contains($0) } }) else { return nil }
        let values = parts.compactMap { UInt64($0) }
        guard values.count == parts.count else { return nil }
        major = values[0]
        minor = values.count > 1 ? values[1] : 0
        patch = values.count > 2 ? values[2] : 0
        revision = values.count > 3 ? values[3] : 0
    }

    var description: String {
        let base = "\(major).\(minor).\(patch)"
        return revision == 0 ? base : "\(base).\(revision)"
    }

    static func < (lhs: Self, rhs: Self) -> Bool {
        if lhs.major != rhs.major { return lhs.major < rhs.major }
        if lhs.minor != rhs.minor { return lhs.minor < rhs.minor }
        if lhs.patch != rhs.patch { return lhs.patch < rhs.patch }
        return lhs.revision < rhs.revision
    }
}

struct ReleaseInfo: Equatable, Sendable {
    let version: AppVersion
    let tag: String
    let pageURL: URL
    let packageURL: URL?
    let packageSHA256: String?
    var isInstallable: Bool { packageURL != nil && packageSHA256 != nil }
}

enum UpdateFeed {
    static let defaultURL = URL(string: "https://api.github.com/repos/HSoo-Kim/folder-splitter/releases?per_page=100")!
    static let packageName = "folder-splitter-mac.zip"
    nonisolated(unsafe) static var session = URLSession(configuration: privateConfiguration())
    private static let cache = ReleaseCache()

    // Only public release metadata is requested; no account, machine or file information is sent.
    static func parse(_ data: Data, githubOnly: Bool) throws -> ReleaseInfo? {
        guard let entries = try JSONSerialization.jsonObject(with: data) as? [[String: Any]] else {
            throw UpdateError.invalidFeed
        }
        var newest: ReleaseInfo?
        for entry in entries {
            guard entry["draft"] as? Bool != true, entry["prerelease"] as? Bool != true,
                  let tag = entry["tag_name"] as? String, let version = AppVersion(tag),
                  let page = acceptedURL(entry["html_url"] as? String, githubOnly: githubOnly),
                  let assets = entry["assets"] as? [[String: Any]],
                  let asset = assets.first(where: { $0["name"] as? String == packageName }) else { continue }
            let package = acceptedURL(asset["browser_download_url"] as? String, githubOnly: githubOnly)
            let digest = asset["digest"] as? String
            let hash = digest.flatMap { value -> String? in
                guard value.hasPrefix("sha256:"), validSHA256(String(value.dropFirst(7))) else { return nil }
                return String(value.dropFirst(7)).lowercased()
            }
            let release = ReleaseInfo(version: version, tag: tag, pageURL: page, packageURL: package, packageSHA256: hash)
            if newest == nil || version > newest!.version { newest = release }
        }
        return newest
    }

    static func fetchNewer(than current: AppVersion, feed: URL = defaultURL) async throws -> ReleaseInfo? {
        do {
            let release: ReleaseInfo?
            if feed.isFileURL {
                release = try parse(Data(contentsOf: feed), githubOnly: false)
            } else {
                guard feed.scheme == "https" else { throw UpdateError.invalidFeed }
                release = try await fetchRelease(feed: feed, current: current)
            }
            return release.flatMap { $0.version > current ? $0 : nil }
        } catch let error as UpdateError { throw error }
        catch { throw UpdateError.failed(error.localizedDescription) }
    }

    private static func fetchRelease(feed: URL, current: AppVersion) async throws -> ReleaseInfo? {
        var page: URL? = feed
        var visited = Set<URL>()
        while let url = page {
            guard visited.insert(url).inserted else { throw UpdateError.invalidFeed }
            // Only page 1 is cached. A 304 there must still check later pages when it
            // contains no Mac release: those pages may have changed independently.
            let cached = url == feed ? await cache.entry(for: feed) : nil
            var request = URLRequest(url: url, timeoutInterval: 10)
            request.setValue("FolderSplitter-macOS/\(current)", forHTTPHeaderField: "User-Agent")
            request.setValue("application/vnd.github+json", forHTTPHeaderField: "Accept")
            if let etag = cached?.etag { request.setValue(etag, forHTTPHeaderField: "If-None-Match") }
            let (data, response) = try await session.data(for: request)
            guard let http = response as? HTTPURLResponse else { throw UpdateError.invalidFeed }
            let entry: ReleaseCache.Entry
            if http.statusCode == 304 {
                guard let cached else { throw UpdateError.invalidFeed }
                entry = cached
            } else {
                try checkHTTP(http)
                entry = try .init(etag: http.value(forHTTPHeaderField: "ETag"),
                                  release: parse(data, githubOnly: feed.host == "api.github.com"),
                                  nextPage: nextPage(in: http, relativeTo: feed))
                if url == feed { await cache.store(entry, for: feed) }
            }
            if let release = entry.release { return release }
            page = entry.nextPage
        }
        return nil
    }

    private static func nextPage(in response: HTTPURLResponse, relativeTo feed: URL) throws -> URL? {
        guard let links = response.value(forHTTPHeaderField: "Link") else { return nil }
        for link in links.split(separator: ",") {
            let fields = link.split(separator: ";").map { $0.trimmingCharacters(in: .whitespacesAndNewlines) }
            guard fields.dropFirst().contains(where: { $0 == "rel=\"next\"" || $0 == "rel=next" }) else { continue }
            guard let target = fields.first, target.hasPrefix("<"), target.hasSuffix(">"),
                  let url = URL(string: String(target.dropFirst().dropLast()), relativeTo: feed)?.absoluteURL,
                  url.scheme == "https", url.host == feed.host, url.port == feed.port,
                  url.user == nil, url.password == nil else { throw UpdateError.invalidFeed }
            return url
        }
        return nil
    }

    private static func acceptedURL(_ text: String?, githubOnly: Bool) -> URL? {
        guard let text, let url = URL(string: text), url.user == nil, url.password == nil else { return nil }
        if githubOnly {
            guard text.hasPrefix("https://github.com/"), url.host == "github.com", url.port == nil else { return nil }
        } else {
            guard url.scheme == "https" || url.isFileURL else { return nil }
        }
        return url
    }
}

private actor ReleaseCache {
    struct Entry: Sendable {
        let etag: String?
        let release: ReleaseInfo?
        let nextPage: URL?
    }
    private var entries: [URL: Entry] = [:]
    func entry(for url: URL) -> Entry? { entries[url] }
    func store(_ entry: Entry, for url: URL) { entries[url] = entry }
}

enum UpdateInstaller {
    static func canInstall(_ release: ReleaseInfo, replacing bundle: URL) -> Bool {
        guard release.isInstallable, bundle.isFileURL else { return false }
        return writableDirectory(bundle) && writableDirectory(bundle.deletingLastPathComponent())
    }

    static func install(_ release: ReleaseInfo, replacing bundle: URL, bundleIdentifier: String,
                        progress: @escaping @Sendable (Double?) -> Void = { _ in }) async throws {
        guard let packageURL = release.packageURL, let expectedHash = release.packageSHA256,
              validSHA256(expectedHash) else { throw UpdateError.unverifiable }
        guard canInstall(release, replacing: bundle) else { throw UpdateError.notWritable }
        guard packageURL.isFileURL || packageURL.scheme == "https" else { throw UpdateError.unverifiable }
        let fm = FileManager.default
        let work = bundle.deletingLastPathComponent().appendingPathComponent(".Folder Splitter update-\(UUID().uuidString)", isDirectory: true)
        var preserveRecoveryCopy = false
        defer { if !preserveRecoveryCopy { try? fm.removeItem(at: work) } }
        do {
            try fm.createDirectory(at: work, withIntermediateDirectories: false)
            let archive = work.appendingPathComponent("package.zip")
            progress(nil)
            if packageURL.isFileURL {
                try fm.copyItem(at: packageURL, to: archive)
                progress(1)
            } else {
                try await PackageDownload.download(packageURL, to: archive, progress: progress)
            }
            guard try sha256(archive) == expectedHash.lowercased() else { throw UpdateError.checksum }
            try Task.checkCancellation()
            let extracted = work.appendingPathComponent("extracted", isDirectory: true)
            try fm.createDirectory(at: extracted, withIntermediateDirectories: false)
            try run("/usr/bin/ditto", ["-x", "-k", archive.path, extracted.path])
            let apps = try fm.contentsOfDirectory(at: extracted, includingPropertiesForKeys: [.isDirectoryKey, .isSymbolicLinkKey])
                .filter { $0.pathExtension == "app" }
            guard apps.count == 1, let app = apps.first,
                  try app.resourceValues(forKeys: [.isDirectoryKey, .isSymbolicLinkKey]).isDirectory == true,
                  try app.resourceValues(forKeys: [.isSymbolicLinkKey]).isSymbolicLink != true,
                  let info = try PropertyListSerialization.propertyList(from: Data(contentsOf: app.appendingPathComponent("Contents/Info.plist")), format: nil) as? [String: Any],
                  info["CFBundleIdentifier"] as? String == bundleIdentifier else { throw UpdateError.wrongApp }
            guard let version = info["CFBundleShortVersionString"] as? String,
                  AppVersion(version) == release.version else { throw UpdateError.versionMismatch }
            try run("/usr/bin/xattr", ["-dr", "com.apple.quarantine", app.path])
            try Task.checkCancellation()
            let previousFolder = work.appendingPathComponent("previous", isDirectory: true)
            try fm.createDirectory(at: previousFolder, withIntermediateDirectories: false)
            let previous = previousFolder.appendingPathComponent(bundle.lastPathComponent, isDirectory: true)
            try fm.moveItem(at: bundle, to: previous)
            do {
                try fm.moveItem(at: app, to: bundle)
            } catch {
                do { try fm.moveItem(at: previous, to: bundle) }
                catch {
                    // Never remove the only original copy if the filesystem also refuses rollback.
                    preserveRecoveryCopy = true
                    throw UpdateError.recovery(previous.path)
                }
                throw error
            }
            // The swap is complete. Disposal failures must not turn a successful update into a failure.
            do { try fm.trashItem(at: previous, resultingItemURL: nil) }
            catch { try? fm.removeItem(at: previous) }
        } catch let error as UpdateError { throw error }
        catch { throw UpdateError.failed(error.localizedDescription) }
    }

    private static func writableDirectory(_ url: URL) -> Bool {
        let fm = FileManager.default
        guard let attributes = try? fm.attributesOfItem(atPath: url.path),
              attributes[.type] as? FileAttributeType == .typeDirectory,
              let permissions = attributes[.posixPermissions] as? NSNumber,
              permissions.intValue & 0o222 != 0 else { return false }
        return fm.isWritableFile(atPath: url.path)
    }

    private static func sha256(_ url: URL) throws -> String {
        let handle = try FileHandle(forReadingFrom: url)
        defer { try? handle.close() }
        var hash = SHA256()
        while let data = try handle.read(upToCount: 1024 * 1024), !data.isEmpty { hash.update(data: data) }
        return hash.finalize().map { String(format: "%02x", $0) }.joined()
    }

    private static func run(_ executable: String, _ arguments: [String]) throws {
        let process = Process()
        process.executableURL = URL(fileURLWithPath: executable)
        process.arguments = arguments
        process.standardOutput = FileHandle.nullDevice
        process.standardError = FileHandle.nullDevice
        try process.run()
        process.waitUntilExit()
        guard process.terminationStatus == 0 else { throw UpdateError.extraction }
    }
}

private func validSHA256(_ text: String) -> Bool {
    text.utf8.count == 64 && text.utf8.allSatisfy { (48...57).contains($0) || (65...70).contains($0) || (97...102).contains($0) }
}

private func privateConfiguration() -> URLSessionConfiguration {
    let configuration = URLSessionConfiguration.ephemeral
    configuration.httpCookieStorage = nil
    configuration.httpShouldSetCookies = false
    configuration.urlCredentialStorage = nil
    configuration.urlCache = nil
    return configuration
}

private func checkHTTP(_ response: URLResponse) throws {
    guard let http = response as? HTTPURLResponse else { throw UpdateError.invalidFeed }
    if http.statusCode == 429 || (http.statusCode == 403 && http.value(forHTTPHeaderField: "x-ratelimit-remaining") == "0") {
        throw UpdateError.rateLimited
    }
    guard (200..<300).contains(http.statusCode) else { throw UpdateError.http(http.statusCode) }
}

// URLSession's serial delegate queue owns mutable state, including the continuation.
private final class PackageDownload: NSObject, URLSessionDownloadDelegate, @unchecked Sendable {
    private let destination: URL
    private let progress: @Sendable (Double?) -> Void
    private var continuation: CheckedContinuation<Void, any Error>?
    private var downloadError: (any Error)?
    private var receivedFile = false

    private init(destination: URL, progress: @escaping @Sendable (Double?) -> Void) {
        self.destination = destination
        self.progress = progress
    }

    static func download(_ url: URL, to destination: URL, progress: @escaping @Sendable (Double?) -> Void) async throws {
        let delegate = PackageDownload(destination: destination, progress: progress)
        let session = URLSession(configuration: privateConfiguration(), delegate: delegate, delegateQueue: nil)
        defer { session.invalidateAndCancel() }
        try await withCheckedThrowingContinuation { (continuation: CheckedContinuation<Void, any Error>) in
            delegate.continuation = continuation
            session.downloadTask(with: url).resume()
        }
    }

    func urlSession(_ session: URLSession, downloadTask: URLSessionDownloadTask,
                    didWriteData bytesWritten: Int64, totalBytesWritten: Int64, totalBytesExpectedToWrite: Int64) {
        progress(totalBytesExpectedToWrite > 0 ? min(1, max(0, Double(totalBytesWritten) / Double(totalBytesExpectedToWrite))) : nil)
    }

    func urlSession(_ session: URLSession, task: URLSessionTask, willPerformHTTPRedirection response: HTTPURLResponse,
                    newRequest request: URLRequest, completionHandler: @escaping (URLRequest?) -> Void) {
        // GitHub packages redirect to GitHub's asset CDN; never permit an HTTPS downgrade.
        completionHandler(request.url?.scheme == "https" ? request : nil)
    }

    func urlSession(_ session: URLSession, downloadTask: URLSessionDownloadTask, didFinishDownloadingTo location: URL) {
        do {
            guard let response = downloadTask.response else { throw UpdateError.invalidFeed }
            try checkHTTP(response)
            try FileManager.default.moveItem(at: location, to: destination)
            receivedFile = true
        } catch { downloadError = error }
    }

    func urlSession(_ session: URLSession, task: URLSessionTask, didCompleteWithError error: (any Error)?) {
        if let error = downloadError ?? error { continuation?.resume(throwing: error) }
        else if !receivedFile { continuation?.resume(throwing: UpdateError.invalidFeed) }
        else { progress(1); continuation?.resume() }
        continuation = nil
    }
}

enum UpdateError: LocalizedError {
    case invalidFeed, unverifiable, notWritable, checksum, wrongApp, versionMismatch, extraction, rateLimited
    case http(Int), failed(String), recovery(String)

    var errorDescription: String? {
        switch self {
        case .invalidFeed:
            return tr("The update release information is invalid.", "업데이트 릴리스 정보가 올바르지 않습니다.")
        case .unverifiable:
            return tr("This release has no verifiable Mac package.", "이 릴리스에는 검증할 수 있는 Mac 패키지가 없습니다.")
        case .notWritable:
            return tr("The app or its parent folder is not writable. Move the app to a writable Applications folder.", "앱 또는 상위 폴더에 쓸 수 없습니다. 쓰기 가능한 응용 프로그램 폴더로 앱을 이동해 주세요.")
        case .checksum:
            return tr("The downloaded package failed its SHA-256 check. The app was not changed.", "다운로드한 패키지의 SHA-256 검증에 실패했습니다. 앱은 변경되지 않았습니다.")
        case .wrongApp:
            return tr("The update package must contain exactly one app with the correct bundle identifier.", "업데이트 패키지에는 올바른 번들 식별자를 가진 앱이 정확히 하나 있어야 합니다.")
        case .versionMismatch:
            return tr("The app version in the package does not match the release version.", "패키지의 앱 버전이 릴리스 버전과 일치하지 않습니다.")
        case .extraction:
            return tr("The update package could not be prepared. The app was not changed.", "업데이트 패키지를 준비할 수 없습니다. 앱은 변경되지 않았습니다.")
        case .rateLimited:
            return tr("GitHub's update request limit has been reached. Please try again later.", "GitHub 업데이트 요청 한도에 도달했습니다. 잠시 후 다시 시도해 주세요.")
        case .http(let status):
            return tr("The update download failed (HTTP \(status)).", "업데이트 다운로드에 실패했습니다(HTTP \(status)).")
        case .failed(let detail):
            return tr("The update could not be completed: \(detail)", "업데이트를 완료할 수 없습니다: \(detail)")
        case .recovery(let path):
            return tr("The app could not be restored. The original app is preserved at \(path).", "앱을 복원할 수 없습니다. 원본 앱은 \(path)에 보관되어 있습니다.")
        }
    }
}
