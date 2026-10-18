using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Avalonia.Controls;

namespace ClaudeSessionOverlay.Platform;

/// <summary>
/// macOS.
///   데스크톱 앱 세션: claude://code/continue 딥 링크(앱이 그 세션을 열고 앞으로 나온다)
///   터미널 세션: 부모 프로세스를 따라 올라가 처음 만나는 .app(Terminal, iTerm, VS Code …)을 앞으로.
///     Terminal·iTerm2면 세션의 tty로 그 탭까지 고른다(AppleScript, 처음 한 번 자동화 허용을 묻는다)
///   음성: say, 소리: afplay, 로그인 시 실행: ~/Library/LaunchAgents의 plist
///   자리 비움: 화면 잠금(CGSession)·디스플레이 잠듦(CGDisplayIsAsleep)을 새로고침마다 확인
/// </summary>
[SupportedOSPlatform("macos")]
internal sealed class MacPlatform : IPlatform
{
    private const string AgentLabel = "local.claude-session-overlay";
    private static readonly string AgentFile = Path.Combine(PlatformPaths.Home, "Library", "LaunchAgents", AgentLabel + ".plist");

    private readonly ProcessSpeechQueue _speech;
    private readonly Lazy<List<(string Name, string Locale)>> _voices = new(LoadVoices);

    public MacPlatform()
    {
        _speech = new ProcessSpeechQueue(text =>
            VoiceFor(Loc.IsEnglish ? "en" : "ko") is string voice
                ? Shell.Start("/usr/bin/say", "-v", voice, text)
                : Shell.Start("/usr/bin/say", text));
    }

    // ── 앞으로 가져오기 ────────────────────────────────────────────────

    public bool Focus(SessionInfo session)
    {
        if (!session.IsAlive) return false;
        if (session.HostSessionId is string host && host.StartsWith("local_"))
        {
            // 분할 화면 위치를 알아내 포커스만 옮기는 Windows판(UI 자동화)과 달리, 늘 딥 링크로 연다
            Shell.Run("/usr/bin/open", Shell.DeepLink(host));
            return true;
        }
        if (session.Pid is not int pid) return false;
        // ps 걷기와 AppleScript(처음엔 허용 창이 떠 답을 기다린다)는 느릴 수 있어 UI 스레드 밖에서
        Task.Run(() => FocusTerminal(pid));
        return true;
    }

    private static void FocusTerminal(int pid)
    {
        string? bundle = AppBundleOf(pid);
        if (bundle is null) return;
        string? script = TtyOf(pid) is string tty ? Path.GetFileName(bundle) switch
        {
            "Terminal.app" => TerminalTabScript(tty),
            "iTerm.app" or "iTerm2.app" => ITermTabScript(tty),
            _ => null,
        } : null;
        // 탭을 못 고르면(다른 터미널, 자동화 거부, 탭이 닫힘) 앱만 앞으로
        if (script is null || Shell.Run("/usr/bin/osascript", "-e", script).Trim() != "true")
            Shell.Run("/usr/bin/open", "-a", bundle);
    }

    /// <summary>프로세스가 붙은 터미널 장치: "/dev/ttys003". 없으면 null</summary>
    private static string? TtyOf(int pid)
    {
        string tty = Shell.Run("/bin/ps", "-o", "tty=", "-p", pid.ToString()).Trim();
        return System.Text.RegularExpressions.Regex.IsMatch(tty, "^tty[a-z0-9]+$") ? "/dev/" + tty : null;
    }

    internal static string TerminalTabScript(string tty) => $$"""
        tell application id "com.apple.Terminal"
            repeat with w in windows
                repeat with t in tabs of w
                    if tty of t is "{{tty}}" then
                        set miniaturized of w to false
                        set selected of t to true
                        set index of w to 1
                        activate
                        return true
                    end if
                end repeat
            end repeat
            return false
        end tell
        """;

    internal static string ITermTabScript(string tty) => $$"""
        tell application id "com.googlecode.iterm2"
            repeat with w in windows
                repeat with t in tabs of w
                    repeat with s in sessions of t
                        if tty of s is "{{tty}}" then
                            select w
                            select t
                            select s
                            activate
                            return true
                        end if
                    end repeat
                end repeat
            end repeat
            return false
        end tell
        """;

    /// <summary>pid에서 부모 쪽으로 올라가며 실행 파일 경로에 .app이 든 첫 프로세스의 가장 바깥 .app 폴더</summary>
    private static string? AppBundleOf(int pid)
    {
        var seen = new HashSet<int>();
        for (int current = pid; current > 1 && seen.Add(current);)
        {
            string line = Shell.Run("/bin/ps", "-o", "ppid=,comm=", "-p", current.ToString()).Trim();
            if (line == "") break;
            int space = line.IndexOf(' ');
            if (space < 0 || !int.TryParse(line[..space], out int parent)) break;
            string command = line[(space + 1)..].Trim();
            int app = command.IndexOf(".app/", StringComparison.Ordinal);
            if (app > 0) return command[..(app + 4)];
            current = parent;
        }
        return null;
    }

    public void OpenFolder(string path) => Shell.Run("/usr/bin/open", path);

    // ── 음성·소리 ──────────────────────────────────────────────────────

    public void Speak(string text) => _speech.Enqueue(text);
    public void StopSpeaking() => _speech.Stop();

    // 음성과 같은 줄에 세운다: 알림음이 끝난 뒤에 읽어 둘이 겹치지 않는다
    public void PlayChime() => _speech.EnqueueSound(() => Shell.Start("/usr/bin/afplay", "/System/Library/Sounds/Glass.aiff"));

    /// <summary>
    /// say -v '?'는 이름순이라 언어만 맞춰 첫 줄을 고르면 장난감 목소리(Albert, Bells …)나
    /// 저품질 Eloquence 목소리(Eddy, Flo …)가 걸린다. 이것들은 빼고, 고급·향상 목소리를 먼저 고른다
    /// </summary>
    private static readonly HashSet<string> OddVoices = new(StringComparer.OrdinalIgnoreCase)
    {
        // 장난감·효과음 목소리
        "Albert", "Bad News", "Bahh", "Bells", "Boing", "Bubbles", "Cellos", "Good News", "Jester",
        "Organ", "Superstar", "Trinoids", "Whisper", "Wobble", "Zarvox",
        // 오래된 합성 목소리
        "Fred", "Junior", "Kathy", "Ralph",
        // Eloquence: 한국어를 뭉개 읽는다
        "Eddy", "Flo", "Grandma", "Grandpa", "Reed", "Rocko", "Sandy", "Shelley",
    };

    private static readonly string[] BetterMarks = ["Premium", "Enhanced", "고급", "향상"];

    /// <summary>화면 언어에 맞는 목소리(한국어면 Yuna 등). 쓸 만한 게 없으면 시스템 기본 목소리</summary>
    private string? VoiceFor(string language) =>
        _voices.Value
            .Where(v => v.Locale.StartsWith(language + "_", StringComparison.OrdinalIgnoreCase))
            .Where(v => !OddVoices.Contains(BaseName(v.Name)))
            .OrderByDescending(v => BetterMarks.Any(m => v.Name.Contains(m, StringComparison.OrdinalIgnoreCase)))
            .Select(v => v.Name)
            .FirstOrDefault();

    /// <summary>"Yuna (Premium)" → "Yuna", "Eddy (한국어(대한민국))" → "Eddy"</summary>
    private static string BaseName(string voice) => voice.Split(" (", 2)[0].Trim();

    /// <summary>say -v '?' 줄: "Yuna                ko_KR    # 안녕하세요…"</summary>
    private static List<(string, string)> LoadVoices()
    {
        var voices = new List<(string, string)>();
        foreach (var line in Shell.Run("/usr/bin/say", "-v", "?").Split('\n'))
        {
            int hash = line.IndexOf('#');
            var head = (hash >= 0 ? line[..hash] : line).TrimEnd();
            int lastSpace = head.LastIndexOf(' ');
            if (lastSpace <= 0) continue;
            string locale = head[(lastSpace + 1)..];
            string name = head[..lastSpace].Trim();
            if (name != "" && locale.Contains('_')) voices.Add((name, locale));
        }
        return voices;
    }

    // ── 자리 비움 ──────────────────────────────────────────────────────

    public bool IsAway
    {
        get
        {
            try { return IsScreenLocked() || CGDisplayIsAsleep(CGMainDisplayID()) != 0; }
            catch { return false; }
        }
    }

    private static bool IsScreenLocked()
    {
        IntPtr dict = CGSessionCopyCurrentDictionary();
        if (dict == IntPtr.Zero) return false;
        IntPtr key = CFStringCreateWithCString(IntPtr.Zero, "CGSSessionScreenIsLocked", 0x08000100);
        try { return CFDictionaryGetValue(dict, key) != IntPtr.Zero; }
        finally
        {
            CFRelease(key);
            CFRelease(dict);
        }
    }

    // ── 로그인 시 실행 ─────────────────────────────────────────────────

    public string AutostartLabel => Loc.T("로그인 시 열기", "Open at login");

    public bool IsAutostart => File.Exists(AgentFile);

    public void SetAutostart(bool isOn)
    {
        try
        {
            if (!isOn) { File.Delete(AgentFile); return; }
            // .app 안에서 돌면 앱으로 연다(메뉴 막대 앱답게), 아니면 실행 파일을 바로
            string exe = Environment.ProcessPath ?? "";
            int app = exe.IndexOf(".app/", StringComparison.Ordinal);
            string[] args = app > 0 ? ["/usr/bin/open", "-a", exe[..(app + 4)]] : [exe];
            string arguments = string.Concat(args.Select(a => $"\n        <string>{System.Security.SecurityElement.Escape(a)}</string>"));
            Directory.CreateDirectory(Path.GetDirectoryName(AgentFile)!);
            File.WriteAllText(AgentFile, $"""
                <?xml version="1.0" encoding="UTF-8"?>
                <!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
                <plist version="1.0">
                <dict>
                    <key>Label</key>
                    <string>{AgentLabel}</string>
                    <key>ProgramArguments</key>
                    <array>{arguments}
                    </array>
                    <key>RunAtLoad</key>
                    <true/>
                </dict>
                </plist>
                """);
        }
        catch { /* 권한 등: 메뉴 체크 표시가 그대로 남아 알 수 있다 */ }
    }

    // ── 오버레이 창 ────────────────────────────────────────────────────

    public void PrepareOverlay(Window window)
    {
        // NSView → NSWindow. 모든 Space에 보이고(canJoinAllSpaces) ⌘` 창 순환에서 빠진다(ignoresCycle)
        if (window.TryGetPlatformHandle() is not { } handle || handle.Handle == IntPtr.Zero) return;
        try
        {
            IntPtr nsWindow = handle.HandleDescriptor == "NSWindow" ? handle.Handle : ObjC.Send(handle.Handle, ObjC.Sel("window"));
            if (nsWindow == IntPtr.Zero) return;
            const nuint CanJoinAllSpaces = 1 << 0, IgnoresCycle = 1 << 6;
            ObjC.Send(nsWindow, ObjC.Sel("setCollectionBehavior:"), CanJoinAllSpaces | IgnoresCycle);
        }
        catch { /* 창 성격을 못 바꿔도 오버레이는 뜬다 */ }
    }

    // ── 네이티브 ───────────────────────────────────────────────────────

    private const string CoreGraphics = "/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics";
    private const string CoreFoundation = "/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation";

    [DllImport(CoreGraphics)] private static extern IntPtr CGSessionCopyCurrentDictionary();
    [DllImport(CoreGraphics)] private static extern uint CGMainDisplayID();
    [DllImport(CoreGraphics)] private static extern int CGDisplayIsAsleep(uint display);
    [DllImport(CoreFoundation)] private static extern IntPtr CFDictionaryGetValue(IntPtr dict, IntPtr key);
    [DllImport(CoreFoundation)] private static extern IntPtr CFStringCreateWithCString(IntPtr alloc, string text, uint encoding);
    [DllImport(CoreFoundation)] private static extern void CFRelease(IntPtr cf);

    private static class ObjC
    {
        private const string Lib = "/usr/lib/libobjc.A.dylib";
        [DllImport(Lib, EntryPoint = "sel_registerName")] public static extern IntPtr Sel(string name);
        [DllImport(Lib, EntryPoint = "objc_msgSend")] public static extern IntPtr Send(IntPtr receiver, IntPtr selector);
        [DllImport(Lib, EntryPoint = "objc_msgSend")] public static extern void Send(IntPtr receiver, IntPtr selector, nuint value);
    }
}
