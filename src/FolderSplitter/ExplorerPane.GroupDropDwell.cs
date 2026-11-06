namespace QuadExplorer;

public sealed partial class ExplorerPane
{
    // How long a drag must rest at a group's end or on a collapsed chip before the drop joins the group:
    // long enough to pass over without joining, short enough not to feel like waiting.
    internal const int GroupJoinDwellMilliseconds = 300;
    private readonly System.Windows.Forms.Timer groupJoinTimer = new() { Interval = GroupJoinDwellMilliseconds };
    // Elapsed dwell for the progress fill. Tests replace it to step through the wait without sleeping.
    internal static Func<long> DwellClock = static () => Environment.TickCount64;
    private readonly System.Windows.Forms.Timer groupJoinProgressTimer = new() { Interval = 25 };
    private TabGroup? dwellGroup;
    private bool dwellCollapsed;
    private bool dwellArmed;
    private long dwellStartedAt;
    private Point dwellPoint;
    private FolderTab? dwellTab;
    private IReadOnlyList<FolderTab>? dwellTabs;
    private IReadOnlyList<string>? dwellPaths;
    private int dwellMinimumIndex;
    private TabGroup? joinFeedbackGroup;
    private Rectangle[] dropGroupHighlights = [];
    private bool refreshingDropFeedback;
    internal event Action? StripDropPlanChanged;

    private void InitializeGroupDropDwell()
    {
        groupJoinTimer.Tick += (_, _) =>
        {
            groupJoinTimer.Stop();
            groupJoinProgressTimer.Stop();
            if (dwellGroup is null || IsDisposed) return;
            dwellArmed = true;
            RefreshGroupDropFeedback();
        };
        groupJoinProgressTimer.Tick += (_, _) =>
        {
            if (dwellGroup is null || dwellArmed || IsDisposed)
            {
                groupJoinProgressTimer.Stop();
                return;
            }
            RefreshGroupDropFeedback();
        };
    }

    private void RefreshGroupDropFeedback()
    {
        if (dwellGroup is null) return;
        var plan = FindStripDropPlan(dwellPoint, dwellTab, null, dwellTabs, dwellPaths, dwellMinimumIndex);
        if (dwellPaths is not null)
        {
            externalDropPlan = plan;
            externalDropAllowed = plan is { Allowed: true };
            RefreshExternalDropDescription();
        }
        RepaintDropFeedback();
    }

    private void RepaintDropFeedback()
    {
        if (IsDisposed || refreshingDropFeedback) return;
        refreshingDropFeedback = true;
        try
        {
            // OLE can remain in its modal loop with no further DragOver. Commit this frame now.
            tabStrip.Invalidate();
            tabStrip.Update();
            StripDropPlanChanged?.Invoke();
        }
        finally { refreshingDropFeedback = false; }
    }

    private TabGroup? UpdateStripDropDwell(TabGroup? candidate, bool collapsed, Point screen,
        FolderTab? tab, IReadOnlyList<FolderTab>? moving, IReadOnlyList<string>? paths, int minimumIndex, int insertion)
    {
        if (candidate is null)
        {
            if (dwellGroup is not null) ClearStripDropDwell();
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
            dwellStartedAt = DwellClock();
            groupJoinTimer.Start();
            groupJoinProgressTimer.Start();
        }
        // The hit-tested spot, not raw pointer coordinates, owns the dwell. Small scaled
        // pointer jitter anywhere within that same half-tab/chip/gap never restarts it.
        dwellPoint = screen;
        dwellTab = tab;
        dwellTabs = moving;
        dwellPaths = paths;
        dwellMinimumIndex = minimumIndex;
        return dwellArmed ? candidate : null;
    }

    private TabDropPlan AddStripDropFeedback(TabDropPlan plan, int incomingCount)
    {
        var group = plan.Allowed ? plan.JoinGroup : null;
        SetGroupJoinFeedback(group, incomingCount);
        plan = plan with
        {
            DwellGroup = dwellGroup,
            DwellProgress = dwellGroup is null ? 0 : dwellArmed ? 1
                : Math.Clamp((DwellClock() - dwellStartedAt) / (float)GroupJoinDwellMilliseconds, 0, 1),
            IncomingCount = incomingCount
        };
        if (group is null) return plan;
        int width = Math.Max(48, (int)Math.Round(120 * DeviceDpi / 96d));
        int count = 0;
        foreach (var member in tabs)
            if (member.Group == group && member.Button.Available)
            {
                width = member.Button.Width;
                count++;
            }
        if (dropGroupHighlights.Length != count + 1) dropGroupHighlights = new Rectangle[count + 1];
        var highlights = dropGroupHighlights;
        if (groupChips.TryGetValue(group, out var chip)) highlights[0] = tabStrip.RectangleToScreen(chip.Bounds);
        int index = 1;
        foreach (var member in tabs)
            if (member.Group == group && member.Button.Available)
                highlights[index++] = tabStrip.RectangleToScreen(member.Button.Bounds);
        Rectangle strip = tabStrip.RectangleToScreen(tabStrip.ClientRectangle);
        int left = Math.Clamp(plan.Preview.Left + plan.Preview.Width, strip.Left, Math.Max(strip.Left, strip.Right - width));
        var slot = Rectangle.Intersect(strip, new Rectangle(left, plan.Preview.Top, width, plan.Preview.Height));
        return plan with { GhostSlot = slot, GroupHighlight = highlights };
    }

    private void SetGroupJoinFeedback(TabGroup? group, int incomingCount)
    {
        joinFeedbackGroup = group;
        foreach (var pair in groupChips)
        {
            pair.Value.DropJoinCount = pair.Key == group ? incomingCount : 0;
            pair.Value.DropJoinArmed = pair.Key == group;
        }
        foreach (var member in tabs) member.Button.DropJoinArmed = group is not null && member.Group == group;
    }

    internal void ClearStripDropDwell()
    {
        bool changed = dwellGroup is not null || joinFeedbackGroup is not null;
        var oldGroup = dwellGroup ?? joinFeedbackGroup;
        groupJoinTimer.Stop();
        groupJoinProgressTimer.Stop();
        dwellGroup = null;
        dwellArmed = false;
        dwellTab = null;
        dwellTabs = null;
        dwellPaths = null;
        SetGroupJoinFeedback(null, 0);
        if (!changed) return;
        if (externalDropPlan is { } oldPlan)
            externalDropPlan = oldPlan with
            {
                JoinGroup = oldPlan.JoinGroup == oldGroup ? null : oldPlan.JoinGroup,
                DwellGroup = null, DwellProgress = 0, GhostSlot = Rectangle.Empty, GroupHighlight = []
            };
        RefreshExternalDropDescription();
        RepaintDropFeedback();
    }
}
