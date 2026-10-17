using ClaudeSessionOverlay.Platform;

namespace ClaudeSessionOverlay;

/// <summary>세션이 끝나거나 사람을 기다릴 때 짧은 소리와 음성으로 알린다. 자리에 없을 때(잠금·잠자기·디스플레이 꺼짐)는 울리지 않는다</summary>
internal sealed class Notifier(SessionStore store, IPlatform platform)
{
    // 돌아온 직후 밀려 있던 알림이 한꺼번에 울리지 않게 잠깐 더 조용히 있는다
    private static readonly TimeSpan QuietAfterReturn = TimeSpan.FromSeconds(15);
    // 새로고침(1초) 사이가 이만큼 벌어졌으면 잠자기에서 깨어난 것으로 본다
    private static readonly TimeSpan SleepGap = TimeSpan.FromSeconds(10);

    private DateTime _lastTick = DateTime.Now;
    private DateTime _quietUntil;
    private bool _wasAway;

    /// <summary>새로고침마다: 자리 비움이 바뀌었는지 본다</summary>
    public void Tick()
    {
        var now = DateTime.Now;
        bool isAway = platform.IsAway;
        if (now - _lastTick > SleepGap) _quietUntil = now + QuietAfterReturn;
        if (isAway && !_wasAway) platform.StopSpeaking(); // 읽던 것도 멈춘다
        if (!isAway && _wasAway) _quietUntil = now + QuietAfterReturn;
        _wasAway = isAway;
        _lastTick = now;
    }

    private bool IsAway => _wasAway || DateTime.Now < _quietUntil;

    public void Announce(IReadOnlyList<Notice> notices)
    {
        if (notices.Count == 0 || IsAway) return;
        if (store.State.IsSoundOn) platform.PlayChime();
        if (!store.State.IsVoiceOn) return;
        foreach (var notice in notices) platform.Speak(Overview.Spoken(notice));
    }

    public void Test()
    {
        platform.PlayChime();
        platform.Speak(Overview.TestSpoken);
    }
}
