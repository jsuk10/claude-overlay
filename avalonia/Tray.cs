using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using ClaudeSessionOverlay.Platform;

namespace ClaudeSessionOverlay;

/// <summary>
/// 알림 영역(Windows)·메뉴 막대(macOS) 아이콘: 오버레이 보이기/숨기기, 알림, 로그인 시 실행, 종료.
/// macOS에서 네이티브 라이브러리(MacBridge)가 있으면 메뉴 막대에 상태별 개수를 보이고 메뉴에 세션 목록을 넣으며,
/// 위젯이 읽는 status.json도 쓴다. 없으면 Avalonia 트레이 아이콘으로 명령만 보인다.
/// </summary>
internal sealed class Tray : IDisposable
{
    private sealed record Command(string Id, Func<string> Text, Action Click, Func<bool>? IsChecked = null);

    private readonly MainWindow _window;
    private readonly List<Command?> _commands; // null은 구분선
    private readonly TrayIcon? _icon;
    private readonly List<(NativeMenuItem Item, Command Command)> _items = [];
    private readonly bool _isNative;
    private readonly StatusFeed? _feed;

    public Tray(MainWindow window, bool isDemo = false)
    {
        _window = window;
        var platform = IPlatform.Current;

        _commands =
        [
            new("toggleHidden", () => _window.IsUserHidden ? Loc.T("오버레이 보이기", "Show overlay") : Loc.T("오버레이 숨기기", "Hide overlay"), _window.ToggleUserHidden),
            new("toggleCollapsed", () => _window.IsCollapsed ? Loc.T("목록 펼치기", "Expand list") : Loc.T("목록 접기", "Collapse list"), _window.ToggleCollapsed),
            new("showSeen", () => Loc.T("확인한 세션 다시 보기", "Show seen sessions again"), _window.ShowSeenAgain),
            new("resetPosition", () => Loc.T("위치 초기화", "Reset position"), _window.ResetPosition),
            null,
            new("sound", () => Loc.T("알림 소리", "Notification sound"), _window.ToggleSound, () => _window.IsSoundOn),
            new("voice", () => Loc.T("음성으로 알려주기 (완료·확인 필요·컨텍스트)", "Voice alerts (done · needs input · context)"), _window.ToggleVoice, () => _window.IsVoiceOn),
            new("testNotice", () => Loc.T("알림 들어보기", "Test notification"), _window.TestNotice),
            null,
            new("autostart", () => platform.AutostartLabel, () => platform.SetAutostart(!platform.IsAutostart), () => platform.IsAutostart),
            null,
            new("quit", () => Loc.T("종료", "Quit"), _window.ExitApp),
        ];

        if (OperatingSystem.IsMacOS() && MacBridge.IsAvailable)
        {
            _isNative = true;
            // 시연은 아무것도 저장하지 않는다(위젯 확인용 --demo-widget만 예외)
            if (!isDemo || Program.Options.Has("--demo-widget")) _feed = new StatusFeed();
            MacBridge.Start(OnNativeAction);
            _window.Refreshed += PushToMenuBar;
            return;
        }

        var menu = new NativeMenu();
        foreach (var command in _commands)
        {
            if (command is null) { menu.Add(new NativeMenuItemSeparator()); continue; }
            var item = new NativeMenuItem(command.Text());
            if (command.IsChecked is not null) item.ToggleType = NativeMenuItemToggleType.CheckBox;
            item.Click += (_, _) =>
            {
                command.Click();
                UpdateItems();
            };
            menu.Add(item);
            _items.Add((item, command));
        }
        // 언어·상태가 바뀌었을 수 있으니 열 때마다 문구와 체크를 채운다
        menu.Opening += (_, _) => UpdateItems();
        UpdateItems();

        _icon = new TrayIcon
        {
            Icon = BuildIcon(),
            ToolTipText = Loc.T("Claude 세션 오버레이", "Claude Session Overlay"),
            Menu = menu,
            IsVisible = true,
        };
        // Windows: 왼쪽 클릭으로 보이기/숨기기(macOS는 클릭하면 메뉴가 열린다)
        _icon.Clicked += (_, _) => _window.ToggleUserHidden();
        TrayIcon.SetIcons(Application.Current!, [_icon]);

        _window.SummaryChanged += summary => _icon.ToolTipText = Truncate(Loc.T($"Claude 세션 — {summary}", $"Claude Sessions — {summary}"), 63);
    }

    // ── macOS 메뉴 막대(네이티브) ─────────────────────────────────────────

    /// <summary>새로고침(1초)마다: 메뉴 막대 글자·메뉴 내용을 넘기고, 바뀌었으면 위젯 파일을 쓴다</summary>
    private void PushToMenuBar(List<SessionRow> rows, List<SessionInfo> sessions)
    {
        if (!OperatingSystem.IsMacOS()) return;
        var board = StatusBoard.Build(rows, sessions);
        var commands = _commands.Select(c => c is null
            ? new { id = (string?)null, title = (string?)null, @checked = (bool?)null, separator = (bool?)true }
            : new { id = (string?)c.Id, title = (string?)c.Text(), @checked = c.IsChecked?.Invoke(), separator = (bool?)null });
        MacBridge.Update(JsonSerializer.Serialize(new { board, commands }, Json.Options));
        if (_feed?.Write(board) == true) MacBridge.ReloadWidgets();
    }

    private void OnNativeAction(string id)
    {
        if (id.StartsWith("open:")) { _window.OpenSessionById(id["open:".Length..]); return; }
        _commands.FirstOrDefault(c => c?.Id == id)?.Click();
        _window.RefreshNow();
    }

    // ── Avalonia 트레이 ───────────────────────────────────────────────────

    private void UpdateItems()
    {
        foreach (var (item, command) in _items)
        {
            item.Header = command.Text();
            if (command.IsChecked is not null) item.IsChecked = command.IsChecked();
        }
    }

    private static string Truncate(string text, int max) => text.Length <= max ? text : text[..(max - 1)] + "…";

    /// <summary>앱 아이콘(assets/icon/icon-64.png)</summary>
    private static WindowIcon BuildIcon() =>
        new(AssetLoader.Open(new Uri("avares://ClaudeSessionOverlay/Assets/icon-64.png")));

    public void Dispose()
    {
        if (_isNative && OperatingSystem.IsMacOS()) MacBridge.Stop();
        _icon?.Dispose();
    }
}
