using System.ComponentModel;
using System.IO;
using System.Windows.Media;

namespace ClaudeSessionOverlay;

public enum RowState { Waiting, Working, Done, Idle, Offline }

/// <summary>오버레이 목록의 한 줄(세션)과 그 아래 백그라운드 작업 줄들</summary>
public sealed class SessionRow : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>경과 시간 글자만 다시 그린다(1초마다)</summary>
    public void Tick() => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(StatusText)));

    public required SessionInfo Info { get; init; }
    public required RowState State { get; init; }
    public required bool IsFavorite { get; init; }
    public required List<TaskRow> Tasks { get; init; }

    public string SessionId => Info.SessionId;
    public bool IsAlive => State != RowState.Offline;
    public bool HasTasks => Tasks.Count > 0;

    // 백그라운드 작업은 기본으로 접어 두고, 칩을 눌러 펼친다
    public required bool IsTasksExpanded { get; init; }
    public bool ShowTasks => HasTasks && IsTasksExpanded;
    public string TasksChevron => IsTasksExpanded ? "▾" : "▸";
    public string TasksCountText => $"{Tasks.Count}";
    public string TasksTip => IsTasksExpanded ? $"백그라운드 작업 {Tasks.Count}개 접기" : $"백그라운드 작업 {Tasks.Count}개 펼치기";

    public string Title => Info.Title != "" ? Info.Title : Folder;

    public string Folder => Info.Cwd == "" ? "(알 수 없는 폴더)"
        : Path.GetFileName(Info.Cwd.TrimEnd('\\', '/')) is { Length: > 0 } name ? name : Info.Cwd;

    /// <summary>드라이브 + 마지막 두 폴더: C:\…\work\Taycan-client</summary>
    public string ShortPath
    {
        get
        {
            if (Info.Cwd == "") return Folder;
            string full = Info.Cwd.TrimEnd('\\', '/');
            string root = Path.GetPathRoot(full) ?? "";
            var dirs = full[root.Length..].Split(['\\', '/'], StringSplitOptions.RemoveEmptyEntries);
            if (dirs.Length <= 2) return root.TrimEnd('\\', '/') + "\\" + string.Join('\\', dirs);
            return $@"{root.TrimEnd('\\', '/')}\…\{dirs[^2]}\{dirs[^1]}";
        }
    }

    public string StatusText => State switch
    {
        RowState.Waiting => "입력 대기",
        RowState.Working => Info.TurnStartedAt is double t ? $"작업 중 {Elapsed(t)}" : "작업 중",
        RowState.Done => Info.LastCompletedAt is double c && c > 1000 ? $"완료 · {Ago(c)}" : "완료",
        RowState.Idle => "대기",
        _ => "꺼짐",
    };

    public string Detail
    {
        get
        {
            // 모델 · 비용 · 경로: 같은 저장소를 C:·D:에 둬도 드라이브로 구분된다(줄이 넘치면 경로 끝이 잘린다)
            var parts = new List<string>();
            if (Info.Measure is { } m)
            {
                if (m.Model != "") parts.Add(m.Model.Replace("claude-", ""));
                if (m.CostUsd is double usd) parts.Add($"${usd:0.00}");
            }
            parts.Add(ShortPath);
            return string.Join(" · ", parts);
        }
    }

    // 토큰·컨텍스트: ☆ 바로 옆. %는 자동 압축 기준(100%가 되면 압축된다)
    public double? ContextPercent => Info.ContextPercent;
    public bool HasContext => ContextPercent is not null;
    public Brush ContextBrush => UsagePalette.Fill(ContextPercent);
    public string ContextLabel => ContextPercent is double p ? $"{p:0}%" : "";
    public string TokenLabel => Info.Measure?.ContextTokens is double used ? Kilo(used) : "";
    public string ContextTip
    {
        get
        {
            if (Info.Measure is not { } m) return "컨텍스트";
            double limit = m.CompactLimit ?? m.ContextWindow;
            string used = m.ContextTokens is double t ? Kilo(t) : "—";
            return $"컨텍스트 {used} / 자동 압축 {Kilo(limit)} ({ContextLabel}) · 모델 창 {Kilo(m.ContextWindow)}";
        }
    }

    private static string Kilo(double tokens) =>
        tokens >= 1_000_000 ? $"{tokens / 1_000_000:0.#}M" : $"{tokens / 1000:0}k";

    // 컨텍스트 경고: 거의 다 찼으면 줄에 압축 버튼을 띄운다(압축은 그 세션의 Mod가 한다)
    public bool IsContextCritical => IsAlive && ContextPercent >= SessionStore.ContextAlertPercent;
    public bool ShowCompact => HasContext && Info.CanCompact;
    public bool CanRequestCompact => Info.CanCompact && !Info.IsCompactRequested;
    public string CompactLabel => Info.IsCompactRequested ? "⋯" : "⇣";
    public Brush CompactBrush => IsContextCritical ? UsagePalette.Critical : Brushes2.Idle;
    public string CompactTip => Info.IsCompactRequested
        ? "압축 요청함: 그 세션의 턴이 끝나면 압축합니다"
        : $"눌러서 이 세션을 압축(/compact) · {ContextTip}";

    public Brush DotBrush => State switch
    {
        RowState.Waiting => Brushes2.Waiting,
        RowState.Working => Brushes2.Working,
        RowState.Done => Brushes2.Done,
        RowState.Idle => Brushes2.Idle,
        _ => Brushes2.Offline,
    };

    public bool IsPulsing => State is RowState.Working;
    public string Star => IsFavorite ? "★" : "☆";
    public Brush StarBrush => IsFavorite ? Brushes2.Waiting : Brushes2.Idle;
    public double Opacity => IsAlive ? 1.0 : 0.55;

    public int SortKey => State switch
    {
        RowState.Waiting => 0,
        RowState.Done => 1,
        RowState.Working => 2,
        RowState.Idle => 3,
        _ => 4,
    };

    internal static string Elapsed(double sinceMs)
    {
        var span = TimeSpan.FromMilliseconds(Math.Max(0, SessionStore.NowMs - sinceMs));
        return span.TotalHours >= 1 ? $"{(int)span.TotalHours}h {span.Minutes}m"
            : span.TotalMinutes >= 1 ? $"{span.Minutes}m {span.Seconds}s"
            : $"{span.Seconds}s";
    }

    internal static string Ago(double atMs)
    {
        var span = TimeSpan.FromMilliseconds(Math.Max(0, SessionStore.NowMs - atMs));
        return span.TotalMinutes < 1 ? "방금"
            : span.TotalHours < 1 ? $"{(int)span.TotalMinutes}분 전"
            : span.TotalDays < 1 ? $"{(int)span.TotalHours}시간 전"
            : $"{(int)span.TotalDays}일 전";
    }
}

public sealed class TaskRow
{
    public required TaskDoc Doc { get; init; }

    /// <summary>데스크톱에서 이 세션이 띄운 다른 세션(사이드 세션)이면 그 세션: 누르면 그쪽으로 간다</summary>
    public SessionInfo? Session { get; init; }

    public string Label => Doc.Kind switch
    {
        "shell" => $"› {Doc.Description}",
        "session" => $"⧉ {Doc.Description}",
        _ => $"↳ {Doc.Description}",
    };

    public string StatusText => Doc.Status switch
    {
        "running" => "실행 중",
        "pending" => "준비 중",
        "waiting" => "대기",
        "idle" => "유휴",
        "completed" => "완료",
        "failed" => "실패",
        "killed" => "중지됨",
        var other => other,
    };

    public Brush DotBrush => Doc.Status switch
    {
        "running" or "pending" => Brushes2.Working,
        "waiting" => Brushes2.Waiting,
        "completed" => Brushes2.Done,
        "failed" or "killed" => Brushes2.Failed,
        _ => Brushes2.Idle,
    };
}

internal static class Brushes2
{
    public static readonly Brush Working = Freeze(new SolidColorBrush(Color.FromRgb(0xD9, 0x77, 0x57)));
    public static readonly Brush Waiting = Freeze(new SolidColorBrush(Color.FromRgb(0xE8, 0xB9, 0x31)));
    public static readonly Brush Done = Freeze(new SolidColorBrush(Color.FromRgb(0x5B, 0xB9, 0x74)));
    public static readonly Brush Failed = Freeze(new SolidColorBrush(Color.FromRgb(0xE0, 0x5A, 0x4E)));
    public static readonly Brush Idle = Freeze(new SolidColorBrush(Color.FromRgb(0x8A, 0x86, 0x80)));
    public static readonly Brush Offline = Freeze(new SolidColorBrush(Color.FromRgb(0x55, 0x52, 0x4E)));

    private static Brush Freeze(Brush b) { b.Freeze(); return b; }
}
