using System.Text.Json;
using System.Text.Json.Serialization;

namespace ClaudeSessionOverlay;

/// <summary>Claude Code가 실행 중인 프로세스마다 쓰는 %USERPROFILE%\.claude\sessions\&lt;pid&gt;.json</summary>
public sealed class RegistryDoc
{
    public int Pid { get; set; }
    public string SessionId { get; set; } = "";
    public string Cwd { get; set; } = "";
    public double StartedAt { get; set; }
    public string? ProcStart { get; set; }
    public string Kind { get; set; } = "";
    public string Entrypoint { get; set; } = "";
    public string? HostSessionId { get; set; }
    public string? Name { get; set; }
    public string Status { get; set; } = "idle";
    public double? StatusUpdatedAt { get; set; }
    public double? UpdatedAt { get; set; }
}

/// <summary>session-hud Mod가 쓰는 %USERPROFILE%\.claude\session-hud\sessions\&lt;id&gt;.json</summary>
public sealed class HudDoc
{
    public int Schema { get; set; }
    public string SessionId { get; set; } = "";
    public bool Ended { get; set; }
    public int? Pid { get; set; }
    public string Cwd { get; set; } = "";
    public string Title { get; set; } = "";
    public string LastPrompt { get; set; } = "";
    public string Status { get; set; } = "idle";
    public double StartedAt { get; set; }
    public double? TurnStartedAt { get; set; }
    public double? LastCompletedAt { get; set; }
    public double? SeenAt { get; set; }
    public double UpdatedAt { get; set; }
    public string[] Surfaces { get; set; } = [];
    public MeasureDoc? Measure { get; set; }
    public TaskDoc[] Tasks { get; set; } = [];
}

/// <summary>데스크톱 앱 Code 탭이 세션마다 쓰는 local_&lt;id&gt;.json (필요한 필드만)</summary>
public sealed class DesktopDoc
{
    public string SessionId { get; set; } = "";
    public string? CliSessionId { get; set; }
    public string? Title { get; set; }
    public bool IsArchived { get; set; }
    public double? LastFocusedAt { get; set; }
    public double? LastActivityAt { get; set; }
    public string? Model { get; set; }
    public JsonElement? InterruptedAwaitingPerson { get; set; }
    public SpawnedFromDoc? SpawnedFrom { get; set; }
}

public sealed class SpawnedFromDoc
{
    public string? SessionId { get; set; }
    public string? Title { get; set; }
}

public sealed class MeasureDoc
{
    public string Model { get; set; } = "";
    public double? ContextTokens { get; set; }
    public double ContextWindow { get; set; }
    public double? ContextPercent { get; set; }
    public double? CostUsd { get; set; }
    public double? WeeklyPercent { get; set; }
    public string? WeeklyResetsAt { get; set; }
    public double? FiveHourPercent { get; set; }
    public string? FiveHourResetsAt { get; set; }
    /// <summary>자동 압축이 일어나는 토큰 수</summary>
    public double? CompactLimit { get; set; }
    /// <summary>자동 압축 기준으로 얼마나 찼는지(%)</summary>
    public double? CompactPercent { get; set; }
}

public sealed class TaskDoc
{
    public string Id { get; set; } = "";
    public string Kind { get; set; } = "agent";
    public string Description { get; set; } = "";
    public string? Type { get; set; }
    public string Status { get; set; } = "";
    public string? ParentId { get; set; }

    [JsonIgnore]
    public bool IsRunning => Status is "running" or "pending" or "waiting";
}

/// <summary>subagents\agent-&lt;id&gt;.meta.json</summary>
public sealed class SubagentMetaDoc
{
    public string? AgentType { get; set; }
    public string? Description { get; set; }
    public string? RequestShape { get; set; }
}

public sealed class FavoritesDoc
{
    public List<string> Ids { get; set; } = [];
}

/// <summary>오버레이 자신의 상태(확인 시각, 창 위치)</summary>
public sealed class OverlayState
{
    public Dictionary<string, double> SeenAt { get; set; } = [];
    public double? Left { get; set; }
    public double? Top { get; set; }
    public bool IsHidden { get; set; }
    public bool IsCollapsed { get; set; }
    public HashSet<string> ExpandedTasks { get; set; } = [];
    public bool IsSoundOn { get; set; } = true;
    public bool IsVoiceOn { get; set; } = true;
    public OverlayTheme Theme { get; set; } = OverlayTheme.Dark;
    public UiLanguage Language { get; set; } = UiLanguage.System;
    /// <summary>처음 실행할 때 로그인 시 실행할지 이미 물었다</summary>
    public bool IsAutostartAsked { get; set; }
}

/// <summary>session-hud Mod에 남기는 요청(requests\&lt;id&gt;.json)</summary>
public sealed class HudRequestDoc
{
    public string Action { get; set; } = "";
    public double RequestedAt { get; set; }
    public double? HandledAt { get; set; }
    public string? Result { get; set; }
}

public enum NoticeKind { Done, Waiting, ContextFull }

/// <summary>상태가 바뀐 순간: 소리·음성으로 알린다</summary>
public sealed record Notice(NoticeKind Kind, SessionInfo Session);

public enum LiveStatus { Working, Waiting, Idle }

/// <summary>여러 출처를 합친 세션 하나</summary>
public sealed class SessionInfo
{
    public required string SessionId { get; init; }
    public int? Pid { get; set; }
    public string Cwd { get; set; } = "";
    public string Title { get; set; } = "";
    public string? HostSessionId { get; set; }
    public string? ParentHostSessionId { get; set; }
    public bool IsDesktop { get; set; }
    public bool IsAlive { get; set; }
    public LiveStatus Status { get; set; } = LiveStatus.Idle;
    public double? StatusSince { get; set; }
    public double? TurnStartedAt { get; set; }
    public double? LastCompletedAt { get; set; }
    public double SeenAt { get; set; }
    public MeasureDoc? Measure { get; set; }
    public bool HasHud { get; set; }
    public bool IsCompactRequested { get; set; }
    /// <summary>이 세션의 Mod가 압축 요청을 처리할 수 있다(Mod schema 2 이상)</summary>
    public bool CanCompact { get; set; }

    /// <summary>자동 압축 기준 컨텍스트 %(구버전 Mod면 창 기준 %)</summary>
    public double? ContextPercent => Measure?.CompactPercent ?? Measure?.ContextPercent;
    public List<TaskDoc> Tasks { get; set; } = [];
}

public static class Json
{
    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
        NumberHandling = JsonNumberHandling.AllowReadingFromString,
    };
}
