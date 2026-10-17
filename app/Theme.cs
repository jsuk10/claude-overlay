using System.Windows;
using System.Windows.Media;
using Microsoft.Win32;

namespace ClaudeSessionOverlay;

/// <summary>테마 색(Palette.Theme)을 앱 리소스에 칠한다. XAML은 DynamicResource로 읽으므로 바꾸면 바로 다시 그려진다</summary>
internal static class Theme
{

    private static OverlayTheme _mode = OverlayTheme.Dark;
    private static bool _isWatchingSystem;

    public static OverlayTheme Mode => _mode;

    /// <summary>지금 라이트 색으로 그려져 있는가</summary>
    public static bool IsLight { get; private set; }

    public static void Apply(OverlayTheme mode)
    {
        _mode = mode;
        if (mode == OverlayTheme.System && !_isWatchingSystem)
        {
            // Windows의 앱 모드(다크/라이트)가 바뀌면 따라간다
            SystemEvents.UserPreferenceChanged += (_, e) =>
            {
                if (e.Category == UserPreferenceCategory.General && _mode == OverlayTheme.System)
                    Application.Current?.Dispatcher.Invoke(() => Paint(IsSystemLight()));
            };
            _isWatchingSystem = true;
        }
        Paint(mode switch
        {
            OverlayTheme.Light => true,
            OverlayTheme.System => IsSystemLight(),
            _ => false,
        });
    }

    private static void Paint(bool isLight)
    {
        IsLight = isLight;
        var resources = Application.Current.Resources;
        foreach (var (key, colors) in Palette.Theme)
        {
            var brush = new SolidColorBrush(ToColor(isLight ? colors.Light : colors.Dark));
            brush.Freeze();
            resources[key] = brush;
        }
    }

    private static bool IsSystemLight()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return key?.GetValue("AppsUseLightTheme") is int value && value != 0;
        }
        catch { return false; }
    }

    private static Color ToColor(uint argb) =>
        Color.FromArgb((byte)(argb >> 24), (byte)(argb >> 16), (byte)(argb >> 8), (byte)argb);
}
