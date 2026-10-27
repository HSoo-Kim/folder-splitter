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
        ["AI search"] = ("AI 검색", "AI 搜索", "AI 検索"),
        ["Login"] = ("로그인", "登录", "ログイン"),
        ["CLI not found; open settings"] = ("CLI를 찾을 수 없음 · 설정 열기", "未找到 CLI · 打开设置", "CLI が見つかりません · 設定を開く"),
        ["Open the provider's interactive login window."] = ("제공자의 로그인 창을 엽니다.", "打开提供方的登录窗口。", "プロバイダーのログイン画面を開きます。"),
        ["Complete login in the provider window."] = ("제공자의 창에서 로그인을 완료하세요.", "请在提供方窗口中完成登录。", "プロバイダーの画面でログインを完了してください。"),
        ["Login failed. Check the provider CLI and try again."] = ("로그인하지 못했습니다. 제공자 CLI를 확인한 뒤 다시 시도하세요.", "登录失败。请检查提供方 CLI 后重试。", "ログインできませんでした。CLI を確認して再試行してください。"),
        ["Login failed: {0}"] = ("로그인 실패: {0}", "登录失败：{0}", "ログイン失敗: {0}"),
        ["Search contents of this file"] = ("이 파일 내용 검색", "搜索此文件内容", "このファイルの内容を検索"),
        ["Clear selected file"] = ("선택 파일 해제", "清除所选文件", "選択したファイルを解除"),
        ["Selected file is ready. Enter a question; nothing has been sent."] = ("파일을 선택했습니다. 질문을 입력하세요. 아직 전송되지 않았습니다.", "已选择文件。请输入问题；尚未发送任何内容。", "ファイルを選択しました。質問を入力してください。まだ送信されていません。"),
        ["Selected file is no longer available."] = ("선택한 파일을 찾을 수 없습니다.", "所选文件已不可用。", "選択したファイルは利用できません。"),
        ["Uses your Codex ChatGPT login. No API key or Codex tools are used."] = ("Codex에 로그인한 ChatGPT 구독 계정을 사용합니다. API 키나 Codex 도구는 사용하지 않습니다.", "使用 Codex 中登录的 ChatGPT 订阅账户，不使用 API 密钥或 Codex 工具。", "Codex にログインした ChatGPT サブスクリプションを使用します。API キーや Codex のツールは使用しません。"),
        ["Document sources"] = ("문서 근거", "文档来源", "文書の出典"),
        ["Unverified answer · {0} files"] = ("답변 미검증 · 파일 {0}개", "回答未验证 · {0} 个文件", "回答未検証 · {0} ファイル"),
        ["Check the excerpts below."] = ("아래 원문 발췌를 확인하세요.", "请查看下方的原文摘录。", "以下の原文の抜粋を確認してください。"),
        ["This folder is outside the allowed search locations. Open it in a pane or provide its full path."] = ("이 폴더는 허용된 검색 범위 밖입니다. 폴더를 창에서 열거나 전체 경로를 입력하세요.", "此文件夹不在允许的搜索范围内。请在窗格中打开它或输入完整路径。", "このフォルダーは検索可能な範囲外です。ペインで開くか、フルパスを入力してください。"),
        ["Only the listed files and folders were checked."] = ("표시된 파일·폴더 범위만 확인했습니다.", "仅检查了列出的文件和文件夹。", "表示されたファイルとフォルダーの範囲のみ確認しました。"),
        ["Excerpts sent to AI · no permanent body index"] = ("발췌를 AI에 전달 · 영구 본문 색인 없음", "摘录发送给 AI · 无永久正文索引", "抜粋を AI に送信 · 永続索引なし"),
        ["Searching {0} · folders {1} · candidates {2} · read {3}"] = ("검색 {0}회 · 폴더 {1}개 · 후보 {2}개 · 읽음 {3}개", "搜索 {0} 次 · 文件夹 {1} · 候选 {2} · 已读 {3}", "検索 {0} 回 · フォルダー {1} · 候補 {2} · 読了 {3}"),
        ["Checked {0} folders · read {1} files · failed {2}"] = ("확인한 폴더 {0}개 · 읽은 파일 {1}개 · 읽기 실패 {2}개", "检查文件夹 {0} · 读取文件 {1} · 失败 {2}", "確認フォルダー {0} · 読んだファイル {1} · 失敗 {2}"),
        ["Only part of the search scope was checked. Provide a candidate folder to continue."] = ("탐색 범위 중 일부만 확인했습니다. 후보 폴더를 알려주시면 이어서 찾습니다.", "仅检查了部分范围。请提供候选文件夹以继续。", "検索範囲の一部のみ確認しました。候補フォルダーを指定すると続けられます。"),
        ["Filename mode sends conversation only. Document mode sends candidate paths and relevant excerpts to your selected AI."] = ("파일 찾기는 대화만 전달합니다. 내용 읽기는 후보 경로와 관련 본문 발췌를 선택한 AI에 전달합니다.", "文件名模式仅发送对话。文档模式向所选 AI 发送候选路径及相关摘录。", "ファイル名モードは会話のみ送信します。文書モードは候補パスと関連する本文の抜粋を選択した AI に送信します。"),
        ["AI search mode"] = ("AI 검색 모드", "AI 搜索模式", "AI 検索モード"),
        ["Find by name"] = ("파일명으로 찾기", "按文件名查找", "ファイル名で探す"),
        ["Search contents"] = ("내용으로 찾기", "搜索内容", "内容で探す"),
        ["Find files by name, folder or file type."] = ("이름, 폴더, 파일 종류로 찾아요.", "按名称、文件夹或文件类型查找。", "名前・フォルダー・種類で探します。"),
        ["Find answers in your documents. Relevant excerpts go to your AI."] = ("문서에서 답을 찾아요. 관련 본문을 AI에 전달합니다.", "在文档中查找答案，相关摘录会发送给 AI。", "文書から答えを探します。関連する抜粋を AI に送ります。"),
        ["Your AI reads excerpts."] = ("AI가 본문을 읽어요.", "AI 阅读相关摘录。", "AI が本文を読みます。"),
        ["Up / Down recalls sent messages at the first / last line. New chat clears recall, not your draft."] = ("첫 줄에서 ↑, 마지막 줄에서 ↓로 보낸 메시지를 불러옵니다. 새 대화는 기록만 지우고 작성 중인 글은 유지합니다.", "首行按 ↑、末行按 ↓ 可调出已发送消息。新对话会清除历史，但保留草稿。", "先頭行で ↑、最終行で ↓ を押すと送信済みメッセージを呼び出せます。新しい会話では履歴のみ消去し、下書きは残します。"),
        ["Chat kept · context separated by mode."] = ("대화 유지 · 모드별 맥락 분리", "保留对话 · 各模式上下文分离", "会話を保持 · モード別に文脈を分離"),
        ["Copy conversation"] = ("대화 전체 복사", "复制完整对话", "会話全体をコピー"),
        ["Select all"] = ("모두 선택", "全选", "すべて選択"),
        ["{0} files found"] = ("찾은 파일 {0}개", "找到 {0} 个文件", "見つかったファイル {0} 件"),
        ["Sources · {0} files"] = ("답변 근거 · 파일 {0}개", "答案来源 · {0} 个文件", "回答の出典 · ファイル {0} 件"),
        ["Click a file to select it. Double-click to open it here."] = ("파일을 클릭해 선택하세요. 두 번 클릭하면 현재 영역에서 엽니다.", "单击文件以选中。双击可在当前窗格中打开。", "ファイルをクリックして選択。ダブルクリックで現在のペインに開きます。"),
        ["Click to select · double-click to open"] = ("클릭해 선택 · 두 번 클릭해 열기", "单击选择 · 双击打开", "クリックで選択 · ダブルクリックで開く"),
        ["Find files"] = ("파일 찾기", "查找文件", "ファイルを探す"),
        ["Read and answer"] = ("내용 읽고 답변", "阅读内容并回答", "内容を読んで回答"),
        ["Selected AI receives relevant body excerpts. No permanent body index."] = ("선택한 AI 제공자에 관련 본문 발췌를 보냅니다. 본문을 영구 색인하지 않습니다.", "相关正文摘录会发送给所选 AI 提供方，不建立永久正文索引。", "関連する本文の抜粋を選択した AI プロバイダーに送信します。本文の永続索引は作りません。"),
        ["Ask about document contents…"] = ("문서 내용에 관해 질문하세요…", "询问文档内容…", "文書の内容について質問してください…"),
        ["Summarize the proposal documents"] = ("제안서 문서 내용을 요약해 줘", "总结提案文档内容", "提案書の内容を要約して"),
        ["Example: Summarize the proposal documents"] = ("예: 제안서 문서 내용을 요약해 줘", "例如：总结提案文档内容", "例：提案書の内容を要約して"),
        ["Finding relevant documents → reading excerpts…"] = ("관련 문서 검색 → 본문 발췌를 읽는 중…", "查找相关文档 → 阅读摘录…", "関連文書を検索 → 本文の抜粋を読み込み中…"),
        ["Ask a question about your documents. Only relevant excerpts are read for this request."] = ("문서 내용에 관해 질문하세요. 이번 요청에 관련된 본문 발췌만 읽습니다.", "请询问文档内容。本次请求只读取相关摘录。", "文書の内容について質問してください。このリクエストに関連する抜粋だけを読み込みます。"),
        ["Open"] = ("열기", "打开", "開く"),
        ["Reasoning effort"] = ("추론 수준", "推理强度", "推論レベル"),
        ["Effort"] = ("추론", "推理", "推論"),
        ["Local"] = ("로컬", "本地", "ローカル"),
        ["Loading available models…"] = ("모델 목록을 불러오는 중…", "正在加载可用模型…", "モデル一覧を読み込み中…"),
        ["Models loaded."] = ("모델 목록을 불러왔습니다.", "已加载模型列表。", "モデル一覧を読み込みました。"),
        ["No models listed. Enter a model ID."] = ("목록이 비어 있습니다. 모델 ID를 입력하세요.", "模型列表为空，请输入模型 ID。", "一覧が空です。モデル ID を入力してください。"),
        ["Could not list models. Enter a model ID or open settings."] = ("목록을 불러오지 못했습니다. 모델 ID를 입력하거나 설정을 확인하세요.", "无法加载模型列表。请输入模型 ID 或检查设置。", "一覧を取得できません。モデル ID を入力するか設定を確認してください。"),
        ["Default"] = ("기본", "默认", "標準"),
        ["Low"] = ("낮음", "低", "低"),
        ["Medium"] = ("보통", "中", "中"),
        ["High"] = ("높음", "高", "高"),
        ["Very high"] = ("매우 높음", "很高", "非常に高い"),
        ["Maximum"] = ("최대", "最高", "最大"),
        ["Reasoning depends on the selected model."] = ("추론 수준은 선택한 모델이 지원할 때 적용됩니다.", "推理强度仅在所选模型支持时可用。", "推論レベルは選択したモデルが対応している場合に利用できます。"),
        ["Search conditions: {0}"] = ("검색 조건: {0}", "搜索条件：{0}", "検索条件: {0}"),
        ["Show / hide AI search (Alt+I)"] = ("AI 검색 표시 / 숨기기 (Alt+I)", "显示 / 隐藏 AI 搜索 (Alt+I)", "AI 検索を表示 / 非表示 (Alt+I)"),
        ["AI search index is unavailable."] = ("AI 검색 색인을 사용할 수 없습니다.", "AI 搜索索引不可用。", "AI 検索索引は利用できません。"),
        ["AI settings could not be saved: {0}"] = ("AI 설정을 저장하지 못했습니다: {0}", "无法保存 AI 设置：{0}", "AI 設定を保存できませんでした: {0}"),
        ["AI settings could not be loaded: {0}"] = ("AI 설정을 불러오지 못했습니다: {0}", "无法加载 AI 设置：{0}", "AI 設定を読み込めませんでした: {0}"),
        ["AI search · Beta"] = ("AI 검색 · 베타", "AI 搜索 · 测试版", "AI 検索 · ベータ"),
        ["AI Beta"] = ("AI 베타", "AI 测试版", "AI ベータ"),
        ["Current pane"] = ("현재 영역", "当前窗格", "現在のペイン"),
        ["New pane"] = ("새 영역", "新建窗格", "新しいペイン"),
        ["New chat"] = ("새 대화", "新对话", "新しい会話"),
        ["AI search settings"] = ("AI 검색 설정", "AI 搜索设置", "AI 検索設定"),
        ["Detach AI search"] = ("AI 검색 분리", "分离 AI 搜索", "AI 検索を切り離す"),
        ["Dock AI search"] = ("AI 검색 붙이기", "停靠 AI 搜索", "AI 検索をドッキング"),
        ["Describe it naturally"] = ("말하듯 입력해 보세요", "像聊天一样输入", "話すように入力してください"),
        ["AI extracts name, folder and extension terms, then searches the actual file index."] = ("AI가 대화에서 파일 이름·폴더·확장자 조건을 뽑아 실제 파일 색인을 검색합니다.", "AI 从对话提取文件名、文件夹和扩展名条件，再搜索实际文件索引。", "AI が会話から名前・フォルダー・拡張子の条件を抽出し、実際のファイル索引を検索します。"),
        ["Describe a file"] = ("말하듯 입력해 보세요", "像聊天一样输入", "話すように入力"),
        ["AI: name/folder/ext → real index"] = ("AI: 이름·폴더·확장자 → 실제 색인", "AI：名称·文件夹·扩展名 → 实际索引", "AI: 名前・フォルダー・拡張子 → 実索引"),
        ["Example: Find proposal PDF files"] = ("예: 제안서 PDF 파일 찾아줘", "例如：查找提案 PDF 文件", "例：提案書の PDF を探して"),
        ["Find proposal PDF files"] = ("제안서 PDF 파일 찾아줘", "查找提案 PDF 文件", "提案書の PDF を探して"),
        ["Describe a file, or refine your last search…"] = ("찾을 파일을 설명하거나 이전 검색을 좁혀 보세요…", "描述文件，或缩小上次搜索范围…", "ファイルを説明するか、前の検索を絞り込んでください…"),
        ["AI search message"] = ("AI 검색 메시지", "AI 搜索消息", "AI 検索メッセージ"),
        ["Send"] = ("보내기", "发送", "送信"),
        ["Stop"] = ("중지", "停止", "停止"),
        ["Stop search (Esc)"] = ("검색 중지 (Esc)", "停止搜索 (Esc)", "検索を停止 (Esc)"),
        ["Enter to send · Shift+Enter for a new line"] = ("Enter 보내기 · Shift+Enter 줄 바꿈", "Enter 发送 · Shift+Enter 换行", "Enter で送信 · Shift+Enter で改行"),
        ["AI chat is kept only while this window is open."] = ("대화는 앱을 종료하면 사라집니다.", "对话仅保留到应用关闭。", "会話はアプリを終了すると消去されます。"),
        ["Configure a provider and model in AI search settings."] = ("AI 검색 설정에서 제공자와 모델을 설정하세요.", "请在 AI 搜索设置中配置提供方和模型。", "AI 検索設定でプロバイダーとモデルを設定してください。"),
        ["Understanding your request → searching the file index…"] = ("요청 해석 → 실제 파일 색인 검색 중…", "理解请求 → 搜索实际文件索引…", "リクエストを解析 → 実際のファイル索引を検索中…"),
        ["Search failed. Check provider, endpoint and model in settings."] = ("검색하지 못했습니다. 설정에서 제공자·주소·모델을 확인하세요.", "搜索失败。请在设置中检查提供方、地址和模型。", "検索できませんでした。設定でプロバイダー・接続先・モデルを確認してください。"),
        ["No matching indexed files. Try fewer terms or another folder."] = ("일치하는 색인 파일이 없습니다. 조건을 줄이거나 다른 폴더를 찾아보세요.", "没有匹配的索引文件。请减少条件或尝试其他文件夹。", "一致する索引ファイルがありません。条件を減らすか、別のフォルダーを試してください。"),
        ["Search stopped. You can send another message."] = ("검색을 중지했습니다. 새 메시지를 보낼 수 있습니다.", "搜索已停止。可以发送新消息。", "検索を停止しました。別のメッセージを送信できます。"),
        ["Results come from your file index. Ask a follow-up to narrow them down."] = ("결과는 실제 파일 색인에서 가져옵니다. 이어서 말하면 결과를 좁힐 수 있어요.", "结果来自实际文件索引。继续提问即可缩小范围。", "結果は実際のファイル索引から取得します。続けて質問すると絞り込めます。"),
        ["Provider error: {0}"] = ("제공자 오류: {0}", "提供方错误：{0}", "プロバイダーエラー: {0}"),
        ["{0} indexed results"] = ("색인 검색 결과 {0}개", "{0} 个索引结果", "索引の検索結果 {0} 件"),
        ["More results exist. Refine your search."] = ("결과가 더 있습니다. 검색 조건을 좁혀 보세요.", "还有更多结果。请缩小搜索范围。", "結果はほかにもあります。検索条件を絞り込んでください。"),
        ["You"] = ("나", "你", "あなた"),
        ["AI assistant"] = ("AI 도우미", "AI 助手", "AI アシスタント"),
        ["Indexed file results"] = ("실제 파일 색인 결과", "实际文件索引结果", "実際のファイル索引の結果"),
        ["Provider"] = ("제공자", "提供方", "プロバイダー"),
        ["Configured provider is unsupported. Choose Local endpoint or Claude CLI."] = ("설정된 제공자는 지원하지 않습니다. 로컬 엔드포인트, Claude CLI, ChatGPT 중에서 선택하세요.", "不支持已配置的提供方。请选择本地端点、Claude CLI 或 ChatGPT。", "設定されたプロバイダーは未対応です。ローカルエンドポイント、Claude CLI、ChatGPT から選んでください。"),
        ["Local endpoint"] = ("로컬 엔드포인트", "本地端点", "ローカルエンドポイント"),
        ["Local endpoint URL"] = ("로컬 엔드포인트 주소", "本地端点地址", "ローカルエンドポイント URL"),
        ["Model"] = ("모델", "模型", "モデル"),
        ["CLI path (optional)"] = ("CLI 경로 (선택)", "CLI 路径（可选）", "CLI パス（省略可）"),
        ["Local model ID"] = ("로컬 모델 ID", "本地模型 ID", "ローカルモデル ID"),
        ["Choose the model in the conversation. Configure its connection here."] = ("모델은 대화창에서 고르고, 여기서는 연결을 설정합니다.", "在对话窗口选择模型，在此配置连接。", "モデルは会話画面で選び、ここで接続を設定します。"),
        ["Context · last request: {0:N0} / {1:N0} ({2:P1})"] = ("컨텍스트 · 직전 요청: {0:N0} / {1:N0} ({2:P1})", "上下文 · 上次请求：{0:N0} / {1:N0}（{2:P1}）", "コンテキスト · 前回のリクエスト：{0:N0} / {1:N0}（{2:P1}）"),
        ["Context · last request: {0:N0} input tokens"] = ("컨텍스트 · 직전 요청: 입력 {0:N0}토큰", "上下文 · 上次请求：输入 {0:N0} 个词元", "コンテキスト · 前回のリクエスト：入力 {0:N0} トークン"),
        ["Measured input tokens for the last request, not cumulative chat usage."] = ("직전 모델 요청의 입력 토큰 수입니다. 대화 누적 사용량이 아닙니다.", "上次模型请求的输入词元数，并非对话累计用量。", "前回のモデルへの入力トークン数です。会話の累計ではありません。"),
        ["Near the context limit. Narrow your request or start a new chat."] = ("컨텍스트 한도에 가까워졌습니다. 질문 범위를 줄이거나 새 대화를 시작하세요.", "接近上下文上限。请缩小问题范围或开始新对话。", "コンテキスト上限に近づいています。質問を絞るか新しい会話を開始してください。"),
        ["Personal AI instructions (optional)"] = ("AI 사전 지침 (선택)", "AI 自定义指令（可选）", "AI カスタム指示（任意）"),
        ["Used in both modes. Filename search sends no file contents; document reading may send relevant excerpts."] = ("두 모드에 적용됩니다. 파일명 검색은 내용을 보내지 않고, 문서 읽기는 관련 부분을 보낼 수 있습니다.", "适用于两种模式。文件名搜索不发送文件内容，文档阅读可能发送相关摘录。", "両モードに適用します。ファイル名検索は本文を送らず、文書読取は関連箇所を送る場合があります。"),
        ["Readable: PDF, HWP/HWPX/HML, DOCX, PPTX, XLS/XLSX, TXT/MD/CSV/LOG · text only, no OCR."] = ("읽기 지원: PDF, HWP/HWPX/HML, DOCX, PPTX, XLS/XLSX, TXT/MD/CSV/LOG · 텍스트만, OCR 없음", "可读取：PDF、HWP/HWPX/HML、DOCX、PPTX、XLS/XLSX、TXT/MD/CSV/LOG · 仅文本，不支持 OCR", "読取：PDF、HWP/HWPX/HML、DOCX/PPTX、XLS/XLSX、TXT/MD/CSV/LOG（本文のみ・OCR なし）"),
        ["Test / list models"] = ("연결 테스트 / 모델 목록", "测试连接 / 模型列表", "接続テスト / モデル一覧"),
        ["Provider connection status"] = ("제공자 연결 상태", "提供方连接状态", "プロバイダーの接続状態"),
        ["Use your local OpenAI-compatible endpoint. Saving works offline; test when it is running."] = ("로컬 OpenAI 호환 엔드포인트를 사용합니다. 연결 없이 저장할 수 있고, 서버 실행 후 테스트하세요.", "使用本地 OpenAI 兼容端点。可以离线保存，服务器运行后再测试。", "ローカルの OpenAI 互換エンドポイントを使用します。オフラインでも保存でき、サーバー起動後にテストできます。"),
        ["Only conversation text is sent. File lists and file contents are not sent. Uses your installed CLI and its signed-in account."] = ("대화 문장만 전송, 파일 목록·내용 전송 안 함. 설치된 CLI와 로그인된 계정을 사용합니다.", "仅发送对话文本，不发送文件列表或内容。使用已安装的 CLI 及其登录账户。", "会話の文章だけを送信し、ファイル一覧・内容は送信しません。インストール済み CLI とログイン中のアカウントを使用します。"),
        ["Enter an http:// or https:// endpoint without credentials."] = ("인증 정보가 없는 http:// 또는 https:// 주소를 입력하세요.", "请输入不含凭据的 http:// 或 https:// 地址。", "認証情報を含まない http:// または https:// の URL を入力してください。"),
        ["Choose or enter a model. You can save without connecting."] = ("모델을 선택하거나 입력하세요. 연결하지 않고 저장할 수 있습니다.", "请选择或输入模型。无需连接即可保存。", "モデルを選択または入力してください。接続せずに保存できます。"),
        ["Provider connection testing is unavailable."] = ("제공자 연결 테스트를 사용할 수 없습니다.", "提供方连接测试不可用。", "プロバイダーの接続テストは利用できません。"),
        ["Connecting and listing models…"] = ("연결 및 모델 목록 확인 중…", "正在连接并获取模型列表…", "接続してモデル一覧を取得中…"),
        ["Connected: {0}"] = ("연결됨: {0}", "已连接：{0}", "接続済み: {0}"),
        ["Connection failed: {0}"] = ("연결 실패: {0}", "连接失败：{0}", "接続失敗: {0}"),
        ["Folder tree"] = ("폴더 트리", "文件夹树", "フォルダーツリー"),
        ["Show / hide folder tree (Ctrl+Shift+E)"] = ("폴더 트리 표시 / 숨기기 (Ctrl+Shift+E)", "显示 / 隐藏文件夹树 (Ctrl+Shift+E)", "フォルダーツリーを表示 / 非表示 (Ctrl+Shift+E)"),
        ["Collapse / expand folder tree (Ctrl+Shift+E)"] = ("폴더 트리 접기 / 펼치기 (Ctrl+Shift+E)", "折叠 / 展开文件夹树 (Ctrl+Shift+E)", "フォルダーツリーを折りたたむ / 展開 (Ctrl+Shift+E)"),
        ["Drag to resize favorites and the folder tree"] = ("드래그해 즐겨찾기와 폴더 트리 높이 조절", "拖动以调整收藏夹和文件夹树的高度", "ドラッグしてお気に入りとフォルダーツリーの高さを調整"),
        ["Settings"] = ("설정", "设置", "設定"),
        ["Show command bar"] = ("명령 막대 표시", "显示命令栏", "コマンドバーを表示"),
        ["All panes"] = ("모든 영역에 공통 적용", "应用于所有窗格", "すべてのペインに適用"),
        ["Active pane color"] = ("활성 영역 색상", "活动窗格颜色", "アクティブなペインの色"),
        ["Theme default"] = ("테마 기본 색상", "主题默认颜色", "テーマの既定色"),
        ["Blue"] = ("파란색", "蓝色", "青"),
        ["Green"] = ("초록색", "绿色", "緑"),
        ["Amber"] = ("황갈색", "琥珀色", "アンバー"),
        ["This pane"] = ("이 영역에만 적용", "仅应用于此窗格", "このペインのみに適用"),
        ["Windows settings"] = ("Windows 공통 설정", "Windows 共享设置", "Windows 共通設定"),
        ["Apply"] = ("적용", "应用", "適用"),
        ["Vertical separators"] = ("세로 구분선", "垂直分隔线", "縦の区切り線"),
        ["Highlight sorted column"] = ("정렬 열 강조", "突出显示排序列", "並べ替え列を強調表示"),
        ["Row spacing"] = ("줄 간격", "行间距", "行間"),
        ["Compact"] = ("빽빽하게", "紧凑", "コンパクト"),
        ["Comfortable"] = ("보통", "标准", "標準"),
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
        ["Filter this folder"] = ("폴더 내에서 찾기", "筛选此文件夹", "このフォルダーを絞り込む"),
        ["Filter this folder (Ctrl+F)"] = ("폴더 내에서 찾기 (Ctrl+F)", "筛选此文件夹 (Ctrl+F)", "このフォルダーを絞り込む (Ctrl+F)"),
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
        ["File total: {0}"] = ("파일 총 용량 {0}", "文件总大小 {0}", "ファイル合計 {0}"),
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
        ["Quick access"] = ("빠른 접근", "快速访问", "クイックアクセス"),
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
        ["Close pane (Ctrl+W when empty)"] = ("영역 닫기 (빈 영역: Ctrl+W)", "关闭窗格（空窗格：Ctrl+W）", "ペインを閉じる（空のペイン: Ctrl+W）"),
        ["Select this tab, then close with Ctrl+W"] = ("이 탭을 선택한 뒤 Ctrl+W로 닫기", "选中此标签页后按 Ctrl+W 关闭", "このタブを選択して Ctrl+W で閉じる"),
        ["Undo tab or pane close in any pane (Ctrl+Shift+T)"] = ("전체 탭·영역 닫기 되돌리기 (Ctrl+Shift+T)", "撤销所有窗格中的标签页或窗格关闭 (Ctrl+Shift+T)", "全ペインのタブ・ペインを閉じる操作を元に戻す (Ctrl+Shift+T)"),
        ["Redo tab or pane close in any pane"] = ("전체 탭·영역 닫기 다시 실행", "重做所有窗格中的标签页或窗格关闭", "全ペインのタブ・ペインを閉じる操作をやり直す"),
        ["Undo tab close in this pane"] = ("이 영역의 탭 닫기 되돌리기", "撤销此窗格中的标签页关闭", "このペインのタブを閉じる操作を元に戻す"),
        ["Redo tab close in this pane"] = ("이 영역의 탭 닫기 다시 실행", "重做此窗格中的标签页关闭", "このペインのタブを閉じる操作をやり直す"),
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
        ["Pane {0}  |  1 tab"] = ("영역 {0}  |  탭 1개", "窗格 {0}  |  1 个标签页", "ペイン {0}  |  タブ 1 個"),
        ["Pane {0}  |  {1} tabs"] = ("영역 {0}  |  탭 {1}개", "窗格 {0}  |  {1} 个标签页", "ペイン {0}  |  タブ {1} 個"),
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
            빈 영역에서 Ctrl+W를 다시 누르면 해당 분할을 닫습니다.
            Ctrl+Shift+T   마지막으로 닫은 탭 또는 빈 분할 복원
            Ctrl+Tab / Ctrl+Shift+Tab   탭 전환

            탐색
            Ctrl+L / Alt+D   주소창
            Ctrl+F   폴더 내에서 찾기
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
            在空窗格中再次按 Ctrl+W 可关闭该窗格。
            Ctrl+Shift+T   恢复最后关闭的标签页或空窗格
            Ctrl+Tab / Ctrl+Shift+Tab   切换标签页

            导航
            Ctrl+L / Alt+D   地址栏
            Ctrl+F   筛选此文件夹
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
            空のペインで Ctrl+W をもう一度押すと、そのペインを閉じます。
            Ctrl+Shift+T   最後に閉じたタブまたは空のペインを復元
            Ctrl+Tab / Ctrl+Shift+Tab   タブの切り替え

            ナビゲーション
            Ctrl+L / Alt+D   アドレス バー
            Ctrl+F   このフォルダーを絞り込む
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
        Press Ctrl+W again in an empty pane to close that split.
        Ctrl+Shift+T   Restore the last closed tab or empty pane
        Ctrl+Tab / Ctrl+Shift+Tab   Switch tabs

        NAVIGATION
        Ctrl+L / Alt+D   Address bar
        Ctrl+F   Filter this folder
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
