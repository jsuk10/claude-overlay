using System.Diagnostics;
using Avalonia.Controls;

namespace ClaudeSessionOverlay.Platform;

/// <summary>운영체제마다 다른 일: 세션 창 앞으로 가져오기, 음성·소리, 자리 비움, 로그인 시 실행, 오버레이 창 성격</summary>
internal interface IPlatform
{
    /// <summary>세션이 도는 창(데스크톱 앱 또는 터미널)을 앞으로 가져온다</summary>
    bool Focus(SessionInfo session);

    void OpenFolder(string path);

    /// <summary>차례로 읽는다(겹치지 않게 쌓인다)</summary>
    void Speak(string text);
    void StopSpeaking();
    void PlayChime();

    /// <summary>화면 잠금·모니터 꺼짐처럼 자리에 없을 때(매 새로고침마다 묻는다)</summary>
    bool IsAway { get; }

    bool IsAutostart { get; }
    void SetAutostart(bool isOn);
    string AutostartLabel { get; }

    /// <summary>처음 실행할 때 로그인 시 실행을 물어도 되는지(DMG에서 바로 연 앱은 그 경로가 곧 사라진다)</summary>
    bool CanOfferAutostart { get; }

    /// <summary>예/아니요 창을 띄우고 답할 때까지 기다린다</summary>
    bool Confirm(string message);

    /// <summary>창이 처음 뜬 뒤: 포커스를 뺏지 않고, Alt+Tab·Mission Control에 안 나오고, 모든 데스크톱(Space)에 보이게</summary>
    void PrepareOverlay(Window window);

    public static IPlatform Current { get; } =
        OperatingSystem.IsMacOS() ? new MacPlatform()
        : OperatingSystem.IsWindows() ? new WindowsPlatform()
        : new LinuxPlatform();
}

internal static class Shell
{
    public static void Open(string target)
    {
        try { Process.Start(new ProcessStartInfo(target) { UseShellExecute = true }); }
        catch { /* 등록된 처리기가 없다 */ }
    }

    /// <summary>프로그램을 돌려 표준 출력을 받는다(실패하면 빈 글자)</summary>
    public static string Run(string file, params string[] args)
    {
        try
        {
            var info = new ProcessStartInfo(file) { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true };
            foreach (var a in args) info.ArgumentList.Add(a);
            using var p = Process.Start(info)!;
            string output = p.StandardOutput.ReadToEnd();
            p.WaitForExit(3000);
            return output;
        }
        catch { return ""; }
    }

    public static Process? Start(string file, params string[] args)
    {
        try
        {
            var info = new ProcessStartInfo(file) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
            foreach (var a in args) info.ArgumentList.Add(a);
            return Process.Start(info);
        }
        catch { return null; }
    }

    public static string DeepLink(string hostSessionId) => Links.DeepLink(hostSessionId);
}

/// <summary>음성 프로그램(say, spd-say)과 알림음을 한 번에 하나씩 돌리는 줄(알림음이 끝난 뒤 읽는다)</summary>
internal sealed class ProcessSpeechQueue
{
    private readonly Func<string, Process?> _start;
    private readonly System.Threading.Channels.Channel<Func<Process?>> _queue = System.Threading.Channels.Channel.CreateUnbounded<Func<Process?>>();
    private Process? _current;

    public ProcessSpeechQueue(Func<string, Process?> start)
    {
        _start = start;
        Task.Run(async () =>
        {
            await foreach (var run in _queue.Reader.ReadAllAsync())
            {
                var p = run();
                if (p is null) continue;
                _current = p;
                try { await p.WaitForExitAsync(); } catch { }
                _current = null;
                p.Dispose();
            }
        });
    }

    public void Enqueue(string text) => _queue.Writer.TryWrite(() => _start(text));

    /// <summary>음성이 아닌 소리(알림음 등): 같은 줄에 세워 겹치지 않게 한다</summary>
    public void EnqueueSound(Func<Process?> start) => _queue.Writer.TryWrite(start);

    public void Stop()
    {
        while (_queue.Reader.TryRead(out _)) { }
        try { _current?.Kill(); } catch { }
    }
}
