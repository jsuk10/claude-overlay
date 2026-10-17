namespace ClaudeSessionOverlay;

/// <summary>목록의 점·글자·바 색 이름. 앱마다 자기 붓(Brush)으로 바꿔 그린다</summary>
public enum Tone { Working, Waiting, Done, Failed, Idle, Offline, UsageNormal, UsageWarn, UsageCritical }

/// <summary>오버레이 색 테마. 상단바 오른쪽 클릭 메뉴에서 고르고 overlay-state.json에 기억한다.</summary>
public enum OverlayTheme { Dark, Light, System }

public static class Palette
{
    /// <summary>
    /// 컨텍스트·사용량 바: Claude 데스크톱 우하단 사용량 링과 같은 색과 기준
    /// (--cds-fill-accent / --cds-fill-warning / --cds-fill-danger, 75%·90%에서 바뀐다)
    /// </summary>
    public const double WarnAt = 75;
    public const double CriticalAt = 90;

    public const uint Accent = 0xFFD97757;

    public static Tone Usage(double? percent) =>
        percent is not double p ? Tone.UsageNormal : p >= CriticalAt ? Tone.UsageCritical : p >= WarnAt ? Tone.UsageWarn : Tone.UsageNormal;

    public static double FillWidth(double? percent, double width) =>
        percent is double p ? Math.Round(Math.Clamp(p, 0, 100) / 100 * width) : 0;

    /// <summary>ARGB</summary>
    public static uint Argb(Tone tone) => tone switch
    {
        Tone.Working => 0xFFD97757,
        Tone.Waiting => 0xFFE8B931,
        Tone.Done => 0xFF5BB974,
        Tone.Failed => 0xFFE05A4E,
        Tone.Idle => 0xFF8A8680,
        Tone.Offline => 0xFF55524E,
        Tone.UsageNormal => 0xFF2A78D6,
        Tone.UsageWarn => 0xFFFAB219,
        _ => 0xFFD03B3B,
    };

    /// <summary>테마 색: 키마다 (다크, 라이트) ARGB</summary>
    public static readonly IReadOnlyDictionary<string, (uint Dark, uint Light)> Theme = new Dictionary<string, (uint, uint)>
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
}
