namespace ClaudeSessionOverlay;

/// <summary>게임·영상처럼 앞 창이 모니터 전체를 덮고 있는지 판단한다.</summary>
internal static class FullscreenDetector
{
    private static readonly HashSet<string> ShellClasses =
    [
        "Progman", "WorkerW", "Shell_TrayWnd", "Shell_SecondaryTrayWnd",
        "Windows.UI.Core.CoreWindow", "XamlExplorerHostIslandWindow", "TopLevelWindowForOverflowXamlIsland",
    ];

    public static bool IsForegroundFullscreen(IntPtr ownWindow)
    {
        // D3D 전용 전체화면·프레젠테이션 모드는 셸이 직접 알려준다
        if (Native.SHQueryUserNotificationState(out int state) == 0 && (state == 3 || state == 4))
            return true;

        IntPtr fg = Native.GetForegroundWindow();
        if (fg == IntPtr.Zero || fg == ownWindow) return false;
        if (fg == Native.GetShellWindow() || fg == Native.GetDesktopWindow()) return false;
        if (ShellClasses.Contains(Native.ClassName(fg))) return false;
        if (!Native.GetWindowRect(fg, out var win)) return false;

        IntPtr monitor = Native.MonitorFromWindow(fg, Native.MONITOR_DEFAULTTONEAREST);
        var info = new Native.MONITORINFO { cbSize = System.Runtime.InteropServices.Marshal.SizeOf<Native.MONITORINFO>() };
        if (!Native.GetMonitorInfo(monitor, ref info)) return false;

        var m = info.rcMonitor;
        bool coversMonitor = win.Left <= m.Left && win.Top <= m.Top && win.Right >= m.Right && win.Bottom >= m.Bottom;
        if (!coversMonitor) return false;

        // 작업 표시줄 자동 숨김이면 최대화 창도 모니터를 덮는다: 제목 표시줄이 있는 최대화 창은 제외
        const int WS_CAPTION = 0x00C00000;
        const int GWL_STYLE = -16;
        bool hasCaption = (Native.GetWindowLong(fg, GWL_STYLE) & WS_CAPTION) == WS_CAPTION;
        return !hasCaption;
    }
}
