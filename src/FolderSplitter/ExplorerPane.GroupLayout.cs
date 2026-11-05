namespace QuadExplorer;

public sealed partial class ExplorerPane
{
    // Chips and expanded members share one overflow window; collapsed members occupy no space.
    private void LayoutGroupedTabs()
    {
        int Scaled(int value) => (int)Math.Round(value * tabStrip.DeviceDpi / 96d);
        int chrome = Scaled(70), floor = Scaled(90);
        int rowHeight = Math.Max(TextRenderer.MeasureText("가Ag", tabStrip.Font).Height, Scaled(16)) + Scaled(8);
        newTab.Size = closePane.Size = tabList.Size = new Size(Scaled(30), rowHeight);
        var items = new List<ToolStripItem>();
        var natural = new List<int>();
        var minimum = new List<int>();
        TabGroup? previous = null;
        foreach (var tab in tabs)
        {
            tab.Button.Visible = false;
            if (tab.Group is { } group && group != previous)
            {
                var chip = groupChips[group];
                chip.RefreshLabel(text, GroupMembers(group).Count);
                chip.Visible = false;
                int width = chip.GetPreferredSize(Size.Empty).Width;
                items.Add(chip);
                natural.Add(width);
                minimum.Add(width);
            }
            if (tab.Group?.Collapsed != true)
            {
                items.Add(tab.Button);
                natural.Add(Math.Clamp(TextWidth(tab.Button.Title) + chrome, Scaled(110), Scaled(240)));
                minimum.Add(floor);
            }
            previous = tab.Group;
        }
        int count = items.Count;
        int available = Math.Max(0, tabStrip.ClientSize.Width - tabStrip.Padding.Horizontal
            - newTab.Width - newTab.Margin.Horizontal - closePane.Width - closePane.Margin.Horizontal - Scaled(6));
        int margins = items.Sum(item => item.Margin.Horizontal);
        bool overflow = minimum.Sum() + margins > available;
        int room = Math.Max(1, available - (overflow ? tabList.Width + tabList.Margin.Horizontal : 0));
        int first = 0, end = count;
        var widths = natural.ToArray();
        if (overflow && count > 0)
        {
            widths = minimum.ToArray();
            first = Math.Clamp(firstVisibleTab, 0, count - 1);
            int selected = ActiveTab is { } active ? items.IndexOf(active.Button) : -1;
            if (selected >= 0 && selected < first) first = selected;
            if (first > 0 && items[first - 1] is TabGroupChip && WindowWidth(first - 1, first + 1) <= room) first--;
            end = FitEnd(first);
            if (selected >= end)
            {
                first = selected;
                end = FitEnd(first);
                while (first > 0 && WindowWidth(first - 1, end) <= room) first--;
            }
            // A newly selected group's first member should retain its adjacent chip when it fits.
            if (first > 0 && items[first - 1] is TabGroupChip && WindowWidth(first - 1, end) <= room) first--;
            while (end < count && WindowWidth(first, end + 1) <= room) end++;
            while (first > 0 && WindowWidth(first - 1, end) <= room) first--;
            int spare = room - WindowWidth(first, end);
            if (spare > 0)
            {
                var expandable = Enumerable.Range(first, end - first).Where(index => items[index] is FolderTabButton).ToArray();
                if (expandable.Length > 0)
                    foreach (int index in expandable) widths[index] = Math.Min(natural[index], widths[index] + spare / expandable.Length);
            }
            if (end == first + 1) widths[first] = Math.Min(widths[first], Math.Max(1, room - items[first].Margin.Horizontal));
        }
        else if (natural.Sum() + margins > available && count > 0)
        {
            int low = floor, high = natural.Max();
            while (low < high)
            {
                int middle = (low + high + 1) / 2;
                int total = Enumerable.Range(0, count).Sum(index => items[index] is TabGroupChip ? natural[index] : Math.Min(natural[index], middle));
                if (total + margins <= available) low = middle;
                else high = middle - 1;
            }
            for (int index = 0; index < count; index++)
                if (items[index] is FolderTabButton) widths[index] = Math.Min(natural[index], low);
        }
        firstVisibleTab = first;
        for (int index = 0; index < count; index++)
        {
            var item = items[index];
            item.Size = new Size(widths[index], rowHeight);
            item.Visible = index >= first && index < end;
            if (item is not FolderTabButton button) { item.Invalidate(); continue; }
            button.Padding = new Padding(0, 0, button.CloseWidth, 0);
            button.Text = FitTitle(button.Title, widths[index] - chrome);
        }
        foreach (var tab in tabs)
            tab.Button.ToolTipText = (tab.Host?.LocationPath ?? text.T("New tab")) + Environment.NewLine
                + text.T(tab == ActiveTab ? "Close current tab (Ctrl+W)" : "Select this tab, then close with Ctrl+W");
        tabList.Visible = overflow || tabs.Any(tab => tab.Group?.Collapsed == true);
        foreach (var old in tabList.DropDownItems.Cast<ToolStripItem>().ToArray()) old.Dispose();
        previous = null;
        foreach (var tab in tabs)
        {
            if (tab.Group is { } group && previous != group)
            {
                tabList.DropDownItems.Add(new ToolStripMenuItem(GroupLabel(group))
                {
                    Enabled = false, ForeColor = Theme.GroupColor(group.Color), AccessibleName = GroupLabel(group)
                });
            }
            var member = tab;
            var item = new ToolStripMenuItem((tab.Group is null ? "" : "    ") + tab.Button.Title)
            {
                Checked = tab == ActiveTab, ToolTipText = tab.Host?.LocationPath ?? ""
            };
            item.Click += (_, _) => SelectTab(tabs.IndexOf(member));
            tabList.DropDownItems.Add(item);
            previous = tab.Group;
        }

        int WindowWidth(int start, int stop) => Enumerable.Range(start, stop - start)
            .Sum(index => widths[index] + items[index].Margin.Horizontal);
        int FitEnd(int start)
        {
            int stop = start;
            while (stop < count && (stop == start || WindowWidth(start, stop + 1) <= room)) stop++;
            return stop;
        }
    }
}
