using System.IO;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace ClaudeSessionOverlay;

/// <summary>
/// 설치 파일이 부른다: ~/.claude/settings.json의 env.CLAUDE_CODE_PLUGIN_DIRS에 session-hud Mod 폴더를 넣거나 뺀다.
/// Windows에서 여러 폴더는 ';'로 잇는다. 다른 설정과 다른 폴더는 그대로 둔다.
/// </summary>
internal static class ModSetup
{
    private const string VarName = "CLAUDE_CODE_PLUGIN_DIRS";
    private static string SettingsFile => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".claude", "settings.json");

    public static void Install(string modDir) => Update(dirs =>
    {
        // 다른 곳의 session-hud가 남아 있으면 Mod가 두 번 로드되니 이 폴더로 바꾼다
        dirs.RemoveAll(d => IsSessionHud(d));
        dirs.Add(Path.GetFullPath(modDir));
    });

    public static void Uninstall(string modDir) => Update(dirs => dirs.RemoveAll(d => IsSame(d, modDir)));

    private static bool IsSessionHud(string dir) =>
        string.Equals(Path.GetFileName(dir.TrimEnd('\\', '/')), "session-hud", StringComparison.OrdinalIgnoreCase);

    private static bool IsSame(string a, string b) =>
        string.Equals(Path.GetFullPath(a).TrimEnd('\\'), Path.GetFullPath(b).TrimEnd('\\'), StringComparison.OrdinalIgnoreCase);

    private static void Update(Action<List<string>> change)
    {
        var root = File.Exists(SettingsFile)
            ? JsonNode.Parse(File.ReadAllText(SettingsFile)) as JsonObject ?? throw new InvalidOperationException("settings.json is not an object")
            : [];
        if (root["env"] is not JsonObject env)
        {
            env = [];
            root["env"] = env;
        }

        var dirs = (env[VarName]?.GetValue<string>() ?? "").Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
        change(dirs);

        if (dirs.Count > 0) env[VarName] = string.Join(';', dirs);
        else env.Remove(VarName);
        if (env.Count == 0) root.Remove("env");

        Directory.CreateDirectory(Path.GetDirectoryName(SettingsFile)!);
        var options = new JsonSerializerOptions { WriteIndented = true, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };
        File.WriteAllText(SettingsFile, root.ToJsonString(options) + "\n");
    }
}
