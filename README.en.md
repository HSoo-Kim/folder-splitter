<img src="src/QuadExplorer/Assets/app.png" width="88" alt="Folder Splitter icon">

# Folder Splitter

[한국어](README.md) · **English** · [简体中文](README.zh-CN.md)

A **Windows file manager that shows up to 4 folders at once in a single window, with independent tabs in each pane**.

It uses the native Windows file list (Shell `IExplorerBrowser`). It doesn't modify or replace the existing `explorer.exe` and runs as a separate app.

## Running

[Download the latest Windows executable](https://github.com/HSoo-Kim/folder-splitter/releases/latest)

Built for Windows 11 x64. Extract `folder-splitter-win-x64.zip` from the release and run `FolderSplitter.exe`. The distributed executable bundles the .NET runtime, so you don't need to install a runtime separately.

Early versions aren't code-signed. Windows may warn that it can't verify the publisher of the downloaded app.

## Features

- **1 / 2 / 4 panes**: Switch from the top bar. Tabs in panes you hide are kept.
- **Independent tabs per pane**: New tab, duplicate tab, reopen closed tab, move a tab to another pane.
- **Separate navigation**: Address input, path autocomplete, back/forward/up to parent folder.
- **Resizable splits**: Drag the dividers, or use `Equal size` to even out the layout.
- **Quick access and favorites**: User folder, Desktop, Downloads, Documents, This PC, Recycle Bin. You can pin any folder you like.
- **Native Windows file operations**: Right-click menu in the file list, copy/cut/paste, rename, delete, and drag and drop between panes.
- **View options**: Details, List, or Icons view from each pane's options menu.
- **Session restore**: Tab paths, selected tabs, favorites, layout and split ratios, and window size are saved locally.
- **Open in external Explorer**: Open the same folder in Windows File Explorer from the pane options.

Column sorting and the right-click menu in the file list are handled by the Windows Shell. The app doesn't embed the Windows 11 command bar or the modern tab UI of the built-in File Explorer.

## Usage

1. Click `4 panes` at the top.
2. Type a folder path into each pane's address bar and press Enter.
3. Add a tab with the `+` on the right side of the pane.
4. Right-click a tab to duplicate it, move it to another pane, or close other tabs.
5. Click the file list, then use the same shortcuts as in Windows.

Shortcuts in the left sidebar open in the currently active pane. The active pane is marked with a blue line at the top. Right-click a favorite to remove it.

### Keyboard shortcuts

| Key | Action |
|---|---|
| `Ctrl+1`, `Ctrl+2`, `Ctrl+4` | Switch layout |
| `F6` | Move to the next pane |
| `Ctrl+T` | Open the current folder in a new tab |
| `Ctrl+W`, tab ×, middle-click a tab | Close tab |
| `Ctrl+Shift+T` | Reopen closed tab |
| `Ctrl+Tab`, `Ctrl+Shift+Tab` | Next/previous tab |
| `Ctrl+L`, `Alt+D` | Select the address bar |
| `Alt+Left`, `Alt+Right`, `Alt+Up` | Back/forward/parent folder |
| `Ctrl+D` | Add the current folder to favorites |
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
- The current version uses a light theme. Full search indexing, replacing the default File Explorer, and importing existing File Explorer tabs aren't included.

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

Run the UI integration tests on a signed-in, interactive Windows desktop. The tests only use temporary folders and wait for navigation-complete events. They don't rely on fixed time delays.

Output:

```text
artifacts/FolderSplitter/FolderSplitter.exe
artifacts/folder-splitter-win-x64.zip
```

## Project layout

```text
src/QuadExplorer/
  MainForm.cs           Window, splits, quick access, shortcuts
  ExplorerPane.cs       Per-pane tabs and address bar
  ShellViewHost.cs      Native Shell view lifetime management
  ShellInterop.cs       Windows SDK COM/PInvoke declarations
  SessionState.cs       Settings validation and atomic saving
  Theme.cs              Light theme and shared controls
  Assets/               Source PNG for the app icon and multi-resolution ICO
tests/
  QuadExplorer.Tests/   Settings model regression tests
  QuadExplorer.UiTests/ Integration tests with real Shell views
```

The app code has no external NuGet package dependencies. It uses the Windows Shell and .NET Windows Forms.

When you replace the source icon, regenerate the Windows ICO with the command below.

```powershell
.\tools\Build-Icon.ps1 -SourcePng .\src\QuadExplorer\Assets\app.png
```
