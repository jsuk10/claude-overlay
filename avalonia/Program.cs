using Avalonia;

namespace ClaudeSessionOverlay;

internal static class Program
{
    /// <summary>
    /// 인자
    ///   --demo [--theme dark|light] [--lang en|ko] [--snapshot out.png]   가짜 세션으로 띄운다(아무것도 저장하지 않는다)
    ///   --dump [--no-mod]   지금 보여 줄 목록을 ~/.claude/session-hud/dump.txt(와 표준 출력)에 쓰고 끝낸다
    ///   --snapshot          실제 목록을 그려 ~/.claude/session-hud/snapshot.png로 저장하고 끝낸다
    /// </summary>
    [STAThread]
    public static int Main(string[] args)
    {
        Options = new AppOptions(args);
        if (Options.IsDump) return Dump();

        // 하나만 띄운다(시연은 실행 중인 오버레이와 별개)
        FileStream? instanceLock = null;
        if (!Options.IsDemo)
        {
            try
            {
                Directory.CreateDirectory(SessionStore.Root);
                instanceLock = new FileStream(Path.Combine(SessionStore.Root, "overlay-avalonia.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            }
            catch (IOException) { return 0; }
        }

        using (instanceLock)
            return BuildAvaloniaApp().StartWithClassicDesktopLifetime(args, Avalonia.Controls.ShutdownMode.OnExplicitShutdown);
    }

    public static AppOptions Options { get; private set; } = new([]);

    // 디자이너도 이 메서드를 쓴다
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            // 메뉴 막대 앱: Dock 아이콘 없이
            .With(new MacOSPlatformOptions { ShowInDock = false })
            .LogToTrace();

    private static int Dump()
    {
        SessionStore.IgnoreMod = Options.Has("--no-mod");
        SessionStore.IsReadOnly = true; // 진단만: 확인 상태 등을 건드리지 않는다
        var store = new SessionStore();
        Loc.Apply(store.State.Language);
        var favorites = store.LoadFavorites();
        var sessions = store.LoadSessions(favorites);
        var shown = Overview.BuildRows(sessions, favorites).Select(r =>
            $"[{r.State}] {r.Title} | {r.StatusText} · {r.Detail} | ctx={r.ContextLabel} ({r.Info.Measure?.ContextTokens:0}/{r.Info.Measure?.CompactLimit:0}) week={r.Info.Measure?.WeeklyPercent} 5h={r.Info.Measure?.FiveHourPercent} compact={r.ShowCompact}"
            + string.Concat(r.Tasks.Select(t => $"\n    {t.Label} ({t.StatusText})")));
        var all = sessions.Select(s =>
            $"{s.Status,-8} alive={s.IsAlive} seen={s.SeenAt:0} done={s.LastCompletedAt:0} parent={s.ParentHostSessionId} | {s.Title}");
        string text = "보이는 줄:\n" + string.Join("\n", shown) + "\n\n전체 세션:\n" + string.Join("\n", all)
            + "\n\n데스크톱 앱 데이터 폴더: " + string.Join(", ", PlatformPaths.DesktopDataDirs())
            + (SessionStore.LastError is string error ? "\n마지막 읽기 오류: " + error : "");
        File.WriteAllText(Path.Combine(SessionStore.Root, "dump.txt"), text);
        Console.WriteLine(text);
        return 0;
    }
}

internal sealed class AppOptions(string[] args)
{
    public bool Has(string name) => args.Contains(name);

    public string? Value(string name) =>
        Array.IndexOf(args, name) is int i and >= 0 && i + 1 < args.Length && !args[i + 1].StartsWith("--") ? args[i + 1] : null;

    public bool IsDemo => Has("--demo");
    public bool IsDump => Has("--dump");
}
