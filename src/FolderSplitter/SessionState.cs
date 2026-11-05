using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Security;
using System.Text.Json;

namespace QuadExplorer;

public sealed record PaneState
{
    public List<string> Tabs { get; init; } = [];
    public int SelectedTab { get; init; }
    public List<TabGroupState> Groups { get; init; } = [];
}

// Start and Count refer to slots in the saved pane's Tabs list.
public sealed record TabGroupState
{
    public string Name { get; init; } = "";
    public string Color { get; init; } = "Grey";
    public bool Collapsed { get; init; }
    public int Start { get; init; }
    public int Count { get; init; }
}

// The browser-local portion of a session; shared preferences remain on SessionState.
public sealed record BrowserWindowState
{
    public int Layout { get; init; } = 1;
    public PaneLayout? LayoutTree { get; init; }
    public List<PaneState> Panes { get; init; } = [];
    public int ActivePane { get; init; }
    public int? WindowX { get; init; }
    public int? WindowY { get; init; }
    public int WindowWidth { get; init; } = 1400;
    public int WindowHeight { get; init; } = 900;
    public bool Maximized { get; init; }
    public bool Hidden { get; init; }
    public bool Minimized { get; init; }
    public bool ShowFolderTree { get; init; } = true;
    public int FolderTreeHeight { get; init; }
    public bool ShowPreview { get; init; }
    public bool ShowDetails { get; init; }
    public int PreviewWidth { get; init; }
    public bool ShowSidebar { get; init; } = true;
    public int SidebarWidth { get; init; } = 220;
    public bool FavoritesExpanded { get; init; } = true;
    public double ColumnRatio { get; init; } = .5;
    public double LeftRowRatio { get; init; } = .5;
    public double RightRowRatio { get; init; } = .5;
}

// A named set of panes and tabs the user can switch back to.
public sealed record Workspace
{
    public string Name { get; init; } = "";
    public PaneLayout? LayoutTree { get; init; }
    public List<PaneState> Panes { get; init; } = [];
    public int ActivePane { get; init; }
}

public sealed record CustomColorPair
{
    public string? Background { get; init; }
    public string? Accent { get; init; }
}

public sealed record SessionState
{
    public string Language { get; init; } = "ko";
    public string ColorTheme { get; init; } = "light";
    public string? CustomAccent { get; init; }
    public string? CustomBackground { get; init; }
    public List<CustomColorPair> RecentCustomColors { get; init; } = [];
    public List<string> PickerColors { get; init; } = [];
    // Application-wide active pane outline; default follows the selected palette.
    public string ActivePaneColor { get; init; } = "default";
    // Null keeps the standard font; TextSize is small, normal or large.
    public string? FontFamily { get; init; }
    public string TextSize { get; init; } = "normal";
    public bool ShowFolderTree { get; init; } = true;
    public bool ShowCommandBar { get; init; } = true;
    // Missing in older sessions: retain the existing visible global toolbar.
    public bool GlobalToolbarPinned { get; init; } = true;
    public bool ShowVerticalSeparators { get; init; }
    public bool ComfortableRows { get; init; } = true;
    public bool HighlightSortedColumn { get; init; }
    public int FolderTreeHeight { get; init; }
    public bool GlobalHotkey { get; init; } = true;
    public bool StartWithWindows { get; init; } = true;
    // Replaces 0.7.5's CloseToTray (stored off by default) so everyone starts with the new default.
    public bool KeepInTray { get; init; } = true;
    public bool TrayNoticeShown { get; init; }
    public bool CheckUpdates { get; init; } = true;
    public bool AutoUpdate { get; init; } = true;
    public bool ShowPreview { get; init; }
    public bool ShowDetails { get; init; }
    public int PreviewWidth { get; init; }
    public int Layout { get; init; } = 1;
    public PaneLayout? LayoutTree { get; init; }
    public List<PaneState> Panes { get; init; } = [];
    public List<string> Favorites { get; init; } = [];
    public List<string> DeletedFavorites { get; init; } = [];
    public bool ExplorerFavoritesImported { get; init; }
    public int ActivePane { get; init; }
    public int? WindowX { get; init; }
    public int? WindowY { get; init; }
    public int WindowWidth { get; init; } = 1400;
    public int WindowHeight { get; init; } = 900;
    public bool Maximized { get; init; }
    public bool Hidden { get; init; }
    public bool Minimized { get; init; }
    public bool ShowSidebar { get; init; } = true;
    public int SidebarWidth { get; init; } = 220;
    public bool FavoritesExpanded { get; init; } = true;
    public double ColumnRatio { get; init; } = .5;
    public double LeftRowRatio { get; init; } = .5;
    public double RightRowRatio { get; init; } = .5;
    public List<BrowserWindowState> Windows { get; init; } = [];
    public int LastActiveWindow { get; init; }
    public List<Workspace> Workspaces { get; init; } = [];
    // Folders opened often, used to rank address-bar search results; older versions ignore this entry.
    public List<FolderVisit> FrequentFolders { get; init; } = [];
    // Restart quietly once private memory grew this many MB since startup; 0 turns it off.
    public int AutoRestartMemory { get; init; } = 100;
}

public static class SessionStore
{
    public const string CustomTheme = "custom";
    public const int RecentCustomColorLimit = 6;
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public static string DefaultPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "QuadExplorer", "session.json");

    public static SessionState Load(string path) => Load(path, out _);

    public static SessionState Load(string path, out string? warning)
    {
        warning = null;
        try
        {
            var state = JsonSerializer.Deserialize<SessionState>(File.ReadAllText(path), JsonOptions);
            if (state is null)
            {
                warning = "Session file contained no session data. Default session loaded.";
                return Normalize(new SessionState());
            }

            // Keep the exact pre-migration file once, without changing the source on load.
            if (state.Windows is null || state.Windows.Count == 0)
            {
                try
                {
                    var backupPath = path + ".pre-multiwindow.bak";
                    if (!File.Exists(backupPath))
                        File.Copy(path, backupPath, overwrite: false);
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or SecurityException)
                {
                    warning = $"Session loaded, but could not back up the legacy session: {exception.Message}";
                }
            }

            return Normalize(state);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException
            or SecurityException or JsonException)
        {
            warning = $"Could not load session: {exception.Message}";
            return Normalize(new SessionState());
        }
    }

    public static void Save(string path, SessionState state)
    {
        var fullPath = Path.GetFullPath(path);
        var directory = Path.GetDirectoryName(fullPath)!;
        Directory.CreateDirectory(directory);
        var temporaryPath = Path.Combine(directory, $".{Path.GetFileName(fullPath)}.{Guid.NewGuid():N}.tmp");

        try
        {
            using (var stream = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                JsonSerializer.Serialize(stream, Normalize(state), JsonOptions);
                stream.Flush(flushToDisk: true);
            }

            File.Move(temporaryPath, fullPath, overwrite: true);
        }
        finally
        {
            File.Delete(temporaryPath);
        }
    }

    public static BrowserWindowState LegacyWindow(SessionState state) => new()
    {
        Layout = state.Layout, LayoutTree = state.LayoutTree, Panes = state.Panes,
        ActivePane = state.ActivePane, WindowX = state.WindowX, WindowY = state.WindowY,
        WindowWidth = state.WindowWidth, WindowHeight = state.WindowHeight,
        Maximized = state.Maximized, Hidden = state.Hidden, Minimized = state.Minimized,
        ShowFolderTree = state.ShowFolderTree, FolderTreeHeight = state.FolderTreeHeight,
        ShowPreview = state.ShowPreview, ShowDetails = state.ShowDetails, PreviewWidth = state.PreviewWidth,
        ShowSidebar = state.ShowSidebar, SidebarWidth = state.SidebarWidth, FavoritesExpanded = state.FavoritesExpanded,
        ColumnRatio = state.ColumnRatio, LeftRowRatio = state.LeftRowRatio, RightRowRatio = state.RightRowRatio
    };

    // Only projects legacy fields: the window list and shared settings are left untouched.
    public static SessionState ProjectWindow(SessionState state, BrowserWindowState window) => state with
    {
        Layout = window.Layout, LayoutTree = window.LayoutTree, Panes = window.Panes,
        ActivePane = window.ActivePane, WindowX = window.WindowX, WindowY = window.WindowY,
        WindowWidth = window.WindowWidth, WindowHeight = window.WindowHeight,
        Maximized = window.Maximized, Hidden = window.Hidden, Minimized = window.Minimized,
        ShowFolderTree = window.ShowFolderTree, FolderTreeHeight = window.FolderTreeHeight,
        ShowPreview = window.ShowPreview, ShowDetails = window.ShowDetails, PreviewWidth = window.PreviewWidth,
        ShowSidebar = window.ShowSidebar, SidebarWidth = window.SidebarWidth, FavoritesExpanded = window.FavoritesExpanded,
        ColumnRatio = window.ColumnRatio, LeftRowRatio = window.LeftRowRatio, RightRowRatio = window.RightRowRatio
    };

    public static BrowserWindowState NormalizeBrowserWindow(BrowserWindowState window)
    {
        window ??= new BrowserWindowState();
        var defaultTab = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (string.IsNullOrWhiteSpace(defaultTab))
            defaultTab = Environment.CurrentDirectory;

        var panes = new List<PaneState>(4);
        for (var index = 0; index < 4; index++)
        {
            var pane = window.Panes is not null && index < window.Panes.Count ? window.Panes[index] : null;
            var tabs = new List<string>();
            List<int>? savedTabSlots = pane?.Groups is { Count: > 0 } ? [] : null;
            if (pane?.Tabs is not null)
            {
                for (int slot = 0; slot < pane.Tabs.Count; slot++)
                {
                    string? tab = pane.Tabs[slot];
                    if (string.IsNullOrWhiteSpace(tab))
                        continue;
                    savedTabSlots?.Add(slot);
                    tabs.Add(tab);
                    if (tabs.Count == 32)
                        break;
                }
            }

            if (pane is null && index == 0)
                tabs.Add(defaultTab);
            panes.Add(new PaneState
            {
                Tabs = tabs,
                SelectedTab = tabs.Count == 0 ? -1 : Math.Clamp(pane?.SelectedTab ?? 0, 0, tabs.Count - 1),
                Groups = NormalizeTabGroups(pane?.Groups, pane?.Tabs?.Count ?? 0, savedTabSlots)
            });
        }

        var layout = window.Layout is >= 1 and <= 4 ? window.Layout : 4;
        bool hasTree = PaneLayout.TryNormalize(window.LayoutTree, out var layoutTree);
        if (!hasTree)
        {
            layoutTree = PaneLayout.Preset(layout);
            if (!layoutTree.IsLeaf)
            {
                layoutTree = layoutTree with
                {
                    Ratio = NormalizeRatio(window.ColumnRatio),
                    First = layoutTree.First!.IsLeaf ? layoutTree.First
                        : layoutTree.First! with { Ratio = NormalizeRatio(window.LeftRowRatio) },
                    Second = layoutTree.Second!.IsLeaf ? layoutTree.Second
                        : layoutTree.Second! with { Ratio = NormalizeRatio(window.RightRowRatio) }
                };
            }
        }
        int[] visible = layoutTree.GetPanes();
        layout = visible.Length;
        int active = hasTree
            ? visible.Contains(window.ActivePane) ? window.ActivePane : visible[0]
            : Math.Clamp(window.ActivePane, 0, layout - 1);
        return window with
        {
            Layout = layout, LayoutTree = layoutTree, Panes = panes, ActivePane = active,
            WindowWidth = Math.Clamp(window.WindowWidth, 900, 10000),
            WindowHeight = Math.Clamp(window.WindowHeight, 600, 10000),
            SidebarWidth = Math.Clamp(window.SidebarWidth, 140, 480),
            FolderTreeHeight = window.FolderTreeHeight <= 0 ? 0 : Math.Clamp(window.FolderTreeHeight, 80, 2000),
            ColumnRatio = NormalizeRatio(window.ColumnRatio),
            LeftRowRatio = NormalizeRatio(window.LeftRowRatio),
            RightRowRatio = NormalizeRatio(window.RightRowRatio)
        };
    }

    public static SessionState Normalize(SessionState state)
    {
        state ??= new SessionState();
        var windows = new List<BrowserWindowState>();
        foreach (var window in state.Windows ?? [])
            windows.Add(NormalizeBrowserWindow(window));
        if (windows.Count == 0)
            windows.Add(NormalizeBrowserWindow(LegacyWindow(state)));
        string? accent = NormalizeColor(state.CustomAccent), background = NormalizeColor(state.CustomBackground);
        var recentCustomColors = NormalizeRecentCustomColors(state.RecentCustomColors);
        if (recentCustomColors.Count == 0 && background is not null && accent is not null)
            recentCustomColors.Add(new CustomColorPair { Background = background, Accent = accent });
        return ProjectWindow(state with
        {
            Language = NormalizeLanguage(state.Language),
            ColorTheme = string.Equals(state.ColorTheme?.Trim(), CustomTheme, StringComparison.OrdinalIgnoreCase)
                && accent is not null && background is not null ? CustomTheme : NormalizeColorTheme(state.ColorTheme),
            CustomAccent = accent, CustomBackground = background,
            RecentCustomColors = recentCustomColors,
            PickerColors = NormalizePickerColors(state.PickerColors),
            ActivePaneColor = NormalizeActivePaneColor(state.ActivePaneColor),
            FontFamily = string.IsNullOrWhiteSpace(state.FontFamily) || state.FontFamily.Trim().Length > 64 ? null : state.FontFamily.Trim(),
            TextSize = state.TextSize is "small" or "large" ? state.TextSize : "normal",
            Favorites = NormalizeFavorites(state.Favorites),
            DeletedFavorites = NormalizeFavorites(state.DeletedFavorites),
            Workspaces = NormalizeWorkspaces(state.Workspaces),
            FrequentFolders = FolderVisits.Normalize(state.FrequentFolders),
            AutoRestartMemory = state.AutoRestartMemory is 0 or 100 or 200 or 300 or 500 ? state.AutoRestartMemory : 100,
            Windows = windows,
            LastActiveWindow = Math.Clamp(state.LastActiveWindow, 0, windows.Count - 1)
        }, windows[0]);
    }

    private static List<TabGroupState> NormalizeTabGroups(
        IReadOnlyList<TabGroupState>? source, int originalTabCount, IReadOnlyList<int>? savedTabSlots)
    {
        var groups = new List<TabGroupState>();
        if (source is null || savedTabSlots is null || savedTabSlots.Count == 0)
            return groups;

        var ranges = new List<(int Start, int End)>();
        foreach (var group in source)
        {
            // Validate the original range before filtering blank slots or limiting its size.
            // Subtraction avoids overflow on malformed counts and starts.
            if (group is null || group.Start < 0 || group.Count < 1
                || group.Start >= originalTabCount || group.Count > originalTabCount - group.Start)
                continue;
            int originalEnd = group.Start + group.Count;
            bool overlaps = false;
            foreach (var range in ranges)
            {
                if (group.Start < range.End && originalEnd > range.Start)
                {
                    overlaps = true;
                    break;
                }
            }
            if (overlaps)
                continue;

            int end = group.Start + Math.Min(group.Count, 12);
            int start = -1, count = 0;
            for (int slot = 0; slot < savedTabSlots.Count; slot++)
            {
                int originalSlot = savedTabSlots[slot];
                if (originalSlot < group.Start)
                    continue;
                if (originalSlot >= end)
                    break;
                if (start < 0)
                    start = slot;
                count++;
            }
            if (count == 0)
                continue;

            string name = (group.Name ?? "").Trim();
            groups.Add(group with
            {
                Name = name.Length > 40 ? name[..40] : name,
                Color = NormalizeTabGroupColor(group.Color),
                Start = start,
                Count = count
            });
            ranges.Add((group.Start, originalEnd));
        }
        groups.Sort(static (first, second) => first.Start.CompareTo(second.Start));
        return groups;
    }

    private static string NormalizeTabGroupColor(string? color) => color?.Trim().ToLowerInvariant() switch
    {
        "blue" => "Blue", "red" => "Red", "yellow" => "Yellow", "green" => "Green",
        "pink" => "Pink", "purple" => "Purple", "cyan" => "Cyan", "orange" => "Orange",
        _ => "Grey"
    };

    public static Workspace NormalizeWorkspace(Workspace workspace)
    {
        var state = NormalizeBrowserWindow(new BrowserWindowState
        {
            LayoutTree = workspace.LayoutTree, Panes = workspace.Panes ?? [], ActivePane = workspace.ActivePane
        });
        return new Workspace
        {
            Name = (workspace.Name ?? "").Trim(), LayoutTree = state.LayoutTree, Panes = state.Panes, ActivePane = state.ActivePane
        };
    }

    private static List<Workspace> NormalizeWorkspaces(List<Workspace>? source)
    {
        var workspaces = new List<Workspace>();
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var workspace in source ?? [])
        {
            if (workspace is null || string.IsNullOrWhiteSpace(workspace.Name)) continue;
            var normalized = NormalizeWorkspace(workspace);
            if (!names.Add(normalized.Name)) continue;
            workspaces.Add(normalized);
            if (workspaces.Count == 20) break;
        }
        return workspaces;
    }

    public static string NormalizeLanguage(string? language) => language?.Trim().ToLowerInvariant() switch
    {
        "en" => "en",
        "zh-cn" => "zh-CN",
        "ja" => "ja",
        _ => "ko"
    };

    public static string NormalizeColorTheme(string? theme) =>
        ColorThemes.FirstOrDefault(name => string.Equals(theme?.Trim(), name, StringComparison.OrdinalIgnoreCase)) ?? "light";

    public static readonly string[] ColorThemes = ["light", "dark", "beige", "green", "sky", "pastel", "dim",
        "solarized", "everforest", "gruvbox", "rosepine", "latte", "everforest-dark", "nord", "mocha"];

    public static string? NormalizeColor(string? color)
    {
        string? hex = color?.Trim();
        return hex is { Length: 7 } && hex[0] == '#'
            && int.TryParse(hex.AsSpan(1), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out _)
            ? hex.ToUpperInvariant() : null;
    }

    public static Color ParseColor(string hex)
    {
        string normalized = NormalizeColor(hex) ?? throw new ArgumentException("Expected #RRGGBB.", nameof(hex));
        int rgb = int.Parse(normalized.AsSpan(1), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture);
        return Color.FromArgb(255, (rgb >> 16) & 255, (rgb >> 8) & 255, rgb & 255);
    }

    public static string FormatColor(Color color) => $"#{color.R:X2}{color.G:X2}{color.B:X2}";

    private static List<string> NormalizePickerColors(IReadOnlyList<string>? source)
    {
        var colors = new List<string>();
        foreach (string? value in source ?? [])
        {
            if (NormalizeColor(value) is not { } color) continue;
            colors.Add(color);
            if (colors.Count == 16) break;
        }
        return colors;
    }

    public static List<CustomColorPair> RememberCustomColors(
        IReadOnlyList<CustomColorPair> recent, Color background, Color accent) =>
        NormalizeRecentCustomColors(recent, new CustomColorPair
        {
            Background = FormatColor(background), Accent = FormatColor(accent)
        });

    private static List<CustomColorPair> NormalizeRecentCustomColors(
        IReadOnlyList<CustomColorPair>? source, CustomColorPair? newest = null)
    {
        var result = new List<CustomColorPair>(RecentCustomColorLimit);
        if (newest is not null)
            result.Add(newest);
        if (source is null)
            return result;
        foreach (var pair in source)
        {
            string? background = NormalizeColor(pair?.Background), accent = NormalizeColor(pair?.Accent);
            if (background is null || accent is null)
                continue;
            bool duplicate = false;
            foreach (var existing in result)
            {
                if (existing.Background == background && existing.Accent == accent)
                {
                    duplicate = true;
                    break;
                }
            }
            if (duplicate)
                continue;
            result.Add(new CustomColorPair { Background = background, Accent = accent });
            if (result.Count == RecentCustomColorLimit)
                break;
        }
        return result;
    }

    public static string NormalizeActivePaneColor(string? color) => color?.Trim().ToLowerInvariant() switch
    {
        "blue" => "blue", "green" => "green", "amber" => "amber", _ => "default"
    };

    private static List<string> NormalizeFavorites(List<string>? source)
    {
        var favorites = new List<string>();
        var seenFavorites = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (source is not null)
        {
            foreach (var favorite in source)
            {
                if (string.IsNullOrWhiteSpace(favorite) || !seenFavorites.Add(favorite))
                    continue;
                favorites.Add(favorite);
                if (favorites.Count == 100)
                    break;
            }
        }
        return favorites;
    }

    private static double NormalizeRatio(double ratio) =>
        double.IsFinite(ratio) ? Math.Clamp(ratio, .15, .85) : .5;
}
