using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Threading;

namespace ClaudeSessionOverlay;

public partial class MainWindow : Window
{
    private readonly SessionStore _store;
    private readonly Notifier _notifier;
    private string? _lastFiveHour;
    private readonly DispatcherTimer _refreshTimer = new() { Interval = TimeSpan.FromSeconds(1) };
    private readonly DispatcherTimer _fullscreenTimer = new() { Interval = TimeSpan.FromMilliseconds(700) };
    private List<SessionRow> _rows = [];
    private string _signature = "";
    private bool _isFullscreenHidden;
    private bool _isMenuOpen;
    private IntPtr _hwnd;

    public event Action<string>? SummaryChanged;

    public bool IsUserHidden => _store.State.IsHidden;

    public MainWindow(SessionStore store, bool applyTheme = true)
    {
        _store = store;
        _notifier = new Notifier(store);
        if (applyTheme) ClaudeSessionOverlay.Theme.Apply(store.State.Theme);
        Loc.Apply(store.State.Language);
        InitializeComponent();
        FiveHourTrack.SizeChanged += (_, _) => _lastFiveHour = null;
        SourceInitialized += OnSourceInitialized;
        _refreshTimer.Tick += (_, _) => Refresh();
        _fullscreenTimer.Tick += (_, _) => CheckFullscreen();
    }

    public void Start()
    {
        PlaceWindow();
        ApplyCollapsed();
        Refresh();
        UpdateVisibility();
        _refreshTimer.Start();
        _fullscreenTimer.Start();
    }

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        _hwnd = new WindowInteropHelper(this).Handle;
        // Alt+Tab에 안 나오고, 눌러도 다른 창의 포커스를 뺏지 않는다
        int style = Native.GetWindowLong(_hwnd, Native.GWL_EXSTYLE);
        Native.SetWindowLong(_hwnd, Native.GWL_EXSTYLE, style | Native.WS_EX_TOOLWINDOW | Native.WS_EX_NOACTIVATE);
        _notifier.WatchDisplay(_hwnd);
    }

    // ── 목록 ────────────────────────────────────────────────────────────

    private void Refresh()
    {
        if (_isMenuOpen) return;

        HashSet<string> favorites, expanded;
        List<SessionInfo> sessions;
        if (IsDemo)
        {
            (sessions, favorites, expanded) = DemoData.Load();
        }
        else
        {
            favorites = _store.LoadFavorites();
            sessions = _store.LoadSessions(favorites);
            expanded = _store.State.ExpandedTasks;
            bool isFirstLoad = _lastSessions.Count == 0;
            _lastSessions = sessions;
            if (isFirstLoad) WarmDesktop();
            _notifier.Announce(_store.Notices);
        }
        var rows = BuildRows(sessions, favorites, expanded);

        string signature = string.Join("|", rows.Select(r =>
            $"{r.SessionId}:{r.State}:{r.IsFavorite}:{r.IsTasksExpanded}:{r.Title}:{r.Detail}:{r.ContextLabel}:{r.CompactLabel}:{r.CanRequestCompact}:" +
            string.Join(",", r.Tasks.Select(t => $"{t.Doc.Id}={t.Doc.Status}/{t.Doc.Description}"))));

        if (signature != _signature)
        {
            _signature = signature;
            _rows = rows;
            RowList.ItemsSource = _rows;
        }
        else
        {
            foreach (var row in _rows) row.Tick();
        }

        EmptyText.Visibility = _rows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        ShowWeekly(sessions);
        ShowFiveHour(sessions);
        ShowFooterButtons();

        int working = rows.Count(r => r.State == RowState.Working);
        int waiting = rows.Count(r => r.State == RowState.Waiting);
        int done = rows.Count(r => r.State == RowState.Done);
        var parts = new List<string>();
        if (waiting > 0) parts.Add(Loc.T($"대기 {waiting}", $"{waiting} waiting"));
        if (working > 0) parts.Add(Loc.T($"작업 {working}", $"{working} working"));
        if (done > 0) parts.Add(Loc.T($"완료 {done}", $"{done} done"));
        string summary = parts.Count == 0 ? Loc.T("조용함", "All quiet") : string.Join(" · ", parts);
        SummaryText.Text = parts.Count == 0 ? "" : summary;
        SummaryChanged?.Invoke(summary);

        KeepTopmost();
    }

    /// <summary>5시간 세션 한도(계정 단위): 맨 아래 바와 초기화까지 남은 시간</summary>
    private void ShowFiveHour(List<SessionInfo> sessions)
    {
        var latest = sessions
            .Where(x => x.Measure?.FiveHourPercent is not null)
            .OrderByDescending(x => x.MeasuredAt)
            .Select(x => x.Measure!)
            .FirstOrDefault();
        if (latest?.FiveHourPercent is not double p)
        {
            if (_lastFiveHour == "none") return;
            _lastFiveHour = "none";
            FiveHourFill.Width = 0;
            FiveHourPercentText.Text = "—";
            FiveHourPercentText.Foreground = (System.Windows.Media.Brush)FindResource("SubtleBrush");
            FiveHourResetText.Text = "";
            FooterPanel.ToolTip = Loc.T("5시간 세션 한도: Claude 데스크톱 앱이 사용량을 기록하면 표시됩니다",
                "5-hour session limit: shown once the Claude desktop app records usage");
            return;
        }

        string reset = "";
        if (DateTimeOffset.TryParse(latest.FiveHourResetsAt, out var resetsAt))
        {
            var left = resetsAt - DateTimeOffset.Now;
            reset = left <= TimeSpan.Zero ? Loc.T(" · reset 곧", " · reset soon")
                : left.TotalHours >= 1 ? Loc.T($" · reset {(int)left.TotalHours}시간 {left.Minutes}분", $" · reset {(int)left.TotalHours}h {left.Minutes}m")
                : Loc.T($" · reset {Math.Max(1, left.Minutes)}분", $" · reset {Math.Max(1, left.Minutes)}m");
        }

        string key = $"{p}|{reset}";
        if (key == _lastFiveHour) return;
        _lastFiveHour = key;

        var brush = ToneBrushes.Of(Palette.Usage(p));
        FiveHourFill.Width = Palette.FillWidth(p, Math.Max(0, FiveHourTrack.ActualWidth));
        FiveHourFill.Background = brush;
        FiveHourPercentText.Text = $"{p:0}%";
        FiveHourPercentText.Foreground = brush;
        FiveHourResetText.Text = reset;
        FooterPanel.ToolTip = Loc.T($"5시간 세션 한도 {p:0.#}% 사용{reset}", $"5-hour session limit {p:0.#}% used{reset}");
    }

    /// <summary>주간 사용량은 계정 단위: Mod가 가장 최근에 알려 준 값을 쓴다</summary>
    private void ShowWeekly(List<SessionInfo> sessions)
    {
        double? weekly = sessions
            .Where(x => x.Measure?.WeeklyPercent is not null)
            .OrderByDescending(x => x.MeasuredAt)
            .Select(x => x.Measure!.WeeklyPercent)
            .FirstOrDefault();
        if (weekly is not double p)
        {
            WeeklyPanel.Visibility = Visibility.Collapsed;
            return;
        }
        var brush = ToneBrushes.Of(Palette.Usage(p));
        WeeklyFill.Width = Palette.FillWidth(p, 52);
        WeeklyFill.Background = brush;
        WeeklyText.Text = $"{p:0}%";
        WeeklyText.Foreground = brush;
        WeeklyPanel.ToolTip = Loc.T($"주간 사용량 {p:0.#}%", $"Weekly usage {p:0.#}%");
        WeeklyPanel.Visibility = Visibility.Visible;
    }

    internal static List<SessionRow> BuildRows(List<SessionInfo> sessions, HashSet<string> favorites, HashSet<string>? expanded = null) =>
        Overview.BuildRows(sessions, favorites, expanded);

    private void ForceRefresh()
    {
        _signature = "";
        Refresh();
    }

    // ── 행 동작 ──────────────────────────────────────────────────────────

    private static SessionRow? RowOf(object sender)
    {
        if (sender is MenuItem { Parent: ContextMenu { PlacementTarget: FrameworkElement target } })
            return target.DataContext as SessionRow;
        return (sender as FrameworkElement)?.DataContext as SessionRow;
    }

    private List<SessionInfo> _lastSessions = [];

    /// <summary>시연용: 실제 세션 대신 DemoData의 가짜 세션을 보여 준다(--demo)</summary>
    public bool IsDemo { get; init; }

    /// <summary>마우스가 오버레이에 올라오면 클릭에 앞서 데스크톱 앱의 접근성 정보를 깨워 둔다</summary>
    private void Card_MouseEnter(object sender, MouseEventArgs e) => WarmDesktop();

    private void WarmDesktop()
    {
        var sessions = _lastSessions;
        Task.Run(() =>
        {
            var desktop = sessions.FirstOrDefault(x => x.IsAlive && x.HostSessionId?.StartsWith("local_") == true && x.Pid is not null);
            if (desktop is not null) DesktopPanes.Warm(WindowFocuser.DesktopWindowOf(desktop));
        });
    }

    private void OpenSession(SessionInfo info)
    {
        WindowFocuser.Focus(info);
        _store.MarkSeen(info.SessionId);
        ForceRefresh();
    }

    private void Row_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is DependencyObject source && FindParent<Button>(source) is not null) return;
        if (RowOf(sender) is { } row) OpenSession(row.Info);
    }

    private void Task_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        if ((sender as FrameworkElement)?.DataContext is not TaskRow task) return;
        if (task.Session is { } child) { OpenSession(child); return; }
        // 서브에이전트·백그라운드 셸은 상위 세션을 연다
        var parent = FindParent<FrameworkElement>((DependencyObject)sender, skipSelf: true, ofType: typeof(SessionRow));
        if (parent?.DataContext is SessionRow row) OpenSession(row.Info);
    }

    private void Compact_Click(object sender, RoutedEventArgs e)
    {
        if (RowOf(sender) is not { } row) return;
        _store.RequestCompact(row.SessionId);
        ForceRefresh();
    }

    private void TasksToggle_Click(object sender, RoutedEventArgs e)
    {
        if (RowOf(sender) is not { } row) return;
        var expanded = _store.State.ExpandedTasks;
        if (!expanded.Remove(row.SessionId)) expanded.Add(row.SessionId);
        _store.SaveState();
        ForceRefresh();
    }

    private void Star_Click(object sender, RoutedEventArgs e)
    {
        if (RowOf(sender) is not { } row) return;
        _store.SetFavorite(row.SessionId, !row.IsFavorite);
        ForceRefresh();
    }

    private void RowMenu_Opened(object sender, RoutedEventArgs e) => _isMenuOpen = true;

    private void RowMenu_Closed(object sender, RoutedEventArgs e)
    {
        _isMenuOpen = false;
        ForceRefresh();
    }

    private void MenuOpen_Click(object sender, RoutedEventArgs e)
    {
        if (RowOf(sender) is { } row) OpenSession(row.Info);
    }

    private void MenuSeen_Click(object sender, RoutedEventArgs e)
    {
        if (RowOf(sender) is not { } row) return;
        _store.MarkSeen(row.SessionId);
        _isMenuOpen = false;
        ForceRefresh();
    }

    private void MenuFavorite_Click(object sender, RoutedEventArgs e)
    {
        if (RowOf(sender) is not { } row) return;
        _store.SetFavorite(row.SessionId, !row.IsFavorite);
        _isMenuOpen = false;
        ForceRefresh();
    }

    private void MenuFolder_Click(object sender, RoutedEventArgs e)
    {
        if (RowOf(sender) is { Info.Cwd: var cwd } && Directory.Exists(cwd))
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{cwd}\"") { UseShellExecute = true });
    }

    private static T? FindParent<T>(DependencyObject child, bool skipSelf = false, Type? ofType = null) where T : FrameworkElement
    {
        DependencyObject? current = skipSelf ? ParentOf(child) : child;
        while (current is not null)
        {
            if (current is T match && (ofType is null || ofType.IsInstanceOfType(match.DataContext))) return match;
            current = ParentOf(current);
        }
        return null;
    }

    // Run 같은 글자 조각은 시각 트리에 없으니 논리 트리로 올라간다
    private static DependencyObject? ParentOf(DependencyObject node) =>
        node is System.Windows.Media.Visual or System.Windows.Media.Media3D.Visual3D
            ? System.Windows.Media.VisualTreeHelper.GetParent(node)
            : LogicalTreeHelper.GetParent(node);

    // ── 보이기·숨기기 ─────────────────────────────────────────────────────

    public void ToggleUserHidden()
    {
        _store.State.IsHidden = !_store.State.IsHidden;
        _store.SaveState();
        UpdateVisibility();
    }

    public void ShowSeenAgain()
    {
        _store.ClearSeen();
        if (_store.State.IsHidden) ToggleUserHidden();
        ForceRefresh();
    }

    // ── 상단바 오른쪽 클릭 메뉴 ──────────────────────────────────────────

    private void HeaderMenu_Opened(object sender, RoutedEventArgs e)
    {
        var mode = _store.State.Theme;
        ThemeDarkItem.IsChecked = mode == OverlayTheme.Dark;
        ThemeLightItem.IsChecked = mode == OverlayTheme.Light;
        ThemeSystemItem.IsChecked = mode == OverlayTheme.System;
        HeaderCollapseItem.Header = IsCollapsed ? Loc.T("목록 펼치기", "Expand list") : Loc.T("목록 접기", "Collapse list");
        var language = _store.State.Language;
        LangKoItem.IsChecked = language == UiLanguage.Korean;
        LangEnItem.IsChecked = language == UiLanguage.English;
        LangSystemItem.IsChecked = language == UiLanguage.System;
    }

    private void ThemeItem_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem { Tag: string tag } || !Enum.TryParse<OverlayTheme>(tag, out var mode)) return;
        SetTheme(mode);
    }

    private void SetTheme(OverlayTheme mode)
    {
        _store.State.Theme = mode;
        _store.SaveState();
        ClaudeSessionOverlay.Theme.Apply(mode);
        ShowFooterButtons();
    }

    // ── 맨 아래 줄 버튼 ──────────────────────────────────────────────────

    private const string SunGlyph = "\uE706";   // 밝기
    private const string MoonGlyph = "\uE708";  // 달

    /// <summary>지금 보이는 테마의 반대로 바꾼다(Windows 설정 따라가기였어도)</summary>
    private void ThemeToggle_Click(object sender, RoutedEventArgs e) =>
        SetTheme(ClaudeSessionOverlay.Theme.IsLight ? OverlayTheme.Dark : OverlayTheme.Light);

    private void VoiceToggle_Click(object sender, RoutedEventArgs e) => ToggleVoice();

    /// <summary>'TTS' 글자를 눌러도 스위치처럼</summary>
    private void VoicePanel_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is DependencyObject source && FindParent<System.Windows.Controls.Primitives.ToggleButton>(source) is not null) return;
        ToggleVoice();
    }

    private void ShowFooterButtons()
    {
        bool isLight = ClaudeSessionOverlay.Theme.IsLight;
        // 누르면 바뀔 모습을 보여 준다: 다크일 때 해, 라이트일 때 달
        ThemeToggleButton.Content = isLight ? MoonGlyph : SunGlyph;
        ThemeToggleButton.ToolTip = isLight ? Loc.T("다크 모드로", "Switch to dark") : Loc.T("라이트 모드로", "Switch to light");

        bool isVoiceOn = _store.State.IsVoiceOn;
        VoiceToggle.IsChecked = isVoiceOn;
        VoicePanel.ToolTip = isVoiceOn ? Loc.T("음성 알림(TTS) 켜짐: 눌러서 끄기", "Voice alerts (TTS) on: click to turn off")
                                       : Loc.T("음성 알림(TTS) 꺼짐: 눌러서 켜기", "Voice alerts (TTS) off: click to turn on");
    }

    private void LanguageItem_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem { Tag: string tag } || !Enum.TryParse<UiLanguage>(tag, out var language)) return;
        _store.State.Language = language;
        _store.SaveState();
        Loc.Apply(language);
        _lastFiveHour = null;
        ApplyCollapsed();
        ForceRefresh();
    }

    private void HeaderCollapse_Click(object sender, RoutedEventArgs e) => ToggleCollapsed();
    private void HeaderShowSeen_Click(object sender, RoutedEventArgs e) => ShowSeenAgain();
    private void HeaderHide_Click(object sender, RoutedEventArgs e) => ToggleUserHidden();

    public bool IsSoundOn => _store.State.IsSoundOn;
    public bool IsVoiceOn => _store.State.IsVoiceOn;

    public void ToggleSound()
    {
        _store.State.IsSoundOn = !_store.State.IsSoundOn;
        _store.SaveState();
    }

    public void ToggleVoice()
    {
        _store.State.IsVoiceOn = !_store.State.IsVoiceOn;
        _store.SaveState();
        if (_store.State.IsVoiceOn) _notifier.SayVoiceOn();
        ShowFooterButtons();
    }

    public void TestNotice() => _notifier.Test();

    public void ExitApp()
    {
        _notifier.Dispose();
        SaveWindowPosition();
        Application.Current.Shutdown();
    }

    private void CheckFullscreen()
    {
        bool isFullscreen = FullscreenDetector.IsForegroundFullscreen(_hwnd);
        if (isFullscreen == _isFullscreenHidden) return;
        _isFullscreenHidden = isFullscreen;
        UpdateVisibility();
    }

    private void UpdateVisibility()
    {
        bool shouldShow = !_store.State.IsHidden && !_isFullscreenHidden;
        if (shouldShow && !IsVisible) { Show(); KeepTopmost(); }
        else if (!shouldShow && IsVisible) Hide();
    }

    private void KeepTopmost()
    {
        if (_hwnd == IntPtr.Zero || !IsVisible) return;
        Native.SetWindowPos(_hwnd, Native.HWND_TOPMOST, 0, 0, 0, 0,
            Native.SWP_NOMOVE | Native.SWP_NOSIZE | Native.SWP_NOACTIVATE);
    }

    // ── 접기 ────────────────────────────────────────────────────────────

    public bool IsCollapsed => _store.State.IsCollapsed;

    public void ToggleCollapsed()
    {
        _store.State.IsCollapsed = !_store.State.IsCollapsed;
        _store.SaveState();
        ApplyCollapsed();
    }

    private void ApplyCollapsed()
    {
        bool isCollapsed = _store.State.IsCollapsed;
        Body.Visibility = isCollapsed ? Visibility.Collapsed : Visibility.Visible;
        CollapseButton.Content = isCollapsed ? "▾" : "▴";
        CollapseButton.ToolTip = isCollapsed ? Loc.T("펼치기", "Expand") : Loc.T("접기 (머리 줄만 보기)", "Collapse (header only)");
    }

    private void Collapse_Click(object sender, RoutedEventArgs e) => ToggleCollapsed();

    private void Minimize_Click(object sender, RoutedEventArgs e)
    {
        if (!_store.State.IsHidden) ToggleUserHidden();
    }

    private void Close_Click(object sender, RoutedEventArgs e) => ExitApp();

    internal void SaveSnapshot(string path)
    {
        var root = (FrameworkElement)Content;
        var dpi = System.Windows.Media.VisualTreeHelper.GetDpi(this);
        var bmp = new System.Windows.Media.Imaging.RenderTargetBitmap(
            (int)(root.ActualWidth * dpi.DpiScaleX), (int)(root.ActualHeight * dpi.DpiScaleY),
            dpi.PixelsPerInchX, dpi.PixelsPerInchY, System.Windows.Media.PixelFormats.Pbgra32);
        bmp.Render(root);
        var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
        encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bmp));
        using var file = File.Create(path);
        encoder.Save(file);
    }

    // ── 위치 ────────────────────────────────────────────────────────────

    private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is DependencyObject source && FindParent<Button>(source) is not null) return;
        if (e.ClickCount == 2)
        {
            ToggleCollapsed();
            return;
        }
        DragMove();
        SaveWindowPosition();
    }

    private void PlaceWindow()
    {
        var work = SystemParameters.WorkArea;
        double left = _store.State.Left ?? work.Right - Width - 16;
        double top = _store.State.Top ?? work.Top + 16;
        // 모니터 구성이 바뀌어 화면 밖이면 기본 위치로
        bool isOnScreen = left > SystemParameters.VirtualScreenLeft - Width + 40
            && left < SystemParameters.VirtualScreenLeft + SystemParameters.VirtualScreenWidth - 40
            && top > SystemParameters.VirtualScreenTop - 10
            && top < SystemParameters.VirtualScreenTop + SystemParameters.VirtualScreenHeight - 40;
        Left = isOnScreen ? left : work.Right - Width - 16;
        Top = isOnScreen ? top : work.Top + 16;
    }

    public void ResetPosition()
    {
        _store.State.Left = null;
        _store.State.Top = null;
        PlaceWindow();
        SaveWindowPosition();
    }

    private void SaveWindowPosition()
    {
        _store.State.Left = Left;
        _store.State.Top = Top;
        _store.SaveState();
    }
}
