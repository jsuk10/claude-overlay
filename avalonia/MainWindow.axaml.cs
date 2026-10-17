using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using ClaudeSessionOverlay.Platform;

namespace ClaudeSessionOverlay;

public partial class MainWindow : Window
{
    private readonly SessionStore _store;
    private readonly IPlatform _platform = IPlatform.Current;
    private readonly Notifier _notifier;
    private readonly bool _isDemo;
    private readonly DispatcherTimer _refreshTimer = new() { Interval = TimeSpan.FromSeconds(1) };
    private readonly DispatcherTimer _savePositionTimer = new() { Interval = TimeSpan.FromMilliseconds(600) };
    private List<SessionRow> _rows = [];
    private string _signature = "";
    private string? _lastFiveHour;
    private bool _isMenuOpen;
    private bool _isPlaced;

    public event Action<string>? SummaryChanged;

    public bool IsUserHidden => _store.State.IsHidden;

    // 디자이너용
    public MainWindow() : this(new SessionStore(), isDemo: true) { }

    internal MainWindow(SessionStore store, bool isDemo)
    {
        _store = store;
        _isDemo = isDemo;
        _notifier = new Notifier(store, _platform);
        InitializeComponent();

        if (OperatingSystem.IsWindows())
        {
            FontFamily = new FontFamily("Segoe UI Variable Text, Segoe UI, Malgun Gothic");
            ThemeToggleButton.FontFamily = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets");
        }

        Header.PointerPressed += Header_PointerPressed;
        HeaderMenu.Opened += HeaderMenu_Opened;
        FiveHourTrack.SizeChanged += (_, _) => _lastFiveHour = null;
        _refreshTimer.Tick += (_, _) => Refresh();
        _savePositionTimer.Tick += (_, _) =>
        {
            _savePositionTimer.Stop();
            SaveWindowPosition();
        };
        PositionChanged += (_, _) =>
        {
            if (!_isPlaced) return;
            _savePositionTimer.Stop();
            _savePositionTimer.Start();
        };
        Opened += (_, _) => _platform.PrepareOverlay(this);
    }

    public void Start()
    {
        PlaceWindow();
        ApplyCollapsed();
        Refresh();
        UpdateVisibility();
        _refreshTimer.Start();
    }

    // ── 목록 ────────────────────────────────────────────────────────────

    private void Refresh()
    {
        if (_isMenuOpen) return;

        HashSet<string> favorites, expanded;
        List<SessionInfo> sessions;
        if (_isDemo)
        {
            (sessions, favorites, expanded) = DemoData.Load();
        }
        else
        {
            favorites = _store.LoadFavorites();
            sessions = _store.LoadSessions(favorites);
            expanded = _store.State.ExpandedTasks;
            _notifier.Tick();
            _notifier.Announce(_store.Notices);
        }
        var rows = Overview.BuildRows(sessions, favorites, expanded);

        string signature = Overview.Signature(rows);
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

        EmptyText.IsVisible = _rows.Count == 0;
        ShowWeekly(sessions);
        ShowFiveHour(sessions);
        ShowFooterButtons();

        var (summary, isQuiet) = Overview.Summary(rows);
        SummaryText.Text = isQuiet ? "" : summary;
        SummaryChanged?.Invoke(summary);
    }

    /// <summary>5시간 세션 한도(계정 단위): 맨 아래 바와 초기화까지 남은 시간</summary>
    private void ShowFiveHour(List<SessionInfo> sessions)
    {
        var usage = Overview.FiveHour(sessions);
        if (usage is null)
        {
            if (_lastFiveHour == "none") return;
            _lastFiveHour = "none";
            FiveHourFill.Width = 0;
            FiveHourPercentText.Text = "—";
            FiveHourPercentText.Bind(Avalonia.Controls.Documents.TextElement.ForegroundProperty, this.GetResourceObservable("SubtleBrush"));
            FiveHourResetText.Text = "";
            ToolTip.SetTip(FooterPanel, Overview.FiveHourUnknownTip);
            return;
        }

        string key = $"{usage.Percent}|{usage.Reset}|{FiveHourTrack.Bounds.Width}";
        if (key == _lastFiveHour) return;
        _lastFiveHour = key;

        var brush = ToneBrush.Of(Palette.Usage(usage.Percent));
        FiveHourFill.Width = Palette.FillWidth(usage.Percent, Math.Max(0, FiveHourTrack.Bounds.Width));
        FiveHourFill.Background = brush;
        FiveHourPercentText.Text = $"{usage.Percent:0}%";
        FiveHourPercentText.Foreground = brush;
        FiveHourResetText.Text = usage.Reset;
        ToolTip.SetTip(FooterPanel, usage.Tip);
    }

    /// <summary>주간 사용량은 계정 단위: 가장 최근에 잰 값</summary>
    private void ShowWeekly(List<SessionInfo> sessions)
    {
        if (Overview.Weekly(sessions) is not double p)
        {
            WeeklyPanel.IsVisible = false;
            return;
        }
        var brush = ToneBrush.Of(Palette.Usage(p));
        WeeklyFill.Width = Palette.FillWidth(p, 52);
        WeeklyFill.Background = brush;
        WeeklyText.Text = $"{p:0}%";
        WeeklyText.Foreground = brush;
        ToolTip.SetTip(WeeklyPanel, Overview.WeeklyTip(p));
        WeeklyPanel.IsVisible = true;
    }

    private void ForceRefresh()
    {
        _signature = "";
        Refresh();
    }

    // ── 행 동작 ──────────────────────────────────────────────────────────

    private static SessionRow? RowOf(object? sender) => (sender as StyledElement)?.DataContext as SessionRow;

    private static bool IsInsideButton(object? source) =>
        source is Visual visual && (visual is Button || visual.FindAncestorOfType<Button>() is not null);

    private void OpenSession(SessionInfo info)
    {
        if (_isDemo) return;
        _platform.Focus(info);
        _store.MarkSeen(info.SessionId);
        ForceRefresh();
    }

    private void Row_PointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (e.InitialPressMouseButton != MouseButton.Left || IsInsideButton(e.Source)) return;
        if (RowOf(sender) is { } row) OpenSession(row.Info);
    }

    private void Task_PointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (e.InitialPressMouseButton != MouseButton.Left) return;
        e.Handled = true;
        if ((sender as Control)?.DataContext is not TaskRow task) return;
        if (task.Session is { } child) { OpenSession(child); return; }
        // 서브에이전트·백그라운드 셸은 상위 세션을 연다
        var parent = (sender as Visual)?.GetVisualAncestors().OfType<Control>().Select(c => c.DataContext).OfType<SessionRow>().FirstOrDefault();
        if (parent is not null) OpenSession(parent.Info);
    }

    private void Compact_Click(object? sender, RoutedEventArgs e)
    {
        if (RowOf(sender) is not { } row) return;
        _store.RequestCompact(row.SessionId);
        ForceRefresh();
    }

    private void TasksToggle_Click(object? sender, RoutedEventArgs e)
    {
        if (RowOf(sender) is not { } row) return;
        var expanded = _store.State.ExpandedTasks;
        if (!expanded.Remove(row.SessionId)) expanded.Add(row.SessionId);
        _store.SaveState();
        ForceRefresh();
    }

    private void Star_Click(object? sender, RoutedEventArgs e)
    {
        if (RowOf(sender) is not { } row) return;
        _store.SetFavorite(row.SessionId, !row.IsFavorite);
        ForceRefresh();
    }

    private void RowMenu_Opened(object? sender, RoutedEventArgs e) => _isMenuOpen = true;

    private void RowMenu_Closed(object? sender, RoutedEventArgs e)
    {
        _isMenuOpen = false;
        ForceRefresh();
    }

    private void MenuOpen_Click(object? sender, RoutedEventArgs e)
    {
        if (RowOf(sender) is { } row) OpenSession(row.Info);
    }

    private void MenuSeen_Click(object? sender, RoutedEventArgs e)
    {
        if (RowOf(sender) is not { } row) return;
        _store.MarkSeen(row.SessionId);
        _isMenuOpen = false;
        ForceRefresh();
    }

    private void MenuFavorite_Click(object? sender, RoutedEventArgs e)
    {
        if (RowOf(sender) is not { } row) return;
        _store.SetFavorite(row.SessionId, !row.IsFavorite);
        _isMenuOpen = false;
        ForceRefresh();
    }

    private void MenuFolder_Click(object? sender, RoutedEventArgs e)
    {
        if (RowOf(sender) is { Info.Cwd: var cwd } && Directory.Exists(cwd)) _platform.OpenFolder(cwd);
    }

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

    private void UpdateVisibility()
    {
        bool shouldShow = !_store.State.IsHidden;
        if (shouldShow && !IsVisible) Show();
        else if (!shouldShow && IsVisible) Hide();
    }

    // ── 상단바 오른쪽 클릭 메뉴 ──────────────────────────────────────────

    private void HeaderMenu_Opened(object? sender, RoutedEventArgs e)
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

    private void ThemeItem_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem { Tag: string tag } || !Enum.TryParse<OverlayTheme>(tag, out var mode)) return;
        SetTheme(mode);
    }

    private void SetTheme(OverlayTheme mode)
    {
        _store.State.Theme = mode;
        _store.SaveState();
        ThemePaint.Apply(mode);
        ShowFooterButtons();
    }

    private void LanguageItem_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem { Tag: string tag } || !Enum.TryParse<UiLanguage>(tag, out var language)) return;
        _store.State.Language = language;
        _store.SaveState();
        Loc.Apply(language);
        _lastFiveHour = null;
        ApplyCollapsed();
        ForceRefresh();
    }

    private void HeaderCollapse_Click(object? sender, RoutedEventArgs e) => ToggleCollapsed();
    private void HeaderShowSeen_Click(object? sender, RoutedEventArgs e) => ShowSeenAgain();
    private void HeaderHide_Click(object? sender, RoutedEventArgs e) => ToggleUserHidden();

    // ── 맨 아래 줄 ──────────────────────────────────────────────────────

    /// <summary>지금 보이는 테마의 반대로 바꾼다(시스템 설정 따라가기였어도)</summary>
    private void ThemeToggle_Click(object? sender, RoutedEventArgs e) =>
        SetTheme(ThemePaint.IsLight ? OverlayTheme.Dark : OverlayTheme.Light);

    private void VoicePanel_PointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (e.InitialPressMouseButton == MouseButton.Left) ToggleVoice();
    }

    private void ShowFooterButtons()
    {
        bool isLight = ThemePaint.IsLight;
        // 누르면 바뀔 모습을 보여 준다: 다크일 때 해, 라이트일 때 달
        ThemeToggleButton.Content = OperatingSystem.IsWindows()
            ? (isLight ? "" : "")
            : (isLight ? "☾" : "☀");
        ToolTip.SetTip(ThemeToggleButton, isLight ? Loc.T("다크 모드로", "Switch to dark") : Loc.T("라이트 모드로", "Switch to light"));

        bool isVoiceOn = _store.State.IsVoiceOn;
        VoiceTrack.Bind(Border.BackgroundProperty, this.GetResourceObservable(isVoiceOn ? "AccentBrush" : "ChipHoverBrush"));
        if (isVoiceOn) VoiceKnob.Fill = Brushes.White;
        else VoiceKnob.Bind(Avalonia.Controls.Shapes.Shape.FillProperty, this.GetResourceObservable("SubtleBrush"));
        VoiceKnob.HorizontalAlignment = isVoiceOn ? HorizontalAlignment.Right : HorizontalAlignment.Left;
        ToolTip.SetTip(VoicePanel, isVoiceOn
            ? Loc.T("음성 알림(TTS) 켜짐: 눌러서 끄기", "Voice alerts (TTS) on: click to turn off")
            : Loc.T("음성 알림(TTS) 꺼짐: 눌러서 켜기", "Voice alerts (TTS) off: click to turn on"));
    }

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
        if (_store.State.IsVoiceOn) _notifier.Test();
        ShowFooterButtons();
    }

    public void TestNotice() => _notifier.Test();

    public void ExitApp()
    {
        _platform.StopSpeaking();
        SaveWindowPosition();
        App.Quit();
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
        Body.IsVisible = !isCollapsed;
        CollapseButton.Content = isCollapsed ? "▾" : "▴";
        ToolTip.SetTip(CollapseButton, isCollapsed ? Loc.T("펼치기", "Expand") : Loc.T("접기 (머리 줄만 보기)", "Collapse (header only)"));
    }

    private void Collapse_Click(object? sender, RoutedEventArgs e) => ToggleCollapsed();

    private void Minimize_Click(object? sender, RoutedEventArgs e)
    {
        if (!_store.State.IsHidden) ToggleUserHidden();
    }

    private void Close_Click(object? sender, RoutedEventArgs e) => ExitApp();

    internal void SaveSnapshot(string path)
    {
        double scale = RenderScaling;
        var size = Root.Bounds.Size;
        using var bitmap = new RenderTargetBitmap(
            new PixelSize((int)Math.Ceiling(size.Width * scale), (int)Math.Ceiling(size.Height * scale)), new Vector(96 * scale, 96 * scale));
        bitmap.Render(Root);
        bitmap.Save(path);
    }

    // ── 위치 ────────────────────────────────────────────────────────────

    // 카드 둘레의 그림자 여백(XAML의 Card Margin)
    private const double ShadowMargin = 12;

    private void Header_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed || IsInsideButton(e.Source)) return;
        if (e.ClickCount == 2)
        {
            ToggleCollapsed();
            return;
        }
        BeginMoveDrag(e);
    }

    private void PlaceWindow()
    {
        var screen = Screens.Primary ?? Screens.All.FirstOrDefault();
        if (screen is null) return;
        double scale = screen.Scaling;
        var work = screen.WorkingArea;
        // 기본: 주 화면 오른쪽 위, 카드가 가장자리에서 16만큼 떨어지게
        var fallback = new PixelPoint(
            (int)(work.Right - (Width - ShadowMargin + 16) * scale),
            (int)(work.Y + (16 - ShadowMargin) * scale));

        var position = fallback;
        if (_store.State.Left is double left && _store.State.Top is double top)
        {
            var saved = new PixelPoint((int)(left * scale), (int)(top * scale));
            // 모니터 구성이 바뀌어 화면 밖이면 기본 위치로
            var probe = saved + new PixelVector((int)(Width / 2 * scale), (int)(20 * scale));
            if (Screens.All.Any(s => s.Bounds.Contains(probe))) position = saved;
        }
        Position = position;
        _isPlaced = true;
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
        if (_isDemo) return;
        double scale = (Screens.ScreenFromWindow(this) ?? Screens.Primary)?.Scaling ?? 1;
        _store.State.Left = Position.X / scale;
        _store.State.Top = Position.Y / scale;
        _store.SaveState();
    }
}
