<img src="src/FolderSplitter/Assets/app.png" width="88" alt="Folder Splitter icon">

# Folder Splitter

[한국어](README.md) · [English](README.en.md) · **简体中文** · [日本語](README.ja.md)

一款 Windows 文件管理器，可在一个窗口中**同时查看最多 4 个文件夹，每个区域都有各自独立的标签页**。

文件列表使用 Windows 原生组件（Shell `IExplorerBrowser`）。它不会修改或替换现有的 `explorer.exe`，而是作为独立应用运行。

## 界面预览

![Folder Splitter 的四分屏界面与各区域的独立标签页](docs/images/folder-splitter-overview.png)

这是在实际应用中打开示例文件夹的画面。每个区域都有独立的标签页和地址栏。

## 运行

[下载最新的 Windows 可执行文件](https://github.com/HSoo-Kim/folder-splitter/releases/latest)

适用于 Windows 11 x64。解压发布页中的 `folder-splitter-win-x64.zip`，然后运行 `FolderSplitter.exe`。发布的可执行文件已内置 .NET 运行时，无需另行安装运行时。

早期版本没有代码签名。Windows 可能会提示无法验证所下载应用的发布者。

## 功能

- **当前版本**：窗口左下角显示正在运行的版本，隐藏侧边栏后仍然可见。
- **语言选择**：顶部可选择 한국어 / English / 简体中文 / 日本語，并保存选择。原生文件列表与文件夹选择窗口使用 Windows 的语言。
- **浏览文件夹**：点击地址栏旁的文件夹图标打开选择窗口。将鼠标停在导航图标和主要按钮上，可显示所选语言的功能说明。
- **1 / 2 / 3 / 4 分屏**：在顶部切换，被收起区域中的标签页也会保留。三分屏菜单支持左右或上下的 2:1 布局。
- **拖动标签页分屏**：拖到文件列表边缘即可分屏，拖到中央则移入该区域。最多支持四个区域。也可使用标签页右键菜单的 `Split tab`。
- **各区域独立标签页**：新建标签页、复制标签页、恢复已关闭的标签页、将标签页移到其他区域。
- **独立导航**：输入地址、路径自动补全、后退/前进/转到上级文件夹。
- **调整分屏大小**：拖动分隔线，或用 `Equal size` 均分布局。
- **快速访问与收藏夹**：用户文件夹、桌面、下载、文档、此电脑、回收站。也可以固定任意文件夹。
- **Windows 原生文件操作**：文件列表的右键菜单、复制/剪切/粘贴、重命名、删除、跨区域拖放。
- **视图选择**：在各区域的选项菜单中选择详细信息、列表或图标视图。
- **恢复工作状态**：标签页路径、选中的标签页、收藏夹、分屏方式与比例、窗口大小都保存在本地。
- **在外部资源管理器中打开**：通过区域选项，用 Windows 文件资源管理器打开同一文件夹。

文件列表的列排序和右键菜单由 Windows Shell 处理。本应用并不嵌入原生资源管理器中 Windows 11 的顶部命令栏或新版标签页界面本身。

## 使用方法

1. 点击顶部的 `4 panes`。
2. 在各区域的地址栏中输入文件夹路径并按 Enter。
3. 点击区域右侧的 `+` 添加标签页。
4. 右键单击标签页，可复制、移到其他区域或关闭其他标签页。
5. 点击文件列表后，即可使用与 Windows 相同的快捷键。

左侧的快捷方式会在当前活动区域中打开。活动区域以顶部的蓝线标示。收藏项可通过右键单击移除。

拖动快速访问右边缘即可调整宽度。`Sidebar` 可隐藏或恢复整个侧栏，宽度和显示状态会保存。

未使用的分屏保持空白。输入地址或点击快捷方式即可打开文件夹。关闭最后一个标签页后不会自动重开同一文件夹。已有会话的标签页会保留。

点击 `Favorites` 可折叠列表。首次运行默认导入 Windows 文件资源管理器固定的文件夹。`Manage...` 提供重新导入、全部移除和恢复列表。移除只影响本应用，不修改资源管理器。恢复列表包括移除的项目，以及回收站、此电脑等默认位置。

### 快捷键

| 按键 | 操作 |
|---|---|
| `Ctrl+1`、`Ctrl+2`、`Ctrl+3`、`Ctrl+4` | 切换分屏方式 |
| `F6` | 移到下一个区域 |
| `Ctrl+T` | 在新标签页中打开当前文件夹 |
| `Ctrl+W`、标签页上的 ×、中键单击标签页 | 关闭标签页 |
| `Ctrl+Shift+T` | 恢复已关闭的标签页 |
| `Ctrl+Tab`、`Ctrl+Shift+Tab` | 下一个/上一个标签页 |
| `Ctrl+L`、`Alt+D` | 选中地址栏 |
| `Alt+Left`、`Alt+Right`、`Alt+Up` | 后退/前进/上级文件夹 |
| `Ctrl+D` | 收藏当前文件夹 |
| `F5` | 刷新 |
| `Ctrl+C`、`Ctrl+X`、`Ctrl+V` | 复制/剪切/粘贴所选文件 |
| `F2` | 重命名所选文件 |
| `Delete` | 删除所选项目：遵循 Windows Shell 策略 |
| `Ctrl+Shift+N` | 新建文件夹 |
| `F1` | 快捷键帮助 |

## 数据与兼容性

- 设置文件位置：`%LOCALAPPDATA%\QuadExplorer\session.json`
- 为保留旧版 QuadExplorer 的标签页和收藏夹，设置路径保持不变。
- 使用同一设置文件的应用一次只能运行一个。
- 每个区域最多 32 个标签页，收藏夹最多 100 个。
- 对于已断开的驱动器或已不存在的文件夹，会显示错误，并可通过地址栏选择其他文件夹。
- 文件删除、覆盖和冲突处理都交由 Windows Shell 完成。应用本身不会递归删除用户文件。
- OneDrive/Dropbox 状态显示、第三方 Shell 扩展和网络共享，会受相应提供方的安装状态和权限影响。
- 当前版本仅提供浅色主题。不包含全盘搜索索引、替换默认资源管理器、导入现有资源管理器标签页等功能。

可为独立的工作会话或测试指定设置路径。

```powershell
.\FolderSplitter.exe --session C:\Work\folder-session.json C:\Projects D:\Research
```

额外传入的文件夹参数会在活动区域中以新标签页打开。

## 从源码构建

要求：Windows 11 x64、.NET 10 SDK。

```powershell
.\build.ps1                 # Release 构建 + 会话回归测试
.\build.ps1 -UiTests        # 追加真实原生窗口集成测试
.\build.ps1 -Publish        # 生成内置运行时的可执行文件和 ZIP
```

UI 集成测试需在已登录的交互式 Windows 桌面上运行。测试只使用临时文件夹，并等待导航完成事件，不依赖固定时长的延时。

输出：

```text
artifacts/FolderSplitter/FolderSplitter.exe
artifacts/folder-splitter-win-x64.zip
```

## 项目结构

```text
src/FolderSplitter/
  MainForm.cs           窗口、分屏、快速访问、快捷键
  ExplorerPane.cs       各区域的标签页与地址栏
  ShellViewHost.cs      原生 Shell 视图的生命周期管理
  ShellInterop.cs       Windows SDK COM/PInvoke 声明
  SessionState.cs       设置校验与原子化保存
  Theme.cs              浅色主题与通用控件
  Assets/               应用图标源 PNG 与多分辨率 ICO
tests/
  QuadExplorer.Tests/   设置模型回归测试
  QuadExplorer.UiTests/ 真实 Shell 视图集成测试
```

应用代码不依赖任何外部 NuGet 包，只使用 Windows Shell 和 .NET Windows Forms。

替换图标源文件后，用以下命令重新生成 Windows 用的 ICO。

```powershell
.\tools\Build-Icon.ps1 -SourcePng .\src\FolderSplitter\Assets\app.png
```
