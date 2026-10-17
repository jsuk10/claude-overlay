using System.ComponentModel;

namespace ClaudeSessionOverlay;

public enum RowState { Waiting, Working, Done, Idle, Offline }

/// <summary>오버레이 목록의 한 줄(세션)과 그 아래 백그라운드 작업 줄들. 색은 Tone으로만 알려 주고 그리는 건 앱이 한다</summary>
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
    public string TasksTip => IsTasksExpanded ? Loc.T($"백그라운드 작업 {Tasks.Count}개 접기", $"Collapse {Tasks.Count} background tasks") : Loc.T($"백그라운드 작업 {Tasks.Count}개 펼치기", $"Expand {Tasks.Count} background tasks");

    public string Title => Info.Title != "" ? Info.Title : Folder;

    public string Folder => Info.Cwd == "" ? Loc.T("(알 수 없는 폴더)", "(unknown folder)")
        : Path.GetFileName(Info.Cwd.TrimEnd('\\', '/')) is { Length: > 0 } name ? name : Info.Cwd;

    /// <summary>Windows는 드라이브 + 마지막 두 폴더(C:\…\work\Taycan-client), macOS·Linux는 ~/…/work/app</summary>
    public string ShortPath => Info.Cwd == "" ? Folder : ShortenPath(Info.Cwd);

    public static string ShortenPath(string path)
    {
        bool isWindowsPath = path.Length >= 2 && path[1] == ':' || path.StartsWith(@"\\");
        string full = path.TrimEnd('\\', '/');
        string root;
        char sep;
        if (isWindowsPath)
        {
            sep = '\\';
            root = full.Length >= 2 && full[1] == ':' ? full[..2] : "\\";
            full = full[root.Length..];
        }
        else
        {
            sep = '/';
            string home = PlatformPaths.Home.TrimEnd('/');
            if (home.Length > 1 && (full == home || full.StartsWith(home + "/")))
            {
                root = "~";
                full = full[home.Length..];
            }
            else root = "";
        }
        var dirs = full.Split(['\\', '/'], StringSplitOptions.RemoveEmptyEntries);
        if (dirs.Length <= 2) return root + sep + string.Join(sep, dirs);
        return $"{root}{sep}…{sep}{dirs[^2]}{sep}{dirs[^1]}";
    }

    public string StatusText => State switch
    {
        RowState.Waiting => Loc.T("입력 대기", "Needs input"),
        RowState.Working => Info.TurnStartedAt is double t ? Loc.T($"작업 중 {Elapsed(t)}", $"Working {Elapsed(t)}") : Loc.T("작업 중", "Working"),
        RowState.Done => Info.LastCompletedAt is double c && c > 1000 ? Loc.T($"완료 · {Ago(c)}", $"Done · {Ago(c)}") : Loc.T("완료", "Done"),
        RowState.Idle => Loc.T("대기", "Idle"),
        _ => Loc.T("꺼짐", "Offline"),
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
    public Tone ContextTone => Palette.Usage(ContextPercent);
    public string ContextLabel => ContextPercent is double p ? $"{p:0}%" : "";
    public string TokenLabel => Info.Measure?.ContextTokens is double used ? Kilo(used) : "";
    public string ContextTip
    {
        get
        {
            if (Info.Measure is not { } m) return Loc.T("컨텍스트", "Context");
            double limit = m.CompactLimit ?? m.ContextWindow;
            string used = m.ContextTokens is double t ? Kilo(t) : "—";
            return Loc.T($"컨텍스트 {used} / 자동 압축 {Kilo(limit)} ({ContextLabel}) · 모델 창 {Kilo(m.ContextWindow)}",
                $"Context {used} / auto-compact at {Kilo(limit)} ({ContextLabel}) · model window {Kilo(m.ContextWindow)}");
        }
    }

    private static string Kilo(double tokens) =>
        tokens >= 1_000_000 ? $"{tokens / 1_000_000:0.#}M" : $"{tokens / 1000:0}k";

    // 컨텍스트 경고: 거의 다 찼으면 줄에 압축 버튼을 띄운다(압축은 그 세션의 Mod가 한다)
    public bool IsContextCritical => IsAlive && ContextPercent >= SessionStore.ContextAlertPercent;
    public bool ShowCompact => HasContext && Info.CanCompact;
    public bool CanRequestCompact => Info.CanCompact && !Info.IsCompactRequested;
    public string CompactLabel => Info.IsCompactRequested ? "⋯" : "⇣";
    public Tone CompactTone => IsContextCritical ? Tone.UsageCritical : Tone.Idle;
    public string CompactTip => Info.IsCompactRequested
        ? Loc.T("압축 요청함: 그 세션의 턴이 끝나면 압축합니다", "Compact requested: runs when that session's turn ends")
        : Loc.T($"눌러서 이 세션을 압축(/compact) · {ContextTip}", $"Click to compact this session (/compact) · {ContextTip}");

    public Tone DotTone => State switch
    {
        RowState.Waiting => Tone.Waiting,
        RowState.Working => Tone.Working,
        RowState.Done => Tone.Done,
        RowState.Idle => Tone.Idle,
        _ => Tone.Offline,
    };

    public bool IsPulsing => State is RowState.Working;
    public string Star => IsFavorite ? "★" : "☆";
    public Tone StarTone => IsFavorite ? Tone.Waiting : Tone.Idle;
    public double Opacity => IsAlive ? 1.0 : 0.55;

    public int SortKey => State switch
    {
        RowState.Waiting => 0,
        RowState.Done => 1,
        RowState.Working => 2,
        RowState.Idle => 3,
        _ => 4,
    };

    public static string Elapsed(double sinceMs)
    {
        var span = TimeSpan.FromMilliseconds(Math.Max(0, SessionStore.NowMs - sinceMs));
        return span.TotalHours >= 1 ? $"{(int)span.TotalHours}h {span.Minutes}m"
            : span.TotalMinutes >= 1 ? $"{span.Minutes}m {span.Seconds}s"
            : $"{span.Seconds}s";
    }

    public static string Ago(double atMs)
    {
        var span = TimeSpan.FromMilliseconds(Math.Max(0, SessionStore.NowMs - atMs));
        return span.TotalMinutes < 1 ? Loc.T("방금", "just now")
            : span.TotalHours < 1 ? Loc.T($"{(int)span.TotalMinutes}분 전", $"{(int)span.TotalMinutes}m ago")
            : span.TotalDays < 1 ? Loc.T($"{(int)span.TotalHours}시간 전", $"{(int)span.TotalHours}h ago")
            : Loc.T($"{(int)span.TotalDays}일 전", $"{(int)span.TotalDays}d ago");
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
        "running" => Loc.T("실행 중", "running"),
        "pending" => Loc.T("준비 중", "pending"),
        "waiting" => Loc.T("대기", "waiting"),
        "idle" => Loc.T("유휴", "idle"),
        "completed" => Loc.T("완료", "done"),
        "failed" => Loc.T("실패", "failed"),
        "killed" => Loc.T("중지됨", "stopped"),
        var other => other,
    };

    public Tone DotTone => Doc.Status switch
    {
        "running" or "pending" => Tone.Working,
        "waiting" => Tone.Waiting,
        "completed" => Tone.Done,
        "failed" or "killed" => Tone.Failed,
        _ => Tone.Idle,
    };
}
