using System.Text.Json;

namespace ClaudeSessionOverlay;

/// <summary>
/// 메뉴 막대(macOS 상태 항목)와 위젯이 그리는 요약판. 글자·색은 여기서 정해 보내고 받는 쪽(Swift)은 그리기만 한다.
/// 위젯은 샌드박스 안에서 돌아 오버레이와 직접 말할 수 없으므로 ~/.claude/session-hud/status.json을 읽는다.
/// </summary>
public sealed class BoardDoc
{
    public int Schema { get; set; } = 1;
    public double UpdatedAt { get; set; }
    public string Language { get; set; } = "ko";
    public string Summary { get; set; } = "";
    public bool IsQuiet { get; set; }
    public int Waiting { get; set; }
    public int Working { get; set; }
    public int Done { get; set; }
    public BoardUsage? Weekly { get; set; }
    public BoardUsage? FiveHour { get; set; }
    public List<BoardSession> Sessions { get; set; } = [];
    /// <summary>위젯·메뉴의 고정 문구(화면 언어대로)</summary>
    public Dictionary<string, string> Labels { get; set; } = [];
    /// <summary>상태 색(#RRGGBB): waiting, working, done</summary>
    public Dictionary<string, string> Colors { get; set; } = [];
}

public sealed class BoardUsage
{
    public double Percent { get; set; }
    public string Color { get; set; } = "";
    /// <summary>메뉴에 그대로 쓰는 글자: "17% · reset 1시간 31분"</summary>
    public string Text { get; set; } = "";
    public string? ResetsAt { get; set; }
}

public sealed class BoardSession
{
    public string Id { get; set; } = "";
    public string Title { get; set; } = "";
    /// <summary>waiting, working, done, idle, offline</summary>
    public string State { get; set; } = "";
    /// <summary>시간 없는 상태 이름: "작업 중". 위젯은 Since로 시계를 붙인다</summary>
    public string StateLabel { get; set; } = "";
    /// <summary>오버레이와 같은 상태 글자(경과 시간 포함): 메뉴용</summary>
    public string StatusText { get; set; } = "";
    /// <summary>작업 시작 또는 완료 시각(ms)</summary>
    public double? Since { get; set; }
    public string Detail { get; set; } = "";
    public string Folder { get; set; } = "";
    public string Model { get; set; } = "";
    public string Color { get; set; } = "";
    public double? Context { get; set; }
    public string? ContextColor { get; set; }
    public bool IsFavorite { get; set; }
    public int Tasks { get; set; }
    /// <summary>위젯에서 누르면 여는 주소: 데스크톱 앱 세션은 claude://, 터미널 세션은 오버레이(claude-overlay://)</summary>
    public string OpenUrl { get; set; } = "";
}

public static class Links
{
    public const string OverlayScheme = "claude-overlay";

    /// <summary>데스크톱 앱이 그 세션을 열고 앞으로 나온다</summary>
    public static string DeepLink(string hostSessionId) => $"claude://code/continue?session={Uri.EscapeDataString(hostSessionId)}";

    /// <summary>오버레이가 받아 세션을 앞으로 가져오고 확인 처리한다</summary>
    public static string OverlayOpen(string sessionId) => $"{OverlayScheme}://open?session={Uri.EscapeDataString(sessionId)}";

    public static string OverlayShow => $"{OverlayScheme}://show";

    /// <summary>claude-overlay://open?session=… 에서 세션 id. 다른 주소면 null</summary>
    public static string? SessionOf(Uri uri)
    {
        if (uri.Scheme != OverlayScheme || uri.Host != "open") return null;
        foreach (var pair in uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var kv = pair.Split('=', 2);
            if (kv.Length == 2 && kv[0] == "session") return Uri.UnescapeDataString(kv[1]);
        }
        return null;
    }
}

public static class StatusBoard
{
    private static string Hex(Tone tone) => $"#{Palette.Argb(tone) & 0xFFFFFF:X6}";

    public static BoardDoc Build(List<SessionRow> rows, List<SessionInfo> sessions)
    {
        var (summary, isQuiet) = Overview.Summary(rows);
        var doc = new BoardDoc
        {
            UpdatedAt = SessionStore.NowMs,
            Language = Loc.IsEnglish ? "en" : "ko",
            Summary = summary,
            IsQuiet = isQuiet,
            Waiting = rows.Count(r => r.State == RowState.Waiting),
            Working = rows.Count(r => r.State == RowState.Working),
            Done = rows.Count(r => r.State == RowState.Done),
            Labels = new()
            {
                ["title"] = Loc.Get("L_AppTitle"),
                ["empty"] = Loc.Get("L_Empty"),
                ["week"] = Loc.Get("L_Weekly"),
                ["session"] = Loc.Get("L_SessionLimit"),
                ["stale"] = Loc.T("오버레이가 꺼져 있어요", "The overlay isn't running"),
                ["waiting"] = Loc.T("입력 대기", "Needs input"),
                ["working"] = Loc.T("작업 중", "Working"),
                ["done"] = Loc.T("완료", "Done"),
                ["more"] = Loc.T("외 %d개", "+%d more"),
                ["updated"] = Loc.T("업데이트", "Updated"),
            },
            Colors = new()
            {
                ["waiting"] = Hex(Tone.Waiting),
                ["working"] = Hex(Tone.Working),
                ["done"] = Hex(Tone.Done),
            },
        };

        if (Overview.Weekly(sessions) is double week)
            doc.Weekly = new BoardUsage { Percent = Math.Round(week), Color = Hex(Palette.Usage(week)), Text = $"{week:0}%" };
        if (Overview.FiveHour(sessions) is { } five)
        {
            string? resetsAt = sessions.Where(x => x.Measure?.FiveHourPercent is not null)
                .OrderByDescending(x => x.MeasuredAt).Select(x => x.Measure!.FiveHourResetsAt).FirstOrDefault();
            doc.FiveHour = new BoardUsage { Percent = Math.Round(five.Percent), Color = Hex(Palette.Usage(five.Percent)), Text = $"{five.Percent:0}%{five.Reset}", ResetsAt = resetsAt };
        }

        foreach (var row in rows)
        {
            var info = row.Info;
            doc.Sessions.Add(new BoardSession
            {
                Id = row.SessionId,
                Title = row.Title,
                State = row.State.ToString().ToLowerInvariant(),
                StateLabel = row.State switch
                {
                    RowState.Waiting => Loc.T("입력 대기", "Needs input"),
                    RowState.Working => Loc.T("작업 중", "Working"),
                    RowState.Done => Loc.T("완료", "Done"),
                    RowState.Idle => Loc.T("대기", "Idle"),
                    _ => Loc.T("꺼짐", "Offline"),
                },
                StatusText = row.StatusText,
                Since = row.State switch
                {
                    RowState.Working => info.TurnStartedAt,
                    RowState.Done => info.LastCompletedAt is double c && c > 1000 ? c : null,
                    _ => null,
                },
                Detail = row.Detail,
                Folder = row.ShortPath,
                Model = info.Measure?.Model.Replace("claude-", "") ?? "",
                Color = Hex(row.DotTone),
                Context = row.ContextPercent is double p ? Math.Round(p) : null,
                ContextColor = row.HasContext ? Hex(row.ContextTone) : null,
                IsFavorite = row.IsFavorite,
                Tasks = row.Tasks.Count,
                OpenUrl = info.IsAlive && info.HostSessionId is string host && host.StartsWith("local_")
                    ? Links.DeepLink(host)
                    : Links.OverlayOpen(info.SessionId),
            });
        }
        return doc;
    }

    // 한글을 \uXXXX로 바꾸지 않는다(사람이 열어 봐도 읽히게)
    private static readonly JsonSerializerOptions Options = new(Json.Options) { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    public static string ToJson(BoardDoc doc) => JsonSerializer.Serialize(doc, Options);

    /// <summary>위젯을 다시 그릴지 판단하는 지문: 매초·매분 바뀌는 글자(경과 시간, 초기화까지 남은 시간)는 뺀다</summary>
    public static string Signature(BoardDoc doc) =>
        $"{doc.Language}|{doc.Weekly?.Percent}|{doc.FiveHour?.Percent}|{doc.FiveHour?.ResetsAt}|" +
        string.Join("|", doc.Sessions.Select(s => $"{s.Id}:{s.State}:{s.Title}:{s.Detail}:{s.Context}:{s.IsFavorite}:{s.Tasks}:{s.Since}"));
}

/// <summary>
/// 위젯이 읽는 status.json을 쓴다. 지문이 바뀌었을 때와, 위젯이 오버레이가 살아 있는지 알 수 있게 1분마다.
/// Write가 true를 돌려주면 위젯 타임라인을 다시 불러 달라고 한다(호출 쪽에서).
/// </summary>
public sealed class StatusFeed
{
    private static readonly TimeSpan Heartbeat = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan MinReload = TimeSpan.FromSeconds(5);

    public static string FilePath => Path.Combine(SessionStore.Root, "status.json");

    private string _signature = "";
    private DateTime _writtenAt;
    private DateTime _reloadedAt;
    private bool _isReloadPending;

    /// <summary>파일을 쓰고, 위젯을 다시 불러야 하면 true</summary>
    public bool Write(BoardDoc doc)
    {
        var now = DateTime.UtcNow;
        string signature = StatusBoard.Signature(doc);
        bool isChanged = signature != _signature;
        if (isChanged || now - _writtenAt >= Heartbeat)
        {
            try
            {
                Directory.CreateDirectory(SessionStore.Root);
                string temp = FilePath + ".tmp";
                File.WriteAllText(temp, StatusBoard.ToJson(doc));
                File.Move(temp, FilePath, overwrite: true);
                _signature = signature;
                _writtenAt = now;
                if (isChanged) _isReloadPending = true;
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }

        // 위젯 새로 고침은 시스템이 횟수를 제한한다: 너무 자주 부르지 않는다
        if (!_isReloadPending || now - _reloadedAt < MinReload) return false;
        _isReloadPending = false;
        _reloadedAt = now;
        return true;
    }
}
