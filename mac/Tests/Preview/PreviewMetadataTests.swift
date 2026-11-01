import Foundation
import CoreGraphics
import ImageIO
import UniformTypeIdentifiers
import Testing

private struct PreviewFixture {
    let root: URL
    init() throws {
        root = FileManager.default.temporaryDirectory.appendingPathComponent("PreviewMetadata-" + UUID().uuidString, isDirectory: true)
        try FileManager.default.createDirectory(at: root, withIntermediateDirectories: true)
    }
    func remove() { try? FileManager.default.removeItem(at: root) }
}

@Test func recursivePreviewSizeCountsHiddenItemsWithoutFollowingLinks() throws {
    let fixture = try PreviewFixture(); defer { fixture.remove() }
    let folder = fixture.root.appendingPathComponent("선택 폴더", isDirectory: true)
    let child = folder.appendingPathComponent("Nested", isDirectory: true)
    try FileManager.default.createDirectory(at: child, withIntermediateDirectories: true)
    try Data(repeating: 1, count: 19).write(to: child.appendingPathComponent("report.txt"))
    try Data(repeating: 2, count: 7).write(to: folder.appendingPathComponent(".hidden"))
    let outside = fixture.root.appendingPathComponent("outside", isDirectory: true)
    try FileManager.default.createDirectory(at: outside, withIntermediateDirectories: true)
    try Data(repeating: 3, count: 1000).write(to: outside.appendingPathComponent("not-counted"))
    try FileManager.default.createSymbolicLink(at: folder.appendingPathComponent("link"), withDestinationURL: outside)
    let size = PreviewMetadata.recursiveSize(folder)
    #expect(size.bytes == 26)
    #expect(size.count == 4)
    #expect(size.error == nil)
}

@Test func previewMetadataReadsImageDimensionsAndDownsampledPoster() async throws {
    let fixture = try PreviewFixture(); defer { fixture.remove() }
    let url = fixture.root.appendingPathComponent("서울 사진.png")
    let context = try #require(CGContext(data: nil, width: 2400, height: 1200, bitsPerComponent: 8, bytesPerRow: 0, space: CGColorSpaceCreateDeviceRGB(), bitmapInfo: CGImageAlphaInfo.premultipliedLast.rawValue))
    let image = try #require(context.makeImage())
    let destination = try #require(CGImageDestinationCreateWithURL(url as CFURL, UTType.png.identifier as CFString, 1, nil))
    CGImageDestinationAddImage(destination, image, nil)
    #expect(CGImageDestinationFinalize(destination))
    let metadata = await PreviewMetadata.read(url, includePoster: true)
    #expect(metadata.dimensions == CGSize(width: 2400, height: 1200))
    #expect(metadata.poster != nil)
    #expect(metadata.isFolder == false)
    #expect(metadata.size > 0)
    #expect(metadata.error == nil)
    let summaryOnly = await PreviewMetadata.read(url, richDetails: false)
    #expect(summaryOnly.dimensions == nil)
    #expect(summaryOnly.poster == nil)
    #expect(summaryOnly.size == metadata.size)
}

@Test func previewMetadataPreservesPDFPageCount() async throws {
    let fixture = try PreviewFixture(); defer { fixture.remove() }
    let url = fixture.root.appendingPathComponent("문서.pdf")
    let context = try #require(CGContext(url as CFURL, mediaBox: nil, nil))
    for _ in 0..<3 { context.beginPDFPage(nil); context.endPDFPage() }
    context.closePDF()
    let metadata = await PreviewMetadata.read(url, includePoster: true)
    #expect(metadata.pages == 3)
    #expect(metadata.poster != nil)
    #expect(metadata.error == nil)
}

@Test func previewMetadataReportsMissingItemsInsteadOfInventingValues() async throws {
    let fixture = try PreviewFixture(); defer { fixture.remove() }
    let metadata = await PreviewMetadata.read(fixture.root.appendingPathComponent("missing.txt"))
    #expect(metadata.error != nil)
    #expect(metadata.pages == nil)
    #expect(metadata.dimensions == nil)
    #expect(metadata.poster == nil)
}
