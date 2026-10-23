using System;
using System.Collections.Generic;
using System.IO;
using System.Security;
using System.Text.Json;

namespace QuadExplorer;

public sealed record PaneState
{
    public List<string> Tabs { get; init; } = [];
    public int SelectedTab { get; init; }
}

// A named set of panes and tabs the user can switch back to.
public sealed record Workspace
{
    public string Name { get; init; } = "";
    public PaneLayout? LayoutTree { get; init; }
    public List<PaneState> Panes { get; init; } = [];
    public int ActivePane { get; init; }
}

public sealed record SessionState
{
    public string Language { get; init; } = "ko";
    public string ColorTheme { get; init; } = "light";
    // Null keeps the standard font; TextSize is small, normal or large.
    public string? FontFamily { get; init; }
    public string TextSize { get; init; } = "normal";
    public bool ShowFolderTree { get; init; } = true;
    public int FolderTreeHeight { get; init; }
    public bool GlobalHotkey { get; init; } = true;
    // Replaces 0.7.5's CloseToTray (stored off by default) so everyone starts with the new default.
    public bool KeepInTray { get; init; } = true;
    public bool TrayNoticeShown { get; init; }
    public bool CheckUpdates { get; init; } = true;
    public bool AutoUpdate { get; init; } = true;
    public bool ShowPreview { get; init; }
    public int PreviewWidth { get; init; }
    public int Layout { get; init; } = 1;
    public PaneLayout? LayoutTree { get; init; }
    public List<PaneState> Panes { get; init; } = [];
    public List<string> Favorites { get; init; } = [];
    public List<string> DeletedFavorites { get; init; } = [];
    public bool ExplorerFavoritesImported { get; init; }
    public int ActivePane { get; init; }
    public int WindowWidth { get; init; } = 1400;
    public int WindowHeight { get; init; } = 900;
    public bool Maximized { get; init; }
    public bool ShowSidebar { get; init; } = true;
    public int SidebarWidth { get; init; } = 220;
    public bool FavoritesExpanded { get; init; } = true;
    public double ColumnRatio { get; init; } = .5;
    public double LeftRowRatio { get; init; } = .5;
    public double RightRowRatio { get; init; } = .5;
    public List<Workspace> Workspaces { get; init; } = [];
    // Restart quietly once private memory grew this many MB since startup; 0 turns it off.
    public int AutoRestartMemory { get; init; } = 100;
}

public static class SessionStore
{
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

    public static SessionState Normalize(SessionState state)
    {
        state ??= new SessionState();
        var defaultTab = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (string.IsNullOrWhiteSpace(defaultTab))
            defaultTab = Environment.CurrentDirectory;

        var panes = new List<PaneState>(4);
        for (var index = 0; index < 4; index++)
        {
            var pane = state.Panes is not null && index < state.Panes.Count ? state.Panes[index] : null;
            var tabs = new List<string>();
            if (pane?.Tabs is not null)
            {
                foreach (var tab in pane.Tabs)
                {
                    if (string.IsNullOrWhiteSpace(tab))
                        continue;
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
                SelectedTab = tabs.Count == 0 ? -1 : Math.Clamp(pane?.SelectedTab ?? 0, 0, tabs.Count - 1)
            });
        }

        var layout = state.Layout is >= 1 and <= 4 ? state.Layout : 4;
        bool hasTree = PaneLayout.TryNormalize(state.LayoutTree, out var layoutTree);
        if (!hasTree)
        {
            layoutTree = PaneLayout.Preset(layout);
            if (!layoutTree.IsLeaf)
            {
                layoutTree = layoutTree with
                {
                    Ratio = NormalizeRatio(state.ColumnRatio),
                    First = layoutTree.First!.IsLeaf ? layoutTree.First
                        : layoutTree.First! with { Ratio = NormalizeRatio(state.LeftRowRatio) },
                    Second = layoutTree.Second!.IsLeaf ? layoutTree.Second
                        : layoutTree.Second! with { Ratio = NormalizeRatio(state.RightRowRatio) }
                };
            }
        }
        int[] visible = layoutTree.GetPanes();
        layout = visible.Length;
        int active = hasTree
            ? visible.Contains(state.ActivePane) ? state.ActivePane : visible[0]
            : Math.Clamp(state.ActivePane, 0, layout - 1);
        return state with
        {
            Language = NormalizeLanguage(state.Language),
            ColorTheme = NormalizeColorTheme(state.ColorTheme),
            FontFamily = string.IsNullOrWhiteSpace(state.FontFamily) || state.FontFamily.Trim().Length > 64 ? null : state.FontFamily.Trim(),
            TextSize = state.TextSize is "small" or "large" ? state.TextSize : "normal",
            Layout = layout,
            LayoutTree = layoutTree,
            Panes = panes,
            Favorites = NormalizeFavorites(state.Favorites),
            DeletedFavorites = NormalizeFavorites(state.DeletedFavorites),
            Workspaces = NormalizeWorkspaces(state.Workspaces),
            AutoRestartMemory = state.AutoRestartMemory is 0 or 100 or 200 or 300 or 500 ? state.AutoRestartMemory : 100,
            ActivePane = active,
            WindowWidth = Math.Clamp(state.WindowWidth, 900, 10000),
            WindowHeight = Math.Clamp(state.WindowHeight, 600, 10000),
            SidebarWidth = Math.Clamp(state.SidebarWidth, 140, 480),
            FolderTreeHeight = state.FolderTreeHeight <= 0 ? 0 : Math.Clamp(state.FolderTreeHeight, 80, 2000),
            ColumnRatio = NormalizeRatio(state.ColumnRatio),
            LeftRowRatio = NormalizeRatio(state.LeftRowRatio),
            RightRowRatio = NormalizeRatio(state.RightRowRatio)
        };
    }

    public static Workspace NormalizeWorkspace(Workspace workspace)
    {
        var state = Normalize(new SessionState
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

    public static readonly string[] ColorThemes = ["light", "dark", "beige", "green", "dim",
        "solarized", "everforest", "gruvbox", "rosepine", "latte", "everforest-dark", "nord", "mocha"];

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
