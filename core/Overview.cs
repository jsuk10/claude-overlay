using System.Text.RegularExpressions;

namespace ClaudeSessionOverlay;

/// <summary>세션 목록에서 화면에 보일 줄과 머리·맨 아래 줄 글자를 만든다(앱마다 같은 규칙)</summary>
public static partial class Overview
{
    public static List<SessionRow> BuildRows(List<SessionInfo> sessions, HashSet<string> favorites, HashSet<string>? expanded = null)
    {
        static RowState StateOf(SessionInfo s) =>
            !s.IsAlive ? RowState.Offline
            : s.Status == LiveStatus.Waiting ? RowState.Waiting
            : s.Status == LiveStatus.Working ? RowState.Working
            : (s.LastCompletedAt ?? 0) > s.SeenAt ? RowState.Done
            : RowState.Idle;

        static string TaskStatusOf(RowState state) => state switch
        {
            RowState.Working => "running",
            RowState.Waiting => "waiting",
            RowState.Done => "completed",
            _ => "idle",
        };

        // 데스크톱에서 다른 세션이 띄운 세션은 그 상위 세션 아래에 붙인다
        var byHost = sessions.Where(s => s.HostSessionId is not null && s.IsAlive)
            .GroupBy(s => s.HostSessionId!).ToDictionary(g => g.Key, g => g.First());
        var children = sessions
            .Where(s => s.IsAlive && s.ParentHostSessionId is string p && byHost.ContainsKey(p))
            .ToLookup(s => s.ParentHostSessionId!);
        var nested = children.SelectMany(g => g).Select(s => s.SessionId).ToHashSet();

        var rows = new List<SessionRow>();
        foreach (var info in sessions)
        {
            if (nested.Contains(info.SessionId) && !favorites.Contains(info.SessionId)) continue;

            var state = StateOf(info);
            var tasks = info.Tasks.Where(t => t.IsRunning).Select(t => new TaskRow { Doc = t }).ToList();
            if (info.HostSessionId is string host)
            {
                foreach (var child in children[host])
                {
                    var childState = StateOf(child);
                    if (childState == RowState.Idle && !favorites.Contains(child.SessionId)) continue;
                    tasks.Add(new TaskRow
                    {
                        Doc = new TaskDoc
                        {
                            Id = child.SessionId,
                            Kind = "session",
                            Description = child.Title != "" ? child.Title : Loc.T("파생 세션", "Side session"),
                            Status = TaskStatusOf(childState),
                        },
                        Session = child,
                    });
                }
            }

            bool isFavorite = favorites.Contains(info.SessionId);
            bool isVisible = isFavorite || state is RowState.Waiting or RowState.Working or RowState.Done || tasks.Count > 0;
            if (!isVisible) continue;

            rows.Add(new SessionRow
            {
                Info = info,
                State = state,
                IsFavorite = isFavorite,
                Tasks = tasks,
                IsTasksExpanded = expanded?.Contains(info.SessionId) ?? false,
            });
        }

        return rows
            .OrderBy(r => r.SortKey)
            .ThenByDescending(r => r.IsFavorite)
            .ThenByDescending(r => r.Info.StatusSince ?? r.Info.LastCompletedAt ?? 0)
            .ToList();
    }

    /// <summary>다시 그릴지 판단하는 목록 지문(경과 시간처럼 매초 바뀌는 글자는 뺀다)</summary>
    public static string Signature(List<SessionRow> rows) => string.Join("|", rows.Select(r =>
        $"{r.SessionId}:{r.State}:{r.IsFavorite}:{r.IsTasksExpanded}:{r.Title}:{r.Detail}:{r.ContextLabel}:{r.CompactLabel}:{r.CanRequestCompact}:" +
        string.Join(",", r.Tasks.Select(t => $"{t.Doc.Id}={t.Doc.Status}/{t.Doc.Description}"))));

    /// <summary>머리 줄 요약. 아무 일도 없으면 IsQuiet</summary>
    public static (string Text, bool IsQuiet) Summary(List<SessionRow> rows)
    {
        int working = rows.Count(r => r.State == RowState.Working);
        int waiting = rows.Count(r => r.State == RowState.Waiting);
        int done = rows.Count(r => r.State == RowState.Done);
        var parts = new List<string>();
        if (waiting > 0) parts.Add(Loc.T($"대기 {waiting}", $"{waiting} waiting"));
        if (working > 0) parts.Add(Loc.T($"작업 {working}", $"{working} working"));
        if (done > 0) parts.Add(Loc.T($"완료 {done}", $"{done} done"));
        return parts.Count == 0 ? (Loc.T("조용함", "All quiet"), true) : (string.Join(" · ", parts), false);
    }

    /// <summary>주간 사용량(계정 단위). 모르면 null</summary>
    public static double? Weekly(AccountUsage usage) => usage.Weekly?.Percent;

    public static string WeeklyTip(double percent) => Loc.T($"주간 사용량 {percent:0.#}%", $"Weekly usage {percent:0.#}%");

    public sealed record FiveHourUsage(double Percent, string Reset, string Tip, string? ResetsAt);

    /// <summary>5시간 세션 한도(계정 단위)와 초기화까지 남은 시간. 모르면 null</summary>
    public static FiveHourUsage? FiveHour(AccountUsage usage)
    {
        if (usage.FiveHour is not { } latest) return null;
        double p = latest.Percent;

        string reset = "";
        if (DateTimeOffset.TryParse(latest.ResetsAt, out var resetsAt))
        {
            var left = resetsAt - DateTimeOffset.Now;
            reset = left <= TimeSpan.Zero ? Loc.T(" · reset 곧", " · reset soon")
                : left.TotalHours >= 1 ? Loc.T($" · reset {(int)left.TotalHours}시간 {left.Minutes}분", $" · reset {(int)left.TotalHours}h {left.Minutes}m")
                : Loc.T($" · reset {Math.Max(1, left.Minutes)}분", $" · reset {Math.Max(1, left.Minutes)}m");
        }
        return new FiveHourUsage(p, reset, Loc.T($"5시간 세션 한도 {p:0.#}% 사용{reset}", $"5-hour session limit {p:0.#}% used{reset}"), latest.ResetsAt);
    }

    public static string FiveHourUnknownTip => Loc.T("5시간 세션 한도: Claude 데스크톱 앱이 사용량을 기록하면 표시됩니다",
        "5-hour session limit: shown once the Claude desktop app records usage");

    // ── 음성 알림 문구 ─────────────────────────────────────────────────

    public static string Spoken(Notice notice)
    {
        string name = SpokenName(notice.Session);
        return notice.Kind switch
        {
            NoticeKind.Done => Loc.T($"{name} 작업 종료", $"{name} done"),
            NoticeKind.Waiting => Loc.T($"{name} 확인 필요", $"{name} needs input"),
            _ => Loc.T($"{name} 컨텍스트 {notice.Session.ContextPercent ?? 90:0}퍼센트", $"{name} context {notice.Session.ContextPercent ?? 90:0} percent"),
        };
    }

    public static string TestSpoken => Loc.T("알림 테스트", "Notification test");

    public static string VoiceOnSpoken => Loc.T("TTS 켜짐", "TTS on");

    /// <summary>읽기 좋은 길이로 자른 세션 이름: 기호를 빼고 앞쪽 몇 단어만</summary>
    private static string SpokenName(SessionInfo session)
    {
        string title = session.Title != "" ? session.Title : Path.GetFileName(session.Cwd.TrimEnd('\\', '/'));
        title = Symbols().Replace(title, " ").Trim();
        if (title == "") return Loc.T("Claude 세션", "Claude session");
        return title.Length <= 24 ? title : title[..24];
    }

    [GeneratedRegex(@"[^\p{L}\p{N}\s]+")]
    private static partial Regex Symbols();
}
