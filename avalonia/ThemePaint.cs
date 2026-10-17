using System.Globalization;
using Avalonia;
using Avalonia.Data.Converters;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using Avalonia.Platform;
using Avalonia.Styling;

namespace ClaudeSessionOverlay;

/// <summary>테마 색(Palette.Theme)을 앱 리소스에 칠한다. XAML은 DynamicResource로 읽으므로 바꾸면 바로 다시 그려진다</summary>
internal static class ThemePaint
{
    private static OverlayTheme _mode = OverlayTheme.Dark;
    private static bool _isWatchingSystem;

    /// <summary>지금 라이트 색으로 그려져 있는가</summary>
    public static bool IsLight { get; private set; }

    public static void Apply(OverlayTheme mode)
    {
        _mode = mode;
        var settings = Application.Current?.PlatformSettings;
        if (mode == OverlayTheme.System && !_isWatchingSystem && settings is not null)
        {
            // 운영체제의 다크/라이트가 바뀌면 따라간다
            settings.ColorValuesChanged += (_, _) =>
            {
                if (_mode == OverlayTheme.System) Paint(IsSystemLight());
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
        var app = Application.Current;
        if (app is null) return;
        app.RequestedThemeVariant = isLight ? ThemeVariant.Light : ThemeVariant.Dark; // 메뉴·툴팁 색
        foreach (var (key, colors) in Palette.Theme)
            app.Resources[key] = new ImmutableSolidColorBrush(ToColor(isLight ? colors.Light : colors.Dark));
    }

    private static bool IsSystemLight() =>
        Application.Current?.PlatformSettings?.GetColorValues().ThemeVariant == PlatformThemeVariant.Light;

    public static Color ToColor(uint argb) => Color.FromUInt32(argb);
}

/// <summary>코어의 색 이름(Tone)을 붓으로: {Binding DotTone, Converter={x:Static local:ToneBrush.Converter}}</summary>
internal sealed class ToneBrush : IValueConverter
{
    public static readonly ToneBrush Converter = new();

    private static readonly Dictionary<Tone, IBrush> Brushes =
        Enum.GetValues<Tone>().ToDictionary(t => t, t => (IBrush)new ImmutableSolidColorBrush(ThemePaint.ToColor(Palette.Argb(t))));

    public static IBrush Of(Tone tone) => Brushes[tone];

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is Tone tone ? Of(tone) : null;

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
