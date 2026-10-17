using System.Windows;
using System.Windows.Media;
using Microsoft.Win32;

namespace ClaudeSessionOverlay;

/// <summary>오버레이 색 테마. 상단바 오른쪽 클릭 메뉴에서 고르고 overlay-state.json에 기억한다.</summary>
public enum OverlayTheme { Dark, Light, System }

internal static class Theme
{
    // 키마다 (다크, 라이트) 색. XAML은 DynamicResource로 읽으므로 바꾸면 바로 다시 그려진다
    private static readonly Dictionary<string, (uint Dark, uint Light)> Palette = new()
    {
        ["CardBrush"] = (0xEE1C1B1A, 0xF2FAF9F7),
        ["CardBorderBrush"] = (0x33FFFFFF, 0x26000000),
        ["TextBrush"] = (0xFFF2EFEA, 0xFF1F1E1C),
        ["SubtleBrush"] = (0xFF9C978F, 0xFF6B665F),
        ["TaskTextBrush"] = (0xFFD6D2CB, 0xFF3D3A36),
        ["HoverBrush"] = (0x1FFFFFFF, 0x14000000),
        ["ChipBrush"] = (0x14FFFFFF, 0x0F000000),
        ["ChipHoverBrush"] = (0x2EFFFFFF, 0x21000000),
        // 사용량 바의 빈 칸: 데스크톱 앱과 같이 다크는 흰색 10%, 라이트는 검정 10%
        ["TrackBrush"] = (0x1AFFFFFF, 0x1A000000),
    };

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
        foreach (var (key, colors) in Palette)
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
