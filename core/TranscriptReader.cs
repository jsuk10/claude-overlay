using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace ClaudeSessionOverlay;

/// <summary>
/// Claude Code가 쓰는 대화 기록(~/.claude/projects/&lt;slug&gt;/&lt;sessionId&gt;.jsonl)을 끝에서부터 이어 읽어
/// 모델·컨텍스트 토큰·백그라운드 작업을 얻는다. Mod 없이도 동작하게 하는 주 출처.
/// 파일마다 마지막으로 읽은 위치를 기억해 새로 붙은 줄만 읽는다.
/// </summary>
public sealed partial class TranscriptReader
{
    // 처음 여는 큰 파일은 끝부분만: 최근 상태를 아는 데 충분하다
    private const long InitialTailBytes = 2 * 1024 * 1024;
    // 끝난 작업은 이만큼 더 보여 주고 뺀다
    private const double FinishedTaskVisibleMs = 60_000;

    public sealed class State
    {
        public long Position;
        public string? Model;
        public double? ContextTokens;
        public readonly Dictionary<string, TranscriptTask> Tasks = [];
    }

    public sealed class TranscriptTask
    {
        public required string Id { get; init; }
        public string Kind { get; set; } = "shell";
        public string Description { get; set; } = "";
        public string Status { get; set; } = "running";
        public double ChangedAt { get; set; }
    }

    private readonly Dictionary<string, State> _files = [];

    public State? Read(string path)
    {
        if (!File.Exists(path)) return null;
        if (!_files.TryGetValue(path, out var state))
        {
            state = new State();
            _files[path] = state;
        }

        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            long length = stream.Length;
            if (length < state.Position) state = _files[path] = new State(); // 파일이 새로 쓰였다
            if (length == state.Position) return state;

            long start = state.Position == 0 ? Math.Max(0, length - InitialTailBytes) : state.Position;
            stream.Seek(start, SeekOrigin.Begin);
            var bytes = new byte[length - start];
            int read = 0;
            while (read < bytes.Length)
            {
                int n = stream.Read(bytes, read, bytes.Length - read);
                if (n == 0) break;
                read += n;
            }

            // 마지막 줄이 아직 쓰이는 중일 수 있으니 줄바꿈까지만 읽고, 나머지는 다음에
            int end = Array.LastIndexOf(bytes, (byte)'\n', read - 1);
            if (end < 0) return state;
            int from = 0;
            if (state.Position == 0 && start > 0) from = Array.IndexOf(bytes, (byte)'\n') + 1; // 잘린 첫 줄은 버린다

            double now = SessionStore.NowMs;
            foreach (var line in Encoding.UTF8.GetString(bytes, from, end - from).Split('\n'))
                ParseLine(state, line, now);
            state.Position = start + end + 1;
        }
        catch (IOException) { }
        return state;
    }

    /// <summary>보여 줄 백그라운드 작업: 도는 중이거나 방금 끝난 것</summary>
    public static List<TaskDoc> VisibleTasks(State state, double now) =>
        state.Tasks.Values
            .Where(t => t.Status == "running" || now - t.ChangedAt < FinishedTaskVisibleMs)
            .OrderBy(t => t.ChangedAt)
            .Select(t => new TaskDoc { Id = t.Id, Kind = t.Kind, Description = t.Description, Status = t.Status })
            .ToList();

    private static void ParseLine(State state, string line, double now)
    {
        // 관심 있는 줄만 JSON으로 푼다(대부분의 줄은 건너뛴다)
        bool isAssistant = line.Contains("\"type\":\"assistant\"") && line.Contains("\"usage\"");
        bool isCompact = line.Contains("\"compact_boundary\"");
        bool isTaskStatus = line.Contains("\"task_status\"");
        bool isNotification = line.Contains("task-notification");
        bool isBackground = line.Contains("\"backgroundTaskId\"");
        if (!(isAssistant || isCompact || isTaskStatus || isNotification || isBackground)) return;

        try
        {
            using var doc = JsonDocument.Parse(line);
            var root = doc.RootElement;

            if (isAssistant && !Is(root, "isSidechain") && root.TryGetProperty("message", out var message))
            {
                if (message.TryGetProperty("model", out var model) && model.GetString() is { } id && !id.StartsWith('<'))
                    state.Model = id;
                if (message.TryGetProperty("usage", out var usage))
                    state.ContextTokens = Number(usage, "input_tokens") + Number(usage, "cache_read_input_tokens")
                        + Number(usage, "cache_creation_input_tokens") + Number(usage, "output_tokens");
            }

            // 압축하면 컨텍스트가 줄어든다
            if (isCompact && root.TryGetProperty("compactMetadata", out var compact) && compact.TryGetProperty("postTokens", out var post))
                state.ContextTokens = post.GetDouble();

            if (isTaskStatus && root.TryGetProperty("attachment", out var attachment)
                && attachment.TryGetProperty("taskId", out var taskId) && taskId.GetString() is { } tid)
            {
                var task = TaskOf(state, tid, now);
                task.Kind = Text(attachment, "taskType") == "local_agent" ? "agent" : "shell";
                if (Text(attachment, "description") is { Length: > 0 } description) task.Description = description;
                if (Text(attachment, "status") is { } status) SetStatus(task, status, now);
            }

            if (isBackground && root.TryGetProperty("toolUseResult", out var result)
                && result.ValueKind == JsonValueKind.Object
                && result.TryGetProperty("backgroundTaskId", out var bg) && bg.GetString() is { } bid)
            {
                var task = TaskOf(state, bid, now);
                if (task.Description == "") task.Description = Loc.T("백그라운드 셸", "Background shell");
            }

            if (isNotification && NotificationTask().Match(line) is { Success: true } match)
            {
                var task = TaskOf(state, match.Groups[1].Value, now);
                if (match.Groups[1].Value.StartsWith('a')) task.Kind = "agent";
                SetStatus(task, match.Groups[2].Value, now);
            }
        }
        catch (JsonException) { }
    }

    private static TranscriptTask TaskOf(State state, string id, double now)
    {
        if (!state.Tasks.TryGetValue(id, out var task))
            state.Tasks[id] = task = new TranscriptTask { Id = id, ChangedAt = now };
        return task;
    }

    private static void SetStatus(TranscriptTask task, string status, double now)
    {
        if (task.Status == status) return;
        // 이미 끝난 작업이 이전 '실행 중' 기록으로 되살아나지 않게
        if (status == "running" && task.Status is "completed" or "failed" or "killed") return;
        task.Status = status;
        task.ChangedAt = now;
    }

    private static bool Is(JsonElement e, string name) => e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.True;
    private static string? Text(JsonElement e, string name) => e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
    private static double Number(JsonElement e, string name) => e.TryGetProperty(name, out var v) && v.TryGetDouble(out var d) ? d : 0;

    // <task-id>…</task-id> … <status>…</status> (JSON 안이라 줄바꿈은 \n 글자로 들어 있다)
    [GeneratedRegex(@"<task-id>([^<]+)</task-id>.*?<status>([a-z]+)</status>")]
    private static partial Regex NotificationTask();
}

/// <summary>컨텍스트 창 크기와 자동 압축 기준</summary>
public static class ContextLimits
{
    private static readonly string ClaudeDir = PlatformPaths.ClaudeDir;
    private static Dictionary<string, (double Input, double Output)> _models = [];
    private static double? _autoCompactWindow;
    private static DateTime _loadedAt;

    // Claude Code가 압축 기준을 정하는 방식: 창에서 요약 출력 몫(최대 2만)과 여유분 1만3천을 뺀다
    private const double SummaryReserve = 20_000;
    private const double Buffer = 13_000;

    /// <summary>(모델 창, 자동 압축 기준)</summary>
    public static (double Window, double CompactLimit) For(string model, string? desktopModel)
    {
        Reload();
        string id = model.Replace("[1m]", "");
        double window = _models.TryGetValue(id, out var m) ? m.Input
            : desktopModel?.Contains("[1m]") == true ? 1_000_000 : 200_000;
        double output = _models.TryGetValue(id, out var mo) ? mo.Output : 32_000;
        double effective = Math.Min(window, _autoCompactWindow ?? window) - Math.Min(output, SummaryReserve);
        return (window, effective - Buffer);
    }

    private static void Reload()
    {
        if (DateTime.UtcNow - _loadedAt < TimeSpan.FromSeconds(30)) return;
        _loadedAt = DateTime.UtcNow;

        // 압축 창: 환경 변수 > settings.json
        _autoCompactWindow = null;
        if (double.TryParse(Environment.GetEnvironmentVariable("CLAUDE_CODE_AUTO_COMPACT_WINDOW"), out var env)) _autoCompactWindow = env;
        else
        {
            try
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(ClaudeDir, "settings.json")));
                if (doc.RootElement.TryGetProperty("autoCompactWindow", out var w) && w.TryGetDouble(out var value)) _autoCompactWindow = value;
            }
            catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException) { }
        }

        // 모델별 창 크기: Claude Code가 받아 두는 모델 목록
        var dir = Path.Combine(ClaudeDir, "cache", "model-catalog");
        if (!Directory.Exists(dir)) return;
        var models = new Dictionary<string, (double, double)>();
        foreach (var file in Directory.EnumerateFiles(dir, "*.json").OrderBy(File.GetLastWriteTimeUtc))
        {
            try
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(file));
                if (!doc.RootElement.TryGetProperty("catalog", out var catalog)
                    || !catalog.TryGetProperty("config", out var config)
                    || !config.TryGetProperty("models", out var list)) continue;
                foreach (var item in list.EnumerateArray())
                {
                    if (item.TryGetProperty("id", out var id) && id.GetString() is { } name
                        && item.TryGetProperty("runtime", out var runtime)
                        && runtime.TryGetProperty("max_input_tokens", out var input) && input.TryGetDouble(out var inTokens))
                    {
                        double outTokens = runtime.TryGetProperty("max_output_tokens", out var o) && o.TryGetDouble(out var ov) ? ov : 32_000;
                        models[name] = (inTokens, outTokens); // 최신 파일이 덮어쓴다
                    }
                }
            }
            catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException) { }
        }
        if (models.Count > 0) _models = models;
    }
}
