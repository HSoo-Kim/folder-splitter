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

### Panes and tabs
- **1 to 4 panes**: Switch from the top bar or with `Ctrl+1`–`Ctrl+4`. Three panes can also be arranged 2:1 side by side or top and bottom.
- **Resizing**: Drag the dividers; `Equal size` evens them out again.
- **Tabs per pane**: Each pane has its own tabs, with new tab, duplicate and reopen closed tab.
- **Drag tabs into place**: Drop a tab near a file-list edge to split in that direction, or in the center to move it into that pane. Empty panes accept tabs too.
- **Empty panes**: New split panes start empty. Type an address or click a sidebar shortcut to open a folder.

### Navigation and search
- **Address-bar search**: Type part of a name to find it across all local drives instantly. [Details](#the-address-bar-is-a-search-box)
- **Moving around**: Path input with autocomplete, back/forward/up, and the mouse side buttons.
- **Browse for a folder**: The folder icon beside the address bar opens a folder picker.

### Sidebar
- **Quick access**: User folder, Desktop, Downloads, Documents, This PC and Recycle Bin. Clicking opens in the active pane (blue line at the top).
- **Favorites**: Add with `Ctrl+D`, remove by right-click. File Explorer's pinned folders are imported on first launch; `Manage...` re-imports, removes all or restores. Explorer itself is not changed.
- **Folder tree**: Follows the active pane. Click opens in the current pane, middle-click or Ctrl+click in a new tab, right-click in a new pane or File Explorer. `Ctrl+Shift+E` collapses it.
- **Size**: Drag to set the sidebar width and the favorites/tree height; `Sidebar` hides it.

### Files
- **Native Windows actions**: Right-click menu, copy/cut/paste, rename, delete and drag and drop between panes are handled by Windows as usual.
- **Views**: Pick Details, List or Icons from the pane options, or open the same folder in File Explorer.
- **Item count**: Each pane shows how many items it displays, updated immediately.

### Appearance and settings
- **Light/dark theme**: Toggle at the top right; the file lists follow too.
- **Languages**: 한국어, English, 简体中文, 日本語.
- **Session restore**: Tabs, favorites, split ratios, window size, theme and sidebar state carry over to the next launch.
- **Version and update notice**: The lower left shows the current version and, when a new release is out, a download link beside it. Nothing installs automatically.

File lists are native Windows lists, so column sorting and right-click menus are handled by the Windows Shell. The Windows 11 Explorer command bar and tab UI are not embedded.

## Summoning the app

| Situation | What happens |
|---|---|
| `Win+Alt+E` | Brings the window to the front from any app; press again to hide it to the notification area. |
| Closing the window (X) | The app keeps running among the hidden icons in the notification area. A one-time notice explains this. |
| `Win+E` | Opens Folder Splitter only if turned on in `Settings`; otherwise File Explorer opens. |
| Launching it again | Brings the existing window back instead of opening another. |

- **Quit completely**: Right-click the notification-area icon → `Exit`.
- **In `Settings`**: Win+Alt+E, keeping running in the notification area, Win+E replacement and update checks.
- With the notification-area option off, X quits and a second Win+Alt+E minimizes.
- The Win+E setting only creates and removes one entry in your user registry (HKCU) and needs no administrator rights.

## Usage

1. Choose the number of panes at the top.
2. Type a path or part of a name into a pane's address bar and press Enter.
3. Add tabs with `+` and drag them into the panes you want.
4. Use the usual Windows shortcuts in the file list.

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
| `Win+Alt+E` | Bring Folder Splitter to the front / again to hide it |
| `F5` | Refresh |
| `Ctrl+C`, `Ctrl+X`, `Ctrl+V` | Copy/cut/paste selected files |
| `F2` | Rename selected file |
| `Delete` | Delete selected items: Windows Shell policy applies |
| `Ctrl+Shift+N` | New folder |
| `F1` | Shortcut help |

## Data and compatibility

- Update check: on launch and once a day while running, the app reads only the latest release version from GitHub (`api.github.com`). No file or user information is sent, and the app makes no other network connections. Turn it off under `Settings` → `Check for new versions`.
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
