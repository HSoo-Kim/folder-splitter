import AppKit
import CoreServices
import Darwin
import QuickLookThumbnailing

struct FileListSnapshot: Sendable {
    let items: [FileItem]
    let fileBytes: Int64
    let identities: [URL: String]
    /// Actual directory and stable identity after resolving navigation aliases and symlinks.
    let directory: URL
    let directoryIdentity: String?
    /// Known ubiquitous download state only; ordinary local and legacy Dropbox items are absent.
    let ubiquitousDownloads: [URL: Bool]
}

/// Synchronous disk work intended for the list controller's background loading task.
enum FileListReader {
    private static let navigationKeys: Set<URLResourceKey> = [
        .isDirectoryKey, .isPackageKey, .isAliasFileKey, .isSymbolicLinkKey,
        .volumeIsLocalKey, .fileResourceIdentifierKey, .volumeIdentifierKey
    ]
    private static let itemKeys: Set<URLResourceKey> = [
        .nameKey, .isDirectoryKey, .isPackageKey, .isHiddenKey,
        .isSymbolicLinkKey, .isAliasFileKey, .fileSizeKey,
        .contentModificationDateKey, .creationDateKey, .addedToDirectoryDateKey,
        .localizedTypeDescriptionKey, .typeIdentifierKey, .tagNamesKey,
        .fileResourceIdentifierKey, .volumeIdentifierKey
    ]
    private static let cloudKeys: Set<URLResourceKey> = [
        .isUbiquitousItemKey, .ubiquitousItemDownloadingStatusKey,
        .ubiquitousItemIsDownloadingKey, .ubiquitousItemIsUploadingKey,
        .ubiquitousItemHasUnresolvedConflictsKey
    ]

    static func read(_ folder: URL, showHidden: Bool) throws -> FileListSnapshot {
        try Task.checkCancellation()
        let (directory, directoryValues) = try resolvedTarget(folder)
        guard directoryValues.isDirectory == true, directoryValues.isPackage != true else {
            throw NSError(domain: NSPOSIXErrorDomain, code: Int(ENOTDIR),
                          userInfo: [NSURLErrorKey: folder])
        }
        // One folder-level membership check gates every per-item ubiquitous resource request.
        let ubiquitousFolder = FileManager.default.isUbiquitousItem(at: directory)
        let keys = ubiquitousFolder ? itemKeys.union(cloudKeys) : itemKeys
        let dropboxRoots = CloudStatusReader.dropboxRoots()
        let insideDropbox = dropboxRoots.contains { PathText.isWithin(directory, $0) }
        let directDropboxRoots = Set(dropboxRoots.filter { $0.deletingLastPathComponent().standardizedFileURL == directory.standardizedFileURL })
        let options: FileManager.DirectoryEnumerationOptions = showHidden ? [] : [.skipsHiddenFiles]
        try Task.checkCancellation()
        let children = try FileManager.default.contentsOfDirectory(
            at: directory, includingPropertiesForKeys: Array(keys), options: options
        )
        var items: [FileItem] = []
        items.reserveCapacity(children.count)
        var fileBytes: Int64 = 0
        var identities: [URL: String] = [:]
        identities.reserveCapacity(children.count)
        var ubiquitousDownloads: [URL: Bool] = [:]
        for url in children {
            try Task.checkCancellation()
            let values: URLResourceValues
            do {
                values = try url.resourceValues(forKeys: keys)
            } catch {
                // A rename or deletion between enumeration and metadata retrieval is normal.
                if isMissing(error) { continue }
                throw error
            }
            let name = FileName.display(PathText.nfc(values.name ?? url.lastPathComponent))
            let hidden = values.isHidden == true || name.hasPrefix(".")
            if !showHidden && hidden { continue }
            let isAlias = values.isAliasFile == true
            let isSymbolicLink = values.isSymbolicLink == true
            var targetValues = values
            if isAlias || isSymbolicLink {
                // Broken or inaccessible links remain visible, but cannot be navigated as folders.
                do {
                    let (_, resolvedValues) = try resolvedTarget(url, values: values)
                    targetValues = resolvedValues
                } catch is CancellationError {
                    throw CancellationError()
                } catch {
                    // Keep the original link metadata when its target is unavailable.
                }
            }
            let isPackage = values.isPackage == true || targetValues.isPackage == true
            let isDirectory = targetValues.isDirectory == true && !isPackage
            let size: Int64? = isDirectory || values.isDirectory == true
                ? nil : values.fileSize.map { Int64($0) }
            try Task.checkCancellation()
            let tags = ((try? FinderTags.read(url)) ?? values.tagNames ?? []).map { PathText.nfc($0) }
            try Task.checkCancellation()
            let inDropbox = insideDropbox || (!directDropboxRoots.isEmpty && directDropboxRoots.contains(url.standardizedFileURL))
            let cloud = CloudStatusReader.status(for: url, values: ubiquitousFolder ? values : nil, insideDropbox: inDropbox)
            if ubiquitousFolder, values.isUbiquitousItem == true {
                switch values.ubiquitousItemDownloadingStatus {
                case .notDownloaded: ubiquitousDownloads[url] = false
                case .downloaded, .current: ubiquitousDownloads[url] = true
                default: break
                }
            }
            items.append(FileItem(
                url: url, name: name, isDirectory: isDirectory, isPackage: isPackage,
                isHidden: hidden, isSymbolicLink: isSymbolicLink, isAlias: isAlias,
                size: size, modified: values.contentModificationDate,
                created: values.creationDate, added: values.addedToDirectoryDate,
                kind: PathText.nfc(values.localizedTypeDescription
                    ?? (isDirectory ? tr("Folder", "폴더") : tr("File", "파일"))),
                typeIdentifier: values.typeIdentifier,
                tags: tags, cloud: cloud
            ))
            identities[url] = identity(values)
            if cloud != .dropboxOnlineOnly, let size, size > 0 {
                let (sum, overflow) = fileBytes.addingReportingOverflow(size)
                fileBytes = overflow ? Int64.max : sum
            }
        }
        try Task.checkCancellation()
        return FileListSnapshot(items: items, fileBytes: fileBytes, identities: identities,
                                directory: directory, directoryIdentity: identity(directoryValues),
                                ubiquitousDownloads: ubiquitousDownloads)
    }

    static func identity(_ values: URLResourceValues) -> String? {
        guard let identifier = values.fileResourceIdentifier else { return nil }
        return "\(String(describing: values.volumeIdentifier)):\(String(describing: identifier))"
    }

    static func arrange(_ items: [FileItem], filter: String, settings: FolderViewSettings,
                        foldersFirst: Bool) -> [FileItem] {
        let words = PathText.nfc(filter).split(whereSeparator: { $0.isWhitespace }).map(String.init)
        let locale = Loc.locale
        let matching = words.isEmpty ? items : items.filter { item in
            words.allSatisfy { item.name.range(of: $0, options: .caseInsensitive, locale: locale) != nil }
        }
        return matching.sorted { lhs, rhs in
            if foldersFirst && lhs.isDirectory != rhs.isDirectory { return lhs.isDirectory }
            var order: ComparisonResult
            switch settings.sortKey {
            case .name: order = lhs.name.localizedStandardCompare(rhs.name)
            case .kind: order = lhs.kind.localizedStandardCompare(rhs.kind)
            case .modified: order = compare(lhs.modified, rhs.modified)
            case .created: order = compare(lhs.created, rhs.created)
            case .added: order = compare(lhs.added, rhs.added)
            case .size: order = compare(lhs.size, rhs.size)
            }
            if order == .orderedSame { order = lhs.name.localizedStandardCompare(rhs.name) }
            if order == .orderedSame {
                order = lhs.url.path.compare(rhs.url.path, options: .literal)
            }
            return settings.ascending ? order == .orderedAscending : order == .orderedDescending
        }
    }

    static func failureMessage(_ error: Error, folder: URL) -> String {
        let name = PathText.nfc(folder.lastPathComponent.isEmpty ? folder.path : folder.lastPathComponent)
        let nsError = error as NSError
        let underlying = nsError.userInfo[NSUnderlyingErrorKey] as? NSError
        let errors = [nsError] + (underlying.map { [$0] } ?? [])
        if errors.contains(where: { error in
            (error.domain == NSCocoaErrorDomain && error.code == CocoaError.fileReadNoPermission.rawValue)
                || (error.domain == NSPOSIXErrorDomain && [Int(EACCES), Int(EPERM)].contains(error.code))
        }) {
            return tr("The folder “\(name)” can’t be read. Check its permissions in Finder and allow access in System Settings > Privacy & Security.",
                      "“\(name)” 폴더를 읽을 수 없습니다. Finder에서 접근 권한을 확인하고 시스템 설정 > 개인정보 보호 및 보안에서 접근을 허용하십시오.")
        }
        if isMissing(error) {
            return tr("The folder “\(name)” no longer exists or its volume is unavailable.",
                      "“\(name)” 폴더가 더 이상 존재하지 않거나 볼륨을 사용할 수 없습니다.")
        }
        if errors.contains(where: { $0.domain == NSPOSIXErrorDomain && $0.code == Int(ENOTDIR) }) {
            return tr("“\(name)” is not a browsable folder.", "“\(name)”은(는) 탐색할 수 있는 폴더가 아닙니다.")
        }
        return tr("Couldn’t read the folder “\(name)”. \(nsError.localizedDescription)",
                  "“\(name)” 폴더를 읽을 수 없습니다. \(nsError.localizedDescription)")
    }

    /// Resolve navigation identity off-main before enumerating a possible ancestor link.
    static func directoryLocation(_ folder: URL) throws -> (url: URL, identity: String?) {
        let (directory, values) = try resolvedTarget(folder)
        guard values.isDirectory == true, values.isPackage != true else {
            throw NSError(domain: NSPOSIXErrorDomain, code: Int(ENOTDIR),
                          userInfo: [NSURLErrorKey: folder])
        }
        return (directory, identity(values))
    }

    /// Alias resolution may contact a volume, so navigation must not do it on the main thread.
    static func resolvedFolder(_ item: FileItem) async -> URL? {
        await Task.detached(priority: .userInitiated) { () -> URL? in
            guard let (url, values) = try? resolvedTarget(item.url),
                  values.isDirectory == true, values.isPackage != true else { return nil }
            return url
        }.value
    }

    private static func resolvedTarget(_ url: URL, values: URLResourceValues? = nil) throws
        -> (URL, URLResourceValues) {
        try Task.checkCancellation()
        var current = url.standardizedFileURL
        var metadata: URLResourceValues
        if let values { metadata = values }
        else { metadata = try current.resourceValues(forKeys: navigationKeys) }
        var visited: Set<URL> = []
        while metadata.isAliasFile == true || metadata.isSymbolicLink == true {
            try Task.checkCancellation()
            guard visited.insert(current).inserted else {
                throw NSError(domain: NSPOSIXErrorDomain, code: Int(ELOOP),
                              userInfo: [NSURLErrorKey: current])
            }
            let target: URL
            if metadata.isAliasFile == true {
                target = try URL(resolvingAliasFileAt: current, options: [.withoutUI, .withoutMounting])
            } else {
                target = current.resolvingSymlinksInPath()
            }
            guard target != current else {
                throw NSError(domain: NSPOSIXErrorDomain, code: Int(ELOOP),
                              userInfo: [NSURLErrorKey: current])
            }
            current = target.standardizedFileURL
            metadata = try current.resourceValues(forKeys: navigationKeys)
        }
        // A navigable directory can also have a symlink in an ancestor component.
        current = current.resolvingSymlinksInPath().standardizedFileURL
        try Task.checkCancellation()
        return (current, metadata)
    }

    private static func compare<T: Comparable>(_ lhs: T?, _ rhs: T?) -> ComparisonResult {
        switch (lhs, rhs) {
        case (nil, nil): return .orderedSame
        case (nil, _): return .orderedAscending
        case (_, nil): return .orderedDescending
        case let (lhs?, rhs?):
            return lhs < rhs ? .orderedAscending : lhs > rhs ? .orderedDescending : .orderedSame
        }
    }

    private static func isMissing(_ error: Error) -> Bool {
        let error = error as NSError
        if error.domain == NSCocoaErrorDomain {
            return error.code == CocoaError.fileNoSuchFile.rawValue
                || error.code == CocoaError.fileReadNoSuchFile.rawValue
        }
        if error.domain == NSPOSIXErrorDomain { return error.code == Int(ENOENT) }
        if let underlying = error.userInfo[NSUnderlyingErrorKey] as? NSError {
            return underlying.domain == NSPOSIXErrorDomain && underlying.code == Int(ENOENT)
        }
        return false
    }
}

/// Recursive streams cover the displayed root (and any expanded out-of-tree alias targets).
/// The visible-directory filter can change without rebuilding or resolving the stream roots.
final class FileListWatcher {
    private final class CallbackState {
        let onChange: @Sendable () -> Void
        private let lock = NSLock()
        private var active = true
        private var watchedPaths: Set<String> = []

        init(_ onChange: @escaping @Sendable () -> Void) { self.onChange = onChange }
        var isActive: Bool {
            lock.lock()
            defer { lock.unlock() }
            return active
        }

        func setWatchedPaths(_ paths: Set<String>) {
            lock.lock()
            watchedPaths = paths
            lock.unlock()
        }

        func notify(paths: NSArray, flags: UnsafePointer<FSEventStreamEventFlags>, count: Int) {
            lock.lock()
            let active = active
            let watchedPaths = watchedPaths
            lock.unlock()
            guard active else { return }

            let rescanFlags = FSEventStreamEventFlags(
                kFSEventStreamEventFlagMustScanSubDirs | kFSEventStreamEventFlagUserDropped
                    | kFSEventStreamEventFlagKernelDropped | kFSEventStreamEventFlagEventIdsWrapped
                    | kFSEventStreamEventFlagRootChanged
            )
            let itemFlags = FSEventStreamEventFlags(
                kFSEventStreamEventFlagItemCreated | kFSEventStreamEventFlagItemRemoved
                    | kFSEventStreamEventFlagItemInodeMetaMod | kFSEventStreamEventFlagItemRenamed
                    | kFSEventStreamEventFlagItemModified | kFSEventStreamEventFlagItemFinderInfoMod
                    | kFSEventStreamEventFlagItemChangeOwner | kFSEventStreamEventFlagItemXattrMod
                    | kFSEventStreamEventFlagItemIsFile | kFSEventStreamEventFlagItemIsDir
                    | kFSEventStreamEventFlagItemIsSymlink
            )
            for index in 0..<count {
                if flags[index] & rescanFlags != 0 {
                    notify()
                    return
                }
                guard index < paths.count, let path = paths[index] as? String else { continue }
                let url = URL(fileURLWithPath: path).standardizedFileURL
                if watchedPaths.contains(url.path)
                    || (flags[index] & itemFlags != 0
                        && watchedPaths.contains(url.deletingLastPathComponent().path)) {
                    notify()
                    return
                }
            }
        }

        func notify() {
            lock.lock()
            let shouldNotify = active
            lock.unlock()
            if shouldNotify { onChange() }
        }

        func stop() {
            lock.lock()
            active = false
            lock.unlock()
        }
    }

    private let lock = NSLock()
    private let state: CallbackState
    private let queue = DispatchQueue(label: "FolderSplitter.file-list-events", qos: .utility)
    private var stream: FSEventStreamRef?

    init(folders: [URL], watchedPaths: Set<String>, onChange: @escaping @Sendable () -> Void) {
        state = CallbackState(onChange)
        state.setWatchedPaths(watchedPaths)
        queue.async { [weak self] in self?.start(folders: folders) }
    }

    func updateWatchedPaths(_ paths: Set<String>) { state.setWatchedPaths(paths) }

    private func start(folders: [URL]) {
        let paths = Set(folders.filter(\.isFileURL).map { $0.standardizedFileURL.path })
        guard !paths.isEmpty, state.isActive else { return }
        var context = FSEventStreamContext(
            version: 0, info: Unmanaged.passUnretained(state).toOpaque(),
            retain: { pointer in
                guard let pointer else { return nil }
                _ = Unmanaged<CallbackState>.fromOpaque(pointer).retain()
                return UnsafeRawPointer(pointer)
            },
            release: { pointer in
                guard let pointer else { return }
                Unmanaged<CallbackState>.fromOpaque(pointer).release()
            }, copyDescription: nil
        )
        let flags = FSEventStreamCreateFlags(kFSEventStreamCreateFlagUseCFTypes
            | kFSEventStreamCreateFlagFileEvents | kFSEventStreamCreateFlagWatchRoot)
        guard let created = FSEventStreamCreate(
            kCFAllocatorDefault,
            { _, context, count, eventPaths, eventFlags, _ in
                guard let context else { return }
                // UseCFTypes supplies one CFArray of paths, not a C array of strings.
                let paths = unsafeBitCast(eventPaths, to: NSArray.self)
                Unmanaged<CallbackState>.fromOpaque(context).takeUnretainedValue()
                    .notify(paths: paths, flags: eventFlags, count: count)
            },
            &context, Array(paths).sorted() as CFArray,
            FSEventStreamEventId(kFSEventStreamEventIdSinceNow), 0.25, flags
        ) else { return }
        FSEventStreamSetDispatchQueue(created, queue)
        guard FSEventStreamStart(created) else {
            FSEventStreamInvalidate(created)
            FSEventStreamRelease(created)
            return
        }
        lock.lock()
        if state.isActive {
            stream = created
            lock.unlock()
        } else {
            lock.unlock()
            FSEventStreamStop(created)
            FSEventStreamInvalidate(created)
            FSEventStreamRelease(created)
        }
    }

    func stop() {
        lock.lock()
        state.stop()
        let existing = stream
        stream = nil
        lock.unlock()
        if let existing {
            FSEventStreamStop(existing)
            FSEventStreamInvalidate(existing)
            FSEventStreamRelease(existing)
        }
    }

    deinit { stop() }
}

@MainActor
final class FileListIconCache {
    static let shared = FileListIconCache()

    private final class ThumbnailState {
        var callbacks: [(NSImage) -> Void]
        var rank = -1

        init(_ completion: @escaping (NSImage) -> Void) {
            callbacks = [completion]
        }
    }

    private let cache = NSCache<NSString, NSImage>()
    private var pending: [String: [(NSImage) -> Void]] = [:]
    private var thumbnailPending: [String: ThumbnailState] = [:]
    private let workers: OperationQueue = {
        let queue = OperationQueue()
        queue.name = "FolderSplitter.file-icons"
        queue.qualityOfService = .utility
        queue.maxConcurrentOperationCount = 2
        return queue
    }()

    private init() {
        cache.countLimit = 512
        cache.totalCostLimit = 64 * 1_024 * 1_024
    }

    /// Effective icons arrive first; real Quick Look representations may improve them later.
    /// Only cache lookup and request bookkeeping happen on the main thread.
    func image(for item: FileItem, size: CGFloat, thumbnail: Bool, scale: CGFloat? = nil,
               completion: @escaping (NSImage) -> Void) -> NSImage? {
        let edge = size.isFinite ? min(max(size, 1), 1_024) : 72
        let requestedScale = scale ?? NSScreen.main?.backingScaleFactor ?? 2
        let backingScale = requestedScale.isFinite ? min(max(requestedScale, 1), 8) : 2
        let version = "\(item.url.absoluteString)|\(item.modified?.timeIntervalSince1970 ?? 0)|\(item.size ?? -1)"
        let iconKey = "icon|\(version)"
        guard thumbnail, !item.isDirectory else {
            return effectiveImage(for: item.url, key: iconKey, completion: completion)
        }

        let key = "thumbnail|\(version)|\(edge)|\(backingScale)"
        if let request = thumbnailPending[key] {
            request.callbacks.append(completion)
            return cache.object(forKey: key as NSString) ?? cache.object(forKey: iconKey as NSString)
        }
        if let image = cache.object(forKey: key as NSString) { return image }
        thumbnailPending[key] = ThumbnailState(completion)
        let icon = effectiveImage(for: item.url, key: iconKey) { [weak self] image in
            self?.beginThumbnail(for: item.url, key: key, icon: image, edge: edge, scale: backingScale)
        }
        if let icon {
            beginThumbnail(for: item.url, key: key, icon: icon, edge: edge, scale: backingScale)
        }
        return icon
    }

    func applicationImage(for url: URL, completion: @escaping (NSImage) -> Void) -> NSImage? {
        effectiveImage(for: url, key: "application|\(url.absoluteString)", completion: completion)
    }

    private func effectiveImage(for url: URL, key: String,
                                completion: @escaping (NSImage) -> Void) -> NSImage? {
        if let image = cache.object(forKey: key as NSString) { return image }
        if pending[key] != nil {
            pending[key]?.append(completion)
            return nil
        }
        pending[key] = [completion]
        workers.addOperation { [weak self] in
            let icon = Self.effectiveIcon(for: url)
            DispatchQueue.main.async { [weak self] in
                guard let self, let callbacks = self.pending.removeValue(forKey: key) else { return }
                self.store(icon, key: key, pixels: 32)
                callbacks.forEach { $0(icon) }
            }
        }
        return nil
    }

    private func beginThumbnail(for url: URL, key: String, icon: NSImage, edge: CGFloat, scale: CGFloat) {
        let pixels = Int(ceil(edge * scale))
        publishThumbnail(key: key, image: icon, rank: 0, pixels: pixels, finished: false)
        workers.addOperation { [weak self] in
            let request = QLThumbnailGenerator.Request(
                fileAt: url, size: NSSize(width: edge, height: edge),
                scale: scale, representationTypes: .all
            )
            QLThumbnailGenerator.shared.generateRepresentations(for: request) { representation, type, _ in
                let image = representation.map { Self.thumbnailImage($0, edge: edge) }
                let rank = Int(type.rawValue) + 1
                let finished = type == .thumbnail
                DispatchQueue.main.async { [weak self] in
                    self?.publishThumbnail(key: key, image: image, rank: rank,
                                           pixels: pixels, finished: finished)
                }
            }
        }
    }

    private func publishThumbnail(key: String, image: NSImage?, rank: Int, pixels: Int, finished: Bool) {
        guard let request = thumbnailPending[key] else { return }
        if let image, rank > request.rank {
            request.rank = rank
            store(image, key: key, pixels: pixels)
            request.callbacks.forEach { $0(image) }
        }
        // The most representative requested type always gets a callback, even on failure.
        // Missing representations never replace the effective icon or a successful thumbnail.
        if finished { thumbnailPending.removeValue(forKey: key) }
    }

    nonisolated private static func effectiveIcon(for url: URL) -> NSImage {
        var value: AnyObject?
        if (try? (url as NSURL).getResourceValue(&value, forKey: .effectiveIconKey)) != nil,
           let image = value as? NSImage {
            return image
        }
        return NSWorkspace.shared.icon(forFile: url.path)
    }

    /// Fits the thumbnail into the tile. Page-like thumbnails (documents, PDFs, text) are white
    /// and would vanish on the list background, so they get Finder's hairline frame and soft
    /// shadow; icon representations are drawn as they are.
    nonisolated private static func thumbnailImage(_ representation: QLThumbnailRepresentation,
                                                   edge: CGFloat) -> NSImage {
        let source = representation.cgImage
        let width = CGFloat(source.width)
        let height = CGFloat(source.height)
        let framed = representation.type != .icon
        let inset: CGFloat = framed ? 3 : 0
        let factor = (edge - inset * 2) / max(width, height, 1)
        let content = NSSize(width: (width * factor).rounded(), height: (height * factor).rounded())
        let size = NSSize(width: content.width + inset * 2, height: content.height + inset * 2)
        return NSImage(size: size, flipped: false) { _ in
            guard let context = NSGraphicsContext.current?.cgContext else { return false }
            let frame = CGRect(x: inset, y: inset, width: content.width, height: content.height)
            if framed {
                context.saveGState()
                context.setShadow(offset: CGSize(width: 0, height: -1), blur: 3,
                                  color: NSColor.black.withAlphaComponent(0.22).cgColor)
                context.setFillColor(NSColor.white.cgColor)
                context.fill(frame)
                context.restoreGState()
            }
            context.interpolationQuality = .high
            context.draw(source, in: frame)
            if framed {
                context.setStrokeColor(NSColor.separatorColor.cgColor)
                context.setLineWidth(0.5)
                context.stroke(frame.insetBy(dx: 0.25, dy: 0.25))
            }
            return true
        }
    }

    private func store(_ image: NSImage, key: String, pixels: Int) {
        let representationCost = image.representations.reduce(0) { total, representation in
            let width = max(representation.pixelsWide, 0)
            let height = max(representation.pixelsHigh, 0)
            let area = width.multipliedReportingOverflow(by: height)
            let bytes = area.partialValue.multipliedReportingOverflow(by: 4)
            let sum = total.addingReportingOverflow(bytes.partialValue)
            return area.overflow || bytes.overflow || sum.overflow ? Int.max : sum.partialValue
        }
        cache.setObject(image, forKey: key as NSString, cost: max(representationCost, pixels * pixels * 4))
    }
}
