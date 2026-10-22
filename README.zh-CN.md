<img src="src/FolderSplitter/Assets/app.png" width="88" alt="Folder Splitter icon">

# Folder Splitter

[한국어](README.md) · [English](README.en.md) · **简体中文** · [日本語](README.ja.md)

一款 Windows 文件管理器，可在一个窗口中**同时查看最多 4 个文件夹，每个区域都有各自独立的标签页**。

文件列表使用 Windows 原生组件（Shell `IExplorerBrowser`）。它不会修改或替换现有的 `explorer.exe`，而是作为独立应用运行。

## 地址栏就是搜索框

无需记住完整路径。在地址栏输入**记得的任意名称片段**，匹配的文件夹和文件会随输入立即显示。用 `/` 连接文件夹名和文件名片段，例如 `공유용/월별연구계획`，即可缩小范围。

![在地址栏输入“공유용/월별연구계획”后立即显示文件夹和文件候选](docs/images/folder-splitter-search-v0.7.2.png)

- 选择**文件夹**会进入该文件夹；选择**文件**会打开其所在文件夹并选中该文件。
- 使用上下键和 Enter，或直接点击选择。靠近当前文件夹、已打开标签页、收藏夹和最近关闭标签页的结果优先显示。
- 应用自行为本地驱动器建立索引，无需安装 Everything 等工具，也不会读取文件内容。

### 搜索速度

**约 372 万条路径，p95 为 62ms。** 整体中位数和 p95 均快于 Everything 适配器；对于很短且匹配很多的查询，Everything 更快。

| 响应时间 — 越低越快 | 独立引擎 | 参照 Everything 适配器 |
|---|---:|---:|
| 常见响应 · 中位数（p50） | **21.1ms** | 48.4ms |
| 95% 请求完成的阈值（p95） | **62.1ms** | 164.6ms |
| 韩文部分路径（p95） | **40.3ms** | 141.5ms |
| 简短且匹配很多的查询（p95） | 68.8ms | **7.1ms** |

测量日期为 2026-10-21：约 372 万条共同候选，相同排序和最多 100 条结果，37 个查询、每个引擎测量 370 次。不同查询各有优劣；这并非只比较 Everything 内部匹配器的时间。首次建立索引约需 **50 秒**，与上表预热后的搜索延迟分开计算。[测量方法与完整结果](docs/search-evidence/benchmark.md)

## 界面预览

![Folder Splitter 的四分屏界面与各区域的独立标签页](docs/images/folder-splitter-overview-v0.7.2.png)

这是在实际应用中打开示例文件夹的画面。每个区域都有独立的标签页和地址栏。

## 运行

[下载最新的 Windows 可执行文件](https://github.com/HSoo-Kim/folder-splitter/releases/latest)

适用于 Windows 11 x64。解压发布页中的 `folder-splitter-win-x64.zip`，然后运行 `FolderSplitter.exe`。发布的可执行文件已内置 .NET 运行时，无需另行安装运行时。

早期版本没有代码签名。Windows 可能会提示无法验证所下载应用的发布者。

## 功能

### 分屏与标签页
- **1~4 分屏**：用顶部按钮或 `Ctrl+1`~`Ctrl+4` 切换。三分屏还可选择左右或上下 2:1 布局。
- **调整大小**：拖动分隔线调整，用均分按钮恢复。
- **各区域独立标签页**：每个区域单独使用标签页，支持新建、复制和恢复已关闭的标签页。
- **拖动标签页放置**：放到文件列表边缘即向该方向分屏，放到中央则移入该区域。空白区域同样可以放入。
- **空白区域**：新分出的区域为空。输入地址或点击左侧快捷方式即可打开文件夹。

### 导航与搜索
- **地址栏搜索**：只需输入名称的一部分，即可在所有本地驱动器中立即找到。[详情](#地址栏就是搜索框)
- **移动**：路径输入与自动补全，后退/前进/上级文件夹，以及鼠标侧键。
- **浏览文件夹**：点击地址栏旁的文件夹图标打开文件夹选择窗口。

### 侧边栏
- **快速访问**：用户文件夹、桌面、下载、文档、此电脑、回收站。点击后在活动区域（顶部蓝线）中打开。
- **收藏夹**：用 `Ctrl+D` 添加，右键移除。首次运行时导入资源管理器固定的文件夹，可在管理菜单中重新导入、全部移除或恢复。不影响资源管理器本身。
- **文件夹树**：跟随活动区域的位置。单击在当前区域打开，中键或 Ctrl+单击在新标签页打开，右键可在新区域或资源管理器中打开。`Ctrl+Shift+E` 折叠。
- **大小**：拖动分隔线调整侧边栏宽度和收藏夹/文件夹树高度，用侧边栏按钮隐藏。

### 文件操作
- **Windows 原生操作**：右键菜单、复制/剪切/粘贴、重命名、删除以及跨区域拖放均由 Windows 照常处理。
- **视图**：在区域选项中选择详细信息、列表或图标，或用 Windows 文件资源管理器打开同一文件夹。
- **项目数**：各区域底部显示当前项目数，并立即更新。

### 外观与设置
- **浅色/深色主题**：在右上角切换，文件列表也会随之变化。
- **语言**：한국어、English、简体中文、日本語。
- **恢复工作状态**：标签页、收藏夹、分屏比例、窗口大小、主题和侧边栏状态会延续到下次启动。
- **版本与新版本提示**：左下角显示当前版本，有新版本时在旁边显示下载链接。不会自动安装。

文件列表是 Windows 原生列表，列排序和右键菜单由 Windows Shell 处理。本应用不嵌入 Windows 11 资源管理器的命令栏和标签页界面本身。

## 快速调出

| 情况 | 行为 |
|---|---|
| `Win+Alt+E` | 在任何程序中将窗口调到前台；再按一次隐藏到通知区域。 |
| 关闭窗口（X） | 不退出，而是保留在通知区域（隐藏的图标）中。首次会显示一次提示。 |
| `Win+E` | 仅在“设置”中开启时打开 Folder Splitter，默认打开文件资源管理器。 |
| 再次启动 | 不打开新窗口，而是调出现有窗口。 |

- **完全退出**：右键单击通知区域图标 → “退出”。
- **“设置”中可更改**：Win+Alt+E、关闭后保留在通知区域、替代 Win+E、检查新版本。
- 关闭通知区域选项后，X 会直接退出，再按 Win+Alt+E 则改为最小化。
- Win+E 设置只会在当前用户注册表（HKCU）中创建和删除一项，无需管理员权限。

## 使用方法

1. 在顶部选择分屏数量。
2. 在各区域地址栏中输入路径或名称的一部分，然后按 Enter。
3. 用 `+` 添加标签页，并将标签页拖到想要的区域。
4. 在文件列表中使用与 Windows 相同的快捷键。

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
| 鼠标后退/前进键 | 在指针所在区域后退/前进 |
| `Ctrl+D` | 收藏当前文件夹 |
| `Ctrl+Shift+E` | 折叠/展开文件夹树 |
| `Win+Alt+E` | 调出 Folder Splitter / 再按一次隐藏 |
| `F5` | 刷新 |
| `Ctrl+C`、`Ctrl+X`、`Ctrl+V` | 复制/剪切/粘贴所选文件 |
| `F2` | 重命名所选文件 |
| `Delete` | 删除所选项目：遵循 Windows Shell 策略 |
| `Ctrl+Shift+N` | 新建文件夹 |
| `F1` | 快捷键帮助 |

## 数据与兼容性

- 新版本检查：启动时以及运行期间每天一次，仅从 GitHub（`api.github.com`）读取最新发布的版本号。不会发送任何文件或用户信息，除此之外没有其他网络连接。可在“设置”→“检查新版本”中关闭。
- 设置文件位置：`%LOCALAPPDATA%\QuadExplorer\session.json`
- 为保留旧版 QuadExplorer 的标签页和收藏夹，设置路径保持不变。
- 使用同一设置文件的应用一次只能运行一个。
- 每个区域最多 32 个标签页，收藏夹最多 100 个。
- 对于已断开的驱动器或已不存在的文件夹，会显示错误，并可通过地址栏选择其他文件夹。
- 文件删除、覆盖和冲突处理都交由 Windows Shell 完成。应用本身不会递归删除用户文件。
- OneDrive/Dropbox 状态显示、第三方 Shell 扩展和网络共享，会受相应提供方的安装状态和权限影响。
- 在后台索引已就绪的本地固定驱动器中的文件和文件夹名称及路径，不读取文件内容。首次运行显示索引准备状态；再次运行先使用已保存的索引，同时核对变化。
- 缓存：`%LOCALAPPDATA%\FolderSplitter\Search\paths.v1.index`。指定 `--session` 时使用设置文件旁的 `.search.index`。不索引无权访问的路径以及联接和符号链接内部。网络和可移动驱动器仍可通过直接路径浏览。
- 不包含替换默认资源管理器、导入现有资源管理器标签页等功能。参见[性能测量与限制](docs/search-evidence/benchmark.md)。

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

UI 集成测试在 `tools/PrivateDesktopQa` 创建的独立私有 Windows 桌面上运行，不会在正在使用的屏幕上打开窗口，也不会操作鼠标和键盘。需要已登录的 Windows 会话。测试只使用临时文件夹，并等待导航完成事件，不依赖固定时长的延时。

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
  Theme.cs              浅色/深色主题与圆角通用控件
  FolderTree.cs         左侧文件夹树
  ShellIconCache.cs     快速访问的 Windows 图标
  Assets/               应用图标源 PNG 与多分辨率 ICO
tests/
  QuadExplorer.Tests/   设置模型回归测试
  QuadExplorer.UiTests/ 真实 Shell 视图集成测试
tools/
  PrivateDesktopQa/     私有桌面 UI 测试启动器
```

应用代码不依赖任何外部 NuGet 包（仅 UI 测试使用 Lib.Harmony），只使用 Windows Shell 和 .NET Windows Forms。

替换图标源文件后，用以下命令重新生成 Windows 用的 ICO。

```powershell
.\tools\Build-Icon.ps1 -SourcePng .\src\FolderSplitter\Assets\app.png
```

## 许可证

采用 [MIT 许可证](LICENSE)。个人和公司均可免费使用、修改和分发；分发时请保留版权和许可声明。
