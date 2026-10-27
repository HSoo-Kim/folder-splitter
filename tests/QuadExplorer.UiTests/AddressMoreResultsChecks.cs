using QuadExplorer;
using QuadExplorer.Search;

internal static partial class Program
{
    private static void RunAddressMoreResultsScenario(string root)
    {
        string fixture = Directory.CreateDirectory(Path.Combine(root, "address-more")).FullName;
        foreach (var (prefix, count) in new[] { ("pagetest-", 620), ("edge050-", 50), ("edge051-", 51),
            ("edge200-", 200), ("edge201-", 201), ("edge500-", 500), ("edge501-", 501) })
            for (int i = 0; i < count; i++) File.WriteAllText(Path.Combine(fixture, $"{prefix}{i:D4}.txt"), "paging fixture");
        File.WriteAllText(Path.Combine(fixture, "newer-only.txt"), "superseding query");
        string session = Path.Combine(root, "address-more-session.json");
        SessionStore.Save(session, new SessionState
        {
            Layout = 1, Language = "en", ExplorerFavoritesImported = true,
            Panes = [new PaneState { Tabs = [fixture] }]
        });
        string[] expectedFiles = Directory.GetFiles(fixture).OrderBy(path => path, StringComparer.OrdinalIgnoreCase).ToArray();
        using var real = new IndependentSearchBackend([fixture], Path.Combine(root, "address-more.fsix"));
        using var backend = new DelayedSearchBackend(real);
        using var form = new MainForm(session, [], backend);
        var pane = form.GetPane(0);
        var address = Descendants(pane).OfType<TextBox>().Single(box => box.AccessibleName?.EndsWith(" address") == true);
        using var moreNormalFont = (Font)address.Font.Clone();
        using var moreLargeFont = new Font(moreNormalFont.FontFamily, moreNormalFont.SizeInPoints * 1.5f);
        Task navigation = pane.ActiveView!.HasNavigated ? Task.CompletedTask : NavigationSignal(pane.ActiveView);
        // Keep collecting independent consumer failures instead of stopping at the first regression.
        void Check(bool success, string name)
        {
            checks++;
            if (success) Console.WriteLine("PASS " + name);
            else { failures++; Console.Error.WriteLine("FAIL " + name); }
        }
        AddressCandidates Popup() => form.OwnedForms.OfType<AddressCandidates>().Single();
        ListBox Rows() => Popup().Controls.OfType<ListBox>().Single();
        string[] Paths() => pane.SearchCandidates.Select(hit => hit.FullPath).ToArray();
        bool Page(int hits, string? footer) => pane.CandidatesVisible && pane.SearchCandidates.Count == hits
            && Rows().Items.Count == hits + (footer is null ? 0 : 1)
            && Paths().SequenceEqual(expectedFiles.Where(path =>
                Path.GetFileName(path).Contains(address.Text, StringComparison.OrdinalIgnoreCase)).Take(hits))
            && (footer is null || Rows().Items[hits].ToString() == footer);
        string More() => new UiText(form.Language).T("Show more results");
        string Limit() => new UiText(form.Language).T("Showing the first {0} matches. Type more of the name to narrow the search.", 500);
        async Task<bool> Expand(bool mouse = false)
        {
            var updated = SearchSignal(pane);
            var settled = mouse ? SearchSignal(pane, completed: true) : Task.CompletedTask;
            var rows = Rows();
            rows.SelectedIndex = rows.Items.Count - 1;
            if (mouse)
            {
                MoreClick(rows, rows.Items.Count - 1);
                if (!address.Focused || !pane.CandidatesVisible) return false;
                await settled.WaitAsync(TimeSpan.FromSeconds(15));
                return updated.IsCompleted;
            }
            await MoreKey(address, Keys.Enter);
            await updated.WaitAsync(TimeSpan.FromSeconds(15));
            return true;
        }
        form.Shown += async (_, _) =>
        {
            try
            {
                await navigation.WaitAsync(TimeSpan.FromSeconds(20));
                await real.BuildOrLoadAsync(CancellationToken.None);
                await Search(pane, address, "pagetest-");
                Check(Page(50, More()) && Rows().SelectedIndex == 0,
                    "620 matches start with 50 real hits, initial selection and exactly one final More row");
                // Negative controls exercise the same page predicate against genuinely wrong observable states.
                Check(!Page(200, More()) && !Page(50, null),
                    "page assertion rejects a missing footer and an unexpanded page masquerading as 200 hits");
                var first = Paths();
                string current = pane.CurrentPath;
                SearchHit retainedHit = pane.SearchCandidates[49];
                await MoreIconSignal(Popup(), retainedHit);
                for (int i = 0; i < 55; i++) await MoreKey(address, Keys.Down);
                Check(Rows().SelectedIndex == 50 && Popup().MoreSelected,
                    "Down from first hit reaches the More footer and clamps there");
                bool retainedAtPresentation = false;
                EventHandler preservedIcon = (_, _) =>
                {
                    if (pane.SearchCandidates.Count != 200) return;
                    // Capture synchronously inside presentation, before any UI-turn polling or icon invalidations.
                    using var image = Popup().CopyIcon(retainedHit);
                    retainedAtPresentation = image is not null;
                };
                pane.CandidatesUpdated += preservedIcon;
                try
                {
                    var expanded = SearchSignal(pane);
                    await MoreKey(address, Keys.Enter);
                    await expanded.WaitAsync(TimeSpan.FromSeconds(15));
                }
                finally { pane.CandidatesUpdated -= preservedIcon; }
                Check(retainedAtPresentation, "hit 49 keeps its loaded icon in the exact UI turn that the 200-hit page is presented");
                await MoreIconSignal(Popup(), pane.SearchCandidates[50]);
                using (var laterIcon = Popup().CopyIcon(pane.SearchCandidates[50]))
                    Check(laterIcon is not null, "newly appended hit 50 still receives an icon after preserving earlier icons");
                Check(Page(200, More()) && pane.CurrentPath == current && address.Focused,
                    "Enter on More keeps the address popup open and does not navigate");
                Check(Paths().Take(50).SequenceEqual(first), "200-hit page preserves the first 50 hits in order");
                Check(Rows().SelectedIndex == 50 && Rows().TopIndex <= 50
                    && Rows().ClientRectangle.Contains(Rows().GetItemRectangle(50)),
                    "first newly added hit is selected and fully scrolled into view");
                pane.FocusFiles();
                await Search(pane, address, "pagetest-");
                var doubleExpanded = SearchSignal(pane);
                MoreClick(Rows(), 50, doubleClick: true);
                await doubleExpanded.WaitAsync(TimeSpan.FromSeconds(15));
                Check(Page(200, More()) && pane.CurrentPath == current && address.Focused && Rows().SelectedIndex == 50,
                    "double-clicking More expands once to 200, stays open and selects the first new hit, never 500");
                var second = Paths();
                await Expand();
                Check(Page(500, Limit()) && Paths().Take(200).SequenceEqual(second) && pane.CurrentPath == current,
                    "second expansion preserves 200 hits and ends at 500 hits with only a limit note");
                Check(Rows().SelectedIndex == 200 && Rows().ClientRectangle.Contains(Rows().GetItemRectangle(200)),
                    "500-hit expansion selects and reveals the first new hit");
                await MoreIconSignal(Popup(), pane.SearchCandidates[499]);
                using (var icon = Popup().CopyIcon(pane.SearchCandidates[499]))
                    Check(icon is not null, "the 500th hit receives its Shell icon, not just the first page");
                Rows().SelectedIndex = 499;
                await MoreKey(address, Keys.Down);
                Check(Rows().SelectedIndex == 499 && !Popup().MoreSelected,
                    "Down at the last real hit cannot select the limit note");
                MoreClick(Rows(), 500);
                Check(Page(500, Limit()) && Rows().SelectedIndex == 499 && pane.CurrentPath == current && address.Focused,
                    $"clicking the limit note keeps a real hit selected without paging or navigating [visible={pane.CandidatesVisible}, hits={pane.SearchCandidates.Count}, rows={Rows().Items.Count}, selected={Rows().SelectedIndex}, path={pane.CurrentPath}]");
                var typedAfterNote = SearchSignal(pane);
                address.SelectionStart = address.TextLength;
                address.SelectionLength = 0;
                SendMessage(address.Handle, 0x102, '0', 0);
                await typedAfterNote.WaitAsync(TimeSpan.FromSeconds(15));
                Check(address.Focused && address.Text == "pagetest-0" && Page(50, More()),
                    "typing after clicking the limit note still reaches the address box and resets page one");

                foreach (string theme in new[] { "light", "dark" })
                {
                    form.SetColorTheme(theme);
                    pane.FocusFiles();
                    await Search(pane, address, "pagetest-");
                    await Expand();
                    await Expand();
                    MoreCapture(Popup(), "limit-" + theme);
                    Check(Page(500, Limit()), theme + " limit note remains a single non-hit footer");
                    pane.FocusFiles();
                    await Search(pane, address, "pagetest-");
                    foreach (int scale in new[] { 100, 150 })
                    {
                        address.Font = scale == 100 ? moreNormalFont : moreLargeFont;
                        // Re-query the actual pane so layout is exercised through production presentation.
                        await Search(pane, address, scale == 100 ? "pagetest" : "pagetest-");
                        var rows = Rows();
                        rows.SelectedIndex = 0;
                        rows.TopIndex = rows.Items.Count - 7;
                        Check(rows.ItemHeight >= TextRenderer.MeasureText(More(), rows.Font).Height
                            && rows.ClientRectangle.Contains(rows.GetItemRectangle(50)),
                            $"{theme} {scale}% font keeps the full More row and text height visible");
                        Check(MoreFooterInk(Popup(), Theme.Accent), $"{theme} {scale}% unselected More is drawn in accent colour");
                        MoreCapture(Popup(), $"more-{theme}-{scale}-unselected");
                        rows.SelectedIndex = 50;
                        Check(MoreFooterInk(Popup(), SystemColors.HighlightText),
                            $"{theme} {scale}% selected More uses highlight text colour");
                        MoreCapture(Popup(), $"more-{theme}-{scale}-selected");
                    }
                    address.Font = moreNormalFont;
                }
                form.SetColorTheme("light");

                foreach (int count in new[] { 50, 51, 200, 201, 500, 501 })
                {
                    await Search(pane, address, $"edge{count:D3}-");
                    Check(Page(50, count == 50 ? null : More()), $"exactly {count} matches have the correct first-page boundary");
                    if (count == 50) continue;
                    var before = Paths();
                    bool mousePaged = await Expand(mouse: true);
                    Check(Page(Math.Min(200, count), count > 200 ? More() : null)
                        && Paths().Take(50).SequenceEqual(before) && Rows().SelectedIndex == 50 && pane.CurrentPath == current && address.Focused,
                        $"mouse More expands exactly {count} matches to the correct 200-page boundary without navigating [visible={pane.CandidatesVisible}, focused={address.Focused}, hits={pane.SearchCandidates.Count}, selected={Rows().SelectedIndex}]");
                    if (!mousePaged)
                    {
                        // Preserve the mouse failure above; recover only to exercise independent page boundaries.
                        pane.FocusFiles();
                        await Search(pane, address, $"edge{count:D3}-");
                        await Expand();
                        Check(Page(Math.Min(200, count), count > 200 ? More() : null),
                            $"keyboard expansion independently verifies the exact {count} boundary");
                    }
                    if (count == 51)
                    {
                        var typedAfterMore = SearchSignal(pane);
                        address.SelectionStart = address.TextLength;
                        address.SelectionLength = 0;
                        SendMessage(address.Handle, 0x102, '0', 0);
                        await typedAfterMore.WaitAsync(TimeSpan.FromSeconds(15));
                        Check(address.Focused && address.Text == "edge051-0" && Page(50, More()),
                            "typing after a mouse More expansion reaches the address box and restarts page one");
                    }
                    if (count <= 200) continue;
                    await Expand();
                    Check(Page(Math.Min(500, count), count > 500 ? Limit() : null),
                        $"exactly {count} matches have the correct 500-page boundary (no false limit at 500)");
                }

                await Search(pane, address, "pagetest");
                await Expand();
                await Search(pane, address, "pagetest-");
                Check(Page(50, More()) && Rows().SelectedIndex == 0, "typing one more character resets the expanded page");
                await Expand();
                pane.FocusFiles();
                var refocused = SearchSignal(pane);
                address.Focus();
                await refocused.WaitAsync(TimeSpan.FromSeconds(15));
                Check(Page(50, More()) && Rows().SelectedIndex == 0, "re-focusing the address box restarts at the first page");
                await Expand();
                await MoreKey(address, Keys.Escape);
                Check(!pane.CandidatesVisible && pane.CurrentPath == current, "Escape dismisses an expanded popup without navigation");
                pane.FocusFiles();
                var reopened = SearchSignal(pane);
                address.Focus();
                await reopened.WaitAsync(TimeSpan.FromSeconds(15));
                Check(Page(50, More()), "closing and reopening the popup starts again at 50");
                await Expand();
                await MoreKey(address, Keys.Escape);
                Check(!pane.CandidatesVisible && address.Focused, "Escape from page 200 leaves the query focused for index notifications");
                var indexReopened = SearchSignal(pane);
                File.WriteAllText(Path.Combine(fixture, "reopen-notification.txt"), "real watched index change");
                await indexReopened.WaitAsync(TimeSpan.FromSeconds(20));
                Check(Page(50, More()) && Rows().SelectedIndex == 0 && pane.CurrentPath == current,
                    "a real backend index-change notification after Escape reopens exactly page one, not the old expanded page");
                foreach (string language in new[] { "ko", "en", "zh-CN", "ja" })
                {
                    var localized = SearchSignal(pane);
                    form.SetLanguage(language);
                    await localized.WaitAsync(TimeSpan.FromSeconds(15));
                    Check(form.Language == language && Page(50, new UiText(language).T("Show more results")),
                        language + " language switch with a visible footer safely relocalizes More");
                    await Expand();
                    await Expand();
                    Check(Page(500, new UiText(language).T("Showing the first {0} matches. Type more of the name to narrow the search.", 500)),
                        language + " last-page limit note is localized and excluded from hits");
                    await Search(pane, address, address.Text == "pagetest" ? "pagetest-" : "pagetest");
                }

                pane.FocusFiles();
                await Search(pane, address, "pagetest-");
                backend.DelayNext();
                Rows().SelectedIndex = 50;
                await MoreKey(address, Keys.Enter);
                await backend.Started.Task.WaitAsync(TimeSpan.FromSeconds(10));
                await Search(pane, address, "newer-only");
                var newer = Paths();
                Check(Page(1, null) && newer.Single().EndsWith("newer-only.txt", StringComparison.Ordinal),
                    "new query completes while the old page-more request is blocked");
                var late = SearchSignal(pane, completed: true);
                backend.Release();
                await late.WaitAsync(TimeSpan.FromSeconds(15));
                Check(Page(1, null) && Paths().SequenceEqual(newer) && Rows().SelectedIndex == 0,
                    "late uncancellable page-more results cannot replace or reselect newer candidates");

                await Search(pane, address, "pagetest-");
                await Expand();
                await Expand();
                Rows().SelectedIndex = 499;
                string chosen = pane.SearchCandidates[499].FullPath;
                var selected = SelectionSignal(pane.ActiveView!);
                await MoreKey(address, Keys.Enter);
                await selected.WaitAsync(TimeSpan.FromSeconds(15));
                Check(!pane.CandidatesVisible && pane.CurrentPath == fixture
                    && SelectedPaths(pane.ActiveView!).SequenceEqual([chosen]),
                    "Enter on a real hit at the 500 boundary opens its parent and selects the file");

                await Search(pane, address, "pagetest-");
                string clicked = pane.SearchCandidates[1].FullPath;
                var clickedSelection = SelectionSignal(pane.ActiveView!);
                MoreClick(Rows(), 1);
                Check(address.Focused, "clicking an ordinary hit does not steal address focus before shell navigation");
                await clickedSelection.WaitAsync(TimeSpan.FromSeconds(15));
                Check(!pane.CandidatesVisible && SelectedPaths(pane.ActiveView!).SequenceEqual([clicked]),
                    "ordinary hit remains clickable after footer paging and selects the requested file");

                await Search(pane, address, "pagetest-");
                string doubleChosen = pane.SearchCandidates[2].FullPath;
                int openRequests = 0;
                void CountOpen(SearchHit hit) { openRequests++; }
                Popup().Chosen += CountOpen;
                try
                {
                    var doubleSelection = SelectionSignal(pane.ActiveView!);
                    MoreClick(Rows(), 2, doubleClick: true);
                    await doubleSelection.WaitAsync(TimeSpan.FromSeconds(15));
                    Check(openRequests == 1 && !pane.CandidatesVisible && pane.CurrentPath == fixture
                        && SelectedPaths(pane.ActiveView!).SequenceEqual([doubleChosen]),
                        "double-clicking an ordinary hit issues exactly one open and selects the file");
                }
                finally { Popup().Chosen -= CountOpen; }
            }
            catch (Exception ex) { failures++; Console.Error.WriteLine("FAIL " + ex); }
            finally { backend.Release(); form.ExitApplication(); }
        };
        Application.Run(form);
    }

    private static async Task MoreKey(TextBox address, Keys key)
    {
        var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        KeyEventHandler? up = null;
        up = (_, e) => { if (e.KeyCode == key) done.TrySetResult(); };
        address.KeyUp += up;
        try
        {
            PostMessage(address.Handle, 0x100, (nint)key, 0);
            PostMessage(address.Handle, 0x101, (nint)key, 0);
            await done.Task.WaitAsync(TimeSpan.FromSeconds(10));
        }
        finally { address.KeyUp -= up; }
    }

    private static void MoreClick(ListBox rows, int index, bool doubleClick = false)
    {
        rows.TopIndex = Math.Max(0, index - 6);
        Rectangle bounds = rows.GetItemRectangle(index);
        nint point = (nint)(((bounds.Top + bounds.Height / 2) << 16) | (bounds.Left + 20));
        SendMessage(rows.Handle, 0x201, 1, point);
        SendMessage(rows.Handle, 0x202, 0, point);
        if (doubleClick)
        {
            SendMessage(rows.Handle, 0x203, 1, point);
            SendMessage(rows.Handle, 0x202, 0, point);
        }
    }

    private static async Task MoreIconSignal(AddressCandidates popup, SearchHit hit)
    {
        var rows = popup.Controls.OfType<ListBox>().Single();
        var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        void Probe(object? sender, InvalidateEventArgs e)
        {
            using var icon = popup.CopyIcon(hit);
            if (icon is not null) done.TrySetResult();
        }
        rows.Invalidated += Probe;
        try { Probe(null, new InvalidateEventArgs(Rectangle.Empty)); await done.Task.WaitAsync(TimeSpan.FromSeconds(30)); }
        finally { rows.Invalidated -= Probe; }
    }

    private static void MoreCapture(AddressCandidates popup, string name)
    {
        var rows = popup.Controls.OfType<ListBox>().Single();
        rows.TopIndex = Math.Max(0, rows.Items.Count - 7);
        if (Environment.GetEnvironmentVariable("FOLDERSPLITTER_QA_LOG_DIR") is not { Length: > 0 } output) return;
        using var bitmap = new Bitmap(popup.Width, popup.Height);
        popup.DrawToBitmap(bitmap, popup.ClientRectangle);
        bitmap.Save(Path.Combine(output, "address-" + name + ".png"));
    }

    private static bool MoreFooterInk(AddressCandidates popup, Color expected)
    {
        var rows = popup.Controls.OfType<ListBox>().Single();
        Rectangle row = rows.GetItemRectangle(rows.Items.Count - 1);
        using var bitmap = new Bitmap(rows.Width, rows.Height);
        rows.DrawToBitmap(bitmap, rows.ClientRectangle);
        row.Intersect(rows.ClientRectangle);
        int pixels = 0;
        for (int y = row.Top; y < row.Bottom; y++)
        for (int x = row.Left + 30; x < row.Right; x++)
        {
            Color actual = bitmap.GetPixel(x, y);
            if (Math.Abs(actual.R - expected.R) + Math.Abs(actual.G - expected.G) + Math.Abs(actual.B - expected.B) < 35) pixels++;
        }
        return pixels >= 10;
    }
}
