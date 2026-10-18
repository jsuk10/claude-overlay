using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;

namespace ClaudeSessionOverlay.Platform;

/// <summary>
/// libClaudeOverlayMac.dylib(macos/Bridge, Swift): 메뉴 막대 상태 항목과 위젯 새로 고침.
/// scripts/make-macos-app.sh가 .app에 함께 넣는다. 없으면(dotnet run 등) IsAvailable이 false라 Avalonia 트레이를 쓴다.
/// 모두 주 스레드(Avalonia UI 스레드)에서 부른다.
/// </summary>
[SupportedOSPlatform("macos")]
internal static unsafe class MacBridge
{
    private const string Library = "libClaudeOverlayMac.dylib";

    private static readonly delegate* unmanaged<delegate* unmanaged<byte*, void>, void> StartFn;
    private static readonly delegate* unmanaged<byte*, void> UpdateFn;
    private static readonly delegate* unmanaged<void> StopFn;
    private static readonly delegate* unmanaged<void> ReloadWidgetsFn;
    private static Action<string>? _onAction;

    public static bool IsAvailable { get; }

    static MacBridge()
    {
        if (!NativeLibrary.TryLoad(Library, typeof(MacBridge).Assembly, DllImportSearchPath.AssemblyDirectory, out var lib)) return;
        if (!NativeLibrary.TryGetExport(lib, "cso_status_start", out var start)
            || !NativeLibrary.TryGetExport(lib, "cso_status_update", out var update)
            || !NativeLibrary.TryGetExport(lib, "cso_status_stop", out var stop)
            || !NativeLibrary.TryGetExport(lib, "cso_widgets_reload", out var reload)) return;
        StartFn = (delegate* unmanaged<delegate* unmanaged<byte*, void>, void>)start;
        UpdateFn = (delegate* unmanaged<byte*, void>)update;
        StopFn = (delegate* unmanaged<void>)stop;
        ReloadWidgetsFn = (delegate* unmanaged<void>)reload;
        IsAvailable = true;
    }

    /// <summary>메뉴 막대에 상태 항목을 띄운다. 메뉴에서 고른 항목의 id가 onAction으로 온다</summary>
    public static void Start(Action<string> onAction)
    {
        _onAction = onAction;
        StartFn(&OnAction);
    }

    /// <summary>{ board, commands } JSON(Swift의 MenuPayload)</summary>
    public static void Update(string json)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(json + "\0");
        fixed (byte* p = bytes) UpdateFn(p);
    }

    public static void Stop() => StopFn();

    public static void ReloadWidgets() => ReloadWidgetsFn();

    [UnmanagedCallersOnly]
    private static void OnAction(byte* id)
    {
        try { _onAction?.Invoke(Marshal.PtrToStringUTF8((IntPtr)id) ?? ""); }
        catch { /* 메뉴 처리 중 예외가 네이티브 쪽으로 넘어가면 앱이 죽는다 */ }
    }
}
