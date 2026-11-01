import AppKit

/// Finder's drop rules for files dropped on a tab, a breadcrumb or anything else that stands
/// for a folder: same volume moves, another volume copies, ⌥ forces copy, ⌘ forces move.
@MainActor
enum FileDrop {
    static func mode(for urls: [URL], into folder: URL, operation: NSDragOperation) -> TransferMode? {
        // AppKit narrows the source mask to .copy while ⌥ is held and to .generic while ⌘ is held.
        if operation == .copy { return .copy }
        if operation == .move || operation == .generic { return .move }
        guard operation.contains(.move) || operation.contains(.generic) else {
            return operation.contains(.copy) ? .copy : nil
        }
        let target = volumeID(of: folder)
        let sameVolume = target != nil && urls.allSatisfy { volumeID(of: $0) == target }
        if sameVolume { return .move }
        return operation.contains(.copy) ? .copy : .move
    }

    /// Starts the transfer. Returns false when nothing would happen (dropping onto itself or
    /// into the folder it already lives in).
    @discardableResult
    static func perform(_ urls: [URL], into folder: URL, operation: NSDragOperation) -> Bool {
        let destination = folder.standardizedFileURL
        let items = urls.filter { url in
            let source = url.standardizedFileURL
            return source != destination
                && !PathText.isWithin(destination, source)
                && source.deletingLastPathComponent() != destination
        }
        guard !items.isEmpty, let mode = mode(for: items, into: destination, operation: operation) else { return false }
        let operations = AppServices.shared.fileOperations
        Task { _ = await operations.transfer(items, to: destination, mode: mode) }
        return true
    }

    private static func volumeID(of url: URL) -> String? {
        guard let id = try? url.resourceValues(forKeys: [.volumeIdentifierKey]).volumeIdentifier else { return nil }
        return String(describing: id)
    }
}
