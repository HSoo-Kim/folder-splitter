import Foundation

enum LayoutShape: Int, CaseIterable, Codable {
    case one = 1, two = 2, threeSideBySide = 3, four = 4, threeColumns = 31, threeStacked = 32, twoStacked = 21
    var paneCount: Int {
        switch self {
        case .one: return 1
        case .two, .twoStacked: return 2
        case .threeSideBySide, .threeColumns, .threeStacked: return 3
        case .four: return 4
        }
    }
}

indirect enum PaneLayoutTree: Codable, Equatable {
    case pane(UUID)
    case split(vertical: Bool, ratio: Double, first: PaneLayoutTree, second: PaneLayoutTree)

    private enum CodingKeys: String, CodingKey { case pane, split }
    private enum LeafKeys: String, CodingKey { case _0 }
    private enum SplitKeys: String, CodingKey { case vertical, ratio, first, second }
    init(from decoder: Decoder) throws {
        let c = try decoder.container(keyedBy: CodingKeys.self)
        if c.contains(.pane) {
            let leaf = try c.nestedContainer(keyedBy: LeafKeys.self, forKey: .pane)
            self = .pane((try? leaf.decode(UUID.self, forKey: ._0)) ?? UUID())
        } else {
            let branch = try c.nestedContainer(keyedBy: SplitKeys.self, forKey: .split)
            self = .split(vertical: (try? branch.decode(Bool.self, forKey: .vertical)) ?? true,
                          ratio: (try? branch.decode(Double.self, forKey: .ratio)) ?? 0.5,
                          first: (try? branch.decode(PaneLayoutTree.self, forKey: .first)) ?? .pane(UUID()),
                          second: (try? branch.decode(PaneLayoutTree.self, forKey: .second)) ?? .pane(UUID()))
        }
    }
    func encode(to encoder: Encoder) throws {
        var c = encoder.container(keyedBy: CodingKeys.self)
        switch self {
        case .pane(let id):
            var leaf = c.nestedContainer(keyedBy: LeafKeys.self, forKey: .pane)
            try leaf.encode(id, forKey: ._0)
        case .split(let vertical, let ratio, let first, let second):
            var branch = c.nestedContainer(keyedBy: SplitKeys.self, forKey: .split)
            try branch.encode(vertical, forKey: .vertical); try branch.encode(ratio, forKey: .ratio)
            try branch.encode(first, forKey: .first); try branch.encode(second, forKey: .second)
        }
    }

    var panes: [UUID] {
        switch self {
        case .pane(let id): return [id]
        case .split(_, _, let first, let second): return first.panes + second.panes
        }
    }

    static func preset(_ shape: LayoutShape, panes: [UUID]) -> PaneLayoutTree {
        precondition(panes.count >= shape.paneCount)
        func leaf(_ i: Int) -> PaneLayoutTree { .pane(panes[i]) }
        func split(_ vertical: Bool, _ first: PaneLayoutTree, _ second: PaneLayoutTree, _ ratio: Double = 0.5) -> PaneLayoutTree {
            .split(vertical: vertical, ratio: ratio, first: first, second: second)
        }
        switch shape {
        case .one: return leaf(0)
        case .two: return split(true, leaf(0), leaf(1))
        case .twoStacked: return split(false, leaf(0), leaf(1))
        case .threeSideBySide: return split(true, leaf(0), split(false, leaf(1), leaf(2)))
        case .threeStacked: return split(false, leaf(0), split(true, leaf(1), leaf(2)))
        case .threeColumns: return split(true, leaf(0), split(true, leaf(1), leaf(2)), 1.0 / 3)
        case .four: return split(true, split(false, leaf(0), leaf(1)), split(false, leaf(2), leaf(3)))
        }
    }

    func splitting(_ target: UUID, with newPane: UUID, edge: SplitEdge) -> PaneLayoutTree {
        guard panes.count < 4, panes.contains(target), !panes.contains(newPane) else { return self }
        func replace(_ node: PaneLayoutTree) -> PaneLayoutTree {
            switch node {
            case .pane(let id):
                guard id == target else { return node }
                let before = edge == .left || edge == .top
                return .split(vertical: edge == .left || edge == .right, ratio: 0.5,
                              first: before ? .pane(newPane) : node, second: before ? node : .pane(newPane))
            case .split(let vertical, let ratio, let first, let second):
                return .split(vertical: vertical, ratio: ratio, first: replace(first), second: replace(second))
            }
        }
        return replace(self)
    }

    func removing(_ pane: UUID) -> PaneLayoutTree? {
        switch self {
        case .pane(let id): return id == pane ? nil : self
        case .split(let vertical, let ratio, let first, let second):
            let a = first.removing(pane), b = second.removing(pane)
            if let a, let b { return .split(vertical: vertical, ratio: ratio, first: a, second: b) }
            return a ?? b
        }
    }

    func settingRatio(_ ratio: Double, at path: [Int]) -> PaneLayoutTree {
        func replace(_ node: PaneLayoutTree, _ offset: Int) -> PaneLayoutTree {
            guard case .split(let vertical, let oldRatio, let first, let second) = node else { return node }
            if offset == path.count {
                return .split(vertical: vertical, ratio: Self.clamp(ratio), first: first, second: second)
            }
            switch path[offset] {
            case 0: return .split(vertical: vertical, ratio: oldRatio, first: replace(first, offset + 1), second: second)
            case 1: return .split(vertical: vertical, ratio: oldRatio, first: first, second: replace(second, offset + 1))
            default: return node
            }
        }
        return replace(self, 0)
    }

    func equalized() -> PaneLayoutTree {
        func extent(_ node: PaneLayoutTree) -> (width: Double, height: Double) {
            guard case .split(let vertical, _, let first, let second) = node else { return (1, 1) }
            let a = extent(first), b = extent(second)
            return vertical ? (a.width + b.width, max(a.height, b.height)) : (max(a.width, b.width), a.height + b.height)
        }
        switch self {
        case .pane: return self
        case .split(let vertical, _, let first, let second):
            let a = extent(first), b = extent(second)
            let ratio = vertical ? a.width / (a.width + b.width) : a.height / (a.height + b.height)
            return .split(vertical: vertical, ratio: ratio, first: first.equalized(), second: second.equalized())
        }
    }

    func normalized(keeping valid: Set<UUID>) -> PaneLayoutTree? {
        var seen = Set<UUID>()
        func visit(_ node: PaneLayoutTree) -> PaneLayoutTree? {
            switch node {
            case .pane(let id): return valid.contains(id) && seen.insert(id).inserted ? node : nil
            case .split(let vertical, let ratio, let first, let second):
                let a = visit(first), b = visit(second)
                if let a, let b { return .split(vertical: vertical, ratio: Self.clamp(ratio), first: a, second: b) }
                return a ?? b
            }
        }
        return visit(self)
    }

    var shape: LayoutShape? {
        func structure(_ a: PaneLayoutTree, _ b: PaneLayoutTree) -> Bool {
            switch (a, b) {
            case (.pane, .pane): return true
            case (.split(let av, _, let af, let az), .split(let bv, _, let bf, let bz)):
                return av == bv && structure(af, bf) && structure(az, bz)
            default: return false
            }
        }
        func isColumnRow(_ node: PaneLayoutTree) -> Bool {
            switch node {
            case .pane: return true
            case .split(let vertical, _, let first, let second):
                return vertical && isColumnRow(first) && isColumnRow(second)
            }
        }
        let ids = panes
        if ids.count == 3 && isColumnRow(self) { return .threeColumns }
        return LayoutShape.allCases.first { $0.paneCount == ids.count && structure(self, Self.preset($0, panes: ids)) }
    }

    private static func clamp(_ ratio: Double) -> Double { ratio.isFinite ? min(max(ratio, 0.15), 0.85) : 0.5 }
}
