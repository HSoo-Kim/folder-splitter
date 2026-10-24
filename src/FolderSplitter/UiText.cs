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
        ["Folder tree"] = ("폴더 트리", "文件夹树", "フォルダーツリー"),
        ["Show / hide folder tree (Ctrl+Shift+E)"] = ("폴더 트리 표시 / 숨기기 (Ctrl+Shift+E)", "显示 / 隐藏文件夹树 (Ctrl+Shift+E)", "フォルダーツリーを表示 / 非表示 (Ctrl+Shift+E)"),
        ["Collapse / expand folder tree (Ctrl+Shift+E)"] = ("폴더 트리 접기 / 펼치기 (Ctrl+Shift+E)", "折叠 / 展开文件夹树 (Ctrl+Shift+E)", "フォルダーツリーを折りたたむ / 展開 (Ctrl+Shift+E)"),
        ["Drag to resize favorites and the folder tree"] = ("드래그해 즐겨찾기와 폴더 트리 높이 조절", "拖动以调整收藏夹和文件夹树的高度", "ドラッグしてお気に入りとフォルダーツリーの高さを調整"),
        ["Settings"] = ("설정", "设置", "設定"),
        ["Open Folder Splitter"] = ("Folder Splitter 열기", "打开 Folder Splitter", "Folder Splitter を開く"),
        ["Exit"] = ("종료", "退出", "終了"),
        ["New version {0} available · Download"] = ("새 버전 {0} 있음 · 다운로드", "有新版本 {0} · 下载", "新しいバージョン {0} · ダウンロード"),
        ["Check for new versions"] = ("새 버전 확인", "检查新版本", "新しいバージョンを確認"),
        ["New version {0} available · Update"] = ("새 버전 {0} 있음 · 업데이트", "有新版本 {0} · 更新", "新しいバージョン {0} · 更新"),
        ["Updating to {0}…"] = ("{0}(으)로 업데이트 중…", "正在更新到 {0}…", "{0} に更新中…"),
        ["Install automatically when Update is clicked"] = ("업데이트를 누르면 자동으로 설치", "点击“更新”时自动安装", "「更新」をクリックしたら自動でインストール"),
        ["Automatic update failed: {0}"] = ("자동 업데이트 실패: {0}", "自动更新失败：{0}", "自動更新に失敗しました: {0}"),
        ["Updated. Restart Folder Splitter to finish."] = ("업데이트했습니다. Folder Splitter를 다시 실행하면 적용됩니다.", "已更新。重新启动 Folder Splitter 即可完成。", "更新しました。Folder Splitter を再起動すると反映されます。"),
        ["Folder Splitter keeps running in the notification area. Press Win+Alt+E or click its icon to reopen it; right-click the icon and choose Exit to quit. You can turn this off in Settings."] = ("Folder Splitter가 알림 영역에서 계속 실행 중입니다. Win+Alt+E나 아이콘 클릭으로 다시 열고, 완전히 끝내려면 아이콘을 우클릭해 종료를 누르세요. 설정에서 끌 수 있습니다.", "Folder Splitter 仍在通知区域中运行。按 Win+Alt+E 或单击图标可重新打开；右键单击图标并选择“退出”可完全退出。可在设置中关闭此功能。", "Folder Splitter は通知領域で実行を続けています。Win+Alt+E またはアイコンのクリックで再び開き、完全に終了するにはアイコンを右クリックして「終了」を選んでください。設定でオフにできます。"),
        ["Keep running in the notification area when closed"] = ("닫아도 알림 영역(숨겨진 아이콘)에서 계속 실행", "关闭后在通知区域（隐藏的图标）中继续运行", "閉じても通知領域（隠れているインジケーター）で実行を続ける"),
        ["Shortcuts and Windows integration"] = ("단축키와 Windows 연동", "快捷键与 Windows 集成", "ショートカットと Windows 連携"),
        ["Win+Alt+E brings Folder Splitter to the front"] = ("Win+Alt+E로 Folder Splitter 불러오기", "按 Win+Alt+E 将 Folder Splitter 调到前台", "Win+Alt+E で Folder Splitter を前面に表示"),
        ["Open Folder Splitter with Win+E instead of File Explorer"] = ("Win+E로 파일 탐색기 대신 Folder Splitter 열기", "按 Win+E 时打开 Folder Splitter 而不是文件资源管理器", "Win+E でエクスプローラーの代わりに Folder Splitter を開く"),
        ["Win+Alt+E is already used by another program."] = ("Win+Alt+E를 다른 프로그램이 이미 쓰고 있습니다.", "Win+Alt+E 已被其他程序占用。", "Win+Alt+E は別のプログラムが使用しています。"),
        ["Could not change the Win+E setting: {0}"] = ("Win+E 설정을 바꾸지 못했습니다: {0}", "无法更改 Win+E 设置：{0}", "Win+E の設定を変更できませんでした: {0}"),
        ["Open in current pane"] = ("현재 영역에서 열기", "在当前窗格中打开", "現在のペインで開く"),
        ["Open in new pane"] = ("새 영역에서 열기", "在新窗格中打开", "新しいペインで開く"),
        ["Loading folders..."] = ("하위 폴더 읽는 중...", "正在读取子文件夹...", "サブフォルダーを読み込み中..."),
        ["Folder link (expand disabled)"] = ("폴더 링크 — 펼치기는 지원하지 않습니다", "文件夹链接（不展开）", "フォルダーリンク（展開しません）"),
        ["Access denied"] = ("접근 권한이 없습니다", "没有访问权限", "アクセスできません"),
        ["Folder no longer exists"] = ("폴더가 더 이상 존재하지 않습니다", "文件夹已不存在", "フォルダーは存在しません"),
        ["Cannot read folders: {0}"] = ("폴더를 읽을 수 없습니다: {0}", "无法读取文件夹：{0}", "フォルダーを読み込めません: {0}"),
        ["Maximum 4 panes. Close a pane first."] = ("최대 4영역입니다. 먼저 영역을 닫으세요.", "最多 4 个窗格，请先关闭一个窗格。", "最大4ペインです。先にペインを閉じてください。"),
        ["Dark mode"] = ("밤", "深色", "ダーク"),
        ["Light mode"] = ("낮", "浅色", "ライト"),
        ["New folder"] = ("새 폴더", "新建文件夹", "新しいフォルダー"),
        ["Cut"] = ("잘라내기", "剪切", "切り取り"),
        ["Copy"] = ("복사", "复制", "コピー"),
        ["Paste"] = ("붙여넣기", "粘贴", "貼り付け"),
        ["Rename"] = ("이름 바꾸기", "重命名", "名前の変更"),
        ["Delete"] = ("삭제", "删除", "削除"),
        ["Sort"] = ("정렬", "排序", "並べ替え"),
        ["View"] = ("보기", "查看", "表示"),
        ["Compare with the other pane"] = ("다른 영역과 비교", "与另一个窗格比较", "他のペインと比較"),
        ["Filter this folder"] = ("이 폴더에서 거르기", "筛选此文件夹", "このフォルダーを絞り込む"),
        ["Name"] = ("이름", "名称", "名前"),
        ["Date modified"] = ("수정한 날짜", "修改日期", "更新日時"),
        ["Type"] = ("유형", "类型", "種類"),
        ["Size"] = ("크기", "大小", "サイズ"),
        ["Ascending"] = ("오름차순", "递增", "昇順"),
        ["Descending"] = ("내림차순", "递减", "降順"),
        ["No grouping"] = ("그룹 없음", "不分组", "グループなし"),
        ["Group by: {0}"] = ("그룹: {0}", "分组依据：{0}", "グループ: {0}"),
        ["Extra large icons"] = ("아주 큰 아이콘", "超大图标", "特大アイコン"),
        ["Large icons"] = ("큰 아이콘", "大图标", "大アイコン"),
        ["Medium icons"] = ("보통 아이콘", "中等图标", "中アイコン"),
        ["Small icons"] = ("작은 아이콘", "小图标", "小アイコン"),
        ["List"] = ("목록", "列表", "一覧"),
        ["Details"] = ("자세히", "详细信息", "詳細"),
        ["Tiles"] = ("타일", "平铺", "並べて表示"),
        ["Content"] = ("내용", "内容", "コンテンツ"),
        ["Show hidden items"] = ("숨긴 항목 표시", "显示隐藏的项目", "隠しファイルを表示"),
        ["Show file name extensions"] = ("파일 확장명 표시", "显示文件扩展名", "ファイル名拡張子を表示"),
        ["Details pane"] = ("세부 정보 창", "详细信息窗格", "詳細ウィンドウ"),
        ["Rename {0} items"] = ("항목 {0}개 이름 바꾸기", "重命名 {0} 个项目", "{0} 個の項目の名前を変更"),
        ["Find"] = ("찾기", "查找", "検索する文字列"),
        ["Replace with"] = ("바꿀 내용", "替换为", "置換後の文字列"),
        ["Regular expression"] = ("정규식", "正则表达式", "正規表現"),
        ["Match case"] = ("대소문자 구분", "区分大小写", "大文字と小文字を区別"),
        ["New name"] = ("새 이름", "新名称", "新しい名前"),
        ["Start at"] = ("시작 번호", "起始编号", "開始番号"),
        ["Digits"] = ("자릿수", "位数", "桁数"),
        ["{0}: the name after find and replace   {1}: a running number   The extension is kept."] = ("{0}: 찾아 바꾼 이름   {1}: 차례 번호   확장자는 그대로 둡니다.", "{0}：查找替换后的名称   {1}：序号   扩展名保持不变。", "{0}: 置換後の名前   {1}: 連番   拡張子はそのままです。"),
        ["Current name"] = ("지금 이름", "当前名称", "現在の名前"),
        ["{0} problems: {1}"] = ("문제 {0}개: {1}", "{0} 个问题：{1}", "問題 {0} 件: {1}"),
        ["{0} of {1} names change"] = ("{1}개 중 {0}개 이름이 바뀝니다", "{1} 个中将有 {0} 个名称更改", "{1} 個中 {0} 個の名前が変わります"),
        ["The regular expression is not valid."] = ("정규식이 올바르지 않습니다.", "正则表达式无效。", "正規表現が正しくありません。"),
        ["A name would be empty."] = ("빈 이름이 생깁니다.", "会出现空名称。", "空の名前になります。"),
        ["A name contains characters that are not allowed."] = ("이름에 쓸 수 없는 글자가 있습니다.", "名称包含不允许的字符。", "使用できない文字が含まれています。"),
        ["Two items would get the same name."] = ("두 항목이 같은 이름이 됩니다.", "两个项目将得到相同的名称。", "2 つの項目が同じ名前になります。"),
        ["An item with that name already exists."] = ("같은 이름의 항목이 이미 있습니다.", "已存在同名项目。", "同じ名前の項目が既にあります。"),
        ["Compare panes"] = ("영역 비교", "比较窗格", "ペインの比較"),
        ["Result"] = ("결과", "结果", "結果"),
        ["Show identical items"] = ("같은 항목도 보기", "显示相同的项目", "同じ項目も表示"),
        ["Close"] = ("닫기", "关闭", "閉じる"),
        ["Select in pane {0}"] = ("영역 {0}에서 선택", "在窗格 {0} 中选择", "ペイン {0} で選択"),
        ["Comparing..."] = ("비교하는 중...", "正在比较...", "比較しています..."),
        ["Only in pane {0}"] = ("영역 {0}에만 있음", "仅在窗格 {0} 中", "ペイン {0} のみ"),
        ["Newer in pane {0}"] = ("영역 {0}이 더 새 파일", "窗格 {0} 中较新", "ペイン {0} が新しい"),
        ["Different size"] = ("크기가 다름", "大小不同", "サイズが異なる"),
        ["Folder on one side, file on the other"] = ("한쪽은 폴더, 한쪽은 파일", "一侧是文件夹，另一侧是文件", "一方がフォルダー、もう一方がファイル"),
        ["Same"] = ("같음", "相同", "同じ"),
        ["{0} of {1} items differ"] = ("{1}개 중 {0}개가 다릅니다", "{1} 个中有 {0} 个不同", "{1} 個中 {0} 個が異なります"),
        ["Folder"] = ("폴더", "文件夹", "フォルダー"),
        ["Open another pane to compare."] = ("비교하려면 다른 영역에서 폴더를 여세요.", "请在另一个窗格中打开文件夹以进行比较。", "比較するには別のペインでフォルダーを開いてください。"),
        ["Renamed {0} items."] = ("항목 {0}개의 이름을 바꿨습니다.", "已重命名 {0} 个项目。", "{0} 個の項目の名前を変更しました。"),
        ["Beige"] = ("베이지", "米色", "ベージュ"),
        ["Font and text size"] = ("글꼴과 글자 크기", "字体与文字大小", "フォントと文字サイズ"),
        ["Standard ({0})"] = ("기본 ({0})", "默认 ({0})", "標準 ({0})"),
        ["Small text"] = ("글자 작게", "小号文字", "文字を小さく"),
        ["Normal text"] = ("글자 보통", "常规文字", "文字を標準に"),
        ["Large text"] = ("글자 크게", "大号文字", "文字を大きく"),
        ["Files here: {0}"] = ("이 폴더 파일 {0}", "此文件夹中的文件 {0}", "このフォルダーのファイル {0}"),
        ["Soft green"] = ("연녹색", "浅绿色", "ライトグリーン"),
        ["Sky blue"] = ("하늘색", "天蓝色", "スカイブルー"),
        ["Pastel blue"] = ("파스텔 블루", "粉蓝色", "パステルブルー"),
        ["Soft dark"] = ("은은한 밤", "柔和深色", "ソフトダーク"),
        ["Choose a color theme"] = ("색 테마 고르기", "选择颜色主题", "カラーテーマを選択"),
        ["{0} items"] = ("항목 {0}개", "{0} 个项目", "{0} 項目"),
        ["Counting items..."] = ("항목 수 확인 중...", "正在统计项目...", "項目数を確認中..."),
        ["Building search index..."] = ("검색 색인 만드는 중...", "正在建立搜索索引...", "検索インデックスを作成中..."),
        ["Enter a folder path, or type part of a file/folder name to search; Ctrl+L to select address"] =
            ("폴더 경로로 이동하거나 파일·폴더 이름 일부로 검색 — 주소 선택 Ctrl+L",
             "输入文件夹路径，或输入部分文件/文件夹名进行搜索；Ctrl+L 选中地址",
             "フォルダーのパス、またはファイル・フォルダー名の一部で検索（Ctrl+L でアドレスを選択）"),
        ["Search failed: {0}"] = ("검색 실패: {0}", "搜索失败：{0}", "検索に失敗しました: {0}"),
        ["No longer available: {0}"] = ("더 이상 찾을 수 없습니다: {0}", "已不可用：{0}", "見つからなくなりました: {0}"),
        ["Sidebar"] = ("사이드바", "侧边栏", "サイドバー"),
        ["Show / hide quick access sidebar"] = ("빠른 접근 사이드바 표시 / 숨기기", "显示 / 隐藏快速访问侧边栏", "クイック アクセス サイドバーの表示 / 非表示"),
        ["QUICK ACCESS"] = ("빠른 접근", "快速访问", "クイックアクセス"),
        ["Home"] = ("홈", "主文件夹", "ホーム"),
        ["Desktop"] = ("바탕 화면", "桌面", "デスクトップ"),
        ["Downloads"] = ("다운로드", "下载", "ダウンロード"),
        ["Documents"] = ("문서", "文档", "ドキュメント"),
        ["This PC"] = ("내 PC", "此电脑", "PC"),
        ["Recycle Bin"] = ("휴지통", "回收站", "ごみ箱"),
        ["Recent items"] = ("최근 항목", "最近使用的项目", "最近使った項目"),
        ["Preview"] = ("미리보기", "预览", "プレビュー"),
        ["Dock preview"] = ("다시 붙이기", "重新停靠", "元に戻す"),
        ["Detach preview"] = ("분리", "独立窗口", "別ウィンドウ"),
        ["Previous page"] = ("이전 쪽", "上一页", "前のページ"),
        ["Next page"] = ("다음 쪽", "下一页", "次のページ"),
        ["Loading preview..."] = ("미리보기를 불러오는 중...", "正在加载预览...", "プレビューを読み込み中..."),
        ["Show / hide preview pane (Alt+P)"] = ("미리보기 칸 표시/숨기기 (Alt+P)", "显示/隐藏预览窗格 (Alt+P)", "プレビュー ウィンドウの表示/非表示 (Alt+P)"),
        ["Select a file to preview it."] = ("파일을 선택하면 미리보기가 표시됩니다.", "选择文件即可预览。", "ファイルを選択するとプレビューが表示されます。"),
        ["{0} items selected"] = ("{0}개 항목 선택됨", "已选择 {0} 个项目", "{0} 個の項目を選択"),
        ["Folders are not previewed."] = ("폴더는 미리보기를 표시하지 않습니다.", "文件夹不显示预览。", "フォルダーはプレビューされません。"),
        ["No preview is available for this file type."] = ("이 형식은 미리 볼 수 없습니다.", "此文件类型没有可用的预览。", "この種類のファイルはプレビューできません。"),
        ["Could not show a preview of this file."] = ("이 파일의 미리보기를 표시하지 못했습니다.", "无法显示此文件的预览。", "このファイルのプレビューを表示できませんでした。"),
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
        ["No recent locations"] = ("최근 위치가 없습니다", "没有最近的位置", "最近の場所はありません"),
        ["Back (Alt+Left); right-click for recent locations"] = ("뒤로 (Alt+Left) · 우클릭하면 최근 위치", "后退 (Alt+Left) · 右键单击显示最近的位置", "戻る (Alt+Left) · 右クリックで最近の場所"),
        ["Forward (Alt+Right); right-click for recent locations"] = ("앞으로 (Alt+Right) · 우클릭하면 최근 위치", "前进 (Alt+Right) · 右键单击显示最近的位置", "進む (Alt+Right) · 右クリックで最近の場所"),
        ["{0} selected"] = ("{0}개 선택", "已选择 {0} 个", "{0} 個選択"),
        ["{0} selected · {1}"] = ("{0}개 선택 · {1}", "已选择 {0} 个 · {1}", "{0} 個選択 · {1}"),
        ["Copy path    Ctrl+Shift+C"] = ("경로 복사    Ctrl+Shift+C", "复制路径    Ctrl+Shift+C", "パスのコピー    Ctrl+Shift+C"),
        ["Path copied: {0}"] = ("경로를 복사했습니다: {0}", "已复制路径: {0}", "パスをコピーしました: {0}"),
        ["Open terminal here"] = ("여기서 터미널 열기", "在此处打开终端", "ここでターミナルを開く"),
        ["Could not open a terminal: {0}"] = ("터미널을 열지 못했습니다: {0}", "无法打开终端: {0}", "ターミナルを開けませんでした: {0}"),
        ["Copy selection to other pane    Ctrl+Alt+C"] = ("선택 항목을 다른 영역으로 복사    Ctrl+Alt+C", "将所选项目复制到另一个区域    Ctrl+Alt+C", "選択した項目を別のペインへコピー    Ctrl+Alt+C"),
        ["Move selection to other pane    Ctrl+Alt+X"] = ("선택 항목을 다른 영역으로 이동    Ctrl+Alt+X", "将所选项目移动到另一个区域    Ctrl+Alt+X", "選択した項目を別のペインへ移動    Ctrl+Alt+X"),
        ["(No subfolders)"] = ("(하위 폴더 없음)", "(没有子文件夹)", "(サブフォルダーなし)"),
        ["(Cannot open this folder)"] = ("(이 폴더를 열 수 없음)", "(无法打开此文件夹)", "(このフォルダーを開けません)"),
        ["Open a folder in another pane first."] = ("먼저 다른 영역에 폴더를 열어 주세요.", "请先在另一个区域中打开文件夹。", "先に別のペインでフォルダーを開いてください。"),
        ["Select files or folders first."] = ("먼저 파일이나 폴더를 선택해 주세요.", "请先选择文件或文件夹。", "先にファイルまたはフォルダーを選択してください。"),
        ["Items without a file path cannot be copied or moved."] = ("파일 경로가 없는 항목은 복사하거나 이동할 수 없습니다.", "没有文件路径的项目无法复制或移动。", "ファイル パスのない項目はコピーまたは移動できません。"),
        ["The other pane shows the same folder."] = ("다른 영역도 같은 폴더를 보고 있습니다.", "另一个区域显示的是同一个文件夹。", "別のペインも同じフォルダーを表示しています。"),
        ["Could not copy or move: {0}"] = ("복사하거나 이동하지 못했습니다: {0}", "无法复制或移动: {0}", "コピーまたは移動できませんでした: {0}"),
        ["Copy or move was cancelled."] = ("복사·이동을 취소했습니다.", "已取消复制或移动。", "コピーまたは移動を取り消しました。"),
        ["Moved {0} items to pane {1}."] = ("{0}개 항목을 영역 {1}(으)로 이동했습니다.", "已将 {0} 个项目移动到区域 {1}。", "{0} 個の項目をペイン {1} へ移動しました。"),
        ["Copied {0} items to pane {1}."] = ("{0}개 항목을 영역 {1}(으)로 복사했습니다.", "已将 {0} 个项目复制到区域 {1}。", "{0} 個の項目をペイン {1} へコピーしました。"),
        ["Up to 20 workspaces can be saved."] = ("작업 구성은 20개까지 저장할 수 있습니다.", "最多可保存 20 个工作区。", "作業セットは 20 個まで保存できます。"),
        ["Workspace saved: {0}"] = ("작업 구성을 저장했습니다: {0}", "已保存工作区: {0}", "作業セットを保存しました: {0}"),
        ["Workspace opened: {0}"] = ("작업 구성을 열었습니다: {0}", "已打开工作区: {0}", "作業セットを開きました: {0}"),
        ["Save current workspace..."] = ("현재 구성 저장...", "保存当前工作区...", "現在の作業セットを保存..."),
        ["Delete workspace"] = ("작업 구성 삭제", "删除工作区", "作業セットを削除"),
        ["Workspace name"] = ("작업 구성 이름", "工作区名称", "作業セットの名前"),
        ["Workspace {0}"] = ("작업 구성 {0}", "工作区 {0}", "作業セット {0}"),
        ["Save"] = ("저장", "保存", "保存"),
        ["Cancel"] = ("취소", "取消", "キャンセル"),
        ["Workspaces"] = ("작업 구성", "工作区", "作業セット"),
        ["Save or open a named set of panes and tabs"] = ("분할과 탭 배치를 이름 붙여 저장하거나 불러오기", "以名称保存或打开分屏与标签页布局", "分割とタブの配置に名前を付けて保存・呼び出し"),
        ["Pin"] = ("고정", "固定", "固定"),
        ["Unpin"] = ("고정 해제", "取消固定", "固定を解除"),
        ["New window"] = ("새 창", "新窗口", "新しいウィンドウ"),
        ["Up to 8 preview windows can be open."] = ("미리보기 창은 8개까지 열 수 있습니다.", "最多可打开 8 个预览窗口。", "プレビュー ウィンドウは 8 個まで開けます。"),
        ["Memory cleanup"] = ("메모리 정리", "内存清理", "メモリの整理"),
        ["Clean up memory now (restart)"] = ("지금 메모리 정리 (다시 시작)", "立即清理内存（重新启动）", "今すぐメモリを整理 (再起動)"),
        ["Restart automatically when hidden or minimized"] = ("숨기거나 최소화했을 때 자동 다시 시작", "隐藏或最小化时自动重新启动", "非表示または最小化中に自動で再起動"),
        ["After {0} MB more memory"] = ("메모리가 {0}MB 늘면", "内存增加 {0} MB 后", "メモリが {0} MB 増えたら"),
        ["Off"] = ("끄기", "关闭", "オフ"),
        ["Could not restart Folder Splitter."] = ("Folder Splitter를 다시 시작하지 못했습니다.", "无法重新启动 Folder Splitter。", "Folder Splitter を再起動できませんでした。"),
        ["All tabs"] = ("모든 탭", "所有标签页", "すべてのタブ"),
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
            Ctrl+T   빈 새 탭
            Ctrl+W / 가운데 클릭   탭 닫기
            Ctrl+Shift+T   닫은 탭 다시 열기
            Ctrl+Tab / Ctrl+Shift+Tab   탭 전환

            탐색
            Ctrl+L / Alt+D   주소창
            Alt+Left / Right / Up   뒤로, 앞으로, 상위 폴더
            마우스 뒤로 / 앞으로 버튼   포인터가 있는 영역에서 뒤로, 앞으로
            Win+Alt+E   어디서나 Folder Splitter 불러오기 (다시 누르면 알림 영역으로 숨기기)
            F5   새로고침
            Ctrl+D   현재 폴더 고정
            폴더 선택 버튼은 폴더 선택 창을 엽니다.

            파일
            Windows 메뉴 및 영역 간 파일 끌어 놓기를 사용하세요.
            Ctrl+C / Ctrl+X / Ctrl+V   복사, 잘라내기, 붙여넣기
            F2   이름 바꾸기    |    Delete   휴지통으로
            Ctrl+Shift+N   새 폴더 만들기
            Alt+P   미리보기 칸 표시/숨기기
            Ctrl+Shift+C   선택 항목 경로 복사
            Ctrl+Alt+C / Ctrl+Alt+X   선택 항목을 다른 영역으로 복사 / 이동

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
            Ctrl+T   新建空白标签页
            Ctrl+W / 中键点击   关闭标签页
            Ctrl+Shift+T   重新打开已关闭的标签页
            Ctrl+Tab / Ctrl+Shift+Tab   切换标签页

            导航
            Ctrl+L / Alt+D   地址栏
            Alt+Left / Right / Up   后退、前进、上级文件夹
            鼠标后退 / 前进键   在指针所在区域后退、前进
            Win+Alt+E   随时调出 Folder Splitter（再按一次隐藏到通知区域）
            F5   刷新
            Ctrl+D   固定当前文件夹
            选择文件夹按钮打开文件夹选择窗口。

            文件
            使用 Windows 右键菜单，在窗格之间拖放文件。
            Ctrl+C / Ctrl+X / Ctrl+V   复制、剪切、粘贴
            F2   重命名    |    Delete   移至回收站
            Ctrl+Shift+N   新建文件夹
            Alt+P   显示/隐藏预览窗格
            Ctrl+Shift+C   复制所选项目的路径
            Ctrl+Alt+C / Ctrl+Alt+X   将所选项目复制 / 移动到另一个区域

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
            Ctrl+T   空の新しいタブ
            Ctrl+W / 中クリック   タブを閉じる
            Ctrl+Shift+T   閉じたタブを再度開く
            Ctrl+Tab / Ctrl+Shift+Tab   タブの切り替え

            ナビゲーション
            Ctrl+L / Alt+D   アドレス バー
            Alt+Left / Right / Up   戻る、進む、上のフォルダー
            マウスの戻る / 進むボタン   ポインターがあるペインで戻る、進む
            Win+Alt+E   どこからでも Folder Splitter を前面に（もう一度で通知領域に隠す）
            F5   最新の情報に更新
            Ctrl+D   現在のフォルダーをピン留め
            フォルダーを選択 ボタンでフォルダー選択ダイアログを開きます。

            ファイル
            Windows のコンテキスト メニューを使い、ペイン間でファイルをドラッグできます。
            Ctrl+C / Ctrl+X / Ctrl+V   コピー、切り取り、貼り付け
            F2   名前の変更    |    Delete   ごみ箱へ移動
            Ctrl+Shift+N   新しいフォルダー
            Alt+P   プレビュー ウィンドウの表示/非表示
            Ctrl+Shift+C   選択した項目のパスをコピー
            Ctrl+Alt+C / Ctrl+Alt+X   選択した項目を別のペインへコピー / 移動

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
        Ctrl+T   New blank tab
        Ctrl+W / middle-click   Close tab
        Ctrl+Shift+T   Reopen closed tab
        Ctrl+Tab / Ctrl+Shift+Tab   Switch tabs

        NAVIGATION
        Ctrl+L / Alt+D   Address bar
        Alt+Left / Right / Up   Back, forward, parent
        Mouse back / forward buttons   Back, forward in the pane under the pointer
        Win+Alt+E   Bring Folder Splitter to the front from anywhere (again: hide to the notification area)
        F5   Refresh
        Ctrl+D   Pin current folder
        Choose folder opens the folder picker.

        FILES
        Use Windows context menus and drag files between panes.
        Ctrl+C / Ctrl+X / Ctrl+V   Copy, cut, paste
        F2   Rename    |    Delete   Recycle
        Ctrl+Shift+N   New folder
        Alt+P   Show / hide the preview pane
        Ctrl+Shift+C   Copy the selected items' paths
        Ctrl+Alt+C / Ctrl+Alt+X   Copy / move the selection to the other pane

        Tabs, favorites, layout and language are saved locally.
        Pane options include view modes and Open in File Explorer.
        """ : T("Help");
}
