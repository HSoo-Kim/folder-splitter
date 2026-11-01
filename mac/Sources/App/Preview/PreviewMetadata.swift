import AppKit
import AVFoundation
import CoreServices
import ImageIO
import PDFKit
import UniformTypeIdentifiers

struct PreviewMetadata: @unchecked Sendable {
    var url: URL
    var name: String
    var icon: NSImage
    var isFolder: Bool
    var size: Int64
    var count: Int?
    var kind: String
    var created: Date?
    var modified: Date?
    var lastOpened: Date?
    var tags: [String]
    var dimensions: CGSize?
    var duration: Double?
    var pages: Int?
    var poster: NSImage? = nil
    var typeIdentifier: String?
    var hiddenExtension: Bool
    var locked: Bool
    var owner: String
    var group: String
    var readable: Bool
    var writable: Bool
    var applications: [URL]
    var defaultApplication: URL?
    var error: String?

    static func read(_ url: URL, includeApplications: Bool = false, richDetails: Bool = true, includePoster: Bool = false) async -> PreviewMetadata {
        let fm = FileManager.default
        let keys: Set<URLResourceKey> = [.localizedNameKey, .isDirectoryKey, .isPackageKey, .fileSizeKey, .localizedTypeDescriptionKey, .creationDateKey, .contentModificationDateKey, .tagNamesKey, .typeIdentifierKey, .hasHiddenExtensionKey, .isUserImmutableKey]
        let result: Result<URLResourceValues, Error> = Result { try url.resourceValues(forKeys: keys) }
        let values = try? result.get()
        let attributes = (try? fm.attributesOfItem(atPath: url.path)) ?? [:]
        let lastOpened = MDItemCreate(kCFAllocatorDefault, url.path as CFString).flatMap {
            MDItemCopyAttribute($0, kMDItemLastUsedDate) as? Date
        }
        var metadata = PreviewMetadata(
            url: url, name: FileName.display(values?.localizedName ?? url.lastPathComponent),
            icon: NSWorkspace.shared.icon(forFile: url.path),
            isFolder: values?.isDirectory == true && values?.isPackage != true,
            size: Int64(values?.fileSize ?? 0), kind: values?.localizedTypeDescription ?? tr("Item", "항목"),
            created: values?.creationDate, modified: values?.contentModificationDate, lastOpened: lastOpened,
            tags: values?.tagNames ?? [], typeIdentifier: values?.typeIdentifier,
            hiddenExtension: values?.hasHiddenExtension == true, locked: values?.isUserImmutable == true,
            owner: attributes[.ownerAccountName] as? String ?? "—", group: attributes[.groupOwnerAccountName] as? String ?? "—",
            readable: fm.isReadableFile(atPath: url.path), writable: fm.isWritableFile(atPath: url.path),
            applications: [], error: nil)
        if case .failure(let error) = result { metadata.error = error.localizedDescription; return metadata }
        guard richDetails else { return metadata }
        if metadata.isFolder {
            do { metadata.count = try fm.contentsOfDirectory(at: url, includingPropertiesForKeys: nil).count }
            catch { metadata.error = error.localizedDescription }
        }
        guard !Task.isCancelled else { return metadata }
        if let type = metadata.typeIdentifier.flatMap(UTType.init) {
            if type.conforms(to: .image), let source = CGImageSourceCreateWithURL(url as CFURL, nil),
               let properties = CGImageSourceCopyPropertiesAtIndex(source, 0, nil) as? [CFString: Any],
               let width = properties[kCGImagePropertyPixelWidth] as? NSNumber, let height = properties[kCGImagePropertyPixelHeight] as? NSNumber {
                metadata.dimensions = CGSize(width: width.doubleValue, height: height.doubleValue)
                if includePoster, let thumbnail = CGImageSourceCreateThumbnailAtIndex(source, 0, [
                    kCGImageSourceCreateThumbnailFromImageAlways: true,
                    kCGImageSourceCreateThumbnailWithTransform: true,
                    kCGImageSourceThumbnailMaxPixelSize: 1024
                ] as CFDictionary) {
                    metadata.poster = NSImage(cgImage: thumbnail, size: .zero)
                }
            }
            if type.conforms(to: .pdf), let document = CGPDFDocument(url as CFURL) {
                metadata.pages = document.numberOfPages
                if includePoster, let page = document.page(at: 1) {
                    let bounds = page.getBoxRect(.mediaBox)
                    let scale = min(1, 1024 / max(bounds.width, bounds.height))
                    let width = max(1, Int(bounds.width * scale)), height = max(1, Int(bounds.height * scale))
                    if let context = CGContext(data: nil, width: width, height: height, bitsPerComponent: 8, bytesPerRow: 0, space: CGColorSpaceCreateDeviceRGB(), bitmapInfo: CGImageAlphaInfo.premultipliedLast.rawValue) {
                        let rect = CGRect(x: 0, y: 0, width: width, height: height)
                        context.setFillColor(NSColor.white.cgColor); context.fill(rect)
                        context.concatenate(page.getDrawingTransform(.mediaBox, rect: rect, rotate: 0, preserveAspectRatio: true))
                        context.drawPDFPage(page)
                        if let image = context.makeImage() { metadata.poster = NSImage(cgImage: image, size: .zero) }
                    }
                }
            }
            if type.conforms(to: .audio) || type.conforms(to: .movie) {
                do {
                    let duration = try await AVURLAsset(url: url).load(.duration)
                    if duration.seconds.isFinite { metadata.duration = duration.seconds }
                } catch { if !Task.isCancelled { metadata.error = error.localizedDescription } }
            }
        }
        if includeApplications && !Task.isCancelled {
            metadata.applications = NSWorkspace.shared.urlsForApplications(toOpen: url)
            metadata.defaultApplication = NSWorkspace.shared.urlForApplication(toOpen: url)
        }
        return metadata
    }

    var detailRows: [(String, String)] {
        var rows = [(tr("Kind", "종류"), kind)]
        if !isFolder { rows.append((tr("Size", "크기"), Format.bytes(size))) }
        if let count { rows.append((tr("Contains", "포함 항목"), Format.items(count))) }
        if let created { rows.append((tr("Created", "생성일"), Format.date(created))) }
        if let modified { rows.append((tr("Modified", "수정일"), Format.date(modified))) }
        if let lastOpened { rows.append((tr("Last Opened", "마지막으로 열어 본 날짜"), Format.date(lastOpened))) }
        rows.append((tr("Where", "위치"), PathText.abbreviated(url.deletingLastPathComponent())))
        if let dimensions { rows.append((tr("Dimensions", "치수"), "\(Int(dimensions.width)) × \(Int(dimensions.height))")) }
        if let duration { rows.append((tr("Duration", "재생 시간"), String(format: "%d:%02d", Int(duration) / 60, Int(duration) % 60))) }
        if let pages { rows.append((tr("Pages", "페이지 수"), pages.formatted())) }
        if !tags.isEmpty { rows.append((tr("Tags", "태그"), tags.joined(separator: ", "))) }
        if let error { rows.append((tr("Unavailable", "사용할 수 없음"), error)) }
        return rows
    }

    struct FolderSize: Sendable {
        var bytes: Int64 = 0
        var count = 0
        var error: String?
    }
    static func recursiveSize(_ url: URL) -> FolderSize {
        var result = FolderSize()
        let keys: [URLResourceKey] = [.isRegularFileKey, .isSymbolicLinkKey, .fileSizeKey]
        guard let enumerator = FileManager.default.enumerator(at: url, includingPropertiesForKeys: keys, options: [], errorHandler: { _, error in result.error = error.localizedDescription; return !Task.isCancelled }) else {
            result.error = tr("This folder could not be read.", "이 폴더를 읽을 수 없습니다."); return result
        }
        while !Task.isCancelled, let item = enumerator.nextObject() as? URL {
            result.count += 1
            do {
                let values = try item.resourceValues(forKeys: Set(keys))
                if values.isRegularFile == true { result.bytes += Int64(values.fileSize ?? 0) }
            } catch { result.error = error.localizedDescription }
        }
        return result
    }
}

@MainActor final class PreviewDetailGrid: NSStackView {
    init() {
        super.init(frame: .zero); orientation = .vertical; alignment = .leading; spacing = 8
        setAccessibilityRole(.group); setAccessibilityLabel(tr("Details", "세부사항"))
    }
    required init?(coder: NSCoder) { fatalError("init(coder:) has not been implemented") }
    func setRows(_ rows: [(String, String)]) {
        for child in arrangedSubviews { removeArrangedSubview(child); child.removeFromSuperview() }
        for (name, value) in rows {
            let label = NSTextField(labelWithString: name); label.font = .systemFont(ofSize: 11); label.textColor = .secondaryLabelColor
            let content = NSTextField(wrappingLabelWithString: value); content.font = .systemFont(ofSize: 12); content.isSelectable = true
            let row = NSStackView(views: [label, content]); row.alignment = .firstBaseline; row.spacing = 10
            label.translatesAutoresizingMaskIntoConstraints = false; label.widthAnchor.constraint(equalToConstant: 82).isActive = true
            content.setContentCompressionResistancePriority(.defaultLow, for: .horizontal)
            addArrangedSubview(row); row.widthAnchor.constraint(equalTo: widthAnchor).isActive = true
        }
    }
}

/// Parent notifications are only a hint that an atomic save may have replaced the item.
/// Probe the item's identity/metadata before notifying; sibling writes are not item changes.
@MainActor final class PreviewFileWatcher {
    private final class Monitor: @unchecked Sendable {
        private struct Signature: Equatable {
            let device: dev_t
            let inode: ino_t
            let size: off_t
            let modifiedSeconds: Int
            let modifiedNanos: Int
            let changedSeconds: Int
            let changedNanos: Int
            init?(_ url: URL) {
                var value = stat()
                guard lstat(url.path, &value) == 0 else { return nil }
                device = value.st_dev; inode = value.st_ino; size = value.st_size
                modifiedSeconds = value.st_mtimespec.tv_sec; modifiedNanos = value.st_mtimespec.tv_nsec
                changedSeconds = value.st_ctimespec.tv_sec; changedNanos = value.st_ctimespec.tv_nsec
            }
        }
        private let queue = DispatchQueue(label: "FolderSplitter.preview-events", qos: .utility)
        private let url: URL
        private let changed: @Sendable () -> Void
        private var itemSource: DispatchSourceFileSystemObject?
        private var parentSource: DispatchSourceFileSystemObject?
        private var signature: Signature?
        private var pending: DispatchWorkItem?
        private var itemChanged = false
        private var stopped = false

        init(_ url: URL, changed: @escaping @Sendable () -> Void) {
            self.url = url; self.changed = changed
            queue.async { [self] in
                signature = Signature(url)
                itemSource = watch(url, item: true)
                parentSource = watch(url.deletingLastPathComponent(), item: false)
            }
        }
        private func watch(_ url: URL, item: Bool) -> DispatchSourceFileSystemObject? {
            let fd = open(url.path, O_EVTONLY)
            guard fd >= 0 else { return nil }
            let source = DispatchSource.makeFileSystemObjectSource(fileDescriptor: fd, eventMask: [.write, .delete, .rename, .attrib, .extend, .revoke], queue: queue)
            source.setEventHandler { [weak self] in self?.schedule(item: item) }
            source.setCancelHandler { close(fd) }; source.resume()
            return source
        }
        private func schedule(item: Bool) {
            guard !stopped else { return }
            itemChanged = itemChanged || item
            guard pending == nil else { return }
            // Fixed coalescing window, not a trailing debounce that can starve during a sync.
            let work = DispatchWorkItem { [weak self] in self?.probe() }
            pending = work
            queue.asyncAfter(deadline: .now() + 0.35, execute: work)
        }
        private func probe() {
            pending = nil
            guard !stopped else { return }
            let next = Signature(url)
            let didChange = itemChanged || next != signature
            itemChanged = false
            if next?.device != signature?.device || next?.inode != signature?.inode {
                itemSource?.cancel(); itemSource = watch(url, item: true)
            }
            signature = next
            if didChange { changed() }
        }
        func stop() {
            queue.async { [self] in
                stopped = true; pending?.cancel(); pending = nil
                itemSource?.cancel(); itemSource = nil
                parentSource?.cancel(); parentSource = nil
            }
        }
    }
    private let monitor: Monitor
    init(_ url: URL, changed: @escaping @MainActor () -> Void) {
        monitor = Monitor(url) { Task { @MainActor in changed() } }
    }
    deinit { monitor.stop() }
}
