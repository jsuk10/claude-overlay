namespace ClaudeSessionOverlay;

/// <summary>
/// 운영체제마다 다른 파일 위치. ~/.claude는 어디서나 같고, 데스크톱 앱의 데이터 폴더만 다르다.
///   Windows: %APPDATA%\Claude (MSIX 설치면 %LOCALAPPDATA%\Packages\Claude_*\LocalCache\Roaming\Claude)
///   macOS:   ~/Library/Application Support/Claude
///   Linux:   ~/.config/Claude
/// </summary>
public static class PlatformPaths
{
    public static readonly string Home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
    public static readonly string ClaudeDir = Path.Combine(Home, ".claude");

    /// <summary>데스크톱 앱 데이터 폴더(claude-code-sessions, plan-usage-history.json, claude_desktop_config.json이 있는 곳) 중 실제로 있는 것</summary>
    public static IEnumerable<string> DesktopDataDirs()
    {
        foreach (var dir in DesktopDataCandidates())
            if (Directory.Exists(dir)) yield return dir;
    }

    private static IEnumerable<string> DesktopDataCandidates()
    {
        if (OperatingSystem.IsWindows())
        {
            // MSIX 앱은 %APPDATA%를 패키지 폴더로 돌려 쓰므로 두 곳을 모두 본다
            string packages = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Packages");
            if (Directory.Exists(packages))
                foreach (var pkg in Directory.EnumerateDirectories(packages, "Claude_*"))
                    yield return Path.Combine(pkg, "LocalCache", "Roaming", "Claude");
            yield return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Claude");
        }
        else if (OperatingSystem.IsMacOS())
        {
            yield return Path.Combine(Home, "Library", "Application Support", "Claude");
        }
        else
        {
            string config = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME") is { Length: > 0 } xdg ? xdg : Path.Combine(Home, ".config");
            yield return Path.Combine(config, "Claude");
        }
    }

    /// <summary>데스크톱 앱 Code 탭이 세션 파일을 두는 곳들</summary>
    public static IEnumerable<string> DesktopSessionRoots() =>
        DesktopDataDirs().Select(d => Path.Combine(d, "claude-code-sessions")).Where(Directory.Exists);

    /// <summary>데스크톱 앱 설정(분할 화면 배치 등)</summary>
    public static IEnumerable<string> DesktopConfigFiles() =>
        DesktopDataDirs().Select(d => Path.Combine(d, "claude_desktop_config.json"));
}
