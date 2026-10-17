using System.Diagnostics;

namespace ClaudeSessionOverlay;

/// <summary>세션이 도는 창을 앞으로 가져온다.</summary>
internal static class WindowFocuser
{
    public static bool Focus(SessionInfo session)
    {
        if (!session.IsAlive) return false;

        // 데스크톱 앱 세션: 딥 링크는 늘 첫 번째 분할 화면을 바꾸므로, 이미 어느 화면에 떠 있으면 딥 링크 없이
        // 그 화면의 입력창으로 포커스만 옮긴다. 떠 있지 않으면 딥 링크로 첫 화면에 연다
        if (session.HostSessionId is string host && host.StartsWith("local_"))
        {
            // 어느 화면에 떠 있는지는 앱이 바로바로 써 두는 설정 파일로 즉시 판단한다(몇 ms)
            var pos = DesktopPanes.PositionOf(host);
            if (pos is null) OpenDeepLink(DeepLink(host));

            IntPtr app = DesktopWindowOf(session);
            if (app == IntPtr.Zero) return true;
            BringToFront(app);
            string title = session.Title;
            PaneFocus = Task.Run(() => DesktopPanes.FocusPrompt(app, title, pos ?? new DesktopPanes.PanePos(0, 0), waitForTitle: pos is null));
            return true;
        }

        return session.Pid is int cliPid && (FocusAncestorWindow((uint)cliPid) || FocusConsoleOf((uint)cliPid));
    }

    private static string DeepLink(string host) => $"claude://code/continue?session={Uri.EscapeDataString(host)}";

    private static void OpenDeepLink(string url)
    {
        try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); }
        catch { /* 프로토콜이 등록되지 않은 경우: 창 전환만 한다 */ }
    }

    private static readonly Dictionary<int, IntPtr> DesktopWindows = [];

    /// <summary>데스크톱 세션이 도는 앱 창(pid마다 기억해 두고, 창이 사라졌으면 다시 찾는다)</summary>
    public static IntPtr DesktopWindowOf(SessionInfo session)
    {
        if (session.Pid is not int pid) return IntPtr.Zero;
        lock (DesktopWindows)
        {
            if (DesktopWindows.TryGetValue(pid, out var known) && Native.IsWindow(known) && Native.IsWindowVisible(known)) return known;
            IntPtr hwnd = AncestorWindow((uint)pid);
            if (hwnd != IntPtr.Zero) DesktopWindows[pid] = hwnd;
            return hwnd;
        }
    }

    /// <summary>진단용: 마지막으로 시작한 분할 화면 포커스 작업</summary>
    public static Task<bool> PaneFocus { get; private set; } = Task.FromResult(false);

    /// <summary>pid에서 부모 쪽으로 올라가며 처음 만나는 보이는 최상위 창을 연다</summary>
    private static bool FocusAncestorWindow(uint pid)
    {
        IntPtr hwnd = AncestorWindow(pid);
        return hwnd != IntPtr.Zero && BringToFront(hwnd);
    }

    private static IntPtr AncestorWindow(uint pid)
    {
        var table = Native.ProcessTable();
        var seen = new HashSet<uint>();
        for (uint current = pid; current != 0 && seen.Add(current); )
        {
            IntPtr hwnd = MainWindowOf(current);
            if (hwnd != IntPtr.Zero) return hwnd;
            if (!table.TryGetValue(current, out var entry)) break;
            current = entry.Parent;
        }
        return IntPtr.Zero;
    }

    /// <summary>conhost·Windows Terminal: 콘솔에 붙어서 그 콘솔 창(또는 그 소유 창)을 찾는다</summary>
    private static bool FocusConsoleOf(uint pid)
    {
        IntPtr hwnd = IntPtr.Zero;
        try
        {
            Native.FreeConsole();
            if (Native.AttachConsole(pid)) hwnd = Native.GetConsoleWindow();
        }
        finally { Native.FreeConsole(); }
        if (hwnd == IntPtr.Zero) return false;

        // Windows Terminal의 가상 콘솔 창은 터미널 창이 소유한다
        IntPtr owner = Native.GetAncestor(hwnd, Native.GA_ROOTOWNER);
        if (owner != IntPtr.Zero && owner != hwnd && Native.IsWindowVisible(owner)) hwnd = owner;
        return BringToFront(hwnd);
    }

    private static IntPtr MainWindowOf(uint pid)
    {
        IntPtr found = IntPtr.Zero;
        Native.EnumWindows((hwnd, _) =>
        {
            Native.GetWindowThreadProcessId(hwnd, out uint owner);
            if (owner != pid || !Native.IsWindowVisible(hwnd)) return true;
            if (Native.GetWindow(hwnd, Native.GW_OWNER) != IntPtr.Zero) return true;
            if (Native.GetWindowTextLength(hwnd) == 0) return true;
            found = hwnd;
            return false;
        }, IntPtr.Zero);
        return found;
    }

    public static bool BringToFront(IntPtr hwnd)
    {
        if (!Native.IsWindow(hwnd)) return false;
        // 창 크기는 그대로 둔다. SW_SHOW·SW_RESTORE는 Electron 창(Claude 데스크톱)의 최대화를 풀어 버리므로,
        // 최소화된 창만 원래 상태(최대화였으면 최대화)로 되살린다
        bool wasMaximized = Native.IsZoomed(hwnd);
        if (Native.IsIconic(hwnd))
        {
            var placement = new Native.WINDOWPLACEMENT { length = System.Runtime.InteropServices.Marshal.SizeOf<Native.WINDOWPLACEMENT>() };
            wasMaximized = Native.GetWindowPlacement(hwnd, ref placement) && (placement.flags & Native.WPF_RESTORETOMAXIMIZED) != 0;
            Native.ShowWindow(hwnd, wasMaximized ? Native.SW_SHOWMAXIMIZED : Native.SW_RESTORE);
        }

        // 다른 프로세스 창을 앞으로 올리려면 포그라운드 잠금을 풀어야 한다: Alt를 한 번 눌렀다 떼고, 입력 큐를 잠시 붙인다
        Native.keybd_event(Native.VK_MENU, 0, 0, UIntPtr.Zero);
        Native.keybd_event(Native.VK_MENU, 0, Native.KEYEVENTF_KEYUP, UIntPtr.Zero);

        IntPtr fg = Native.GetForegroundWindow();
        uint fgThread = Native.GetWindowThreadProcessId(fg, out _);
        uint self = Native.GetCurrentThreadId();
        bool attached = fgThread != 0 && fgThread != self && Native.AttachThreadInput(self, fgThread, true);
        try
        {
            Native.BringWindowToTop(hwnd);
            return Native.SetForegroundWindow(hwnd);
        }
        finally
        {
            if (attached) Native.AttachThreadInput(self, fgThread, false);
            KeepMaximized(hwnd, wasMaximized);
        }
    }

    /// <summary>앱이 딥 링크를 처리하며 창을 되돌려도 최대화가 풀리지 않게, 잠깐 지켜보다 다시 최대화한다</summary>
    private static void KeepMaximized(IntPtr hwnd, bool wasMaximized)
    {
        if (!wasMaximized) return;
        Task.Run(async () =>
        {
            for (int i = 0; i < 15; i++)
            {
                if (!Native.IsZoomed(hwnd) && !Native.IsIconic(hwnd) && Native.IsWindow(hwnd))
                    Native.ShowWindow(hwnd, Native.SW_SHOWMAXIMIZED);
                await Task.Delay(100);
            }
        });
    }
}
