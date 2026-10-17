using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using ClaudeSessionOverlay.Platform;

namespace ClaudeSessionOverlay;

/// <summary>알림 영역(Windows)·메뉴 막대(macOS) 아이콘: 오버레이 보이기/숨기기, 알림, 로그인 시 실행, 종료</summary>
internal sealed class Tray : IDisposable
{
    private readonly MainWindow _window;
    private readonly TrayIcon _icon;
    private readonly List<(NativeMenuItem Item, Func<string> Text, Func<bool>? IsChecked)> _items = [];

    public Tray(MainWindow window)
    {
        _window = window;
        var platform = IPlatform.Current;

        var menu = new NativeMenu();
        Add(menu, () => _window.IsUserHidden ? Loc.T("오버레이 보이기", "Show overlay") : Loc.T("오버레이 숨기기", "Hide overlay"), _window.ToggleUserHidden);
        Add(menu, () => _window.IsCollapsed ? Loc.T("목록 펼치기", "Expand list") : Loc.T("목록 접기", "Collapse list"), _window.ToggleCollapsed);
        Add(menu, () => Loc.T("확인한 세션 다시 보기", "Show seen sessions again"), _window.ShowSeenAgain);
        Add(menu, () => Loc.T("위치 초기화", "Reset position"), _window.ResetPosition);
        menu.Add(new NativeMenuItemSeparator());
        Add(menu, () => Loc.T("알림 소리", "Notification sound"), _window.ToggleSound, () => _window.IsSoundOn);
        Add(menu, () => Loc.T("음성으로 알려주기 (완료·확인 필요·컨텍스트)", "Voice alerts (done · needs input · context)"), _window.ToggleVoice, () => _window.IsVoiceOn);
        Add(menu, () => Loc.T("알림 들어보기", "Test notification"), _window.TestNotice);
        menu.Add(new NativeMenuItemSeparator());
        Add(menu, () => platform.AutostartLabel, () => platform.SetAutostart(!platform.IsAutostart), () => platform.IsAutostart);
        menu.Add(new NativeMenuItemSeparator());
        Add(menu, () => Loc.T("종료", "Quit"), _window.ExitApp);
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

    private void Add(NativeMenu menu, Func<string> text, Action click, Func<bool>? isChecked = null)
    {
        var item = new NativeMenuItem(text());
        if (isChecked is not null) item.ToggleType = NativeMenuItemToggleType.CheckBox;
        item.Click += (_, _) =>
        {
            click();
            UpdateItems();
        };
        menu.Add(item);
        _items.Add((item, text, isChecked));
    }

    private void UpdateItems()
    {
        foreach (var (item, text, isChecked) in _items)
        {
            item.Header = text();
            if (isChecked is not null) item.IsChecked = isChecked();
        }
    }

    private static string Truncate(string text, int max) => text.Length <= max ? text : text[..(max - 1)] + "…";

    /// <summary>주황 원 안에 흰 별표 — Claude 느낌의 단순한 아이콘을 그려 쓴다</summary>
    private static WindowIcon BuildIcon()
    {
        using var bitmap = new RenderTargetBitmap(new PixelSize(64, 64));
        using (var g = bitmap.CreateDrawingContext())
        {
            g.DrawEllipse(new SolidColorBrush(ThemePaint.ToColor(Palette.Accent)), null, new Point(32, 32), 30, 30);
            var pen = new Pen(Brushes.White, 6, lineCap: PenLineCap.Round);
            for (int i = 0; i < 4; i++)
            {
                double a = Math.PI / 4 * i;
                var d = new Vector(Math.Cos(a) * 18, Math.Sin(a) * 18);
                g.DrawLine(pen, new Point(32, 32) - d, new Point(32, 32) + d);
            }
        }
        var stream = new MemoryStream();
        bitmap.Save(stream);
        stream.Position = 0;
        return new WindowIcon(stream);
    }

    public void Dispose() => _icon.Dispose();
}
