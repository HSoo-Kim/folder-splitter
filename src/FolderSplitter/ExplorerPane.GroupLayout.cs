namespace QuadExplorer;

public sealed partial class ExplorerPane
{
    // Chips and expanded members share one overflow window; collapsed members occupy no space.
    private void LayoutGroupedTabs()
    {
        int Scaled(int value) => (int)Math.Round(value * tabStrip.DeviceDpi / 96d);
        int chrome = Scaled(70), pinChrome = Scaled(54), floor = Scaled(90);
        int rowHeight = Math.Max(TextRenderer.MeasureText("가Ag", tabStrip.Font).Height, Scaled(16)) + Scaled(8);
        newTab.Size = closePane.Size = tabList.Size = new Size(Scaled(30), rowHeight);
        int pinCount = PinnedCount;
        int pinnedSpace = 0;
        int[] pinNatural = new int[pinCount];
        int pinMargins = 0;
        for (int index = 0; index < pinCount; index++)
        {
            pinNatural[index] = Math.Clamp(TextWidth(tabs[index].Button.Title) + pinChrome, Scaled(90), Scaled(180));
            pinMargins += tabs[index].Button.Margin.Horizontal;
        }
        var items = new List<ToolStripItem>();
        var natural = new List<int>();
        var minimum = new List<int>();
        bool anyCollapsed = false;
        TabGroup? previous = null;
        for (int tabIndex = 0; tabIndex < tabs.Count; tabIndex++)
        {
            var tab = tabs[tabIndex];
            if (tab.Pinned) continue;
            tab.Button.DisplayStyle = ToolStripItemDisplayStyle.ImageAndText;
            tab.Button.ImageAlign = ContentAlignment.MiddleLeft;
            tab.Button.Visible = false;
            if (tab.Group is { } group && group != previous)
            {
                int groupEnd = tabIndex + 1;
                while (groupEnd < tabs.Count && ReferenceEquals(tabs[groupEnd].Group, group)) groupEnd++;
                var chip = groupChips[group];
                chip.RefreshLabel(text, tabs, tabIndex, groupEnd - tabIndex);
                chip.Visible = false;
                int width = chip.GetPreferredSize(Size.Empty).Width;
                items.Add(chip);
                natural.Add(width);
                minimum.Add(chip.MinimumLabelWidth);
                anyCollapsed |= group.Collapsed;
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
        int totalAvailable = Math.Max(0, tabStrip.ClientSize.Width - tabStrip.Padding.Horizontal
            - newTab.Width - newTab.Margin.Horizontal - closePane.Width - closePane.Margin.Horizontal - Scaled(6));
        int margins = items.Sum(item => item.Margin.Horizontal);
        bool showList = anyCollapsed;
        int listWidth = tabList.Width + tabList.Margin.Horizontal;
        // Shrink pinned names first, keeping their readable floor until even an ordinary overflow
        // window cannot coexist with it. Only that narrowest case reduces pins to icon and pin.
        int pinFloor = Scaled(110), compactPin = Scaled(54);
        int pinBudget = Math.Clamp(totalAvailable - natural.Sum() - margins - (showList ? listWidth : 0) - pinMargins,
            Math.Min(pinNatural.Sum(), pinCount * pinFloor), pinNatural.Sum());
        int normalWindow = count == 0 ? 0 : floor + listWidth + Scaled(2);
        if (pinBudget + pinMargins + normalWindow > totalAvailable)
            pinBudget = Math.Max(0, totalAvailable - pinMargins - normalWindow);
        pinBudget = Math.Min(pinBudget, Math.Max(0, totalAvailable - pinMargins));
        int lowPin = 0, highPin = Scaled(180);
        while (lowPin < highPin)
        {
            int middle = (lowPin + highPin + 1) / 2, used = 0;
            for (int index = 0; index < pinCount; index++) used += Math.Min(pinNatural[index], middle);
            if (used <= pinBudget) lowPin = middle;
            else highPin = middle - 1;
        }
        for (int index = 0; index < pinCount; index++)
        {
            var tab = tabs[index];
            var button = tab.Button;
            int width = Math.Max(1, Math.Min(pinNatural[index], lowPin));
            button.Visible = true;
            button.Size = new Size(width, rowHeight);
            button.Padding = new Padding(0, 0, button.ActionWidth, 0);
            button.DisplayStyle = ToolStripItemDisplayStyle.ImageAndText;
            button.ImageAlign = ContentAlignment.MiddleLeft;
            button.Text = width <= compactPin || width - pinChrome < TextWidth("\u2026")
                ? "" : FitTitle(button.Title, width - pinChrome);
            button.PathToolTip = tab.Host?.LocationPath ?? text.T("New tab");
            button.UnpinToolTip = text.T("Unpin tab");
            button.ToolTipText = button.PathToolTip;
            UpdateTabAccessibleName(tab);
            pinnedSpace += width + button.Margin.Horizontal;
        }
        int available = Math.Max(0, totalAvailable - pinnedSpace);
        int room = Math.Max(0, available - (showList ? listWidth : 0));
        bool overflow = minimum.Sum() + margins > room;
        if (overflow && !showList)
        {
            showList = true;
            room = Math.Max(0, available - listWidth);
        }
        // At very narrow physical widths, keep a chip and the selected tab usable together.
        // Tabs may reduce to icon/close-button chrome before an adjacent chip disappears.
        int chipMinimum = int.MaxValue;
        for (int index = 0; index < count; index++)
            if (items[index] is TabGroupChip) chipMinimum = Math.Min(chipMinimum, minimum[index]);
        if (chipMinimum < int.MaxValue)
        {
            int compactTab = Math.Min(floor, Math.Max(Scaled(40), room - chipMinimum - Scaled(2)));
            for (int index = 0; index < count; index++)
                if (items[index] is FolderTabButton) minimum[index] = compactTab;
        }
        int first = 0, end = count;
        var widths = natural.ToArray();
        // Constant-time window queries keep a resize linear in the number of tabs.
        int[] windowPrefix = new int[count + 1];
        for (int index = 0; index < count; index++)
            windowPrefix[index + 1] = windowPrefix[index] + minimum[index] + items[index].Margin.Horizontal;
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
            FitWidths(first, end);
            if (end == first + 1) widths[first] = Math.Min(widths[first], Math.Max(1, room - items[first].Margin.Horizontal));
        }
        else if (natural.Sum() + margins > room && count > 0)
        {
            FitWidths(0, count);
        }
        firstVisibleTab = first;
        for (int index = 0; index < count; index++)
        {
            var item = items[index];
            item.Size = new Size(widths[index], rowHeight);
            item.Visible = index >= first && index < end;
            if (item is not FolderTabButton button) { item.Invalidate(); continue; }
            button.Padding = new Padding(0, 0, button.CloseWidth, 0);
            string label = FitTitle(button.Title, widths[index] - chrome);
            if (button.Text != label) button.Text = label;
        }
        foreach (var tab in tabs)
        {
            if (tab.Pinned) continue;
            UpdateTabAccessibleName(tab);
            tab.Button.ToolTipText = (tab.Host?.LocationPath ?? text.T("New tab")) + Environment.NewLine
                + text.T(tab == ActiveTab ? "Close current tab (Ctrl+W)" : "Select this tab, then close with Ctrl+W");
        }
        tabList.Visible = showList;
        foreach (var old in tabList.DropDownItems.Cast<ToolStripItem>().ToArray()) old.Dispose();
        previous = null;
        foreach (var tab in tabs)
        {
            if (tab.Pinned) continue;
            if (tab.Group is { } group && previous != group)
            {
                string label = GroupLabel(group);
                tabList.DropDownItems.Add(new ToolStripMenuItem(label)
                {
                    Enabled = false, ForeColor = Theme.GroupColor(group.Color), AccessibleName = label
                });
            }
            var member = tab;
            var item = new ToolStripMenuItem((tab.Group is null ? "" : "    ") + tab.Button.Title)
            {
                Checked = tab == ActiveTab, ToolTipText = tab.Host?.LocationPath ?? "",
                AccessibleName = tab.Button.AccessibleName
            };
            item.Click += (_, _) => SelectTab(tabs.IndexOf(member));
            tabList.DropDownItems.Add(item);
            previous = tab.Group;
        }

        // Both chips and tabs can shrink, but neither falls below its own readable minimum.
        void FitWidths(int start, int stop)
        {
            int low = 0, high = 0, itemMargins = 0;
            for (int index = start; index < stop; index++)
            {
                high = Math.Max(high, natural[index]);
                itemMargins += items[index].Margin.Horizontal;
            }
            while (low < high)
            {
                int middle = (low + high + 1) / 2;
                int total = itemMargins;
                for (int index = start; index < stop; index++)
                    total += Math.Clamp(middle, minimum[index], natural[index]);
                if (total <= room) low = middle;
                else high = middle - 1;
            }
            for (int index = start; index < stop; index++)
                widths[index] = Math.Clamp(low, minimum[index], natural[index]);
        }

        int WindowWidth(int start, int stop)
            => windowPrefix[stop] - windowPrefix[start];
        int FitEnd(int start)
        {
            int stop = start;
            while (stop < count && (stop == start || WindowWidth(start, stop + 1) <= room)) stop++;
            return stop;
        }
    }
}
