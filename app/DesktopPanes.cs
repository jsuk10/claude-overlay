using System.IO;
using System.Text.Json;
using System.Windows.Automation;

namespace ClaudeSessionOverlay;

/// <summary>
/// 데스크톱 앱 Code 탭의 분할 화면.
/// 앱이 claude_desktop_config.json의 preferences.epitaxyPrefs["desktop-frame.paneStore.v1"]에 바로바로 써 둔다
/// (첫 화면 = lastPrimaryCodeSession (0,0), 나머지 = extraPanesByMode.code[]의 pos). 어느 화면에 포커스가 있는지는 남기지 않는다.
/// </summary>
internal static class DesktopPanes
{
    public readonly record struct PanePos(int Col, int Row);

    private static readonly string[] ConfigFiles =
    [
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Claude", "claude_desktop_config.json"),
        .. Directory.Exists(PackagesDir)
            ? Directory.GetDirectories(PackagesDir, "Claude_*")
                .Select(d => Path.Combine(d, "LocalCache", "Roaming", "Claude", "claude_desktop_config.json"))
            : [],
    ];

    private static string PackagesDir =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Packages");

    public static IEnumerable<string> ShownSessionIds() => ReadLayout().Keys;

    /// <summary>그 세션이 떠 있는 분할 화면 위치(없으면 null)</summary>
    public static PanePos? PositionOf(string hostSessionId) =>
        ReadLayout().TryGetValue(hostSessionId, out var pos) ? pos : null;

    /// <summary>앱이 파일을 쓰는 순간이면 읽기에 실패하므로 몇 번 다시 읽는다</summary>
    private static Dictionary<string, PanePos> ReadLayout()
    {
        for (int attempt = 0; ; attempt++)
        {
            var layout = TryReadLayout(out bool isComplete);
            if (isComplete || attempt >= 3) return layout;
            Thread.Sleep(60);
        }
    }

    private static Dictionary<string, PanePos> TryReadLayout(out bool isComplete)
    {
        isComplete = true;
        var layout = new Dictionary<string, PanePos>();
        foreach (var file in ConfigFiles)
        {
            if (!File.Exists(file)) continue;
            try
            {
                // 앱이 수시로 다시 쓰므로 공유 모드로 연다
                using var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                using var doc = JsonDocument.Parse(stream);
                if (!doc.RootElement.TryGetProperty("preferences", out var prefs)
                    || !prefs.TryGetProperty("epitaxyPrefs", out var epitaxy)
                    || !epitaxy.TryGetProperty("desktop-frame.paneStore.v1", out var store)
                    || !store.TryGetProperty("state", out var state)) return layout;

                if (state.TryGetProperty("extraPanesByMode", out var extra)
                    && extra.TryGetProperty("code", out var panes) && panes.ValueKind == JsonValueKind.Array)
                {
                    foreach (var pane in panes.EnumerateArray())
                    {
                        if (!pane.TryGetProperty("ref", out var r) || !r.TryGetProperty("id", out var id) || id.GetString() is not { } s) continue;
                        int col = 0, row = 0;
                        if (pane.TryGetProperty("pos", out var pos))
                        {
                            if (pos.TryGetProperty("col", out var c) && c.TryGetInt32(out var ci)) col = ci;
                            if (pos.TryGetProperty("row", out var w) && w.TryGetInt32(out var wi)) row = wi;
                        }
                        layout[s] = new PanePos(col, row);
                    }
                }

                // 첫 화면은 마지막에: 같은 세션이 둘 다 있으면 첫 화면으로 친다
                if (state.TryGetProperty("lastPrimaryCodeSession", out var primary)
                    && primary.ValueKind == JsonValueKind.Object
                    && primary.TryGetProperty("id", out var primaryId) && primaryId.GetString() is { } p)
                    layout[p] = new PanePos(0, 0);
                return layout;
            }
            catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException or InvalidOperationException)
            {
                isComplete = false; // 쓰는 중: 다음 파일, 그래도 안 되면 잠시 뒤 다시
            }
        }
        return layout;
    }

    // ── 분할 화면의 입력창에 포커스 ──────────────────────────────────────
    // 앱 화면은 UI Automation으로 읽는다. 분할 화면마다 머리에 '<제목>, 세션 이름 바꾸기' 버튼이 있고,
    // 그 아래에 프롬프트 입력창(ProseMirror)이 하나 있다.
    // 앱은 처음 조회될 때 접근성 정보를 켜느라 1초쯤 걸리므로, 오버레이에 마우스가 올라오면 미리 깨워 둔다.


    private static readonly Condition PaneCondition = new OrCondition(
        new AndCondition(
            new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Edit),
            new PropertyCondition(AutomationElement.IsKeyboardFocusableProperty, true)),
        new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Button));

    private static readonly object Gate = new();
    private static IntPtr _cachedHwnd;
    private static AutomationElement? _cachedWindow;
    private static DateTime _warmedAt;

    /// <summary>창 요소는 만드는 데 0.1초쯤 걸리므로 창마다 하나를 다시 쓴다</summary>
    private static AutomationElement WindowElement(IntPtr hwnd)
    {
        lock (Gate)
        {
            if (_cachedHwnd != hwnd || _cachedWindow is null)
            {
                _cachedWindow = AutomationElement.FromHandle(hwnd);
                _cachedHwnd = hwnd;
            }
            return _cachedWindow;
        }
    }

    /// <summary>앱의 접근성 정보를 미리 켜 두고 창 요소를 만들어 둔다(백그라운드, 몇 초에 한 번만)</summary>
    public static void Warm(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero || DateTime.Now - _warmedAt < TimeSpan.FromSeconds(5)) return;
        _warmedAt = DateTime.Now;
        Task.Run(() =>
        {
            try { WindowElement(hwnd).FindFirst(TreeScope.Descendants, PaneCondition); }
            catch (Exception e) when (IsTransient(e)) { ForgetWindow(); }
        });
    }

    private static void ForgetWindow()
    {
        lock (Gate) { _cachedWindow = null; _cachedHwnd = IntPtr.Zero; }
    }

    private readonly record struct Found(AutomationElement Element, System.Windows.Rect Rect);

    /// <summary>
    /// 제목이 title인 화면의 입력창에 포커스를 준다. 그 화면이 안 보이면 pos 위치(왼쪽부터 열, 위부터 행) 화면으로.
    /// waitForTitle이면 딥 링크로 화면이 바뀌어 그 제목이 나타날 때까지 잠깐 기다린다.
    /// 창이 앞으로 올라올 때 앱이 예전 포커스를 되돌리므로, 포커스가 실제로 옮겨졌는지 보며 다시 준다.
    /// </summary>
    public static bool FocusPrompt(IntPtr hwnd, string title, PanePos pos, bool waitForTitle)
    {
        // 머리 버튼 이름은 '<제목>, 세션 이름 바꾸기'(앱 언어에 따라 뒷부분이 다르다)
        string headerPrefix = title + ", ";
        var deadline = DateTime.Now + TimeSpan.FromMilliseconds(waitForTitle ? 2500 : 1800);
        while (DateTime.Now < deadline)
        {
            try
            {
                var prompts = new List<Found>();
                System.Windows.Rect? header = null;
                foreach (AutomationElement e in WindowElement(hwnd).FindAll(TreeScope.Descendants, PaneCondition))
                {
                    var info = e.Current;
                    if (info.ControlType == ControlType.Edit)
                    {
                        if ((info.ClassName ?? "").Contains("ProseMirror") && !info.BoundingRectangle.IsEmpty)
                            prompts.Add(new Found(e, info.BoundingRectangle));
                    }
                    else if (title != "" && header is null && (info.Name ?? "").StartsWith(headerPrefix, StringComparison.Ordinal))
                        header = info.BoundingRectangle;
                }

                // 아직 접근성 정보가 안 켜졌거나, 딥 링크로 연 화면이 아직 안 바뀌었으면 잠깐 뒤 다시
                bool isReady = prompts.Count > 0 && (header is not null || !waitForTitle);
                if (isReady)
                {
                    var target = header is { } h ? BelowHeader(prompts, h) : AtPosition(prompts, pos);
                    if (target.Current.HasKeyboardFocus) return true;
                    target.SetFocus();
                    Thread.Sleep(60);
                    if (target.Current.HasKeyboardFocus) return true;
                }
                else if (prompts.Count > 0 && DateTime.Now > deadline - TimeSpan.FromMilliseconds(400))
                {
                    waitForTitle = false; // 제목을 끝내 못 찾으면 위치로라도
                }
            }
            catch (Exception e) when (IsTransient(e)) { ForgetWindow(); }
            Thread.Sleep(80);
        }
        return false;
    }

    private static AutomationElement BelowHeader(List<Found> prompts, System.Windows.Rect header)
    {
        // 같은 열(가로 거리)이 먼저, 그다음 머리 바로 아래(세로 거리)
        var below = prompts.Where(p => p.Rect.Top > header.Top).ToList();
        if (below.Count == 0) below = prompts;
        return below.MinBy(p => Math.Abs(p.Rect.Left - header.Left) * 4 + Math.Abs(p.Rect.Top - header.Top)).Element;
    }

    private static AutomationElement AtPosition(List<Found> prompts, PanePos pos)
    {
        var columns = new List<List<Found>>();
        foreach (var p in prompts.OrderBy(x => x.Rect.Left))
        {
            var column = columns.LastOrDefault();
            // 입력창 너비의 절반 넘게 겹치면 같은 열(위아래로 나뉜 화면)
            if (column is not null && p.Rect.Left < column[0].Rect.Left + column[0].Rect.Width / 2) column.Add(p);
            else columns.Add([p]);
        }
        var col = columns[Math.Clamp(pos.Col, 0, columns.Count - 1)].OrderBy(x => x.Rect.Top).ToList();
        return col[Math.Clamp(pos.Row, 0, col.Count - 1)].Element;
    }

    private static bool IsTransient(Exception e) =>
        e is ElementNotAvailableException or InvalidOperationException or System.Runtime.InteropServices.COMException;
}
