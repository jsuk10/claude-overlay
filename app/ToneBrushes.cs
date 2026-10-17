using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace ClaudeSessionOverlay;

/// <summary>코어의 색 이름(Tone)을 WPF 붓으로</summary>
internal static class ToneBrushes
{
    private static readonly Dictionary<Tone, Brush> Brushes = Enum.GetValues<Tone>().ToDictionary(t => t, t =>
    {
        uint argb = Palette.Argb(t);
        var brush = new SolidColorBrush(Color.FromArgb((byte)(argb >> 24), (byte)(argb >> 16), (byte)(argb >> 8), (byte)argb));
        brush.Freeze();
        return (Brush)brush;
    });

    public static Brush Of(Tone tone) => Brushes[tone];
}

/// <summary>XAML 바인딩용: {Binding DotTone, Converter={StaticResource ToneBrush}}</summary>
public sealed class ToneBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is Tone tone ? ToneBrushes.Of(tone) : Binding.DoNothing;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
