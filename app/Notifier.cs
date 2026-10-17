using System.IO;
using System.Media;
using System.Runtime.InteropServices;
using System.Windows.Interop;
using Microsoft.Win32;
using System.Speech.Synthesis;
using System.Text.RegularExpressions;

namespace ClaudeSessionOverlay;

/// <summary>세션이 끝나거나 사람을 기다릴 때 짧은 소리와 한국어 음성으로 알린다.</summary>
internal sealed partial class Notifier : IDisposable
{
    private static readonly string ChimeFile =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Media", "Windows Notify System Generic.wav");

    private readonly SessionStore _store;
    private readonly SpeechSynthesizer? _voice;
    private readonly SoundPlayer? _chime;

    public Notifier(SessionStore store)
    {
        _store = store;
        try
        {
            _voice = new SpeechSynthesizer();
            SelectVoice();
            Loc.Changed += SelectVoice;
            _voice.SetOutputToDefaultAudioDevice();
            _voice.Rate = 1;
        }
        catch { _voice = null; }

        try { _chime = File.Exists(ChimeFile) ? new SoundPlayer(ChimeFile) : null; }
        catch { _chime = null; }

        SystemEvents.SessionSwitch += OnSessionSwitch;
        SystemEvents.PowerModeChanged += OnPowerModeChanged;
        SystemEvents.SessionEnding += OnSessionEnding;
    }

    // ── 자리에 없을 때는 울리지 않는다: 잠금·로그오프·절전·모니터 꺼짐 ──────

    // 돌아온 직후 밀려 있던 알림이 한꺼번에 울리지 않게 잠깐 더 조용히 있는다
    private static readonly TimeSpan QuietAfterReturn = TimeSpan.FromSeconds(15);

    private bool _isLocked;
    private bool _isSuspended;
    private bool _isDisplayOff;
    private DateTime _quietUntil;
    private IntPtr _displayNotification;

    private bool IsAway => _isLocked || _isSuspended || _isDisplayOff || DateTime.Now < _quietUntil;

    /// <summary>모니터 꺼짐/켜짐을 이 창으로 받는다</summary>
    public void WatchDisplay(IntPtr hwnd)
    {
        var guid = Native.GUID_CONSOLE_DISPLAY_STATE;
        _displayNotification = Native.RegisterPowerSettingNotification(hwnd, ref guid, 0);
        HwndSource.FromHwnd(hwnd)?.AddHook(OnWindowMessage);
    }

    private IntPtr OnWindowMessage(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == Native.WM_POWERBROADCAST && wParam == Native.PBT_POWERSETTINGCHANGE && lParam != IntPtr.Zero)
        {
            var setting = Marshal.PtrToStructure<Native.POWERBROADCAST_SETTING>(lParam);
            if (setting.PowerSetting == Native.GUID_CONSOLE_DISPLAY_STATE)
                SetAway(ref _isDisplayOff, setting.Data == 0); // 0 꺼짐, 1 켜짐, 2 어둡게
        }
        return IntPtr.Zero;
    }

    private void OnSessionSwitch(object? sender, SessionSwitchEventArgs e)
    {
        switch (e.Reason)
        {
            case SessionSwitchReason.SessionLock or SessionSwitchReason.SessionLogoff
                or SessionSwitchReason.ConsoleDisconnect or SessionSwitchReason.RemoteDisconnect:
                SetAway(ref _isLocked, true);
                break;
            case SessionSwitchReason.SessionUnlock or SessionSwitchReason.SessionLogon
                or SessionSwitchReason.ConsoleConnect or SessionSwitchReason.RemoteConnect:
                SetAway(ref _isLocked, false);
                break;
        }
    }

    private void OnPowerModeChanged(object? sender, PowerModeChangedEventArgs e)
    {
        if (e.Mode == PowerModes.Suspend) SetAway(ref _isSuspended, true);
        else if (e.Mode == PowerModes.Resume) SetAway(ref _isSuspended, false);
    }

    private void OnSessionEnding(object? sender, SessionEndingEventArgs e) => SetAway(ref _isLocked, true);

    private void SetAway(ref bool flag, bool isAway)
    {
        if (flag == isAway) return;
        flag = isAway;
        if (isAway) _voice?.SpeakAsyncCancelAll(); // 읽던 것도 멈춘다
        else _quietUntil = DateTime.Now + QuietAfterReturn;
    }

    /// <summary>화면 언어에 맞는 목소리(없으면 Windows 기본 목소리)</summary>
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

    public void Announce(IReadOnlyList<Notice> notices)
    {
        if (notices.Count == 0 || IsAway) return;
        if (_store.State.IsSoundOn) PlayChime();
        if (!_store.State.IsVoiceOn || _voice is null) return;

        foreach (var notice in notices)
        {
            string name = SpokenName(notice.Session);
            string text = notice.Kind switch
            {
                NoticeKind.Done => Loc.T($"{name} 작업 종료", $"{name} done"),
                NoticeKind.Waiting => Loc.T($"{name} 확인 필요", $"{name} needs input"),
                _ => Loc.T($"{name} 컨텍스트 {notice.Session.ContextPercent ?? 90:0}퍼센트", $"{name} context {notice.Session.ContextPercent ?? 90:0} percent"),
            };
            _voice.SpeakAsync(text); // 차례로 쌓여서 겹치지 않는다
        }
    }

    public void Test()
    {
        PlayChime();
        _voice?.SpeakAsync(Loc.T("알림 테스트", "Notification test"));
    }

    private void PlayChime()
    {
        try { _chime?.Play(); }
        catch { SystemSounds.Asterisk.Play(); }
    }

    /// <summary>읽기 좋은 길이로 자른 세션 이름: 기호를 빼고 앞쪽 몇 단어만</summary>
    private static string SpokenName(SessionInfo session)
    {
        string title = session.Title != "" ? session.Title : Path.GetFileName(session.Cwd.TrimEnd('\\', '/'));
        title = Symbols().Replace(title, " ").Trim();
        if (title == "") return Loc.T("Claude 세션", "Claude session");
        return title.Length <= 24 ? title : title[..24];
    }

    [GeneratedRegex(@"[^\p{L}\p{N}\s]+")]
    private static partial Regex Symbols();

    public void Dispose()
    {
        Loc.Changed -= SelectVoice;
        SystemEvents.SessionSwitch -= OnSessionSwitch;
        SystemEvents.PowerModeChanged -= OnPowerModeChanged;
        SystemEvents.SessionEnding -= OnSessionEnding;
        if (_displayNotification != IntPtr.Zero) Native.UnregisterPowerSettingNotification(_displayNotification);
        _voice?.Dispose();
        _chime?.Dispose();
    }
}
