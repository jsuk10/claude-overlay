using System.Windows.Media;

namespace ClaudeSessionOverlay;

/// <summary>
/// 컨텍스트·주간 사용량 바의 색. Claude 데스크톱 우하단 사용량 링과 같은 색과 기준을 쓴다
/// (--cds-fill-accent / --cds-fill-warning / --cds-fill-danger, 75%·90%에서 바뀜, 트랙은 흰색 10%).
/// </summary>
internal static class UsagePalette
{
    public const double WarnAt = 75;
    public const double CriticalAt = 90;

    public static readonly Brush Normal = Make(0x2A, 0x78, 0xD6);
    public static readonly Brush Warn = Make(0xFA, 0xB2, 0x19);
    public static readonly Brush Critical = Make(0xD0, 0x3B, 0x3B);
    public static readonly Brush Track = Make(0xFF, 0xFF, 0xFF, 0x1A);

    public static Brush Fill(double? percent) =>
        percent is not double p ? Normal : p >= CriticalAt ? Critical : p >= WarnAt ? Warn : Normal;

    public static double FillWidth(double? percent, double width) =>
        percent is double p ? Math.Round(Math.Clamp(p, 0, 100) / 100 * width) : 0;

    private static Brush Make(byte r, byte g, byte b, byte a = 0xFF)
    {
        var brush = new SolidColorBrush(Color.FromArgb(a, r, g, b));
        brush.Freeze();
        return brush;
    }
}
