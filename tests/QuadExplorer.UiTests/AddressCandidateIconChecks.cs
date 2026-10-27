using QuadExplorer;
using QuadExplorer.Search;
using System.Runtime.InteropServices;

internal static partial class Program
{
    // Run only after the private-desktop guard in Main; no accounts or network needed.
    private static void RunAddressCandidateIconScenario(string root)
    {
        string folder = Directory.CreateDirectory(Path.Combine(root, "address-icon-folder")).FullName;
        string text = Path.Combine(folder, "sample.txt");
        string image = Path.Combine(folder, "sample.png");
        File.WriteAllText(text, "fixture");
        File.WriteAllBytes(image, []);
        SearchHit[] hits = [new(folder, true), new(text, false), new(image, false)];
        using var owner = new Form { Width = 800, Height = 350 };
        using var address = new TextBox { Dock = DockStyle.Top, AccessibleName = "address icon fixture" };
        owner.Controls.Add(address);
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        bool block = true;
        using var candidates = new AddressCandidates(1, hit =>
        {
            if (block && hit == hits[0])
            {
                entered.Set();
                release.Wait(TimeSpan.FromSeconds(10));
            }
            return AddressShellIcons.Load(hit);
        });
        owner.Shown += async (_, _) =>
        {
            try
            {
                candidates.Present(address, hits);
                Check(await Task.Run(() => entered.Wait(TimeSpan.FromSeconds(10))), "first icon request reaches the worker");
                candidates.Present(address, [hits[1], hits[2]]);
                block = false;
                release.Set();
                await WaitForAddressIcon(candidates, hits[1]);
                await WaitForAddressIcon(candidates, hits[2]);
                using (var stale = candidates.CopyIcon(hits[0]))
                    Check(stale is null, "replaced query never installs its late icon");
                Check(candidates.Hits.SequenceEqual([hits[1], hits[2]]), "query replacement retains current result order");
                foreach (SearchHit hit in hits.Skip(1))
                {
                    using var actual = candidates.CopyIcon(hit);
                    using var expected = AddressTestShellIcon(hit.FullPath, hit.IsDirectory, false);
                    Check(actual is not null && expected is not null && SameAddressIcon(actual, expected),
                        "result uses the Shell icon for " + Path.GetExtension(hit.FullPath));
                }
                candidates.Present(address, hits);
                foreach (SearchHit hit in hits) await WaitForAddressIcon(candidates, hit);
                using (var actual = candidates.CopyIcon(hits[0]))
                using (var expected = AddressTestShellIcon(folder, true, false))
                    Check(actual is not null && expected is not null && SameAddressIcon(actual, expected),
                        "folder uses its actual Shell icon and native size");
                var missing = new SearchHit(Path.Combine(folder, "missing.txt"), false);
                using (var fallback = await Task.Run(() =>
                {
                    int com = AddressShellIcons.InitializeWorker();
                    try { return AddressShellIcons.Load(missing); }
                    finally { if (com >= 0) AddressShellIcons.UninitializeWorker(); }
                }))
                using (var expected = AddressTestShellIcon(".txt", false, true))
                    Check(fallback is not null && expected is not null && SameAddressIcon(fallback, expected),
                        "missing file falls back to its Shell extension association");

                candidates.MoveSelection(1);
                Check(candidates.SelectedHit == hits[1], "keyboard movement selects the expected result");
                SearchHit? chosen = null;
                candidates.Chosen += hit => chosen = hit;
                var rows = candidates.Controls.OfType<ListBox>().Single();
                int y = rows.ItemHeight * 2 + rows.ItemHeight / 2;
                SendMessage(rows.Handle, 0x0201, 1, (nint)((y << 16) | 10));
                SendMessage(rows.Handle, 0x0202, 0, (nint)((y << 16) | 10));
                Check(chosen == hits[2], "native click chooses the row under the pointer");
                Check(rows.AccessibleName == "Pane 1 search results" && rows.DisplayMember == nameof(SearchHit.FullPath),
                    "accessible result names remain full paths");
                using var originalFont = (Font)address.Font.Clone();
                foreach (string theme in new[] { "light", "dark" })
                foreach (int factor in new[] { 100, 150, 200 })
                {
                    Theme.Set(theme);
                    candidates.RefreshTheme();
                    using var scaled = new Font(originalFont.FontFamily, originalFont.SizeInPoints * factor / 100f);
                    address.Font = scaled;
                    candidates.Present(address, hits);
                    if (factor == 100 && Environment.GetEnvironmentVariable("FOLDERSPLITTER_QA_LOG_DIR") is { Length: > 0 } output)
                    {
                        foreach (SearchHit hit in hits) await WaitForAddressIcon(candidates, hit);
                        Directory.CreateDirectory(output);
                        using var shot = new Bitmap(candidates.Width, candidates.Height);
                        candidates.DrawToBitmap(shot, candidates.ClientRectangle);
                        shot.Save(Path.Combine(output, $"address-icons-{theme}.png"));
                    }
                    Check(rows.ItemHeight >= address.Font.Height * 2 + 10 * candidates.DeviceDpi / 96 &&
                        rows.Width > 39 * candidates.DeviceDpi / 96 && rows.BackColor == Theme.Surface,
                        $"{theme} {factor}% DPI-equivalent font fits both text lines and icon gutter");
                    Check(candidates.SelectedHit == hits[0], "theme and sizing preserve selection behavior");
                }
                address.Font = originalFont;
                candidates.Dispose();
                using (var afterDispose = candidates.CopyIcon(hits[0]))
                    Check(afterDispose is null, "disposing the dropdown releases its icon cache");
            }
            catch (Exception ex) { failures++; Console.Error.WriteLine("FAIL " + ex); }
            finally { release.Set(); Theme.Set("light"); owner.Close(); }
        };
        Application.Run(owner);
    }

    private static async Task WaitForAddressIcon(AddressCandidates candidates, SearchHit hit)
    {
        var deadline = DateTime.UtcNow.AddSeconds(20);
        while (DateTime.UtcNow < deadline)
        {
            using var icon = candidates.CopyIcon(hit);
            if (icon is not null) return;
            await Task.Delay(25);
        }
        throw new TimeoutException("Shell icon did not arrive for " + hit.FullPath);
    }

    private static bool SameAddressIcon(Bitmap a, Bitmap b)
    {
        if (a.Size != b.Size || a.Width <= 0 || a.Height <= 0) return false;
        for (int y = 0; y < a.Height; y++)
        for (int x = 0; x < a.Width; x++)
            if (a.GetPixel(x, y).ToArgb() != b.GetPixel(x, y).ToArgb()) return false;
        return true;
    }

    private static Bitmap? AddressTestShellIcon(string path, bool directory, bool association)
    {
        var info = new AddressTestShellInfo();
        nint result = AddressTestSHGetFileInfo(path, directory ? 0x10u : 0x80u, ref info,
            (uint)Marshal.SizeOf<AddressTestShellInfo>(), 0x101u | (association ? 0x10u : 0u));
        try
        {
            if (result == 0 || info.Icon == 0) return null;
            using var icon = Icon.FromHandle(info.Icon);
            return icon.ToBitmap();
        }
        finally { if (info.Icon != 0) AddressTestDestroyIcon(info.Icon); }
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct AddressTestShellInfo
    {
        public nint Icon;
        public int IconIndex;
        public uint Attributes;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string DisplayName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)] public string TypeName;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, EntryPoint = "SHGetFileInfo")]
    private static extern nint AddressTestSHGetFileInfo(string path, uint attributes, ref AddressTestShellInfo info, uint size, uint flags);
    [DllImport("user32.dll", EntryPoint = "DestroyIcon")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AddressTestDestroyIcon(nint icon);
}
