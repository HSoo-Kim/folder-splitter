namespace QuadExplorer;

public sealed partial class ExplorerPane
{
    private readonly System.Windows.Forms.Timer groupJoinTimer = new() { Interval = 500 };
    private TabGroup? dwellGroup;
    private bool dwellCollapsed;
    private bool dwellArmed;
    private Point dwellPoint;
    private FolderTab? dwellTab;
    private IReadOnlyList<FolderTab>? dwellTabs;
    private IReadOnlyList<string>? dwellPaths;
    private int dwellMinimumIndex;
    internal event Action? StripDropPlanChanged;

    private void InitializeGroupDropDwell()
    {
        groupJoinTimer.Tick += (_, _) =>
        {
            groupJoinTimer.Stop();
            if (dwellGroup is null || IsDisposed) return;
            dwellArmed = true;
            var plan = FindStripDropPlan(dwellPoint, dwellTab, null, dwellTabs, dwellPaths, dwellMinimumIndex);
            if (dwellPaths is not null) externalDropPlan = plan;
            tabStrip.Invalidate();
            StripDropPlanChanged?.Invoke();
        };
    }

    private TabGroup? UpdateStripDropDwell(TabGroup? candidate, bool collapsed, Point screen,
        FolderTab? tab, IReadOnlyList<FolderTab>? moving, IReadOnlyList<string>? paths, int minimumIndex, int insertion)
    {
        if (candidate is null)
        {
            ClearStripDropDwell();
            return null;
        }
        string reason = "";
        bool fits;
        if (paths is not null)
        {
            reason = tabs.Count + paths.Count > 32 ? "Maximum 32 tabs per pane. Close a tab first."
                : CountOpenGroupTabs(candidate) + paths.Count > TabGroup.MaxTabs ? "A group holds up to 12 tabs." : "";
            fits = reason.Length == 0;
        }
        else fits = tab is not null && (moving is { Count: > 1 }
            ? CanInsertStripTabs(moving, insertion, candidate, out _, out reason)
            : CanInsertStripTab(tab, insertion, candidate, out _, out reason));
        if (!fits)
        {
            if (reason.Length != 0) SetLocalizedStatus(reason);
            ClearStripDropDwell();
            return null;
        }
        if (dwellGroup != candidate || dwellCollapsed != collapsed)
        {
            ClearStripDropDwell();
            dwellGroup = candidate;
            dwellCollapsed = collapsed;
            // Only an unchanged ambiguous target earns a join; ordinary strip moves stay immediate.
            groupJoinTimer.Start();
        }
        dwellPoint = screen;
        dwellTab = tab;
        dwellTabs = moving;
        dwellPaths = paths;
        dwellMinimumIndex = minimumIndex;
        if (groupChips.TryGetValue(candidate, out var chip)) chip.DropJoinArmed = dwellArmed && collapsed;
        return dwellArmed ? candidate : null;
    }

    internal void ClearStripDropDwell()
    {
        groupJoinTimer.Stop();
        if (dwellGroup is { } group && groupChips.TryGetValue(group, out var chip)) chip.DropJoinArmed = false;
        dwellGroup = null;
        dwellArmed = false;
        dwellTab = null;
        dwellTabs = null;
        dwellPaths = null;
    }
}
