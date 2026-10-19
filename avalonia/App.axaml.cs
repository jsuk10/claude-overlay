using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using ClaudeSessionOverlay.Platform;

namespace ClaudeSessionOverlay;

public partial class App : Application
{
    private Tray? _tray;

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        base.OnFrameworkInitializationCompleted();
        if (ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop) return;

        // 언어가 바뀌면 XAML 문구({DynamicResource L_키})를 바로 바꾼다
        Loc.Changed += () =>
        {
            foreach (var key in Loc.Xaml.Keys) Resources[key] = Loc.Get(key);
        };

        var options = Program.Options;
        if (options.IsDemo)
        {
            // 시연: 가짜 세션, 아무것도 저장하지 않는다
            SessionStore.IsReadOnly = true;
            var demoStore = new SessionStore();
            demoStore.State.IsHidden = false;
            demoStore.State.IsCollapsed = false;
            ThemePaint.Apply(options.Value("--theme") == "light" ? OverlayTheme.Light : OverlayTheme.Dark);
            Loc.Apply(options.Value("--lang") switch
            {
                "ko" => UiLanguage.Korean,
                "en" => UiLanguage.English,
                _ => demoStore.State.Language,
            });
            var demo = new MainWindow(demoStore, isDemo: true);
            demo.Start();
            if (options.Has("--snapshot")) SnapshotAndQuit(demo, options.Value("--snapshot") ?? Path.Combine(Environment.CurrentDirectory, "demo.png"));
            else _tray = new Tray(demo, isDemo: true);
            return;
        }

        var store = new SessionStore();
        ThemePaint.Apply(store.State.Theme);
        Loc.Apply(store.State.Language);
        var window = new MainWindow(store, isDemo: false);
        window.Start();
        if (options.Has("--snapshot"))
        {
            SnapshotAndQuit(window, Path.Combine(SessionStore.Root, "snapshot.png"));
            return;
        }
        _tray = new Tray(window);
        desktop.Exit += (_, _) => _tray?.Dispose();
        ListenForLinks(window);
        OfferAutostart(store);
    }

    /// <summary>처음 실행할 때 한 번: 로그인할 때 같이 열지 묻는다(나중엔 메뉴에서 바꾼다)</summary>
    private static void OfferAutostart(SessionStore store)
    {
        var platform = IPlatform.Current;
        if (!platform.CanOfferAutostart) return;
        // 옮기거나 새로 받은 앱으로 실행했으면 로그인 항목도 이 앱을 가리키게
        if (platform.IsAutostart) platform.SetAutostart(true);
        if (store.State.IsAutostartAsked) return;
        store.State.IsAutostartAsked = true;
        store.SaveState();
        if (platform.IsAutostart) return;

        string question = Loc.T(
            $"로그인할 때 Claude 세션 오버레이를 같이 열까요?\n\n나중에 메뉴의 '{platform.AutostartLabel}'에서 바꿀 수 있습니다.",
            $"Open Claude Session Overlay at login?\n\nYou can change this later with '{platform.AutostartLabel}' in the menu.");
        // 답을 기다리는 동안 오버레이가 멈추지 않게
        Task.Run(() =>
        {
            if (platform.Confirm(question)) platform.SetAutostart(true);
        });
    }

    /// <summary>
    /// 위젯에서 누른 claude-overlay:// 주소(macOS: Info.plist의 CFBundleURLTypes).
    ///   open?session=… 그 세션을 앞으로 가져오고 확인 처리   show  오버레이를 보인다
    /// </summary>
    private void ListenForLinks(MainWindow window)
    {
        if (TryGetFeature(typeof(IActivatableLifetime)) is not IActivatableLifetime activatable) return;
        activatable.Activated += (_, e) =>
        {
            if (e is not ProtocolActivatedEventArgs { Kind: ActivationKind.OpenUri, Uri: { } uri } || uri.Scheme != Links.OverlayScheme) return;
            Dispatcher.UIThread.Post(() =>
            {
                if (Links.SessionOf(uri) is string sessionId) window.OpenSessionById(sessionId);
                else window.ShowOverlay();
            });
        };
    }

    private static void SnapshotAndQuit(MainWindow window, string path)
    {
        DispatcherTimer.RunOnce(() =>
        {
            window.SaveSnapshot(path);
            Quit();
        }, TimeSpan.FromSeconds(2));
    }

    public static void Quit()
    {
        if (Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop) desktop.Shutdown();
    }
}
