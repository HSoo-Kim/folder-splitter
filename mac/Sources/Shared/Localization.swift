import Foundation

/// UI language. `.system` follows the first preferred language (Korean if it starts with "ko").
enum AppLanguage: String, Codable, CaseIterable, Sendable {
    case system
    case english = "en"
    case korean = "ko"
}

enum Loc {
    /// Set once at launch (and when the user changes the setting) from `AppSettings.language`.
    nonisolated(unsafe) static var language: AppLanguage = .system

    static var isKorean: Bool {
        switch language {
        case .korean: return true
        case .english: return false
        case .system: return Locale.preferredLanguages.first?.hasPrefix("ko") ?? false
        }
    }

    static var locale: Locale { Locale(identifier: isKorean ? "ko_KR" : "en_US") }
}

/// Every user-visible string goes through `tr(english, korean)` at its call site, so the two
/// languages stay side by side and no slice has to share a key table.
@inline(__always)
func tr(_ english: String, _ korean: String) -> String {
    Loc.isKorean ? korean : english
}
