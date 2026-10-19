namespace ClaudeSessionOverlay;

/// <summary>
/// 스크린샷·시연용 가짜 세션(--demo). 실제 프로젝트·세션 이름이 드러나지 않게 지어낸 값만 쓴다.
/// </summary>
public static class DemoData
{
    public const string FavoriteId = "demo-1";

    public static (List<SessionInfo> Sessions, HashSet<string> Favorites, HashSet<string> Expanded, AccountUsage Usage) Load()
    {
        double now = SessionStore.NowMs;
        double minutes(double m) => now - m * 60_000;
        // Windows는 드라이브 경로, macOS·Linux는 홈 아래 경로(~/…로 줄여 보인다)
        static string Cwd(string windows, string underHome) =>
            OperatingSystem.IsWindows() ? windows : Path.Combine(PlatformPaths.Home, underHome);

        var sessions = new List<SessionInfo>
        {
            new()
            {
                SessionId = FavoriteId, Pid = 1, IsAlive = true, Status = LiveStatus.Working,
                Title = "Build checkout flow", Cwd = Cwd(@"D:\work\shop\web", "work/shop/web"), TurnStartedAt = minutes(12.4),
                Measure = Measure(3.42, 46), HasHud = true, CanCompact = true,
                Tasks =
                [
                    new() { Id = "a1", Kind = "agent", Description = "Write unit tests for cart", Status = "running" },
                    new() { Id = "a2", Kind = "agent", Description = "Review payment API schema", Status = "running" },
                    new() { Id = "s1", Kind = "shell", Description = "npm run dev", Status = "running" },
                ],
            },
            new()
            {
                SessionId = "demo-2", Pid = 2, IsAlive = true, Status = LiveStatus.Waiting,
                Title = "Migrate database schema", Cwd = Cwd(@"C:\projects\api-server", "projects/api-server"),StatusSince = minutes(1),
                Measure = Measure(5.87, 92), HasHud = true, CanCompact = true,
            },
            new()
            {
                SessionId = "demo-3", Pid = 3, IsAlive = true, Status = LiveStatus.Working,
                Title = "Update API docs", Cwd = Cwd(@"C:\projects\api-server\docs", "projects/api-server/docs"), TurnStartedAt = minutes(3.1),
                Measure = Measure(0.94, 78), HasHud = true, CanCompact = true,
            },
            new()
            {
                SessionId = "demo-4", Pid = 4, IsAlive = true, Status = LiveStatus.Idle,
                Title = "Fix login redirect", Cwd = Cwd(@"D:\work\shop\web", "work/shop/web"), LastCompletedAt = minutes(4), SeenAt = 0,
                Measure = Measure(1.18, 23), HasHud = true, CanCompact = true,
                Tasks = [new() { Id = "a3", Kind = "agent", Description = "Check e2e results", Status = "completed" }],
            },
        };
        var usage = new AccountUsage();
        usage.Offer("demo", now, 42, DateTimeOffset.Now.AddDays(3).ToString("o"),
            65, DateTimeOffset.Now.AddHours(1).AddMinutes(31).AddSeconds(30).ToString("o"));
        return (sessions, [FavoriteId], [FavoriteId], usage);
    }

    private static MeasureDoc Measure(double cost, double compactPercent) => new()
    {
        Model = "claude-opus-5-5",
        ContextWindow = 200_000,
        CompactLimit = 167_000,
        CompactPercent = compactPercent,
        ContextTokens = 167_000 * compactPercent / 100,
        ContextPercent = compactPercent * 167 / 200,
        CostUsd = cost,
    };
}
