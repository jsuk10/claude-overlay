using Avalonia.Controls;

namespace ClaudeSessionOverlay.Platform;

/// <summary>Linux: 시험해 보지 않은 최소 구현(딥 링크·폴더 열기는 xdg-open, 음성은 spd-say가 있으면)</summary>
internal sealed class LinuxPlatform : IPlatform
{
    private static readonly string AutostartFile = Path.Combine(
        Environment.GetEnvironmentVariable("XDG_CONFIG_HOME") is { Length: > 0 } xdg ? xdg : Path.Combine(PlatformPaths.Home, ".config"),
        "autostart", "claude-session-overlay.desktop");

    private readonly ProcessSpeechQueue _speech = new(text => Shell.Start("spd-say", "--wait", text));

    public bool Focus(SessionInfo session)
    {
        if (session.IsAlive && session.HostSessionId is string host && host.StartsWith("local_"))
        {
            Shell.Start("xdg-open", Shell.DeepLink(host))?.Dispose();
            return true;
        }
        return false;
    }

    public void OpenFolder(string path) => Shell.Start("xdg-open", path)?.Dispose();
    public void Speak(string text) => _speech.Enqueue(text);
    public void StopSpeaking() => _speech.Stop();
    public void PlayChime() { }
    public bool IsAway => false;
    public string AutostartLabel => Loc.T("로그인 시 열기", "Open at login");
    public bool IsAutostart => File.Exists(AutostartFile);

    public void SetAutostart(bool isOn)
    {
        try
        {
            if (!isOn) { File.Delete(AutostartFile); return; }
            Directory.CreateDirectory(Path.GetDirectoryName(AutostartFile)!);
            File.WriteAllText(AutostartFile, $"[Desktop Entry]\nType=Application\nName=Claude Session Overlay\nExec=\"{Environment.ProcessPath}\"\n");
        }
        catch { }
    }

    public void PrepareOverlay(Window window) { }
}
