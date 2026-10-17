**English** | [한국어](README.ko.md)

# Claude Code Session Overlay for Windows

An always-on-top Windows overlay that shows every running **Claude Code** session at a glance — CLI and the Claude desktop app's Code tab alike. See which sessions are working, which need your input, how full each context window is, and how much of your weekly and 5-hour session limits you have left. Click a session to jump straight to it.

<p align="center">
  <img src="docs/screenshot-en-dark.png" alt="Claude Code session overlay, dark theme" width="340">
  <img src="docs/screenshot-en-light.png" alt="Claude Code session overlay, light theme" width="340">
</p>

> Not affiliated with or endorsed by Anthropic. "Claude" and "Claude Code" are trademarks of Anthropic.

## Features

- **Live session list** — working, waiting for input (permission prompts, questions) and finished-but-unseen sessions. Sessions you have already looked at are hidden; ★ favorites always stay.
- **Click to jump** — brings the session to the front. For desktop-app sessions shown in split view, focus moves to that pane's prompt box instead of replacing the first pane.
- **Context at a glance** — `46%` next to each session, measured against the **auto-compact threshold** (100% = compaction happens), colored like the Claude desktop usage meter: blue < 75%, yellow < 90%, red ≥ 90%.
- **Usage limits** — weekly usage in the header and the 5-hour session limit in the footer.
- **Background tasks** — background subagents, background shells and side sessions are folded under their parent session (`▸ 3`), click to expand.
- **Alerts** — a chime plus a short voice alert (Windows built-in TTS) when a session finishes, needs input, or its context passes 90%. Muted while the screen is locked, the PC is asleep or the display is off.
- **Stays out of the way** — hides itself when a fullscreen app or game is in front, minimizes to the tray, collapses to a single header line, remembers its position, and can start with Windows.
- **Tells repos apart** — the path is shortened to `drive\…\last two folders`, so the same repo cloned on `C:` and `D:` is easy to tell apart.
- **Dark / light theme and Korean / English UI** — toggle the theme in the footer; pick theme and language from the header's right-click menu.

## Install

Requires Windows 10 or 11 and Claude Code (CLI) and/or the Claude desktop app.

1. Download `ClaudeSessionOverlay-v*-win-x64.zip` from the [latest release](https://github.com/jsuk10/claude-overlay/releases/latest).
2. Unzip it anywhere and run `ClaudeSessionOverlay.exe`. No .NET install needed.
3. Optional: turn on **Start with Windows** from the tray icon's menu.

Windows SmartScreen may warn about an unsigned app the first time; choose **More info → Run anyway**.

### Build from source

Needs the [.NET 9 SDK](https://dotnet.microsoft.com/download/dotnet/9.0).

```powershell
git clone https://github.com/jsuk10/claude-overlay.git
cd claude-overlay
dotnet publish app -c Release -o bin
.\bin\ClaudeSessionOverlay.exe
```

## How it works

The overlay only **reads local files** that Claude Code and the Claude desktop app already write. It makes no network requests and never touches your credentials.

| Data | Source |
| --- | --- |
| Sessions, status (working / idle / waiting for input) | `~/.claude/sessions/<pid>.json` |
| Model, context tokens, background tasks | Session transcripts `~/.claude/projects/<project>/<session>.jsonl` |
| Context window, auto-compact threshold | `~/.claude/cache/model-catalog/*.json`, `autoCompactWindow` in `~/.claude/settings.json` |
| Weekly and 5-hour usage | `plan-usage-history.json` written by the Claude desktop app |
| Titles, split-view layout | Claude desktop app session files and `claude_desktop_config.json` |

Overlay settings (seen sessions, favorites, position, theme, language) are stored in `~/.claude/session-hud/`.

## Optional: the `session-hud` mod

The overlay works on its own. Installing the bundled Claude Code mod adds:

- **Cost** per session and the **reset time** of the usage limits
- A **compact button** (`⇣`) in the overlay that runs `/compact` in that session
- A one-line HUD above the prompt inside Claude Code (its labels are currently Korean: `압축` = compact, `주간` = week):

```
opus-5-5  ctx ████▎      42%  67k/160k  ⇣압축  $1.23        주간 ██▌      31%  ☆
```

To load it in every new session, point `CLAUDE_CODE_PLUGIN_DIRS` at the mod folder in `~/.claude/settings.json`:

```json
{
  "env": { "CLAUDE_CODE_PLUGIN_DIRS": "C:\\path\\to\\claude-overlay\\mod\\session-hud" }
}
```

## Usage tips

- **Right-click a session** — open, mark as seen, favorite, open folder.
- **Right-click the header** — theme, language, collapse, show seen sessions again, hide to tray.
- **Tray icon** — left-click shows/hides the overlay; the menu has sound/voice toggles, a test alert, reset position and start with Windows.
- `ClaudeSessionOverlay.exe --demo` shows the overlay with made-up sessions (`--theme light`, `--lang ko`).
- `ClaudeSessionOverlay.exe --dump` writes what the overlay currently sees to `~/.claude/session-hud/dump.txt`.

## Project layout

| Folder | Contents |
| --- | --- |
| `app/` | The overlay (C#, WPF, .NET 9) |
| `mod/session-hud/` | The optional Claude Code mod (TypeScript) |
| `docs/` | Screenshots |

## License

[MIT](LICENSE)
