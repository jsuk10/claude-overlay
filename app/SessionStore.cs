using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace ClaudeSessionOverlay;

/// <summary>
/// 세 출처를 합쳐 세션 목록을 만든다.
///   1) %USERPROFILE%\.claude\sessions\&lt;pid&gt;.json — Claude Code가 직접 쓰는 실행 중 세션 목록(필수)
///   2) %USERPROFILE%\.claude\session-hud\sessions\&lt;id&gt;.json — session-hud Mod(있으면 입력 대기·비용·백그라운드 작업)
///   3) 데스크톱 앱의 claude-code-sessions\…\local_&lt;id&gt;.json — 제목, 마지막 확인 시각, 파생 세션
/// </summary>
public sealed partial class SessionStore
{
    // Mod 하트비트(10초)를 이만큼 놓치면 그 Mod 문서는 믿지 않는다
    private const double HudStaleMs = 45_000;
    // 백그라운드 서브에이전트 기록이 이만큼 조용하면 끝난 것으로 본다
    private const double SubagentQuietMs = 60_000;

    private static readonly string Home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
    private static readonly string ClaudeDir = Path.Combine(Home, ".claude");
    public static readonly string Root = Path.Combine(ClaudeDir, "session-hud");

    private static string RegistryDir => Path.Combine(ClaudeDir, "sessions");
    private static string ProjectsDir => Path.Combine(ClaudeDir, "projects");
    private static string HudDir => Path.Combine(Root, "sessions");
    private static string FavoritesFile => Path.Combine(Root, "favorites.json");
    private static string StateFile => Path.Combine(Root, "overlay-state.json");
    private static string RequestsDir => Path.Combine(Root, "requests");

    // 작업 중 → 대기가 이만큼 이어져야 '완료'로 알린다(도구 호출 사이 잠깐의 대기를 걸러낸다)
    private const double DoneSettleMs = 2_500;
    public const double ContextAlertPercent = 90;
    private const double ContextRearmPercent = 75;

    private readonly Dictionary<string, string> _desktopFiles = [];
    private readonly Dictionary<string, string?> _projectDirs = [];
    private readonly Dictionary<string, LiveStatus> _lastStatus = [];
    private readonly Dictionary<string, double> _completedAt = [];
    private readonly Dictionary<string, double> _pendingDone = [];
    private readonly HashSet<string> _contextAlerted = [];

    /// <summary>마지막 LoadSessions에서 생긴 알림거리</summary>
    public List<Notice> Notices { get; } = [];
    private readonly Dictionary<string, DateTime> _desktopMisses = [];

    /// <summary>진단용: 마지막으로 읽지 못한 파일과 이유</summary>
    public static string? LastError { get; private set; }

    public OverlayState State { get; }

    public SessionStore()
    {
        Directory.CreateDirectory(HudDir);
        State = ReadJson<OverlayState>(StateFile) ?? new OverlayState();
    }

    public static double NowMs => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

    public List<SessionInfo> LoadSessions(HashSet<string> favorites)
    {
        double now = NowMs;
        Notices.Clear();
        var hudDocs = LoadHudDocs();
        var sessions = new Dictionary<string, SessionInfo>();

        foreach (var reg in LoadRegistry())
        {
            if (sessions.ContainsKey(reg.SessionId)) continue;
            var info = new SessionInfo
            {
                SessionId = reg.SessionId,
                Pid = reg.Pid,
                Cwd = reg.Cwd,
                Title = reg.Name ?? "",
                HostSessionId = reg.HostSessionId,
                IsDesktop = reg.Entrypoint == "claude-desktop" || reg.HostSessionId is not null,
                IsAlive = true,
                Status = reg.Status switch { "busy" => LiveStatus.Working, "idle" => LiveStatus.Idle, _ => LiveStatus.Waiting },
                StatusSince = reg.StatusUpdatedAt,
            };
            if (info.Status == LiveStatus.Working) info.TurnStartedAt = reg.StatusUpdatedAt;
            sessions[info.SessionId] = info;
        }

        // Mod 문서: 실행 중 목록에 없는 세션도 하트비트가 살아 있으면 넣는다(구버전 등)
        foreach (var hud in hudDocs.Values)
        {
            bool isFresh = !hud.Ended && now - hud.UpdatedAt < HudStaleMs;
            if (!sessions.TryGetValue(hud.SessionId, out var info))
            {
                if (!isFresh) continue;
                info = new SessionInfo { SessionId = hud.SessionId, Pid = hud.Pid, Cwd = hud.Cwd, IsAlive = true };
                sessions[hud.SessionId] = info;
            }
            if (!isFresh) continue;

            if (info.Title == "") info.Title = hud.Title;
            if (info.Cwd == "") info.Cwd = hud.Cwd;
            info.Measure = hud.Measure;
            info.MeasuredAt = hud.UpdatedAt;
            info.HasHud = true;
            info.CanCompact = hud.Schema >= 2;
            info.IsCompactRequested = IsCompactPending(hud.SessionId);
            info.Tasks = hud.Tasks.ToList();
            info.SeenAt = Math.Max(info.SeenAt, hud.SeenAt ?? 0);
            if (hud.LastCompletedAt is double done) info.LastCompletedAt = done;
            if (hud.Status == "waiting") info.Status = LiveStatus.Waiting;
            if (info.Status == LiveStatus.Working && hud.TurnStartedAt is double t) info.TurnStartedAt = t;
        }

        foreach (var info in sessions.Values)
        {
            if (info.HostSessionId is string host && LoadDesktopDoc(host) is { } desk)
            {
                if (!string.IsNullOrWhiteSpace(desk.Title)) info.Title = desk.Title!;
                info.SeenAt = Math.Max(info.SeenAt, desk.LastFocusedAt ?? 0);
                info.ParentHostSessionId = desk.SpawnedFrom?.SessionId;
                if (info.Measure is null && desk.Model is string model) info.Measure = new MeasureDoc { Model = model };
                if (desk.InterruptedAwaitingPerson is { ValueKind: not JsonValueKind.Null and not JsonValueKind.Undefined })
                    info.Status = LiveStatus.Waiting;
            }

            if (info.Tasks.Count == 0) info.Tasks = LoadBackgroundSubagents(info, now);

            TrackCompletion(info);
            info.SeenAt = Math.Max(info.SeenAt, State.SeenAt.GetValueOrDefault(info.SessionId));
        }

        // 꺼졌지만 즐겨찾기인 세션은 꺼진 상태로 남겨 둔다
        foreach (var id in favorites)
        {
            if (sessions.ContainsKey(id)) continue;
            var title = hudDocs.TryGetValue(id, out var hud) ? hud.Title : "";
            sessions[id] = new SessionInfo
            {
                SessionId = id,
                Title = title,
                Cwd = hud?.Cwd ?? "",
                IsAlive = false,
                Measure = hud?.Measure,
            };
        }

        return sessions.Values.ToList();
    }

    /// <summary>작업 중 → 대기로 바뀌는 순간을 완료 시각으로 기억한다</summary>
    private void TrackCompletion(SessionInfo info)
    {
        bool isFirstSight = !_lastStatus.TryGetValue(info.SessionId, out var previous);
        _lastStatus[info.SessionId] = info.Status;

        if (!isFirstSight && previous != LiveStatus.Idle && info.Status == LiveStatus.Idle)
        {
            _completedAt[info.SessionId] = info.StatusSince ?? NowMs;
            _pendingDone[info.SessionId] = NowMs;
        }
        if (!isFirstSight && previous != LiveStatus.Waiting && info.Status == LiveStatus.Waiting)
            Notices.Add(new Notice(NoticeKind.Waiting, info));

        // 완료 알림은 잠깐 기다렸다가, 그동안 다시 일을 시작하지 않았을 때만
        if (_pendingDone.TryGetValue(info.SessionId, out var idleSince))
        {
            if (info.Status != LiveStatus.Idle) _pendingDone.Remove(info.SessionId);
            else if (NowMs - idleSince >= DoneSettleMs)
            {
                _pendingDone.Remove(info.SessionId);
                Notices.Add(new Notice(NoticeKind.Done, info));
            }
        }

        // 컨텍스트가 위험 구간에 들어선 순간 한 번(압축 등으로 내려가면 다시 알릴 수 있다)
        if (info.ContextPercent is double ctx)
        {
            if (ctx >= ContextAlertPercent && _contextAlerted.Add(info.SessionId) && !isFirstSight)
                Notices.Add(new Notice(NoticeKind.ContextFull, info));
            else if (ctx < ContextRearmPercent)
                _contextAlerted.Remove(info.SessionId);
        }

        if (_completedAt.TryGetValue(info.SessionId, out var at))
            info.LastCompletedAt = Math.Max(info.LastCompletedAt ?? 0, at);

        // 오버레이를 켜기 전에 끝난 세션은 이미 확인한 것으로 친다
        if (isFirstSight && info.Status == LiveStatus.Idle && !State.SeenAt.ContainsKey(info.SessionId))
        {
            State.SeenAt[info.SessionId] = NowMs;
            SaveState();
        }
    }

    private IEnumerable<RegistryDoc> LoadRegistry()
    {
        if (!Directory.Exists(RegistryDir)) yield break;
        foreach (var file in Directory.EnumerateFiles(RegistryDir, "*.json"))
        {
            var doc = ReadJson<RegistryDoc>(file);
            if (doc is null || doc.SessionId == "" || !IsSameProcess(doc)) continue;
            yield return doc;
        }
    }

    /// <summary>pid가 재사용됐을 수 있으니 프로세스 시작 시각(FILETIME)까지 맞춰 본다</summary>
    private static bool IsSameProcess(RegistryDoc doc)
    {
        try
        {
            using var process = Process.GetProcessById(doc.Pid);
            if (process.HasExited) return false;
            if (doc.ProcStart is null || !long.TryParse(doc.ProcStart, out long fileTime)) return true;
            long actual = process.StartTime.ToFileTimeUtc();
            return Math.Abs(actual - fileTime) < 10_000_000; // 1초
        }
        catch (ArgumentException) { return false; }
        catch { return true; } // 권한 문제로 시작 시각을 못 읽으면 pid만 믿는다
    }

    private Dictionary<string, HudDoc> LoadHudDocs()
    {
        var docs = new Dictionary<string, HudDoc>();
        foreach (var file in Directory.EnumerateFiles(HudDir, "*.json"))
        {
            var doc = ReadJson<HudDoc>(file);
            if (doc is null || doc.SessionId == "") continue;
            if (doc.Ended && DateTime.UtcNow - File.GetLastWriteTimeUtc(file) > TimeSpan.FromDays(1))
            {
                TryDelete(file);
                continue;
            }
            docs[doc.SessionId] = doc;
        }
        return docs;
    }

    // ── 데스크톱 앱 세션 파일 ──────────────────────────────────────────────

    /// <summary>MSIX 앱은 %APPDATA%를 패키지 폴더로 돌려 쓰므로 두 곳을 모두 본다</summary>
    private static IEnumerable<string> DesktopRoots()
    {
        string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        string packages = Path.Combine(local, "Packages");
        if (Directory.Exists(packages))
        {
            foreach (var pkg in Directory.EnumerateDirectories(packages, "Claude_*"))
            {
                var dir = Path.Combine(pkg, "LocalCache", "Roaming", "Claude", "claude-code-sessions");
                if (Directory.Exists(dir)) yield return dir;
            }
        }
        var roaming = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Claude", "claude-code-sessions");
        if (Directory.Exists(roaming)) yield return roaming;
    }

    private DesktopDoc? LoadDesktopDoc(string hostSessionId)
    {
        if (!_desktopFiles.TryGetValue(hostSessionId, out var path) || !File.Exists(path))
        {
            // 못 찾은 세션은 파일이 곧 생길 수 있으니, 너무 자주는 말고 다시 찾아본다
            if (_desktopMisses.TryGetValue(hostSessionId, out var missedAt) && DateTime.UtcNow - missedAt < TimeSpan.FromSeconds(10)) return null;
            path = null;
            foreach (var root in DesktopRoots())
            {
                foreach (var account in Directory.EnumerateDirectories(root))
                    foreach (var org in Directory.EnumerateDirectories(account))
                    {
                        var candidate = Path.Combine(org, hostSessionId + ".json");
                        if (File.Exists(candidate)) { path = candidate; break; }
                    }
                if (path is not null) break;
            }
            if (path is null)
            {
                _desktopMisses[hostSessionId] = DateTime.UtcNow;
                return null;
            }
            _desktopFiles[hostSessionId] = path;
        }
        return ReadJson<DesktopDoc>(path);
    }

    // ── Mod가 없는 세션의 백그라운드 서브에이전트 ─────────────────────────────

    private List<TaskDoc> LoadBackgroundSubagents(SessionInfo info, double now)
    {
        var tasks = new List<TaskDoc>();
        var dir = ProjectDir(info);
        if (dir is null) return tasks;
        var subagents = Path.Combine(dir, info.SessionId, "subagents");
        if (!Directory.Exists(subagents)) return tasks;

        foreach (var meta in Directory.EnumerateFiles(subagents, "agent-*.meta.json"))
        {
            var doc = ReadJson<SubagentMetaDoc>(meta);
            if (doc is null || doc.RequestShape != "background") continue;
            var log = meta[..^".meta.json".Length] + ".jsonl";
            if (!File.Exists(log)) continue;
            double quietFor = now - new DateTimeOffset(File.GetLastWriteTimeUtc(log)).ToUnixTimeMilliseconds();
            if (quietFor > SubagentQuietMs) continue;
            tasks.Add(new TaskDoc
            {
                Id = Path.GetFileName(log)["agent-".Length..^".jsonl".Length],
                Kind = "agent",
                Description = doc.Description ?? doc.AgentType ?? "백그라운드 작업",
                Type = doc.AgentType,
                Status = "running",
            });
        }
        return tasks;
    }

    private string? ProjectDir(SessionInfo info)
    {
        if (_projectDirs.TryGetValue(info.SessionId, out var cached)) return cached;
        string? found = null;
        if (info.Cwd != "")
        {
            var slug = Path.Combine(ProjectsDir, NonAlnum().Replace(info.Cwd, "-"));
            if (Directory.Exists(Path.Combine(slug))) found = slug;
        }
        if (found is null && Directory.Exists(ProjectsDir))
        {
            found = Directory.EnumerateDirectories(ProjectsDir)
                .FirstOrDefault(d => File.Exists(Path.Combine(d, info.SessionId + ".jsonl")));
        }
        _projectDirs[info.SessionId] = found;
        return found;
    }

    [GeneratedRegex("[^A-Za-z0-9]")]
    private static partial Regex NonAlnum();

    // ── 압축 요청 ──────────────────────────────────────────────────────

    public void RequestCompact(string sessionId) =>
        WriteJson(Path.Combine(RequestsDir, sessionId + ".json"), new HudRequestDoc { Action = "compact", RequestedAt = NowMs });

    private static bool IsCompactPending(string sessionId)
    {
        var doc = ReadJson<HudRequestDoc>(Path.Combine(RequestsDir, sessionId + ".json"));
        return doc is { Action: "compact" } && (doc.HandledAt ?? 0) < doc.RequestedAt && NowMs - doc.RequestedAt < 10 * 60_000;
    }

    // ── 즐겨찾기·확인 상태 ─────────────────────────────────────────────

    public HashSet<string> LoadFavorites() =>
        (ReadJson<FavoritesDoc>(FavoritesFile)?.Ids ?? []).ToHashSet();

    public void SetFavorite(string sessionId, bool isOn)
    {
        var ids = LoadFavorites();
        if (isOn) ids.Add(sessionId); else ids.Remove(sessionId);
        WriteJson(FavoritesFile, new FavoritesDoc { Ids = ids.ToList() });
    }

    public void MarkSeen(string sessionId)
    {
        State.SeenAt[sessionId] = NowMs;
        SaveState();
    }

    public void ClearSeen()
    {
        foreach (var key in State.SeenAt.Keys.ToList()) State.SeenAt[key] = 0;
        _completedAt.Clear();
        foreach (var (id, status) in _lastStatus)
            if (status == LiveStatus.Idle) _completedAt[id] = 1;
        SaveState();
    }

    public void SaveState() => WriteJson(StateFile, State);

    private static T? ReadJson<T>(string path) where T : class
    {
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            return JsonSerializer.Deserialize<T>(stream, Json.Options);
        }
        catch (Exception ex)
        {
            LastError = $"{Path.GetFileName(path)}: {ex.Message}";
            return null;
        }
    }

    private static void WriteJson<T>(string path, T value)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            string tmp = path + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(value, Json.Options));
            File.Move(tmp, path, overwrite: true);
        }
        catch { /* 다음 저장 때 다시 시도 */ }
    }

    private static void TryDelete(string path)
    {
        try { File.Delete(path); } catch { }
    }
}
