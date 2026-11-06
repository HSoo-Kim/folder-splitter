using QuadExplorer;
using System.Text.Json;

internal static class GroupClosePreferenceTests
{
    public static readonly (string Name, Action Run)[] Cases =
    [
        ("group close confirmation defaults on for new and legacy sessions", Defaults),
        ("explicit group close confirmation values survive normalization without mutation", Normalize),
        ("group close confirmation true and false roundtrip as a shared session preference", Roundtrip),
        ("browser projections preserve shared group close confirmation across windows", Projection),
        ("frozen 0.8.20 serializer ignores group close confirmation and reupgrade restores default", Compatibility),
        ("group close confirmation strings format in all four languages", Localization)
    ];

    private static void Defaults()
    {
        Equal(true, new SessionState().ConfirmCloseGroup);
        Equal(true, SessionStore.Normalize(new SessionState()).ConfirmCloseGroup);
        Equal(true, SessionStore.Normalize(null!).ConfirmCloseGroup);
        WithFile(path =>
        {
            Equal(true, SessionStore.Load(path).ConfirmCloseGroup);
            foreach (string json in new[] { "{}", Legacy0820SessionFixture.Json })
            {
                File.WriteAllText(path, json);
                var state = SessionStore.Load(path, out var warning);
                Equal<string?>(null, warning);
                Equal(true, state.ConfirmCloseGroup);
            }
        });
    }

    private static void Normalize()
    {
        foreach (bool enabled in new[] { true, false })
        {
            var source = new SessionState { ConfirmCloseGroup = enabled, Language = " EN ", Layout = -1 };
            var normalized = SessionStore.Normalize(source);
            Equal(enabled, normalized.ConfirmCloseGroup);
            Equal(enabled, SessionStore.Normalize(normalized).ConfirmCloseGroup);
            Equal("en", normalized.Language);
            Equal(enabled, source.ConfirmCloseGroup);
            Equal(" EN ", source.Language);
            Equal(-1, source.Layout);
        }
    }

    private static void Roundtrip()
    {
        WithFile(path =>
        {
            foreach (bool enabled in new[] { false, true })
            {
                File.WriteAllText(path, enabled ? "{\"ConfirmCloseGroup\":true}" : "{\"ConfirmCloseGroup\":false}");
                var explicitValue = SessionStore.Load(path, out var warning);
                Equal<string?>(null, warning);
                Equal(enabled, explicitValue.ConfirmCloseGroup);
                SessionStore.Save(path, explicitValue);
                var restored = SessionStore.Load(path, out warning);
                Equal<string?>(null, warning);
                Equal(enabled, restored.ConfirmCloseGroup);
                using var document = JsonDocument.Parse(File.ReadAllText(path));
                Equal(enabled, document.RootElement.GetProperty(nameof(SessionState.ConfirmCloseGroup)).GetBoolean());
                foreach (var window in document.RootElement.GetProperty(nameof(SessionState.Windows)).EnumerateArray())
                    Equal(false, window.TryGetProperty(nameof(SessionState.ConfirmCloseGroup), out _));
            }
        });
    }

    private static void Projection()
    {
        var windows = new List<BrowserWindowState>
        {
            new() { Panes = [new() { Tabs = [@"Z:\first"] }] },
            new() { Layout = 2, Panes = [new() { Tabs = [@"Z:\second"] }] }
        };
        foreach (bool enabled in new[] { false, true })
        {
            var source = new SessionState { ConfirmCloseGroup = enabled, Windows = windows, LastActiveWindow = 1 };
            foreach (var window in windows)
            {
                var projected = SessionStore.ProjectWindow(source, window);
                Equal(enabled, projected.ConfirmCloseGroup);
                Equal(enabled, SessionStore.Normalize(projected).ConfirmCloseGroup);
                Equal(true, ReferenceEquals(windows, projected.Windows));
                Equal(1, projected.LastActiveWindow);
                Equal(window.Panes[0].Tabs[0], SessionStore.LegacyWindow(projected).Panes[0].Tabs[0]);
            }
            Equal(enabled, source.ConfirmCloseGroup);
            WithFile(path =>
            {
                SessionStore.Save(path, source);
                var restored = SessionStore.Load(path);
                Equal(enabled, restored.ConfirmCloseGroup);
                Equal(2, restored.Windows.Count);
                foreach (var window in restored.Windows)
                    Equal(enabled, SessionStore.ProjectWindow(restored, window).ConfirmCloseGroup);
            });
        }
    }

    private static void Compatibility()
    {
        WithFile(path =>
        {
            File.WriteAllText(path, Legacy0820SessionFixture.Json);
            var current = SessionStore.Load(path) with { ConfirmCloseGroup = false };
            SessionStore.Save(path, current);
            var older = Legacy0820SessionFixture.Read(path);
            Equal(current.Language, older.Language);
            Equal(true, current.Panes[0].Tabs.SequenceEqual(older.Panes[0].Tabs));
            Legacy0820SessionFixture.Resave(path, older);
            using var document = JsonDocument.Parse(File.ReadAllText(path));
            Equal(false, document.RootElement.TryGetProperty(nameof(SessionState.ConfirmCloseGroup), out _));
            var upgraded = SessionStore.Load(path, out var warning);
            Equal<string?>(null, warning);
            Equal(true, upgraded.ConfirmCloseGroup);
            Equal(current.Language, upgraded.Language);
            Equal(true, current.Panes[0].Tabs.SequenceEqual(upgraded.Panes[0].Tabs));
        });
    }

    private static void Localization()
    {
        (string Key, object?[] Args, string Korean, string Chinese, string Japanese)[] strings =
        [
            ("Close {1} tabs in the '{0}' group?", ["Work", 3], "'Work' 그룹의 탭 3개를 닫을까요?", "关闭“Work”组中的 3 个标签页？", "「Work」グループの 3 個のタブを閉じますか？"),
            ("Close {0} tabs in this group?", [3], "이 그룹의 탭 3개를 닫을까요?", "关闭此组中的 3 个标签页？", "このグループの 3 個のタブを閉じますか？"),
            ("You can reopen them with Ctrl+Shift+T after closing.", [], "닫은 뒤에도 Ctrl+Shift+T로 다시 열 수 있습니다.", "关闭后可按 Ctrl+Shift+T 重新打开。", "閉じた後も Ctrl+Shift+T で開き直せます。"),
            ("Don't ask again", [], "다시 묻지 않기", "不再询问", "今後は確認しない"),
            ("Confirm before closing a group", [], "그룹 닫기 전에 확인", "关闭组前确认", "グループを閉じる前に確認"),
            ("Delete the saved group '{0}'? Its {1} open tabs will also close.", ["Work", 3], "'Work' 저장한 그룹을 삭제할까요? 열린 탭 3개도 닫힙니다.", "删除已保存的组“Work”？其 3 个已打开的标签页也将关闭。", "保存済みグループ「Work」を削除しますか？開いている 3 個のタブも閉じられます。"),
            ("Close", [], "닫기", "关闭", "閉じる"),
            ("Delete", [], "삭제", "删除", "削除"),
            ("Cancel", [], "취소", "取消", "キャンセル"),
            ("Close group", [], "그룹 닫기", "关闭组", "グループを閉じる"),
            ("Delete group", [], "그룹 삭제", "删除组", "グループを削除")
        ];
        foreach (var item in strings)
        {
            Equal(string.Format(System.Globalization.CultureInfo.GetCultureInfo("en"), item.Key, item.Args), new UiText("en").T(item.Key, item.Args));
            Equal(item.Korean, new UiText("ko").T(item.Key, item.Args));
            Equal(item.Chinese, new UiText("zh-CN").T(item.Key, item.Args));
            Equal(item.Japanese, new UiText("ja").T(item.Key, item.Args));
        }
    }

    private static void WithFile(Action<string> action)
    {
        string directory = Path.Combine(Path.GetTempPath(), "GroupClosePreference-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try { action(Path.Combine(directory, "session.json")); }
        finally { Directory.Delete(directory, recursive: true); }
    }

    private static void Equal<T>(T expected, T actual)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual)) throw new Exception($"Expected {expected}; got {actual}.");
    }
}
