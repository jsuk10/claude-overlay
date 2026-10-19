using System.IO;
using System.Text.Json;

namespace ClaudeSessionOverlay;

/// <summary>한도 하나(주간 또는 5시간)를 어디서 언제 쟀는지</summary>
public sealed record UsageReading(double Percent, string? ResetsAt, double At, string Source);

/// <summary>
/// 주간·5시간 사용량은 세션이 아니라 계정에 붙는다. 출처마다 잰 시각이 달라 한도마다 가장 최근 값을 고른다.
///   - Claude 데스크톱 앱이 5분쯤마다 남기는 plan-usage-history.json(Mod 없이)
///   - session-hud Mod 문서(그 세션이 응답을 받을 때마다, 초기화 시각 포함)
/// </summary>
public sealed class AccountUsage
{
    // 창 길이보다 오래 전에 잰 값은 그사이 초기화됐으니 믿지 않는다
    private const double WeekMs = 7 * 24 * 3_600_000.0;
    private const double FiveHourMs = 5 * 3_600_000.0;

    public UsageReading? Weekly { get; private set; }
    public UsageReading? FiveHour { get; private set; }

    public void Offer(string source, double at, double? weekly, string? weeklyResetsAt, double? fiveHour, string? fiveHourResetsAt)
    {
        double now = SessionStore.NowMs;
        if (weekly is double w && now - at < WeekMs && at > (Weekly?.At ?? 0))
            Weekly = new UsageReading(w, weeklyResetsAt, at, source);
        if (fiveHour is double f && now - at < FiveHourMs && at > (FiveHour?.At ?? 0))
            FiveHour = new UsageReading(f, fiveHourResetsAt, at, source);
    }

    /// <summary>진단용(--dump) 한 줄</summary>
    public override string ToString() => $"week={Describe(Weekly)} 5h={Describe(FiveHour)}";

    private static string Describe(UsageReading? r) => r is null ? "—"
        : $"{r.Percent:0.#}% ({r.Source}, {DateTimeOffset.FromUnixTimeMilliseconds((long)r.At).ToLocalTime():MM-dd HH:mm:ss})";

    // ── 데스크톱 앱 표본 ─────────────────────────────────────────────

    private static (double? FiveHour, double? Weekly, double At) _desktop;
    private static DateTime _desktopLoadedAt;

    /// <summary>데스크톱 앱이 남긴 마지막 표본(plan-usage-history.json)</summary>
    public static (double? FiveHour, double? Weekly, double At) Desktop(IEnumerable<string> desktopDataDirs)
    {
        if (DateTime.UtcNow - _desktopLoadedAt < TimeSpan.FromSeconds(20)) return _desktop;
        _desktopLoadedAt = DateTime.UtcNow;
        foreach (var dir in desktopDataDirs)
        {
            var file = Path.Combine(dir, "plan-usage-history.json");
            if (!File.Exists(file)) continue;
            try
            {
                using var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                using var doc = JsonDocument.Parse(stream);
                if (!doc.RootElement.TryGetProperty("samples", out var samples) || samples.GetArrayLength() == 0) continue;
                var sample = samples[samples.GetArrayLength() - 1];
                double at = sample.TryGetProperty("t", out var t) && t.TryGetDouble(out var tv) ? tv : 0;
                if (at <= _desktop.At) continue;
                double? fh = null, sd = null;
                if (sample.TryGetProperty("u", out var u))
                {
                    if (u.TryGetProperty("fh", out var f) && f.TryGetDouble(out var fv)) fh = fv;
                    if (u.TryGetProperty("sd", out var s) && s.TryGetDouble(out var sv)) sd = sv;
                }
                _desktop = (fh, sd, at);
            }
            catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException or InvalidOperationException) { }
        }
        return _desktop;
    }
}
