namespace QuadExplorer;

public sealed partial class ExplorerPane
{
    private sealed record ClosedGroup(string Name, TabGroupColor Color, string?[] Paths, int SelectedMember);
    private sealed record ClosedTabEntry(string? Path, ClosedGroup? Group = null, TabGroup? OriginalGroup = null)
    {
        internal int Count => Group?.Paths.Length ?? 1;
    }

    private void RememberClosedTab(ClosedTabEntry entry)
    {
        closedTabs.Push(entry);
        if (closedTabs.Count <= 32) return;
        var recent = closedTabs.Take(32).Reverse().ToArray();
        closedTabs.Clear();
        foreach (var item in recent) closedTabs.Push(item);
    }

    private IEnumerable<string> ClosedPaths()
    {
        foreach (var entry in closedTabs)
        {
            if (entry.Group is { } group)
            {
                foreach (string? path in group.Paths) if (path is not null) yield return path;
            }
            else if (entry.Path is { } path) yield return path;
        }
    }
}
