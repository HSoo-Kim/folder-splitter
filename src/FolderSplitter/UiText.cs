using System.Globalization;

namespace QuadExplorer;

public sealed class UiText
{
    public string Language { get; private set; }
    public UiText(string language = "ko") => Language = SessionStore.NormalizeLanguage(language);
    public void SetLanguage(string language) => Language = SessionStore.NormalizeLanguage(language);

    public string T(string key, params object?[] args)
    {
        var value = Strings[key];
        string format = Language switch { "en" => key, "zh-CN" => value.Chinese, "ja" => value.Japanese, _ => value.Korean };
        return args.Length == 0 ? format : string.Format(CultureInfo.GetCultureInfo(Language), format, args);
    }

    private static readonly Dictionary<string, (string Korean, string Chinese, string Japanese)> Strings = new()
    {
        ["Sidebar"] = ("사이드바", "侧边栏", "サイドバー"),
        ["Show / hide quick access sidebar"] = ("빠른 접근 사이드바 표시 / 숨기기", "显示 / 隐藏快速访问侧边栏", "クイック アクセス サイドバーの表示 / 非表示"),
        ["QUICK ACCESS"] = ("빠른 접근", "快速访问", "クイックアクセス"),
        ["Home"] = ("홈", "主文件夹", "ホーム"),
        ["Desktop"] = ("바탕 화면", "桌面", "デスクトップ"),
        ["Downloads"] = ("다운로드", "下载", "ダウンロード"),
        ["Documents"] = ("문서", "文档", "ドキュメント"),
        ["This PC"] = ("내 PC", "此电脑", "PC"),
        ["Recycle Bin"] = ("휴지통", "回收站", "ごみ箱"),
        ["Favorites"] = ("즐겨찾기", "收藏夹", "お気に入り"),
        ["Collapse / expand favorites"] = ("즐겨찾기 목록 접기 / 펼치기", "折叠 / 展开收藏夹", "お気に入りの折りたたみ / 展開"),
        ["Drag to resize quick access; double-click to hide"] = ("좌우로 끌어 빠른 접근 폭 조절 — 두 번 클릭하면 숨기기", "拖动调整快速访问宽度；双击隐藏", "ドラッグでクイック アクセスの幅を調整、ダブルクリックで非表示"),
        ["Manage..."] = ("관리...", "管理...", "管理..."),
        ["Manage favorites: import, remove all, restore"] = ("즐겨찾기 관리 — 탐색기에서 가져오기, 전체 제거, 복원", "管理收藏夹：导入、全部移除、恢复", "お気に入りの管理：インポート、すべて削除、復元"),
        ["+  Pin folder"] = ("+  폴더 고정", "+  固定文件夹", "+  フォルダー固定"),
        ["Pin current folder to favorites (Ctrl+D)"] = ("현재 영역의 폴더를 즐겨찾기에 추가 (Ctrl+D)", "将当前窗格的文件夹添加到收藏夹 (Ctrl+D)", "現在のペインのフォルダーをお気に入りにピン留め (Ctrl+D)"),
        ["Open in new tab"] = ("새 탭에서 열기", "在新标签页中打开", "新しいタブで開く"),
        ["Remove favorite"] = ("즐겨찾기 제거", "移除收藏", "お気に入りから削除"),
        ["Import from File Explorer"] = ("파일 탐색기에서 가져오기", "从文件资源管理器导入", "エクスプローラーからインポート"),
        ["Remove all favorites"] = ("모든 즐겨찾기 제거", "移除所有收藏", "すべてのお気に入りを削除"),
        ["Restore favorites..."] = ("즐겨찾기 복원...", "恢复收藏...", "お気に入りを復元..."),
        ["Restore favorites"] = ("즐겨찾기 복원", "恢复收藏", "お気に入りを復元"),
        ["Restore selected"] = ("선택 항목 복원", "恢复所选项", "選択項目を復元"),
        ["Removed favorites and standard locations (including Recycle Bin). Select items to restore."] = ("제거한 즐겨찾기 및 기본 위치(휴지통 포함)입니다. 복원할 항목을 선택하세요.", "已移除的收藏和标准位置（包括回收站）。请选择要恢复的项目。", "削除したお気に入りと標準の場所 (ごみ箱を含む) です。復元する項目を選択してください。"),
        ["Imported {0} favorites from File Explorer."] = ("파일 탐색기에서 즐겨찾기 {0}개를 가져왔습니다.", "已从文件资源管理器导入 {0} 个收藏。", "エクスプローラーからお気に入りを {0} 件インポートしました。"),
        ["Already in favorites."] = ("이미 즐겨찾기에 있습니다.", "已在收藏夹中。", "すでにお気に入りにあります。"),
        ["Maximum 100 favorites."] = ("즐겨찾기는 최대 100개입니다.", "最多可添加 100 个收藏。", "お気に入りは最大 100 件です。"),
        ["Added to favorites."] = ("즐겨찾기에 추가했습니다.", "已添加到收藏夹。", "お気に入りに追加しました。"),
        ["1 pane"] = ("1 분할", "1 个窗格", "1 ペイン"),
        ["{0} panes"] = ("{0} 분할", "{0} 个窗格", "{0} ペイン"),
        ["Split into {0} panes (Ctrl+{0})"] = ("{0} 분할로 전환 (Ctrl+{0})", "分为 {0} 个窗格 (Ctrl+{0})", "{0} ペインに分割 (Ctrl+{0})"),
        ["Choose three-pane arrangement: left/right or top/bottom 2:1"] = ("3 분할 배치 선택 — 좌우 / 상하 2:1", "选择三窗格布局：左右 / 上下 2:1", "3 ペインの配置を選択：左右 / 上下 2:1"),
        ["Two left, one right"] = ("왼쪽 둘, 오른쪽 하나", "左侧两个，右侧一个", "左に 2 つ、右に 1 つ"),
        ["One left, two right"] = ("왼쪽 하나, 오른쪽 둘", "左侧一个，右侧两个", "左に 1 つ、右に 2 つ"),
        ["Two above, one below"] = ("위쪽 둘, 아래쪽 하나", "上方两个，下方一个", "上に 2 つ、下に 1 つ"),
        ["One above, two below"] = ("위쪽 하나, 아래쪽 둘", "上方一个，下方两个", "上に 1 つ、下に 2 つ"),
        ["Equal size"] = ("균등 분할", "等分大小", "均等サイズ"),
        ["Equalize pane sizes"] = ("분할 영역 크기를 균등하게 맞추기", "均等调整窗格大小", "ペインのサイズを均等にする"),
        ["Shortcuts"] = ("단축키", "快捷键", "ショートカット"),
        ["Show shortcuts and help (F1)"] = ("단축키와 사용법 보기 (F1)", "查看快捷键和帮助 (F1)", "ショートカットとヘルプを表示 (F1)"),
        ["Close tab"] = ("탭 닫기", "关闭标签页", "タブを閉じる"),
        ["Close current tab (Ctrl+W)"] = ("현재 탭 닫기 (Ctrl+W)", "关闭当前标签页 (Ctrl+W)", "現在のタブを閉じる (Ctrl+W)"),
        ["Language"] = ("언어", "语言", "言語"),
        ["Choose UI language"] = ("화면 언어 선택", "选择界面语言", "表示言語を選択"),
        ["Enter a folder path above or choose a folder from Pane options."] = ("위에 폴더 경로를 입력하거나 영역 옵션에서 폴더를 선택하세요.", "在上方输入文件夹路径，或从窗格选项中选择文件夹。", "上にフォルダーのパスを入力するか、ペイン オプションからフォルダーを選択してください。"),
        ["Enter a folder path; Enter to navigate, Ctrl+L to select address"] = ("폴더 경로 입력 — Enter로 이동, Ctrl+L로 주소창 선택", "输入文件夹路径；按 Enter 前往，按 Ctrl+L 选中地址栏", "フォルダーのパスを入力：Enter で移動、Ctrl+L でアドレス バーを選択"),
        ["Go to typed address (Enter)"] = ("입력한 주소로 이동 (Enter)", "前往输入的地址 (Enter)", "入力したアドレスに移動 (Enter)"),
        ["Back (Alt+Left)"] = ("뒤로 이동 (Alt+←)", "后退 (Alt+←)", "戻る (Alt+←)"),
        ["Forward (Alt+Right)"] = ("앞으로 이동 (Alt+→)", "前进 (Alt+→)", "進む (Alt+→)"),
        ["Up (Alt+Up)"] = ("상위 폴더로 이동 (Alt+↑)", "转到上级文件夹 (Alt+↑)", "上のフォルダーへ移動 (Alt+↑)"),
        ["Choose folder"] = ("폴더 선택", "选择文件夹", "フォルダーを選択"),
        ["Browse for a folder: open folder picker"] = ("폴더 찾아보기 — 폴더 선택 창 열기", "浏览文件夹：打开文件夹选择窗口", "フォルダーの参照：フォルダー選択ダイアログを開く"),
        ["Pane options"] = ("영역 옵션", "窗格选项", "ペイン オプション"),
        ["Pane options: view mode, refresh, favorites"] = ("영역 옵션 — 보기 방식, 새로고침, 즐겨찾기 등", "窗格选项：视图模式、刷新、收藏夹等", "ペイン オプション：表示モード、最新の情報に更新、お気に入りなど"),
        ["New tab (Ctrl+T)"] = ("새 탭 열기 (Ctrl+T)", "新建标签页 (Ctrl+T)", "新しいタブ (Ctrl+T)"),
        ["Maximum 32 tabs per pane. Close a tab first."] = ("영역당 탭은 최대 32개입니다. 먼저 탭을 닫으세요.", "每个窗格最多 32 个标签页。请先关闭一个标签页。", "タブは 1 ペインにつき最大 32 個です。先にタブを閉じてください。"),
        ["Pane {0}  |  1 tab  |  {1}"] = ("영역 {0}  |  탭 1개  |  {1}", "窗格 {0}  |  1 个标签页  |  {1}", "ペイン {0}  |  タブ 1 個  |  {1}"),
        ["Pane {0}  |  {1} tabs  |  {2}"] = ("영역 {0}  |  탭 {1}개  |  {2}", "窗格 {0}  |  {1} 个标签页  |  {2}", "ペイン {0}  |  タブ {1} 個  |  {2}"),
        ["New tab"] = ("새 탭", "新建标签页", "新しいタブ"),
        ["Duplicate tab"] = ("탭 복제", "复制标签页", "タブを複製"),
        ["Reopen closed tab"] = ("닫은 탭 다시 열기", "重新打开已关闭的标签页", "閉じたタブを再度開く"),
        ["Move tab to pane"] = ("다른 영역으로 탭 이동", "将标签页移至窗格", "タブを別のペインに移動"),
        ["Pane {0}"] = ("영역 {0}", "窗格 {0}", "ペイン {0}"),
        ["Split tab"] = ("탭 분할", "拆分标签页", "タブを分割"),
        ["Left"] = ("왼쪽", "左侧", "左"),
        ["Right"] = ("오른쪽", "右侧", "右"),
        ["Top"] = ("위쪽", "上方", "上"),
        ["Bottom"] = ("아래쪽", "下方", "下"),
        ["Close tab    Ctrl+W"] = ("탭 닫기    Ctrl+W", "关闭标签页    Ctrl+W", "タブを閉じる    Ctrl+W"),
        ["Close other tabs"] = ("다른 탭 닫기", "关闭其他标签页", "他のタブを閉じる"),
        ["Refresh    F5"] = ("새로고침    F5", "刷新    F5", "最新の情報に更新    F5"),
        ["Add to favorites    Ctrl+D"] = ("즐겨찾기에 추가    Ctrl+D", "添加到收藏夹    Ctrl+D", "お気に入りに追加    Ctrl+D"),
        ["Copy folder path"] = ("폴더 경로 복사", "复制文件夹路径", "フォルダーのパスをコピー"),
        ["Open in File Explorer"] = ("파일 탐색기에서 열기", "在文件资源管理器中打开", "エクスプローラーで開く"),
        ["Choose folder..."] = ("폴더 선택...", "选择文件夹...", "フォルダーを選択..."),
        ["Details view"] = ("자세히 보기", "详细信息视图", "詳細表示"),
        ["List view"] = ("목록 보기", "列表视图", "一覧表示"),
        ["Icons view"] = ("아이콘 보기", "图标视图", "アイコン表示"),
        ["Folder path copied."] = ("폴더 경로를 복사했습니다.", "已复制文件夹路径。", "フォルダーのパスをコピーしました。"),
        ["Drop near a file-list edge to split, or in the center to move a tab.\nEsc cancels."] = ("파일 목록 가장자리에 놓으면 분할, 가운데에 놓으면 탭 이동.\nEsc로 취소합니다.", "拖放到文件列表边缘可拆分，放在中央可移动标签页。\n按 Esc 取消。", "ファイル一覧の端にドロップすると分割、中央にドロップするとタブを移動します。\nEsc でキャンセルします。"),
        ["Split left"] = ("왼쪽으로 분할", "向左拆分", "左に分割"),
        ["Split right"] = ("오른쪽으로 분할", "向右拆分", "右に分割"),
        ["Split top"] = ("위쪽으로 분할", "向上拆分", "上に分割"),
        ["Split bottom"] = ("아래쪽으로 분할", "向下拆分", "下に分割"),
        ["Move tab to pane {0}"] = ("영역 {0}(으)로 탭 이동", "将标签页移至窗格 {0}", "タブをペイン {0} に移動"),
        ["This tab is no longer available."] = ("더 이상 사용할 수 없는 탭입니다.", "此标签页已不可用。", "このタブは使用できなくなりました。"),
        ["Drop near a file-list edge to split.\nEsc cancels."] = ("파일 목록 가장자리에 놓으면 분할합니다.\nEsc로 취소합니다.", "拖放到文件列表边缘可拆分。\n按 Esc 取消。", "ファイル一覧の端にドロップすると分割します。\nEsc でキャンセルします。"),
        ["Maximum 4 panes.\nDrop in a pane's center to move the tab instead."] = ("영역은 최대 4개입니다.\n영역 가운데에 놓으면 탭을 이동합니다.", "最多 4 个窗格。\n请放在窗格中央以移动标签页。", "ペインは最大 4 つです。\nペインの中央にドロップするとタブを移動します。"),
        ["Destination has 32 tabs.\nClose a tab first."] = ("대상 영역에 탭이 32개 있습니다.\n먼저 탭을 닫으세요.", "目标窗格已有 32 个标签页。\n请先关闭一个标签页。", "移動先のペインにはタブが 32 個あります。\n先にタブを閉じてください。"),
        ["Session could not be saved: {0}"] = ("세션을 저장하지 못했습니다: {0}", "无法保存会话：{0}", "セッションを保存できませんでした：{0}"),
        ["Your session could not be saved:\n\n{0}\n\nClose anyway?"] = ("세션을 저장하지 못했습니다:\n\n{0}\n\n그래도 닫으시겠습니까?", "无法保存会话：\n\n{0}\n\n仍要关闭吗？", "セッションを保存できませんでした：\n\n{0}\n\nこのまま閉じますか？"),
        ["Enter a folder path, not a file."] = ("파일이 아닌 폴더 경로를 입력하세요.", "请输入文件夹路径，而不是文件。", "ファイルではなくフォルダーのパスを入力してください。"),
        ["Could not open:\n{0}\n\n{1}"] = ("열지 못했습니다:\n{0}\n\n{1}", "无法打开：\n{0}\n\n{1}", "開けませんでした：\n{0}\n\n{1}"),
        ["Use the address bar to choose another folder, or retry with Go."] = ("주소창에서 다른 폴더를 선택하거나 이동 버튼으로 다시 시도하세요.", "请使用地址栏选择其他文件夹，或点击前往按钮重试。", "アドレス バーで別のフォルダーを選択するか、移動ボタンで再試行してください。"),
        ["Folder unavailable: {0}"] = ("폴더를 사용할 수 없습니다: {0}", "文件夹不可用：{0}", "フォルダーを使用できません：{0}"),
        ["Not a folder or recognized argument:\n{0}"] = ("폴더 또는 지원하는 인수가 아닙니다:\n{0}", "不是文件夹或有效参数：\n{0}", "フォルダーまたは認識できる引数ではありません：\n{0}"),
        ["Folder Splitter is already running. Use its taskbar button to switch to it."] = ("Folder Splitter가 이미 실행 중입니다. 작업 표시줄 버튼으로 전환하세요.", "Folder Splitter 已在运行。请通过任务栏按钮切换到该窗口。", "Folder Splitter はすでに実行中です。タスク バーのボタンから切り替えてください。"),
        ["Folder Splitter shortcuts"] = ("Folder Splitter 단축키", "Folder Splitter 快捷键", "Folder Splitter のショートカット"),
        ["Windows Explorer favorites could not be read."] = ("파일 탐색기의 즐겨찾기를 읽지 못했습니다.", "无法读取文件资源管理器收藏夹。", "エクスプローラーのお気に入りを読み取れませんでした。"),
        ["Legacy Explorer Links were unavailable."] = ("이전 탐색기 Links를 사용할 수 없습니다.", "旧版资源管理器 Links 不可用。", "旧エクスプローラーの Links を使用できませんでした。"),
        ["Explorer Quick Access was unavailable."] = ("탐색기 빠른 접근을 사용할 수 없습니다.", "资源管理器快速访问不可用。", "エクスプローラーのクイック アクセスを使用できませんでした。"),
        ["Some Explorer folders had no readable pin state and were skipped."] = ("일부 탐색기 폴더의 고정 상태를 읽을 수 없어 건너뛰었습니다.", "部分资源管理器文件夹的固定状态不可读，已跳过。", "一部のエクスプローラー フォルダーはピン留め状態を読み取れなかったため、スキップしました。"),
        ["Some legacy Explorer Links could not be read and were skipped."] = ("일부 이전 탐색기 Links를 읽지 못해 건너뛰었습니다.", "无法读取部分旧版资源管理器 Links，已跳过。", "一部の旧エクスプローラーの Links を読み取れなかったため、スキップしました。"),
        ["Some Explorer folders could not be read and were skipped."] = ("일부 탐색기 폴더를 읽지 못해 건너뛰었습니다.", "无法读取部分资源管理器文件夹，已跳过。", "一部のエクスプローラー フォルダーを読み取れなかったため、スキップしました。"),
        ["Legacy Explorer Links could not be read."] = ("이전 탐색기 Links를 읽지 못했습니다.", "无法读取旧版资源管理器 Links。", "旧エクスプローラーの Links を読み取れませんでした。"),
        ["Explorer Quick Access could not be read."] = ("탐색기 빠른 접근을 읽지 못했습니다.", "无法读取资源管理器快速访问。", "エクスプローラーのクイック アクセスを読み取れませんでした。"),
        ["Help"] = (
            """
            배치
            Ctrl+1 / Ctrl+2 / Ctrl+3 / Ctrl+4   영역 수
            Ctrl+Shift+3   왼쪽 하나, 오른쪽 둘
            F6   다음 영역
            3 분할 옆 화살표에서 좌우 / 상하 2:1 배치를 선택합니다.
            구분선을 끌어 크기 조절; 균등 분할로 초기화합니다.

            탭
            탭을 파일 목록 가장자리로 끌면 분할, 가운데로 끌면 이동합니다.
            파란 미리보기로 대상을 확인하며 Esc로 취소합니다.
            키보드로는 탭 우클릭 > 탭 분할을 사용하세요.
            사용하지 않는 영역은 폴더를 열 때까지 비어 있습니다.
            Ctrl+T   현재 폴더에 새 탭
            Ctrl+W / 가운데 클릭   탭 닫기
            Ctrl+Shift+T   닫은 탭 다시 열기
            Ctrl+Tab / Ctrl+Shift+Tab   탭 전환

            탐색
            Ctrl+L / Alt+D   주소창
            Alt+Left / Right / Up   뒤로, 앞으로, 상위 폴더
            F5   새로고침
            Ctrl+D   현재 폴더 고정
            폴더 선택 버튼은 폴더 선택 창을 엽니다.

            파일
            Windows 메뉴 및 영역 간 파일 끌어 놓기를 사용하세요.
            Ctrl+C / Ctrl+X / Ctrl+V   복사, 잘라내기, 붙여넣기
            F2   이름 바꾸기    |    Delete   휴지통으로
            Ctrl+Shift+N   새 폴더 만들기

            탭, 즐겨찾기, 배치 및 언어는 로컬에 저장됩니다.
            영역 옵션에서 보기 방식 및 파일 탐색기에서 열기를 사용할 수 있습니다.
            """,
            """
            布局
            Ctrl+1 / Ctrl+2 / Ctrl+3 / Ctrl+4   窗格数量
            Ctrl+Shift+3   左侧一个，右侧两个
            F6   下一个窗格
            三窗格按钮旁的箭头提供左右 / 上下 2:1 布局。
            拖动分隔线调整大小；等分大小可重置。

            标签页
            将标签页拖到文件列表边缘以拆分，拖到中央以移动。
            蓝色预览显示目标，按 Esc 取消。
            也可通过标签页右键菜单 > 拆分标签页操作。
            未使用的窗格保持空白，直到打开文件夹。
            Ctrl+T   在当前文件夹新建标签页
            Ctrl+W / 中键点击   关闭标签页
            Ctrl+Shift+T   重新打开已关闭的标签页
            Ctrl+Tab / Ctrl+Shift+Tab   切换标签页

            导航
            Ctrl+L / Alt+D   地址栏
            Alt+Left / Right / Up   后退、前进、上级文件夹
            F5   刷新
            Ctrl+D   固定当前文件夹
            选择文件夹按钮打开文件夹选择窗口。

            文件
            使用 Windows 右键菜单，在窗格之间拖放文件。
            Ctrl+C / Ctrl+X / Ctrl+V   复制、剪切、粘贴
            F2   重命名    |    Delete   移至回收站
            Ctrl+Shift+N   新建文件夹

            标签页、收藏、布局和语言保存在本地。
            窗格选项包含视图模式和在文件资源管理器中打开。
            """,
            """
            レイアウト
            Ctrl+1 / Ctrl+2 / Ctrl+3 / Ctrl+4   ペイン数
            Ctrl+Shift+3   左に 1 つ、右に 2 つ
            F6   次のペイン
            3 ペインの横の矢印から左右 / 上下 2:1 の配置を選べます。
            区切り線をドラッグしてサイズを調整し、均等サイズで元に戻します。

            タブ
            タブをファイル一覧の端にドラッグすると分割、中央にドラッグすると移動します。
            青いプレビューで移動先を確認でき、Esc でキャンセルします。
            キーボードではタブを右クリック > タブを分割 を使います。
            未使用のペインはフォルダーを開くまで空のままです。
            Ctrl+T   現在のフォルダーで新しいタブ
            Ctrl+W / 中クリック   タブを閉じる
            Ctrl+Shift+T   閉じたタブを再度開く
            Ctrl+Tab / Ctrl+Shift+Tab   タブの切り替え

            ナビゲーション
            Ctrl+L / Alt+D   アドレス バー
            Alt+Left / Right / Up   戻る、進む、上のフォルダー
            F5   最新の情報に更新
            Ctrl+D   現在のフォルダーをピン留め
            フォルダーを選択 ボタンでフォルダー選択ダイアログを開きます。

            ファイル
            Windows のコンテキスト メニューを使い、ペイン間でファイルをドラッグできます。
            Ctrl+C / Ctrl+X / Ctrl+V   コピー、切り取り、貼り付け
            F2   名前の変更    |    Delete   ごみ箱へ移動
            Ctrl+Shift+N   新しいフォルダー

            タブ、お気に入り、レイアウト、言語はローカルに保存されます。
            ペイン オプションには表示モードと エクスプローラーで開く があります。
            """)
    };

    public string Help => Language == "en" ? """
        LAYOUT
        Ctrl+1 / Ctrl+2 / Ctrl+3 / Ctrl+4   Pane count
        Ctrl+Shift+3   One left, two right
        F6   Next pane
        The arrow beside 3 panes offers left/right and top/bottom 2:1 arrangements.
        Drag dividers to resize; Equal size resets them.

        TABS
        Drag a tab to a file-list edge to split; center to move.
        A blue preview shows the destination. Esc cancels.
        Right-click tab > Split tab is the keyboard alternative.
        Unused panes remain empty until you open a folder.
        Ctrl+T   New tab at current folder
        Ctrl+W / middle-click   Close tab
        Ctrl+Shift+T   Reopen closed tab
        Ctrl+Tab / Ctrl+Shift+Tab   Switch tabs

        NAVIGATION
        Ctrl+L / Alt+D   Address bar
        Alt+Left / Right / Up   Back, forward, parent
        F5   Refresh
        Ctrl+D   Pin current folder
        Choose folder opens the folder picker.

        FILES
        Use Windows context menus and drag files between panes.
        Ctrl+C / Ctrl+X / Ctrl+V   Copy, cut, paste
        F2   Rename    |    Delete   Recycle
        Ctrl+Shift+N   New folder

        Tabs, favorites, layout and language are saved locally.
        Pane options include view modes and Open in File Explorer.
        """ : T("Help");
}
