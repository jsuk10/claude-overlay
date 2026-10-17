using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Avalonia.Controls;
using Microsoft.Win32;

namespace ClaudeSessionOverlay.Platform;

/// <summary>
/// Windows용 Avalonia 빌드. 기능이 가장 많은 Windows판은 WPF 앱(app/)이고, 이쪽은 같은 화면을 가볍게 돌린다:
/// 데스크톱 세션은 딥 링크로, 터미널 세션은 부모 프로세스의 창을 앞으로. 분할 화면 포커스·전체 화면 감지·잠금 감지는 WPF판에만 있다.
/// </summary>
[SupportedOSPlatform("windows")]
internal sealed class WindowsPlatform : IPlatform
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string RunName = "ClaudeSessionOverlay";
    private static readonly string ChimeFile =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Media", "Windows Notify System Generic.wav");

    private readonly System.Speech.Synthesis.SpeechSynthesizer? _voice;

    public WindowsPlatform()
    {
        try
        {
            _voice = new System.Speech.Synthesis.SpeechSynthesizer();
            _voice.SetOutputToDefaultAudioDevice();
            _voice.Rate = 1;
            SelectVoice();
            Loc.Changed += SelectVoice;
        }
        catch { _voice = null; }
    }

    public bool Focus(SessionInfo session)
    {
        if (!session.IsAlive) return false;
        if (session.HostSessionId is string host && host.StartsWith("local_"))
        {
            Shell.Open(Shell.DeepLink(host));
            return true;
        }
        if (session.Pid is not int pid) return false;
        var parents = ParentTable();
        var seen = new HashSet<int>();
        for (int current = pid; current != 0 && seen.Add(current);)
        {
            try
            {
                using var process = Process.GetProcessById(current);
                if (process.MainWindowHandle != IntPtr.Zero) return BringToFront(process.MainWindowHandle);
            }
            catch (ArgumentException) { }
            if (!parents.TryGetValue(current, out current)) break;
        }
        return false;
    }

    private static bool BringToFront(IntPtr hwnd)
    {
        if (IsIconic(hwnd)) ShowWindow(hwnd, 9 /* SW_RESTORE */);
        // 다른 프로세스 창을 앞으로 올리려면 포그라운드 잠금을 풀어야 한다: Alt를 한 번 눌렀다 뗀다
        keybd_event(0x12, 0, 0, UIntPtr.Zero);
        keybd_event(0x12, 0, 2, UIntPtr.Zero);
        return SetForegroundWindow(hwnd);
    }

    public void OpenFolder(string path) =>
        Process.Start(new ProcessStartInfo("explorer.exe", $"\"{path}\"") { UseShellExecute = true });

    public void Speak(string text) => _voice?.SpeakAsync(text);
    public void StopSpeaking() => _voice?.SpeakAsyncCancelAll();

    public void PlayChime()
    {
        if (!File.Exists(ChimeFile) || !PlaySound(ChimeFile, IntPtr.Zero, 0x0001 | 0x00020000 /* SND_ASYNC | SND_FILENAME */))
            MessageBeep(0x40);
    }

    private void SelectVoice()
    {
        if (_voice is null) return;
        string prefix = Loc.IsEnglish ? "en" : "ko";
        try
        {
            var voice = _voice.GetInstalledVoices()
                .FirstOrDefault(v => v.Enabled && v.VoiceInfo.Culture.Name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
            if (voice is not null) _voice.SelectVoice(voice.VoiceInfo.Name);
        }
        catch (InvalidOperationException) { }
    }

    public bool IsAway => false;


    public string AutostartLabel => Loc.T("Windows 시작 시 실행", "Start with Windows");

    public bool IsAutostart
    {
        get
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey);
            return key?.GetValue(RunName) is string;
        }
    }

    public void SetAutostart(bool isOn)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKey);
        if (isOn) key.SetValue(RunName, $"\"{Environment.ProcessPath}\"");
        else key.DeleteValue(RunName, false);
    }

    public void PrepareOverlay(Window window)
    {
        // Alt+Tab에 안 나온다
        if (window.TryGetPlatformHandle()?.Handle is not { } hwnd || hwnd == IntPtr.Zero) return;
        const int GWL_EXSTYLE = -20, WS_EX_TOOLWINDOW = 0x80;
        SetWindowLong(hwnd, GWL_EXSTYLE, GetWindowLong(hwnd, GWL_EXSTYLE) | WS_EX_TOOLWINDOW);
    }

    /// <summary>pid → 부모 pid</summary>
    private static Dictionary<int, int> ParentTable()
    {
        var table = new Dictionary<int, int>();
        IntPtr snapshot = CreateToolhelp32Snapshot(0x2 /* TH32CS_SNAPPROCESS */, 0);
        if (snapshot == new IntPtr(-1)) return table;
        try
        {
            var entry = new PROCESSENTRY32 { dwSize = (uint)Marshal.SizeOf<PROCESSENTRY32>() };
            for (bool ok = Process32First(snapshot, ref entry); ok; ok = Process32Next(snapshot, ref entry))
                table[(int)entry.th32ProcessID] = (int)entry.th32ParentProcessID;
        }
        finally { CloseHandle(snapshot); }
        return table;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct PROCESSENTRY32
    {
        public uint dwSize, cntUsage, th32ProcessID;
        public IntPtr th32DefaultHeapID;
        public uint th32ModuleID, cntThreads, th32ParentProcessID;
        public int pcPriClassBase;
        public uint dwFlags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string szExeFile;
    }

    [DllImport("kernel32.dll", SetLastError = true)] private static extern IntPtr CreateToolhelp32Snapshot(uint flags, uint pid);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, EntryPoint = "Process32FirstW")] private static extern bool Process32First(IntPtr snapshot, ref PROCESSENTRY32 entry);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, EntryPoint = "Process32NextW")] private static extern bool Process32Next(IntPtr snapshot, ref PROCESSENTRY32 entry);
    [DllImport("kernel32.dll")] private static extern bool CloseHandle(IntPtr handle);
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr hwnd, int cmd);
    [DllImport("user32.dll")] private static extern bool IsIconic(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern void keybd_event(byte vk, byte scan, uint flags, UIntPtr extra);
    [DllImport("user32.dll")] private static extern bool MessageBeep(uint type);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongW")] private static extern int GetWindowLong(IntPtr hwnd, int index);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongW")] private static extern int SetWindowLong(IntPtr hwnd, int index, int value);
    [DllImport("winmm.dll", CharSet = CharSet.Unicode, EntryPoint = "PlaySoundW")] private static extern bool PlaySound(string sound, IntPtr module, uint flags);
}
