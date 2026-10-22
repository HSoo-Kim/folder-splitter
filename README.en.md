<img src="src/FolderSplitter/Assets/app.png" width="88" alt="Folder Splitter icon">

# Folder Splitter

[한국어](README.md) · **English** · [简体中文](README.zh-CN.md) · [日本語](README.ja.md)

A **Windows file manager that shows up to 4 folders at once in a single window, with independent tabs in each pane**.

It uses the native Windows file list (Shell `IExplorerBrowser`). It doesn't modify or replace the existing `explorer.exe` and runs as a separate app.

## The address bar is a search box

You don't need the full path. Type **any fragment you remember** into the address bar and matching folders and files appear as you type. Join folder and file name fragments with `/`, such as `공유용/월별연구계획`, to narrow the results.

![Typing '공유용/월별연구계획' into the address bar shows folder and file candidates immediately](docs/images/folder-splitter-search-v0.7.2.png)

- Choose a **folder** to go there; choose a **file** to open its folder with that file selected.
- Use Up/Down and Enter, or click. Results near the current folder, open tabs, favorites and recently closed tabs come first.
- Local drives are indexed by the app itself, so no separate tool such as Everything is needed. File contents are never read.

### Search speed

**p95 of 62 ms across about 3.72 million paths.** Both the overall median and p95 beat the Everything adapter; Everything is faster for very short terms with many matches.

| Response time — lower is faster | Independent engine | Reference Everything adapter |
|---|---:|---:|
| Typical response · median (p50) | **21.1 ms** | 48.4 ms |
| 95% completion threshold (p95) | **62.1 ms** | 164.6 ms |
| Korean partial path (p95) | **40.3 ms** | 141.5 ms |
| Short terms with many matches (p95) | 68.8 ms | **7.1 ms** |

Measured on October 21, 2026: about 3.72 million common candidates, identical ranking and a 100-result limit, 37 queries and 370 measured calls per engine. Results depend on the query; this is not a comparison of Everything's intrinsic matcher alone. Initial indexing took about **50 seconds**, separate from warmed search latency above. [Method and full results](docs/search-evidence/benchmark.md)

## Screenshot

![Folder Splitter showing four panes with independent tabs](docs/images/folder-splitter-overview-v0.7.2.png)

The actual app with sample folders open. Each pane has independent tabs and an address bar.

## Running

[Download the latest Windows executable](https://github.com/HSoo-Kim/folder-splitter/releases/latest)

Built for Windows 11 x64. Extract `folder-splitter-win-x64.zip` from the release and run `FolderSplitter.exe`. The distributed executable bundles the .NET runtime, so you don't need to install a runtime separately.

Early versions aren't code-signed. Windows may warn that it can't verify the publisher of the downloaded app.

## Features

- **Current version**: The lower-left corner shows the running version, even when the sidebar is hidden.
- **Language selection**: Choose 한국어 / English / 简体中文 / 日本語 in the top bar. The selection is saved. Native file lists and folder dialogs follow your Windows language.
- **Browse for a folder**: Click the folder icon beside an address bar. Hover over navigation icons and main buttons for descriptions in the selected language.
- **1 / 2 / 3 / 4 panes**: Switch from the top bar. Hidden panes retain their tabs. The three-pane menu offers two-to-one arrangements horizontally or vertically.
- **Drag tabs to split**: Drop a tab near a file-list edge to split, or in the center to move it into that pane. Up to four panes are supported. The tab context menu also offers `Split tab`.
- **Independent tabs per pane**: New tab, duplicate tab, reopen closed tab, move a tab to another pane.
- **Separate navigation**: Address input, path autocomplete, back/forward/up to parent folder.
- **Partial-path candidates**: Type at least two characters, such as `공유용/월별연구계획_20261019`, in an address bar. Current folder, open tabs, favorites and recently closed tabs rank first. Choose with Up/Down and Enter or click; choosing a file opens its parent with that file selected. The independent index needs no Everything installation.
- **Resizable splits**: Drag the dividers, or use `Equal size` to even out the layout.
- **Quick access and favorites**: User folder, Desktop, Downloads, Documents, This PC, Recycle Bin and your pinned folders, each with its Windows icon.
- **Folder tree**: The tree at the lower left follows the active pane's folder. Click to open in the current pane; middle-click or Ctrl+click to open a new tab. Right-click to open in a new pane or in Windows File Explorer. Collapse or expand it with its `▾ Folder tree` sidebar header, the `Folder tree` toolbar button or `Ctrl+Shift+E`. Drag the divider between favorites and the folder tree up or down to resize them; the collapsed state and height are saved.
- **Light / dark theme**: Switch with the `Light mode`/`Dark mode` button at the top right, next to the language selector; the choice is saved. It covers the sidebar, tabs, address bars, buttons, menus, folder tree and the text, background and sorted column of each pane's file list.
- **Item count**: Each pane's status line shows how many items its file list displays and updates when files are added or deleted.
- **Native Windows file operations**: Right-click menu in the file list, copy/cut/paste, rename, delete, and drag and drop between panes.
- **View options**: Details, List, or Icons view from each pane's options menu.
- **Session restore**: Tab paths, selected tabs, favorites, layout and split ratios, and window size, theme, and folder tree visibility and height are saved locally.
- **Open in external Explorer**: Open the same folder in Windows File Explorer from the pane options.
- **Summon from anywhere**: `Win+Alt+E` brings Folder Splitter to the front from any app; press it again to minimize. Turn it off under `Settings` at the top right.
- **Open with Win+E (optional)**: `Settings` → `Open Folder Splitter with Win+E instead of File Explorer` makes Win+E launch Folder Splitter even when it isn't running, or bring the running window forward. It only changes your user registry; turning it off restores File Explorer.

Column sorting and the right-click menu in the file list are handled by the Windows Shell. The app doesn't embed the Windows 11 command bar or the modern tab UI of the built-in File Explorer.

## Summoning the app

| Situation | What happens |
|---|---|
| `Win+Alt+E` while the app is running | Brings Folder Splitter to the front from any app; press again to minimize. |
| Closing the window | Exits by default. With `Settings` → `Keep running in the notification area when closed`, it stays among the hidden icons at the right of the taskbar and `Win+Alt+E` keeps working. |
| `Win+E` while the app isn't running | Starts Folder Splitter only if `Settings` → `Open Folder Splitter with Win+E instead of File Explorer` is on; otherwise File Explorer opens as usual. |
| Launching it again while it runs | Brings the existing window forward instead of opening another, showing it again if it was hidden. |

Click the notification-area icon to reopen the window; right-click → `Exit` to quit completely. `Win+Alt+E` only works while the app is running, so turn on the notification-area option as well if you want it always available. The Win+E setting only creates and removes its own entry in your user registry (HKCU) and needs no administrator rights.

## Usage

1. Click `4 panes` at the top.
2. Type a folder path into each pane's address bar and press Enter.
3. Add a tab with the `+` on the right side of the pane.
4. Right-click a tab to duplicate it, move it to another pane, or close other tabs.
5. Click the file list, then use the same shortcuts as in Windows.

Shortcuts in the left sidebar open in the currently active pane. The active pane is marked with a blue line at the top. Right-click a favorite to remove it.

Drag the right edge of Quick access to resize it. `Sidebar` hides or restores the entire section; its width and visibility are saved.

Unused split panes remain blank. Enter an address or choose a shortcut to open a folder. Closing the last tab leaves the pane empty. Existing saved tabs are preserved.

Click `Favorites` to collapse its list. Windows File Explorer pinned folders are imported by default on first launch. `Manage...` offers import, remove all, and a restore list. Removal only changes this app, not Explorer. The restore list includes removed entries and standard locations such as Recycle Bin and This PC.

### Keyboard shortcuts

| Key | Action |
|---|---|
| `Ctrl+1`, `Ctrl+2`, `Ctrl+3`, `Ctrl+4` | Switch layout |
| `F6` | Move to the next pane |
| `Ctrl+T` | Open the current folder in a new tab |
| `Ctrl+W`, tab ×, middle-click a tab | Close tab |
| `Ctrl+Shift+T` | Reopen closed tab |
| `Ctrl+Tab`, `Ctrl+Shift+Tab` | Next/previous tab |
| `Ctrl+L`, `Alt+D` | Select the address bar |
| `Alt+Left`, `Alt+Right`, `Alt+Up` | Back/forward/parent folder |
| Mouse back/forward buttons | Back/forward in the pane under the pointer |
| `Ctrl+D` | Add the current folder to favorites |
| `Ctrl+Shift+E` | Collapse or expand the folder tree |
| `Win+Alt+E` | Bring Folder Splitter to the front / again to minimize |
| `F5` | Refresh |
| `Ctrl+C`, `Ctrl+X`, `Ctrl+V` | Copy/cut/paste selected files |
| `F2` | Rename selected file |
| `Delete` | Delete selected items: Windows Shell policy applies |
| `Ctrl+Shift+N` | New folder |
| `F1` | Shortcut help |

## Data and compatibility

- Settings location: `%LOCALAPPDATA%\QuadExplorer\session.json`
- The settings path is unchanged so tabs and favorites from earlier QuadExplorer versions carry over.
- Run only one app instance at a time against the same settings file.
- Limits: up to 32 tabs per pane and 100 favorites.
- For a disconnected drive or a folder that no longer exists, the app shows an error and you can pick another folder from the address bar.
- Deleting, overwriting, and conflict handling are delegated to the Windows Shell. The app itself never recursively deletes user files.
- OneDrive/Dropbox status indicators, third-party Shell extensions, and network shares depend on the provider's installation state and permissions.
- Ready local fixed drives are indexed in the background by file/folder name and path, not file contents. The first run shows index preparation; later runs use the saved snapshot while reconciling changes.
- Cache: `%LOCALAPPDATA%\FolderSplitter\Search\paths.v1.index`. An explicit `--session` uses an adjacent `.search.index` file. Inaccessible paths and junction/symlink contents are excluded. Network and removable drives retain direct-path navigation.
- Replacing File Explorer and importing its existing tabs aren't included. See the [benchmark and limitations](docs/search-evidence/benchmark.md).

You can point to a different settings path for a separate work session or for testing.

```powershell
.\FolderSplitter.exe --session C:\Work\folder-session.json C:\Projects D:\Research
```

Extra folder arguments open as new tabs in the active pane.

## Building from source

Requirements: Windows 11 x64, .NET 10 SDK.

```powershell
.\build.ps1                 # Release build + session regression tests
.\build.ps1 -UiTests        # Add integration tests against real native windows
.\build.ps1 -Publish        # Produce runtime-bundled executable and ZIP
```

The UI integration tests run on a separate private Windows desktop created by `tools/PrivateDesktopQa`, so they never open windows on the screen you're using or touch your mouse and keyboard. A signed-in Windows session is required. The tests only use temporary folders and wait for navigation-complete events. They don't rely on fixed time delays.

Output:

```text
artifacts/FolderSplitter/FolderSplitter.exe
artifacts/folder-splitter-win-x64.zip
```

## Project layout

```text
src/FolderSplitter/
  MainForm.cs           Window, splits, quick access, shortcuts
  ExplorerPane.cs       Per-pane tabs and address bar
  ShellViewHost.cs      Native Shell view lifetime management
  ShellInterop.cs       Windows SDK COM/PInvoke declarations
  SessionState.cs       Settings validation and atomic saving
  Theme.cs              Light/dark themes and rounded shared controls
  FolderTree.cs         Left folder tree
  ShellIconCache.cs     Windows icons for quick access
  Assets/               Source PNG for the app icon and multi-resolution ICO
tests/
  QuadExplorer.Tests/   Settings model regression tests
  QuadExplorer.UiTests/ Integration tests with real Shell views
tools/
  PrivateDesktopQa/     Private-desktop UI test launcher
```

The app code has no external NuGet package dependencies (only the UI tests use Lib.Harmony). It uses the Windows Shell and .NET Windows Forms.

When you replace the source icon, regenerate the Windows ICO with the command below.

```powershell
.\tools\Build-Icon.ps1 -SourcePng .\src\FolderSplitter\Assets\app.png
```

## License

[MIT License](LICENSE). Free for personal and commercial use, modification and distribution; keep the copyright and license notice in copies you distribute.
