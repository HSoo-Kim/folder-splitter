using System.Reflection;
using System.Text.Json;
using QuadExplorer;

internal static partial class Program
{
    private const BindingFlags JfFlags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

    private static T? JfMember<T>(object instance, string name) =>
        (instance.GetType().GetProperty(name, JfFlags)?.GetValue(instance)
            ?? instance.GetType().GetField(name, JfFlags)?.GetValue(instance)) is T value ? value : default;

    private static TabDropPlan? JfExternalPlan(ExplorerPane pane) => JfMember<TabDropPlan>(pane, "externalDropPlan");
    private static System.Windows.Forms.Timer? JfProgressTimer(ExplorerPane pane) =>
        JfMember<System.Windows.Forms.Timer>(pane, "groupJoinProgressTimer");
    private static string JfOutput()
    {
        string output = Environment.GetEnvironmentVariable("FOLDERSPLITTER_QA_LOG_DIR")
            ?? Path.Combine(Environment.CurrentDirectory, "artifacts", "group-feedback");
        Directory.CreateDirectory(output);
        return output;
    }

    private static void RunGroupJoinFeedbackScenario(string root)
    {
        string fixture = Directory.CreateDirectory(Path.Combine(root, "GroupJoinFeedback")).FullName;
        string[] folders = Enumerable.Range(0, 8).Select(index =>
            Directory.CreateDirectory(Path.Combine(fixture, $"Folder-{index}")).FullName).ToArray();
        string session = Path.Combine(root, "group-join-feedback.json");
        SessionStore.Save(session, new SessionState
        {
            Language = "en", Layout = 1, WindowWidth = 1200, WindowHeight = 800,
            ExplorerFavoritesImported = true, CheckUpdates = false, GlobalHotkey = false,
            KeepInTray = false, AutoRestartMemory = 0,
            Panes = [new PaneState { Tabs = [fixture] }]
        });
        using var form = new MainForm(session, []);
        Exception? failure = null;
        form.Shown += async (_, _) =>
        {
            try
            {
                var pane = form.GetPane(0);
                await OitReady(pane.ActiveView!);
                JfExternalFeedbackChecks(form, pane, fixture, folders);
                JfInternalFeedbackChecks(form);
                JfShellDescriptionChecks(form, pane, fixture, folders);
            }
            catch (Exception ex) { failure = ex; }
            finally { form.ExitApplication(); }
        };
        Application.Run(form);
        if (failure is not null) throw new InvalidOperationException("Group-join feedback scenario failed.", failure);
    }

    private static void JfTheme(MainForm form, string mode)
    {
        if (mode is "equal-light" or "equal-dark")
        {
            form.SetColorTheme(mode == "equal-dark" ? "dark" : "light");
            form.SetCustomColors(Theme.GroupColor(TabGroupColor.Blue), Theme.Canvas);
            // Accent readability can quantize the requested seed differently from a group swatch.
            // Stress the equal-color case without weakening either production contrast policy.
            var swatches = (Array)typeof(Theme).GetField("groupColors", BindingFlags.Static | BindingFlags.NonPublic)!
                .GetValue(null)!;
            object blue = swatches.GetValue(1)!;
            blue.GetType().GetProperty("Fill")!.SetValue(blue, Theme.Accent);
            swatches.SetValue(blue, 1);
            Check(Theme.Accent.ToArgb() == Theme.GroupColor(TabGroupColor.Blue).ToArgb(),
                mode + " controlled palette uses exactly equal ARGB accent and group blue");
            return;
        }
        if (mode == "custom-light") form.SetCustomColors(Color.FromArgb(0, 139, 139), Color.FromArgb(247, 243, 232));
        else if (mode == "custom-dark") form.SetCustomColors(Color.FromArgb(202, 77, 140), Color.FromArgb(24, 38, 56));
        else form.SetColorTheme(mode);
    }

    private static void JfExternalFeedbackChecks(MainForm form, ExplorerPane pane, string fixture, string[] folders)
    {
        string output = JfOutput();
        Point cursor = Cursor.Position;
        var evidence = new List<object>();
        void Check(bool success, string name)
        {
            checks++;
            if (!success) failures++;
            Console.WriteLine((success ? "PASS " : "FAIL ") + name);
        }
        try
        {
            foreach (string mode in new[] { "light", "dark", "custom-light", "custom-dark", "equal-light", "equal-dark" })
            foreach (bool collapsed in new[] { false, true })
            {
                JfTheme(form, mode);
                TgResetForm(form);
                TgReset(pane, 4);
                var group = pane.CreateGroup(pane.Tabs[1])!;
                pane.AddToGroup(pane.Tabs[2], group);
                pane.RenameGroup(group, "Blue feedback");
                pane.SetGroupColor(group, TabGroupColor.Blue);
                pane.SelectTab(0, focus: false);
                pane.SetGroupCollapsed(group, collapsed);
                var chip = TgChip(pane, group);
                Point point = TgJoinPoint(pane, group);
                Cursor.Position = point;
                form.Refresh();
                string name = $"external-{mode}-{(collapsed ? "collapsed" : "end")}";
                int paints = 0;
                TabDropPlan? lastPaintPlan = null;
                PaintEventHandler painted = (_, _) => { paints++; lastPaintPlan = JfExternalPlan(pane); };
                pane.TabStrip.Paint += painted;
                try
                {
                    using var drop = new OitOleDrop(pane.TabStrip, fixture, folders.Take(2).ToArray());
                    Check(drop.Enter(point) == 4 && TgJoinTimer(pane).Enabled,
                        name + " registered OLE target starts native dwell with a stationary pointer");
                    Check(TgJoinTimer(pane).Interval == 500,
                        name + " retains the native 500 ms group-join timer contract");
                    var pending = JfExternalPlan(pane)!;
                    Check(pending.JoinGroup is null && pending.MarkerColor.ToArgb() == Theme.Accent.ToArgb(),
                        name + " remains an accent insertion outside the group before arming");
                    Rectangle chipBounds = chip.Bounds;
                    string? chipText = chip.Text;
                    using var before = TgPrintStrip(pane.TabStrip);
                    TgSave(before, Path.Combine(output, name + "-pending.png"));
                    var progressTimer = JfProgressTimer(pane);
                    Check(progressTimer is { Enabled: true, Interval: 25 }
                        && JfMember<TabGroup>(pending, "DwellGroup") == group,
                        name + " pending plan exposes its group and enables the native 25 ms progress timer");
                    float firstProgress = JfMember<float>(pending, "DwellProgress");
                    int progressPaints = paints;
                    if (progressTimer is { Enabled: true })
                    {
                        Thread.Sleep(125);
                        OitFireTimer(progressTimer);
                    }
                    float middleProgress = JfMember<float>(JfExternalPlan(pane)!, "DwellProgress");
                    int firstFramePaints = paints - progressPaints;
                    using var middle = TgPrintStrip(pane.TabStrip);
                    TgSave(middle, Path.Combine(output, name + "-progress-1.png"));
                    if (progressTimer is { Enabled: true })
                    {
                        Thread.Sleep(100);
                        OitFireTimer(progressTimer);
                    }
                    float laterProgress = JfMember<float>(JfExternalPlan(pane)!, "DwellProgress");
                    using var later = TgPrintStrip(pane.TabStrip);
                    TgSave(later, Path.Combine(output, name + "-progress-2.png"));
                    Check(firstFramePaints > 0 && middleProgress > firstProgress && laterProgress > middleProgress
                        && laterProgress < 1 && OitPixelDifference(middle, later) > 5,
                        name + " stationary native progress ticks immediately paint growing intermediate frames");
                    int middleFill = JfProgressFillHeight(middle, pane.TabStrip, pending, Theme.GroupColor(group.Color));
                    int laterFill = JfProgressFillHeight(later, pane.TabStrip, pending, Theme.GroupColor(group.Color));
                    Check(middleFill > 0 && laterFill > middleFill && laterFill < pending.Preview.Height,
                        name + " group-colored fill grows contiguously upward from the marker bottom outside its accent column");
                    Point jitter = new(point.X - 1, point.Y);
                    Cursor.Position = jitter;
                    drop.Over(jitter);
                    Check(JfMember<float>(JfExternalPlan(pane)!, "DwellProgress") >= laterProgress
                        && TgJoinTimer(pane).Enabled,
                        name + " one-pixel jitter within the same group boundary preserves elapsed dwell");
                    int beforeArmPaints = paints;
                    OitFireTimer(TgJoinTimer(pane));
                    int immediatePaints = paints - beforeArmPaints;
                    var armed = JfExternalPlan(pane)!;
                    Check(armed.JoinGroup == group && !TgJoinTimer(pane).Enabled,
                        name + " native WM_TIMER arms without any subsequent DragOver");
                    Check(immediatePaints > 0 && lastPaintPlan?.JoinGroup == group,
                        name + " arming repaints the native strip synchronously before another drag event");
                    Check(progressTimer is { Enabled: false }, name + " armed join stops its progress timer");
                    using var after = TgPrintStrip(pane.TabStrip);
                    TgSave(after, Path.Combine(output, name + "-armed.png"));
                    Rectangle marker = pane.TabStrip.RectangleToClient(armed.Preview);
                    int outsideMarker = JfDifferenceOutside(before, after, marker);
                    Rectangle ghost = JfMember<Rectangle>(armed, "GhostSlot");
                    Rectangle[] highlight = JfMember<Rectangle[]>(armed, "GroupHighlight") ?? [];
                    Check(ghost.Width >= pane.Tabs[0].Button.Width / 2 && ghost.Height >= pane.TabStrip.Height / 2
                        && highlight.Length >= (collapsed ? 1 : 3)
                        && JfMember<int>(armed, "IncomingCount") == 2,
                        name + " armed plan carries a tab-sized ghost, complete group highlight and incoming payload count");
                    Check(outsideMarker > pane.TabStrip.Height * 2,
                        name + " armed shape changes substantially beyond the insertion marker even for a blue group");
                    Check(chip.DropJoinArmed && JfMember<int>(chip, "DropJoinCount") == 2
                        && (collapsed || pane.GroupMembers(group).All(tab => JfMember<bool>(tab.Button, "DropJoinArmed"))),
                        name + " armed chip and visible members advertise the incoming join");
                    Check(chip.Bounds == chipBounds && chip.Text == chipText,
                        name + " incoming count is painted without changing chip text or moving its hover boundary");
                    Check(JfChangedInScreen(before, after, pane.TabStrip, ghost) > pane.TabStrip.Height
                        && highlight.All(rectangle => JfChangedInScreen(before, after, pane.TabStrip, rectangle) > 5),
                        name + " native pixels paint the ghost slot and every highlighted chip or member");
                    Check(JfGhostHidesUnderlay(after, pane.TabStrip, armed),
                        name + " opaque ghost contains no covered tab icon or text ink");
                    Check(armed.Label.Contains("Blue feedback", StringComparison.Ordinal)
                        && armed.Label.Contains("group", StringComparison.OrdinalIgnoreCase),
                        name + " armed label names the target group rather than only opening tabs");
                    evidence.Add(new { name, accent = Theme.Accent.ToArgb(), blue = Theme.GroupColor(group.Color).ToArgb(),
                        blueAccentDistance = JfColorDistance(Theme.Accent, Theme.GroupColor(group.Color)),
                        immediatePaints, firstFramePaints, firstProgress, middleProgress, laterProgress,
                        middleFill, laterFill, outsideMarker, ghost, highlightCount = highlight.Length });
                    int beforeLeavePaints = paints;
                    drop.Leave();
                    Check(paints > beforeLeavePaints && lastPaintPlan is null && JfExternalPlan(pane) is null
                        && !chip.DropJoinArmed && !TgJoinTimer(pane).Enabled && progressTimer?.Enabled != true
                        && pane.GroupMembers(group).All(tab => !JfMember<bool>(tab.Button, "DropJoinArmed")),
                        name + " leaving synchronously repaints and clears ghost, highlight, dwell and both timers");
                    Check(drop.Enter(point) == 4 && JfMember<float>(JfExternalPlan(pane)!, "DwellProgress") < .1f,
                        name + " reentry starts fresh progress instead of retaining the previous armed state");
                    int beforePendingLeave = paints;
                    drop.Leave();
                    Check(paints > beforePendingLeave && lastPaintPlan is null && JfExternalPlan(pane) is null
                        && progressTimer?.Enabled != true && !TgJoinTimer(pane).Enabled,
                        name + " leaving during a fresh pending dwell also stops progress and repaints immediately");
                }
                finally { pane.TabStrip.Paint -= painted; }
            }
        }
        finally
        {
            File.WriteAllText(Path.Combine(output, "native-feedback-evidence.json"),
                JsonSerializer.Serialize(evidence, new JsonSerializerOptions { WriteIndented = true }));
            Cursor.Position = cursor;
            form.SetColorTheme("light");
        }
    }

    private static Bitmap JfOverlayCapture(TabDockOverlay overlay, string name)
    {
        var image = new Bitmap(overlay.ClientSize.Width, overlay.ClientSize.Height);
        overlay.DrawToBitmap(image, overlay.ClientRectangle);
        TgSave(image, Path.Combine(JfOutput(), name + ".png"));
        return image;
    }

    private static int JfChangedIn(Bitmap before, Bitmap after, Rectangle region)
    {
        region.Intersect(new Rectangle(Point.Empty, before.Size));
        int count = 0;
        for (int y = region.Top; y < region.Bottom; y++)
        for (int x = region.Left; x < region.Right; x++)
            if (before.GetPixel(x, y) != after.GetPixel(x, y)) count++;
        return count;
    }

    private static int JfChangedInScreen(Bitmap before, Bitmap after, Control control, Rectangle screen) =>
        JfChangedIn(before, after, control.RectangleToClient(screen));

    private static bool JfGhostHidesUnderlay(Bitmap actual, Control control, TabDropPlan plan)
    {
        Rectangle slot = control.RectangleToClient(plan.GhostSlot);
        if (slot.Width <= 2 || slot.Height <= 2 || !new Rectangle(Point.Empty, actual.Size).Contains(slot)) return false;
        using var overBlack = new Bitmap(slot.Width, slot.Height);
        using var overWhite = new Bitmap(slot.Width, slot.Height);
        using (var black = Graphics.FromImage(overBlack))
        using (var white = Graphics.FromImage(overWhite))
        {
            black.Clear(Color.Black);
            white.Clear(Color.White);
            TabDockOverlay.PaintStripFeedback(black, plan, plan.GhostSlot.Location, false);
            TabDockOverlay.PaintStripFeedback(white, plan, plan.GhostSlot.Location, false);
        }
        using var native = actual.Clone(slot, actual.PixelFormat);
        var area = new Rectangle(Point.Empty, slot.Size);
        // Every pixel, including border gaps and the plus glyph, must be independent of
        // the underlying ink and match the actual strip/overlay capture.
        return OitPixelDifference(overBlack, overWhite) == 0
            && JfChangedIn(native, overBlack, area) == 0;
    }

    private static int JfProgressFillHeight(Bitmap image, Control control, TabDropPlan plan, Color groupColor)
    {
        Rectangle marker = control.RectangleToClient(plan.Preview);
        int x = marker.Right + Math.Max(0, (marker.Width - 1) / 2);
        if (x < 0 || x >= image.Width || marker.Bottom > image.Height) return 0;
        int height = 0;
        for (int y = marker.Bottom - 1; y >= Math.Max(0, marker.Top); y--)
        {
            if (image.GetPixel(x, y).ToArgb() != groupColor.ToArgb()) break;
            height++;
        }
        return height;
    }

    private static void JfInternalFeedbackChecks(MainForm form)
    {
        var transport = new HarmonyLib.Harmony("FolderSplitter.GroupJoinFeedback");
        var method = HarmonyLib.AccessTools.Method(typeof(Control), nameof(Control.DoDragDrop),
            [typeof(object), typeof(DragDropEffects)]);
        transport.Patch(method, prefix: new HarmonyLib.HarmonyMethod(typeof(Program), nameof(DispatchBlankTabDrop)));
        Point cursor = Cursor.Position;
        try
        {
            foreach (string mode in new[] { "light", "dark", "custom-light", "custom-dark", "equal-light", "equal-dark" })
            foreach (bool collapsed in new[] { false, true })
            {
                JfTheme(form, mode);
                TgResetForm(form);
                var pane = form.GetPane(0);
                TgReset(pane, 5);
                var group = pane.CreateGroup(pane.Tabs[1])!;
                pane.AddToGroup(pane.Tabs[2], group);
                pane.RenameGroup(group, "Blue feedback");
                pane.SetGroupColor(group, TabGroupColor.Blue);
                MdSelect(pane, pane.Tabs[0], pane.Tabs[4]);
                pane.SetGroupCollapsed(group, collapsed);
                Point point = TgJoinPoint(pane, group);
                Cursor.Position = point;
                string name = $"internal-{mode}-{(collapsed ? "collapsed" : "end")}";
                TgInterceptDrag(form, pane, null, pane.Tabs[0], point, cancel: true, hover: (overlay, args) =>
                {
                    int stripPaints = 0, overlayPaints = 0;
                    PaintEventHandler stripPainted = (_, _) => stripPaints++;
                    PaintEventHandler overlayPainted = (_, _) => overlayPaints++;
                    pane.TabStrip.Paint += stripPainted;
                    overlay.Paint += overlayPainted;
                    try
                    {
                        var pending = TgDwellPlan(overlay);
                        var progress = JfProgressTimer(pane)
                            ?? throw new InvalidOperationException("Internal dwell has no progress timer.");
                        Check(pending.JoinGroup is null && pending.MarkerColor.ToArgb() == Theme.Accent.ToArgb()
                            && JfMember<TabGroup>(pending, "DwellGroup") == group
                            && progress is { Enabled: true, Interval: 25 },
                            name + " real internal overlay starts pending progress without changing insertion semantics");
                        using var before = JfOverlayCapture(overlay, name + "-pending");
                        Thread.Sleep(100);
                        int firstStrip = stripPaints, firstOverlay = overlayPaints;
                        OitFireTimer(progress!);
                        float firstProgress = JfMember<float>(TgDwellPlan(overlay), "DwellProgress");
                        Check(stripPaints > firstStrip && overlayPaints > firstOverlay && firstProgress is > 0 and < 1,
                            name + " stationary native progress tick synchronously paints strip and overlay");
                        using var firstFrame = JfOverlayCapture(overlay, name + "-progress-1");
                        Thread.Sleep(100);
                        OitFireTimer(progress!);
                        float nextProgress = JfMember<float>(TgDwellPlan(overlay), "DwellProgress");
                        using var nextFrame = JfOverlayCapture(overlay, name + "-progress-2");
                        Rectangle stripRegion = overlay.RectangleToClient(pane.TabStrip.RectangleToScreen(pane.TabStrip.ClientRectangle));
                        Check(nextProgress > firstProgress && nextProgress < 1
                            && JfChangedIn(firstFrame, nextFrame, stripRegion) > 5,
                            name + " overlay progress visibly grows through multiple intermediate frames");
                        int firstFill = JfProgressFillHeight(firstFrame, overlay, pending, Theme.GroupColor(group.Color));
                        int nextFill = JfProgressFillHeight(nextFrame, overlay, pending, Theme.GroupColor(group.Color));
                        Check(firstFill > 0 && nextFill > firstFill && nextFill < pending.Preview.Height,
                            name + " overlay fill grows bottom-up outside the accent marker even with exactly equal ARGB colors");
                        var jitter = new DragEventArgs(args.Data, args.KeyState, point.X - 1, point.Y,
                            args.AllowedEffect, DragDropEffects.None);
                        Cursor.Position = new Point(jitter.X, jitter.Y);
                        TgDragEvent(overlay, "OnDragOver", jitter);
                        Check(JfMember<float>(TgDwellPlan(overlay), "DwellProgress") >= nextProgress
                            && TgJoinTimer(pane).Enabled,
                            name + " internal one-pixel boundary jitter does not restart dwell");
                        int beforeStripArm = stripPaints, beforeOverlayArm = overlayPaints;
                        OitFireTimer(TgJoinTimer(pane));
                        Check(stripPaints > beforeStripArm && overlayPaints > beforeOverlayArm,
                            name + " arm timer paints both live surfaces before any additional DragOver or capture");
                        var armed = TgDwellPlan(overlay);
                        Rectangle ghost = JfMember<Rectangle>(armed, "GhostSlot");
                        Rectangle[] highlights = JfMember<Rectangle[]>(armed, "GroupHighlight") ?? [];
                        Check(armed.JoinGroup == group && armed.Label.Contains("Blue feedback", StringComparison.Ordinal)
                            && JfMember<int>(armed, "IncomingCount") == 2
                            && ghost.Width >= pane.Tabs[0].Button.Width / 2
                            && highlights.Length >= (collapsed ? 1 : 3) && !progress!.Enabled,
                            name + " armed internal overlay names its group and exposes full payload ghost and highlight geometry");
                        using var after = JfOverlayCapture(overlay, name + "-armed");
                        Check(JfChangedInScreen(before, after, overlay, ghost) > pane.TabStrip.Height
                            && highlights.All(rectangle => JfChangedInScreen(before, after, overlay, rectangle) > 5),
                            name + " real overlay paints its tab-width ghost and every group highlight even when blue equals accent");
                        Check(JfGhostHidesUnderlay(after, overlay, armed),
                            name + " opaque overlay ghost contains no covered tab icon or text ink");
                        var label = new Rectangle(8, Math.Min(overlay.ClientSize.Height - overlay.Font.Height * 2,
                            overlay.RectangleToClient(armed.Preview).Bottom + 4),
                            Math.Max(0, overlay.ClientSize.Width - 16), overlay.Font.Height * 2);
                        Check(TgPixels(after, label, Theme.GroupTextColor(group.Color)) > 8
                            && overlay.BackColor.ToArgb() == Theme.Canvas.ToArgb()
                            && PaletteGenerator.Contrast(Theme.GroupTextColor(group.Color), overlay.BackColor) >= 4.5,
                            name + " armed group label is visible in the actual overlay paint output");
                        int beforeLeave = stripPaints;
                        TgDragEvent(overlay, "OnDragLeave", EventArgs.Empty);
                        Check(stripPaints > beforeLeave && !TgJoinTimer(pane).Enabled && !progress.Enabled
                            && !TgChip(pane, group).DropJoinArmed
                            && JfMember<TabDropPlan>(overlay, "preview") is null
                            && pane.GroupMembers(group).All(tab => !JfMember<bool>(tab.Button, "DropJoinArmed")),
                            name + " leaving clears the overlay plan and synchronously repaints all armed strip members");
                        TgDragEvent(overlay, "OnDragEnter", args);
                        Check(TgDwellPlan(overlay).JoinGroup is null
                            && JfMember<float>(TgDwellPlan(overlay), "DwellProgress") < .1f,
                            name + " internal reentry restarts progress from zero");
                        int beforePendingLeave = stripPaints;
                        TgDragEvent(overlay, "OnDragLeave", EventArgs.Empty);
                        Check(stripPaints > beforePendingLeave && !progress.Enabled && !TgJoinTimer(pane).Enabled,
                            name + " leaving a pending internal dwell repaints immediately and stops progress");
                    }
                    finally
                    {
                        pane.TabStrip.Paint -= stripPainted;
                        overlay.Paint -= overlayPainted;
                    }
                });
                Check(!TgJoinTimer(pane).Enabled && JfProgressTimer(pane)?.Enabled != true,
                    name + " Escape cancellation disposes the overlay without leaving either timer active");
            }
        }
        finally
        {
            blankTabDrop = null;
            transport.Unpatch(method, HarmonyLib.HarmonyPatchType.Prefix, transport.Id);
            Cursor.Position = cursor;
            form.SetColorTheme("light");
            TgResetForm(form);
        }
    }

    private static int JfColorDistance(Color a, Color b) =>
        Math.Abs(a.R - b.R) + Math.Abs(a.G - b.G) + Math.Abs(a.B - b.B);

    private static int JfDifferenceOutside(Bitmap before, Bitmap after, Rectangle excluded)
    {
        int count = 0;
        excluded.Inflate(3, 2);
        for (int y = 0; y < before.Height; y++)
        for (int x = 0; x < before.Width; x++)
            if (!excluded.Contains(x, y) && before.GetPixel(x, y) != after.GetPixel(x, y)) count++;
        return count;
    }
}
