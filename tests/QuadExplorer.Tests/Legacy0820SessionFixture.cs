using System.Text.Json;

// Provenance: git show 950e047:src/FolderSplitter/SessionState.cs, PaneLayout.cs and
// FolderVisits.cs. These are the frozen serialized records, not today's SessionState
// with selected properties removed. The concrete paths/settings below are representative
// test data in the complete 0.8.20 save format, not a captured user's session.
// Values are already normalized so the old serializer's read/resave can be exercised
// without copying unrelated legacy normalization and file-replacement implementation.
internal static class Legacy0820SessionFixture
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public const string Json = """
        {
          "Language": "en",
          "ColorTheme": "custom",
          "CustomAccent": "#6699CC",
          "CustomBackground": "#202830",
          "RecentCustomColors": [ { "Background": "#202830", "Accent": "#6699CC" } ],
          "PickerColors": [ "#6699CC", "#202830" ],
          "ActivePaneColor": "amber",
          "FontFamily": "Segoe UI",
          "TextSize": "large",
          "ShowFolderTree": false,
          "ShowCommandBar": false,
          "GlobalToolbarPinned": false,
          "ShowVerticalSeparators": true,
          "ComfortableRows": false,
          "HighlightSortedColumn": true,
          "FolderTreeHeight": 318,
          "GlobalHotkey": false,
          "StartWithWindows": false,
          "KeepInTray": true,
          "TrayNoticeShown": true,
          "CheckUpdates": false,
          "AutoUpdate": false,
          "ShowPreview": true,
          "ShowDetails": true,
          "PreviewWidth": 430,
          "Layout": 3,
          "LayoutTree": {
            "Pane": -1, "Vertical": true, "Ratio": 0.37,
            "First": { "Pane": 0, "Vertical": true, "Ratio": 0.5, "First": null, "Second": null },
            "Second": {
              "Pane": -1, "Vertical": false, "Ratio": 0.61,
              "First": { "Pane": 1, "Vertical": true, "Ratio": 0.5, "First": null, "Second": null },
              "Second": { "Pane": 3, "Vertical": true, "Ratio": 0.5, "First": null, "Second": null }
            }
          },
          "Panes": [
            { "Tabs": [ "C:\\Users\\Example\\Documents", "D:\\Projects\\release" ], "SelectedTab": 1 },
            { "Tabs": [ "\\\\fileserver\\team" ], "SelectedTab": 0 },
            { "Tabs": [], "SelectedTab": -1 },
            { "Tabs": [ "C:\\Users\\Example\\Downloads" ], "SelectedTab": 0 }
          ],
          "Favorites": [ "D:\\Projects", "\\\\fileserver\\team" ],
          "DeletedFavorites": [ "C:\\Users\\Example\\Pictures" ],
          "ExplorerFavoritesImported": true,
          "ActivePane": 3,
          "WindowWidth": 1943,
          "WindowHeight": 1129,
          "Maximized": true,
          "ShowSidebar": false,
          "SidebarWidth": 276,
          "FavoritesExpanded": false,
          "ColumnRatio": 0.37,
          "LeftRowRatio": 0.45,
          "RightRowRatio": 0.61,
          "Workspaces": [ {
            "Name": "Release review",
            "LayoutTree": { "Pane": 2, "Vertical": true, "Ratio": 0.5, "First": null, "Second": null },
            "Panes": [
              { "Tabs": [], "SelectedTab": -1 },
              { "Tabs": [], "SelectedTab": -1 },
              { "Tabs": [ "D:\\Projects\\release" ], "SelectedTab": 0 },
              { "Tabs": [], "SelectedTab": -1 }
            ],
            "ActivePane": 2
          } ],
          "FrequentFolders": [
            { "Path": "D:\\Projects\\release", "Count": 9 },
            { "Path": "C:\\Users\\Example\\Documents", "Count": 4 }
          ],
          "AutoRestartMemory": 200
        }
        """;

    // Same default unknown-property handling and indented serializer as 950e047's store.
    // Deliberately no JsonExtensionData: 0.8.20 discarded Windows and LastActiveWindow.
    public static SessionState Read(string path) =>
        JsonSerializer.Deserialize<SessionState>(File.ReadAllText(path), JsonOptions)!;

    public static void Resave(string path, SessionState state) =>
        File.WriteAllText(path, JsonSerializer.Serialize(state, JsonOptions));

    internal sealed record PaneState
    {
        public List<string> Tabs { get; init; } = [];
        public int SelectedTab { get; init; }
    }

    internal sealed record PaneLayout
    {
        public int Pane { get; init; } = -1;
        public bool Vertical { get; init; } = true;
        public double Ratio { get; init; } = .5;
        public PaneLayout? First { get; init; }
        public PaneLayout? Second { get; init; }
    }

    internal sealed record Workspace
    {
        public string Name { get; init; } = "";
        public PaneLayout? LayoutTree { get; init; }
        public List<PaneState> Panes { get; init; } = [];
        public int ActivePane { get; init; }
    }

    internal sealed record CustomColorPair
    {
        public string? Background { get; init; }
        public string? Accent { get; init; }
    }

    internal sealed record FolderVisit
    {
        public string Path { get; init; } = "";
        public int Count { get; init; }
    }

    internal sealed record SessionState
    {
        public string Language { get; init; } = "ko";
        public string ColorTheme { get; init; } = "light";
        public string? CustomAccent { get; init; }
        public string? CustomBackground { get; init; }
        public List<CustomColorPair> RecentCustomColors { get; init; } = [];
        public List<string> PickerColors { get; init; } = [];
        public string ActivePaneColor { get; init; } = "default";
        public string? FontFamily { get; init; }
        public string TextSize { get; init; } = "normal";
        public bool ShowFolderTree { get; init; } = true;
        public bool ShowCommandBar { get; init; } = true;
        public bool GlobalToolbarPinned { get; init; } = true;
        public bool ShowVerticalSeparators { get; init; }
        public bool ComfortableRows { get; init; } = true;
        public bool HighlightSortedColumn { get; init; }
        public int FolderTreeHeight { get; init; }
        public bool GlobalHotkey { get; init; } = true;
        public bool StartWithWindows { get; init; } = true;
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
        public List<FolderVisit> FrequentFolders { get; init; } = [];
        public int AutoRestartMemory { get; init; } = 100;
    }
}
