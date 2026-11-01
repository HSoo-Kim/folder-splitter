import Foundation
import Testing

private func modelTemp() throws -> URL {
    let url = FileManager.default.temporaryDirectory.appendingPathComponent("models-\(UUID().uuidString)")
    try FileManager.default.createDirectory(at: url, withIntermediateDirectories: true)
    return url
}

@Test func presetShapesAndReadingOrder() throws {
    let ids = (0..<4).map { _ in UUID() }
    for shape in LayoutShape.allCases {
        let tree = PaneLayoutTree.preset(shape, panes: ids)
        #expect(tree.panes == Array(ids.prefix(shape.paneCount)))
        #expect(tree.shape == shape)
        #expect(tree.settingRatio(0.8, at: []).shape == shape)
        #expect(try JSONDecoder().decode(PaneLayoutTree.self, from: JSONEncoder().encode(tree)) == tree)
    }
    let leftAssociated = PaneLayoutTree.split(vertical: true, ratio: 0.8,
        first: .split(vertical: true, ratio: 0.2, first: .pane(ids[0]), second: .pane(ids[1])), second: .pane(ids[2]))
    #expect(leftAssociated.shape == .threeColumns)
    if case .split(_, let outer, let first, _) = leftAssociated.equalized(), case .split(_, let inner, _, _) = first {
        #expect(abs(outer - 2.0 / 3) < 0.0001)
        #expect(inner == 0.5)
    } else { Issue.record("Expected nested split") }
}

@Test func layoutSplitRemoveAndNormalize() {
    let a = UUID(), b = UUID(), c = UUID(), unknown = UUID()
    let one = PaneLayoutTree.pane(a)
    #expect(one.removing(a) == nil)
    #expect(one.splitting(a, with: b, edge: .left).panes == [b, a])
    #expect(one.splitting(a, with: b, edge: .top).shape == .twoStacked)
    let tree = one.splitting(a, with: b, edge: .right).splitting(b, with: c, edge: .bottom)
    #expect(tree.shape == .threeSideBySide)
    #expect(tree.removing(b)?.panes == [a, c])
    #expect(tree.settingRatio(-1, at: []) == tree.settingRatio(0.15, at: []))
    #expect(tree.settingRatio(2, at: [1]) == tree.settingRatio(0.85, at: [1]))
    #expect(tree.settingRatio(.nan, at: []) == tree.settingRatio(0.5, at: []))
    let dirty = PaneLayoutTree.split(vertical: true, ratio: 9, first: .pane(a), second:
        .split(vertical: false, ratio: .nan, first: .pane(a), second:
            .split(vertical: true, ratio: 0.5, first: .pane(unknown), second: .pane(b))))
    #expect(dirty.normalized(keeping: [a, b]) == .split(vertical: true, ratio: 0.85, first: .pane(a), second: .pane(b)))
    #expect(dirty.normalized(keeping: []) == nil)
    let columns = PaneLayoutTree.preset(.threeColumns, panes: [a, b, c]).settingRatio(0.7, at: []).equalized()
    if case .split(_, let ratio, _, _) = columns { #expect(abs(ratio - 1.0 / 3) < 0.0001) }
}

@Test func sessionRoundTripAndBackup() throws {
    let directory = try modelTemp(); defer { try? FileManager.default.removeItem(at: directory) }
    let url = directory.appendingPathComponent("session.json")
    let pane = PaneSnapshot(tabs: [TabSnapshot(path: "/Users/example", back: ["/tmp"], forward: ["/Volumes"])])
    var original = SessionState(windows: [WindowSnapshot(frame: [10, 20, 1000, 800], layout: .pane(pane.id), panes: [pane], activePane: pane.id)], favorites: ["/tmp"])
    try SessionStore.save(original, to: url)
    #expect(SessionStore.load(from: url).state == original)
    original.favorites = ["/Volumes"]
    try SessionStore.save(original, to: url)
    try Data("not JSON".utf8).write(to: url)
    let restored = SessionStore.load(from: url)
    #expect(restored.warning != nil)
    #expect(restored.state.favorites == ["/tmp"])
    // Saving after corruption does not overwrite the known-good backup with corrupt bytes.
    try SessionStore.save(original, to: url)
    try Data("invalid again".utf8).write(to: url)
    #expect(SessionStore.load(from: url).state.favorites == ["/tmp"])
    try FileManager.default.removeItem(at: url.appendingPathExtension("bak"))
    let defaults = SessionStore.load(from: url)
    #expect(defaults.warning != nil)
    #expect(defaults.state.windows.count == 1)
    #expect(defaults.state.windows[0].panes[0].tabs[0].path == PathText.home.path)
}

@Test func missingKeysAndSessionLimitsPreserveHiddenPanes() throws {
    let directory = try modelTemp(); defer { try? FileManager.default.removeItem(at: directory) }
    let url = directory.appendingPathComponent("session.json")
    try Data("{}".utf8).write(to: url)
    let missing = SessionStore.load(from: url)
    #expect(missing.warning == nil)
    #expect(missing.state.windows[0].layout.panes.count == 1)
    let decoded = try JSONDecoder().decode(PaneSnapshot.self, from: Data("{}".utf8))
    #expect(decoded.tabs.isEmpty)
    let history = (0..<80).map { "/folder/\($0)" }
    let repeatedTab = TabSnapshot(path: "/tmp", back: history, forward: history)
    let panes = (0..<6).map { _ in PaneSnapshot(tabs: Array(repeating: repeatedTab, count: 40), selectedTab: 999) }
    let missingID = UUID()
    let dirtyLayout = PaneLayoutTree.split(vertical: true, ratio: 99, first: .pane(panes[0].id), second: .pane(missingID))
    let window = WindowSnapshot(layout: dirtyLayout, panes: panes, activePane: panes[1].id)
    let workspaces = (0..<25).map { Workspace(name: "Workspace \($0)", layout: dirtyLayout, panes: panes) }
    let state = SessionState(windows: [window], favorites: (0..<120).map { "/favorite/\($0)" }, workspaces: workspaces)
    try JSONEncoder().encode(state).write(to: url)
    let result = SessionStore.load(from: url).state
    let w = result.windows[0]
    #expect(w.panes.count == 4)
    #expect(w.layout == .pane(panes[0].id))
    #expect(w.activePane == panes[0].id)
    #expect(w.panes[1].id == panes[1].id) // Hidden pane retains its tabs.
    #expect(w.panes.allSatisfy { $0.tabs.count == 32 && $0.selectedTab == 31 })
    #expect(w.panes[0].tabs[0].back == Array(history.suffix(50)))
    #expect(w.panes[0].tabs[0].forward.count == 50)
    #expect(Set(w.panes.flatMap(\.tabs).map(\.id)).count == 128)
    #expect(result.favorites.count == 100)
    #expect(result.workspaces.count == 20)
    let absent = WindowSnapshot(layout: .pane(missingID), panes: Array(panes.prefix(2)), activePane: missingID)
    let normalized = SessionValidation.state(SessionState(windows: [absent])).windows[0]
    #expect(normalized.layout == .pane(panes[0].id))
    #expect(normalized.panes.count == 2)
}

@Test func closeHistoryHasOneRestorePath() {
    let a = UUID(), b = UUID(), tab = TabSnapshot(path: "/tmp")
    let aItem = ClosedItem.tab(paneID: a, index: 1, tab: tab)
    let bItem = ClosedItem.tab(paneID: b, index: 0, tab: tab)
    let closedPane = ClosedItem.pane(pane: PaneSnapshot(id: b), layoutBefore: .preset(.two, panes: [a, b]), layoutAfter: .pane(a))
    var history = CloseHistory()
    history.push(aItem); history.push(bItem); history.push(closedPane)
    #expect(history.hasTabs(in: a))
    #expect(history.popLatest(tabIn: a) == aItem)
    #expect(!history.hasTabs(in: a))
    #expect(history.popLatest() == closedPane)
    #expect(history.popLatest() == bItem)
    #expect(history.popLatest(tabIn: b) == nil)
    #expect(history.isEmpty)
    history.push(closedPane); history.push(aItem); history.discardPaneRecords()
    #expect(history.popLatest() == aItem)
    #expect(history.isEmpty)

    for _ in 0..<70 { history.push(aItem) }
    var count = 0
    while history.popLatest() != nil { count += 1 }
    #expect(count == 50)
}
@Test func sessionRepairsDuplicatePaneIDsAndSelections() {
    let first = PaneSnapshot(tabs: [TabSnapshot(path: "/tmp")], selectedTab: -12)
    let duplicate = PaneSnapshot(id: first.id, tabs: [TabSnapshot(path: "/other")])
    let empty = PaneSnapshot()
    let tree = PaneLayoutTree.split(vertical: true, ratio: 0.5, first: .pane(first.id), second: .pane(first.id))
    let result = SessionValidation.state(SessionState(windows: [
        WindowSnapshot(frame: [0, 0, .nan, 800], layout: tree, panes: [first, duplicate, empty])
    ])).windows[0]
    #expect(result.panes.count == 2)
    #expect(result.layout == .pane(first.id))
    #expect(result.panes[0].selectedTab == 0)
    #expect(result.panes[1].selectedTab == -1)
    #expect(result.frame == nil)
}

@Test @MainActor func visitsAgeAndRetainUsefulFolders() {
    let log = FolderVisitLog(snapshot: .init(entries: [
        .init(path: "/old", count: 600), .init(path: "/recent", count: 400), .init(path: "/once", count: 1)
    ]), now: { Date(timeIntervalSince1970: 123) })
    log.recordVisit(URL(fileURLWithPath: "/new"))
    #expect(log.snapshot.entries.first?.count == 1)
    #expect(log.snapshot.entries.first?.lastVisited == Date(timeIntervalSince1970: 123))
    #expect(log.frequentFolders == ["/old", "/recent"])
    #expect(!log.snapshot.entries.contains { $0.path == "/once" })
    let tied = FolderVisitLog(snapshot: .init(entries: [.init(path: "/a", count: 3), .init(path: "/b", count: 3)]))
    #expect(tied.frequentFolders == ["/a", "/b"])
    tied.recordVisit(URL(fileURLWithPath: "/b"))
    #expect(tied.frequentFolders == ["/b", "/a"])
    for i in 0..<120 { tied.recordVisit(URL(fileURLWithPath: "/new/\(i)")) }
    #expect(tied.snapshot.entries.count == 100)
    #expect(tied.frequentFolders.contains("/b"))
    #expect(tied.recentFolders.count == 20)
}

@Test @MainActor func storesRespectLimitsAndLRU() throws {
    let favorites = FavoritesStore(paths: ["/tmp", "/TMP", "relative"])
    #expect(favorites.paths == ["/tmp"])
    favorites.add(URL(fileURLWithPath: "/Volumes"), at: 0)
    favorites.move(from: 1, to: 0)
    #expect(favorites.paths == ["/tmp", "/Volumes"])
    let memory = FolderViewMemoryStore(entries: [:])
    for i in 0..<500 { memory.setViewSettings(.default, for: URL(fileURLWithPath: "/folder/\(i)")) }
    _ = memory.viewSettings(for: URL(fileURLWithPath: "/folder/0"))
    memory.setViewSettings(.default, for: URL(fileURLWithPath: "/folder/500"))
    #expect(memory.viewSettings(for: URL(fileURLWithPath: "/folder/0")) != nil)
    #expect(memory.viewSettings(for: URL(fileURLWithPath: "/folder/1")) == nil)
    #expect(memory.entries.count == 500)
    let store = WorkspaceStore(workspaces: [])
    let pane = PaneSnapshot(tabs: [TabSnapshot(path: "/tmp")])
    store.save(Workspace(name: "Work", layout: .pane(pane.id), panes: [pane]))
    store.save(Workspace(name: "WORK", layout: .pane(pane.id), panes: [pane]))
    #expect(store.workspaces.count == 1)
    try store.rename("work", to: "Research")
    #expect(store.workspaces[0].name == "Research")
    store.delete(named: "research")
    #expect(store.workspaces.isEmpty)
}
