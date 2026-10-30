import Foundation

// Matching is deliberately independent of the user's language and filesystem normalization.
enum SearchText {
    static func path(_ value: String) -> String {
        PathText.nfc(URL(fileURLWithPath: (value as NSString).expandingTildeInPath).standardizedFileURL.path)
    }
    static func fold(_ value: String) -> String {
        PathText.nfc(value.folding(options: .caseInsensitive, locale: Locale(identifier: "en_US_POSIX")))
    }
    static func within(_ path: String, _ root: String) -> Bool {
        path == root || root == "/" || path.hasPrefix(root + "/")
    }
}

struct SearchQuery: Sendable {
    let nameTerms: [[UInt8]]
    let pathTerms: [[UInt8]]
    let pathTails: [[UInt8]]
    let terms: [String]
    var isEmpty: Bool { terms.isEmpty }

    init(_ text: String) {
        var parsed: [String] = []
        var token = ""
        var quoted = false
        for ch in SearchText.fold(text) {
            if ch == "\"" { quoted.toggle(); continue }
            if ch.isWhitespace && !quoted {
                if !token.isEmpty { parsed.append(token); token = "" }
            } else if ch != "/" || !token.hasSuffix("/") { token.append(ch) }
        }
        if !token.isEmpty { parsed.append(token) }
        terms = parsed
        nameTerms = parsed.filter { !$0.contains("/") }.map { Array($0.utf8) }
        pathTerms = parsed.filter { $0.contains("/") }.map { Array($0.utf8) }
        pathTails = pathTerms.map { Array($0[($0.lastIndex(of: 47)! + 1)...]) }
    }

    func matches(_ path: String) -> Bool {
        guard !isEmpty else { return false }
        let folded = SearchText.fold(path)
        let name = (folded as NSString).lastPathComponent
        return terms.allSatisfy { $0.contains("/") ? folded.contains($0) : name.contains($0) }
    }
}

// No String allocation per candidate: the hot loop works directly on packed UTF-8 names.
@inline(__always)
func searchContains(_ bytes: UnsafeBufferPointer<UInt8>, _ term: [UInt8]) -> Bool {
    guard !term.isEmpty else { return true }
    guard bytes.count >= term.count, let base = bytes.baseAddress else { return false }
    return term.withUnsafeBufferPointer { needle in
        var offset = 0
        let last = bytes.count - needle.count
        while offset <= last {
            guard let found = memchr(base.advanced(by: offset), Int32(needle[0]), last - offset + 1) else { return false }
            let position = base.distance(to: found.assumingMemoryBound(to: UInt8.self))
            if memcmp(base.advanced(by: position), needle.baseAddress!, needle.count) == 0 { return true }
            offset = position + 1
        }
        return false
    }
}
