using System.Threading;
using System.Windows;

namespace ClaudeSessionOverlay;

public partial class App : Application
{
    private Mutex? _single;
    private TrayIcon? _tray;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // 진단용: 오버레이가 지금 보여줄 목록을 글로 남기고 끝낸다
        if (e.Args.Contains("--dump"))
        {
            var dumpStore = new SessionStore();
            var favorites = dumpStore.LoadFavorites();
            var sessions = dumpStore.LoadSessions(favorites);
            var shown = ClaudeSessionOverlay.MainWindow.BuildRows(sessions, favorites).Select(r =>
                $"[{r.State}] {r.Title} | {r.StatusText} · {r.Detail} | pid={r.Info.Pid} host={r.Info.HostSessionId}"
                + string.Concat(r.Tasks.Select(t => $"\n    {t.Label} ({t.StatusText})")));
            var all = sessions.Select(s =>
                $"{s.Status,-8} alive={s.IsAlive} seen={s.SeenAt:0} done={s.LastCompletedAt:0} parent={s.ParentHostSessionId} | {s.Title}");
            System.IO.File.WriteAllText(System.IO.Path.Combine(SessionStore.Root, "dump.txt"),
                "보이는 줄:\n" + string.Join("\n", shown) + "\n\n전체 세션:\n" + string.Join("\n", all)
                + "\n\n분할 화면에 떠 있는 데스크톱 세션: " + string.Join(", ", DesktopPanes.ShownSessionIds()));
            Shutdown();
            return;
        }

        // 진단용: 오버레이에서 그 세션 줄을 누른 것과 똑같이 연다 (--focus <세션 id>)
        if (e.Args.Length == 2 && e.Args[0] == "--focus")
        {
            var focusStore = new SessionStore();
            var target = focusStore.LoadSessions([]).FirstOrDefault(x => x.SessionId == e.Args[1]);
            var watch = System.Diagnostics.Stopwatch.StartNew();
            if (target is not null) WindowFocuser.Focus(target);
            long frontMs = watch.ElapsedMilliseconds;
            var pane = target?.HostSessionId is string host ? DesktopPanes.PositionOf(host) : null;
            WindowFocuser.PaneFocus.ContinueWith(t => Dispatcher.Invoke(() =>
            {
                System.IO.File.WriteAllText(System.IO.Path.Combine(SessionStore.Root, "focus.txt"),
                    $"session={e.Args[1]} found={target is not null} pane={pane?.ToString() ?? "(첫 화면, 딥 링크)"} promptFocused={t.Result} front={frontMs}ms focus={watch.ElapsedMilliseconds}ms");
                Shutdown();
            }));
            return;
        }

        _single = new Mutex(true, @"Local\ClaudeSessionOverlay", out bool isFirst);
        if (!isFirst)
        {
            Shutdown();
            return;
        }

        var store = new SessionStore();
        var window = new MainWindow(store);

        // 진단용: 그려진 오버레이를 PNG로 저장하고 끝낸다
        if (e.Args.Contains("--snapshot"))
        {
            window.Start();
            var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
            timer.Tick += (_, _) =>
            {
                timer.Stop();
                window.SaveSnapshot(System.IO.Path.Combine(SessionStore.Root, "snapshot.png"));
                Shutdown();
            };
            timer.Start();
            return;
        }

        _tray = new TrayIcon(window);
        window.Start();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _tray?.Dispose();
        _single?.Dispose();
        base.OnExit(e);
    }
}
