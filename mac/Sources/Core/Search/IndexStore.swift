import Foundation

// Version 1: little-endian integers, length-prefixed UTF-8/Data, FNV-1a payload checksum.
// Limits are validated before allocation; an incompatible or damaged cache is never authoritative.
enum SearchStore {
    static func digest(_ data: Data) -> UInt64 {
        data.withUnsafeBytes { bytes in
            var hash: UInt64 = 14695981039346656037
            for byte in bytes { hash = (hash ^ UInt64(byte)) &* 1099511628211 }
            return hash
        }
    }

    static func volumeUUIDs(_ roots: [String]) -> [String] {
        roots.map { (try? URL(fileURLWithPath: $0).resourceValues(forKeys: [.volumeUUIDStringKey]).volumeUUIDString) ?? "" }
    }

    static func save(_ snapshot: SearchSnapshot, scope: SearchScope, to url: URL) throws {
        var writer = SearchBinaryWriter()
        writer.number(0x4653504154483031); writer.number(1); writer.number(scope.fingerprint)
        writer.number(snapshot.eventID)
        writer.strings(scope.roots); writer.strings(snapshot.volumeUUIDs); writer.strings(snapshot.denied)
        writer.number(UInt64(snapshot.roots.count))
        for root in snapshot.roots { writer.string(root.path); writer.number(root.isDirectory ? 1 : 0) }
        writer.number(UInt64(snapshot.directories.count))
        for directory in snapshot.directories {
            writer.string(directory.path); writer.number(UInt64(directory.count)); writer.blob(directory.packed)
        }
        let checksum = digest(writer.data)
        writer.number(checksum)
        try FileManager.default.createDirectory(at: url.deletingLastPathComponent(), withIntermediateDirectories: true)
        try writer.data.write(to: url, options: .atomic)
    }

    static func load(from url: URL, scope: SearchScope, volumeUUIDs: [String]) -> SearchSnapshot? {
        guard let data = try? Data(contentsOf: url, options: .mappedIfSafe), data.count >= 48 else { return nil }
        var reader = SearchBinaryReader(data: data)
        guard let tail = reader.number(at: data.count - 8), digest(data.dropLast(8)) == tail,
              reader.number() == 0x4653504154483031, reader.number() == 1,
              reader.number() == scope.fingerprint, let event = reader.number(),
              reader.strings() == scope.roots, let volumes = reader.strings(), volumes == volumeUUIDs,
              let denied = reader.strings(), let rootCount = reader.count() else { return nil }
        var roots: [SearchHit] = []
        for _ in 0..<rootCount {
            guard let path = reader.string(), scope.roots.contains(path), let type = reader.number(), type <= 1 else { return nil }
            roots.append(SearchHit(path: path, isDirectory: type == 1))
        }
        guard let count = reader.count() else { return nil }
        var directories: [SearchDirectory] = []
        var seen: Set<String> = []
        for _ in 0..<count {
            guard let path = reader.string(), scope.inScope(path), seen.insert(path).inserted,
                  let entryCount = reader.count(), let packed = reader.blob(),
                  SearchDirectory.valid(packed, count: entryCount) else { return nil }
            directories.append(SearchDirectory(path: path, packed: packed, count: entryCount))
        }
        guard reader.offset == data.count - 8 else { return nil }
        return SearchSnapshot(roots: roots, directories: directories, denied: denied, eventID: event, volumeUUIDs: volumes)
    }
}

private struct SearchBinaryWriter {
    var data = Data()
    mutating func number(_ value: UInt64) {
        var little = value.littleEndian
        withUnsafeBytes(of: &little) { data.append(contentsOf: $0) }
    }
    mutating func blob(_ value: Data) { number(UInt64(value.count)); data.append(value) }
    mutating func string(_ value: String) { blob(Data(value.utf8)) }
    mutating func strings(_ values: [String]) { number(UInt64(values.count)); for value in values { string(value) } }
}

private struct SearchBinaryReader {
    let data: Data
    var offset = 0
    func number(at index: Int) -> UInt64? {
        guard index >= 0, index <= data.count - 8 else { return nil }
        return data.withUnsafeBytes { UInt64(littleEndian: $0.loadUnaligned(fromByteOffset: index, as: UInt64.self)) }
    }
    mutating func number() -> UInt64? {
        guard let result = number(at: offset) else { return nil }
        offset += 8; return result
    }
    mutating func count() -> Int? {
        guard let count = number(), count <= UInt64(max(0, data.count - offset)) else { return nil }
        return Int(count)
    }
    mutating func blob() -> Data? {
        guard let length = count(), length <= data.count - offset else { return nil }
        let result = data.subdata(in: offset..<(offset + length)); offset += length; return result
    }
    mutating func string() -> String? {
        guard let bytes = blob(), let value = String(data: bytes, encoding: .utf8), !value.contains("\0") else { return nil }
        return value
    }
    mutating func strings() -> [String]? {
        guard let count = count() else { return nil }
        var values: [String] = []
        for _ in 0..<count { guard let value = string() else { return nil }; values.append(value) }
        return values
    }
}
