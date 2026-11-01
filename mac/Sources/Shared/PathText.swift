import Foundation

/// Parsing and display of paths typed or pasted by the user.
enum PathText {
    static var home: URL { FileManager.default.homeDirectoryForCurrentUser }

    /// NFC form used for every comparison and match (macOS often stores Korean names as NFD).
    @inline(__always)
    static func nfc(_ text: String) -> String { text.precomposedStringWithCanonicalMapping }

    /// True when the text is meant as a location rather than a name search:
    /// "/…", "~", "~/…", "file://…", or a quoted variant of those.
    static func looksLikePath(_ text: String) -> Bool {
        let value = unquoted(text)
        return value.hasPrefix("/") || value == "~" || value.hasPrefix("~/") || value.hasPrefix("file://")
    }

    /// Resolves typed or pasted text to a file URL. Accepts surrounding quotes, `file://` URLs,
    /// `~`, shell-escaped spaces (`My\ Folder`) and paths relative to `base`.
    /// Returns nil for empty text. Does not check existence.
    static func resolve(_ text: String, relativeTo base: URL? = nil) -> URL? {
        var value = unquoted(text)
        guard !value.isEmpty else { return nil }
        if value.hasPrefix("file://"), let url = URL(string: value), url.isFileURL {
            return url.standardizedFileURL
        }
        if value.contains("\\") {
            var unescaped = ""
            var escaping = false
            for ch in value {
                if escaping { unescaped.append(ch); escaping = false }
                else if ch == "\\" { escaping = true }
                else { unescaped.append(ch) }
            }
            value = unescaped
        }
        if value == "~" { return home }
        if value.hasPrefix("~/") { return home.appendingPathComponent(String(value.dropFirst(2))).standardizedFileURL }
        if value.hasPrefix("/") { return URL(fileURLWithPath: value).standardizedFileURL }
        guard let base else { return nil }
        return base.appendingPathComponent(value).standardizedFileURL
    }

    /// "~/Documents/Report" for paths inside the home folder, the full path otherwise.
    static func abbreviated(_ url: URL) -> String {
        let path = url.standardizedFileURL.path
        let homePath = home.path
        if path == homePath { return "~" }
        if path.hasPrefix(homePath + "/") { return "~" + path.dropFirst(homePath.count) }
        return path
    }

    /// Is `url` equal to or inside `folder`?
    static func isWithin(_ url: URL, _ folder: URL) -> Bool {
        let path = url.standardizedFileURL.path
        let root = folder.standardizedFileURL.path
        return path == root || root == "/" || path.hasPrefix(root.hasSuffix("/") ? root : root + "/")
    }

    private static func unquoted(_ text: String) -> String {
        var value = text.trimmingCharacters(in: .whitespacesAndNewlines)
        for (open, close) in [("\"", "\""), ("'", "'"), ("“", "”"), ("‘", "’")]
        where value.count >= 2 && value.hasPrefix(open) && value.hasSuffix(close) {
            value = String(value.dropFirst().dropLast())
            break
        }
        return value
    }
}

/// Number and date formatting that follows the app language.
enum Format {
    /// Finder-style file size in the app language: "3.1 MB", "22 bytes", "Zero bytes" /
    /// "3.1MB", "22바이트", "0바이트". Decimal units like Finder. ByteCountFormatter can't be
    /// used: it always follows the system locale.
    static func bytes(_ count: Int64) -> String {
        let korean = Loc.isKorean
        if count < 1000 {
            if korean { return "\(max(count, 0))바이트" }
            return count <= 0 ? "Zero bytes" : count == 1 ? "1 byte" : "\(count) bytes"
        }
        let units = ["KB", "MB", "GB", "TB", "PB"]
        var value = Double(count) / 1000
        var unit = 0
        while value >= 999.95 && unit < units.count - 1 {
            value /= 1000
            unit += 1
        }
        let digits = unit == 0 ? 0 : unit == 1 ? 1 : 2
        var number = String(format: "%.\(digits)f", value)
        if digits > 0 {
            while number.hasSuffix("0") { number.removeLast() }
            if number.hasSuffix(".") { number.removeLast() }
        }
        return korean ? number + units[unit] : number + " " + units[unit]
    }

    /// "Today at 14:05" / "오늘 오후 2:05", "Yesterday at …", otherwise a medium date and time.
    static func date(_ date: Date) -> String {
        (Loc.isKorean ? koreanDate : englishDate).string(from: date)
    }

    // Date formatters are expensive to create and thread-safe to use; the list formats every row.
    private static let koreanDate = makeDateFormatter("ko_KR")
    private static let englishDate = makeDateFormatter("en_US")
    private static func makeDateFormatter(_ locale: String) -> DateFormatter {
        let formatter = DateFormatter()
        formatter.locale = Locale(identifier: locale)
        formatter.dateStyle = .medium
        formatter.timeStyle = .short
        formatter.doesRelativeDateFormatting = true
        return formatter
    }

    /// "1 item" / "12 items" / "항목 12개".
    static func items(_ count: Int) -> String {
        if Loc.isKorean { return "항목 \(count.formatted())개" }
        return count == 1 ? "1 item" : "\(count.formatted()) items"
    }
}
