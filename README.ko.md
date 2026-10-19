[English](README.md) | **한국어** | [소개 페이지](https://claude-overlay.jsuk.cloud/ko/)

# Claude Code 세션 오버레이 (Windows·macOS)

지금 돌고 있는 **Claude Code** 세션을 한눈에 보여 주는, 항상 위에 떠 있는 Windows·macOS 오버레이입니다(맥에서는 메뉴 막대와 위젯도). CLI 세션과 Claude 데스크톱 앱의 Code 탭 세션을 모두 보여 줍니다. 어떤 세션이 작업 중이고 어떤 세션이 내 입력을 기다리는지, 컨텍스트가 얼마나 찼는지, 주간·5시간 세션 한도가 얼마나 남았는지 보이고, 누르면 그 세션으로 바로 갑니다.

<p align="center">
  <img src="docs/screenshot-ko-dark.png" alt="Claude Code 세션 오버레이, 다크 테마" width="340">
  <img src="docs/screenshot-ko-light.png" alt="Claude Code 세션 오버레이, 라이트 테마" width="340">
</p>

> Anthropic과 관련이 없거나 Anthropic이 보증하지 않는 개인 프로젝트입니다. "Claude"와 "Claude Code"는 Anthropic의 상표입니다.

## 기능

- **세션 현황**: 작업 중, 입력 대기(권한 확인·질문), 끝났는데 아직 확인하지 않은 세션을 보여 줍니다. 이미 확인한 세션은 숨기고, ★ 즐겨찾기는 항상 보입니다.
- **눌러서 이동**: 그 세션을 앞으로 가져옵니다. 데스크톱 앱에서 분할 화면에 이미 떠 있는 세션이면, 첫 번째 화면을 바꾸지 않고 그 화면의 입력창으로 포커스를 옮깁니다.
- **컨텍스트**: 세션마다 `46%`처럼 보입니다. **자동 압축 기준**(100%가 되면 압축)으로 잰 값이고, 색은 데스크톱 앱 사용량 표시와 같습니다: 75% 미만 파랑, 90% 미만 노랑, 그 이상 빨강.
- **사용량 한도**: 머리 줄에 주간 사용량, 맨 아래 줄에 5시간 세션 한도.
- **백그라운드 작업**: 백그라운드 서브에이전트, 백그라운드 셸, 파생 세션을 상위 세션 아래 `▸ 3` 칩에 접어 두고, 누르면 펼칩니다.
- **알림**: 세션이 끝나거나, 입력을 기다리기 시작하거나, 컨텍스트가 90%를 넘으면 짧은 소리와 음성(Windows 기본 TTS)으로 알립니다. 화면 잠금·절전·모니터 꺼짐 중에는 울리지 않습니다.
- **방해하지 않기**: 게임 같은 전체 화면 앱이 앞에 있으면 숨고, 트레이로 최소화하거나 머리 줄만 남기고 접을 수 있습니다. 위치를 기억하고, Windows 시작 시 실행할 수 있습니다.
- **저장소 구분**: 경로를 `드라이브\…\마지막 두 폴더`로 줄여 보여 줘서, 같은 저장소를 C:와 D:에 둬도 구분됩니다.
- **다크/라이트 테마, 한국어/영어 화면**: 맨 아래 버튼으로 테마를 바꾸고, 머리 줄 오른쪽 클릭 메뉴에서 테마와 언어를 고릅니다.

## 설치

Windows 10 또는 11과 Claude Code(CLI) 또는 Claude 데스크톱 앱이 필요합니다.

1. [`ClaudeSessionOverlay.exe`](https://github.com/jsuk10/claude-overlay/releases/latest/download/ClaudeSessionOverlay.exe)를 받아 실행합니다. 파일 하나라 원하는 폴더에 두면 되고, .NET을 따로 설치할 필요가 없습니다.
2. 처음 실행하면 Windows 시작 시 같이 실행할지 묻습니다. 나중에 트레이 아이콘 메뉴의 **Windows 시작 시 실행**에서 바꿀 수 있습니다.

서명되지 않은 앱이라 처음 실행할 때 Windows SmartScreen 경고가 뜰 수 있습니다. **추가 정보 → 실행**을 누르면 됩니다.

설치해서 쓰려면 [`ClaudeSessionOverlay-Setup.exe`](https://github.com/jsuk10/claude-overlay/releases/latest/download/ClaudeSessionOverlay-Setup.exe)를 받으세요. 관리자 권한 없이 `%LOCALAPPDATA%\Programs`에 설치하고 시작 메뉴에 바로 가기를 만듭니다. 설치하면서 **Windows 시작 시 실행**과 아래의 [`session-hud` Mod](#선택-session-hud-mod)를 고를 수 있고, 제거하면 둘 다 지웁니다. [릴리스 페이지](https://github.com/jsuk10/claude-overlay/releases/latest)에는 exe와 README, 선택 사항인 Mod를 묶은 zip도 있습니다.

### 소스에서 빌드

[.NET 9 SDK](https://dotnet.microsoft.com/download/dotnet/9.0)가 필요합니다.

```powershell
git clone https://github.com/jsuk10/claude-overlay.git
cd claude-overlay
dotnet publish app -c Release -o bin
.\bin\ClaudeSessionOverlay.exe
```

## macOS

macOS 11 이상에서 메뉴 막대 앱으로 돕니다(위젯은 macOS 14 이상). `avalonia/`의 Avalonia 오버레이가 Windows 앱과 `core/`를 함께 쓰고, 메뉴 막대 상태 항목과 위젯은 `macos/`의 작은 Swift 코드가 맡습니다. Apple Silicon Mac(macOS 15)에서 시험했습니다. 구조는 [macos/README.md](macos/README.md)에 있습니다.

### 설치

1. [최신 릴리스](https://github.com/jsuk10/claude-overlay/releases/latest)에서 `ClaudeSessionOverlay-macos-arm64.dmg`(Apple Silicon) 또는 `ClaudeSessionOverlay-macos-x64.dmg`(Intel)를 받습니다.
2. DMG를 열고 **Claude Session Overlay**를 **Applications**로 끌어다 놓은 뒤 실행합니다. 메뉴 막대에 ✻ 아이콘이 생기고, 처음 실행하면 로그인할 때 같이 열지 묻습니다(나중에 메뉴의 **로그인 시 열기**에서 바꿉니다).
3. 아직 Apple 공증 전이라 처음 열 때 경고가 뜹니다. **시스템 설정 → 개인정보 보호 및 보안 → 그래도 열기**를 누르세요.

터미널 한 줄로 설치하면 경고 없이 설치되고 바로 실행됩니다.

```bash
curl -fsSL https://claude-overlay.jsuk.cloud/install.sh | bash
```

### 메뉴 막대와 위젯

- **메뉴 막대**: ✻ 옆에 입력 대기(노랑)·작업 중(주황)·완료(초록) 세션 수가 색 점으로 보이고, 5시간 한도가 75%를 넘으면 그 %도 보입니다. 누르면 세션 목록(고르면 그 세션으로 이동), 주간·5시간 사용량, 오버레이 명령(보이기/숨기기, 알림, 로그인 시 열기, 종료)이 나옵니다.
- **위젯**: 알림 센터(메뉴 막대 오른쪽 날짜·시간을 눌러 여는 옆 창)나 바탕화면에 둡니다. 바탕화면 오른쪽 클릭 → **위젯 편집** → "Claude Session Overlay". 작게(상태별 개수·5시간 한도), 중간(세션 3개), 크게(세션 7개·사용량 바), 아주 크게(바탕화면용: 개수·사용량과 세션 7개·폴더). 세션을 누르면 그 세션으로 갑니다.
- **제어 센터**(macOS 26 이상): 제어 센터나 메뉴 막대에 "Claude 세션" 컨트롤을 넣으면 입력 대기 세션 수가 보이고, 누르면 그 세션(없으면 오버레이)이 열립니다. 제어 센터 → **컨트롤 편집**에서 추가합니다.
- 위젯은 오버레이가 쓰는 `~/.claude/session-hud/status.json`을 읽습니다. 오버레이가 꺼져 있으면 몇 분 뒤 "오버레이가 꺼져 있어요"로 바뀝니다.

<p align="center"><img src="docs/widget-ko.png" alt="macOS 위젯(작게·중간)" width="550"></p>

### 소스에서 빌드

필요한 것: [.NET 9 SDK](https://dotnet.microsoft.com/download/dotnet/9.0), Xcode 또는 Command Line Tools(`xcode-select --install`).

```bash
git clone https://github.com/jsuk10/claude-overlay.git
cd claude-overlay
./scripts/make-macos-app.sh          # Intel Mac: ./scripts/make-macos-app.sh osx-x64
open "out/Claude Session Overlay.app"
```

스크립트가 Swift 코드(메뉴 막대 라이브러리, 위젯)를 universal로 빌드하고, .NET 자체 포함 빌드와 함께 `.app`(Dock 아이콘 없는 메뉴 막대 앱)으로 묶어 서명한 뒤 `out/ClaudeSessionOverlay-macos-<arm64|x64>.dmg`를 만듭니다. 기본은 임시 서명이고, `SIGN_IDENTITY`에 Developer ID 인증서 이름을 주면 그 인증서와 강화 런타임으로 서명합니다. `v*` 태그를 올리면 GitHub Actions([release.yml](.github/workflows/release.yml))가 Windows 설치 파일·zip·exe와 두 DMG를 만들어 그 릴리스에 붙입니다.

Windows와 다른 점: 데스크톱 앱 세션은 늘 `claude://` 딥 링크로 엽니다(분할 화면 포커스 없음). 터미널 세션은 Terminal·iTerm2면 그 탭까지 골라 앞으로 가져오고(처음 한 번 자동화 허용을 묻습니다), 다른 앱(VS Code 등)은 앱만 앞으로 가져옵니다. 음성 알림은 `say`로 하고, 장난감·저품질 목소리를 빼고 Yuna(한국어)처럼 자연스러운 목소리를 고릅니다. 오버레이는 모든 Space에 보이지만 전체 화면 앱 위에는 뜨지 않습니다. 데스크톱 앱 데이터는 `~/Library/Application Support/Claude/`에서 읽습니다.

## 동작 방식

오버레이는 Claude Code와 Claude 데스크톱 앱이 원래 남기는 **로컬 파일만 읽습니다**. 네트워크 요청을 보내지 않고, 로그인 정보도 건드리지 않습니다.

| 정보 | 출처 |
| --- | --- |
| 세션, 상태(작업 중 / 대기 / 입력 대기) | `~/.claude/sessions/<pid>.json` |
| 모델, 컨텍스트 토큰, 백그라운드 작업 | 대화 기록 `~/.claude/projects/<프로젝트>/<세션>.jsonl` |
| 컨텍스트 창, 자동 압축 기준 | `~/.claude/cache/model-catalog/*.json`, `~/.claude/settings.json`의 `autoCompactWindow` |
| 주간·5시간 사용량 | Claude 데스크톱 앱이 쓰는 `plan-usage-history.json` |
| 제목, 분할 화면 배치 | Claude 데스크톱 앱의 세션 파일과 `claude_desktop_config.json` |

오버레이 설정(확인한 세션, 즐겨찾기, 위치, 테마, 언어)은 `~/.claude/session-hud/`에 저장됩니다.

## 선택: `session-hud` Mod

오버레이는 혼자서 동작합니다. 함께 들어 있는 Claude Code Mod를 설치하면 다음이 더해집니다.

- 세션별 **비용**과 사용량 한도의 **초기화 시각**
- 오버레이의 **압축 버튼**(`⇣`): 그 세션에서 `/compact`를 실행합니다
- Claude Code 프롬프트 위의 한 줄 HUD:

```
opus-5-5  ctx ████▎      42%  67k/160k  ⇣압축  $1.23        주간 ██▌      31%  ☆
```

Windows 설치 파일에서 **Claude Code에 session-hud Mod 넣기**를 체크하면 아래 설정을 대신 해 줍니다. 이미 다른 `session-hud` 폴더가 들어 있으면 설치한 폴더로 바꿉니다. 새로 시작하는 Claude Code 세션부터 로드됩니다.

손으로 하려면, 새로 시작하는 모든 세션에 로드하도록 `~/.claude/settings.json`에서 `CLAUDE_CODE_PLUGIN_DIRS`가 Mod 폴더를 가리키게 합니다.

```json
{
  "env": { "CLAUDE_CODE_PLUGIN_DIRS": "C:\\path\\to\\claude-overlay\\mod\\session-hud" }
}
```

## 사용 팁

- **세션 오른쪽 클릭**: 열기, 확인 처리, 즐겨찾기, 폴더 열기
- **머리 줄 오른쪽 클릭**: 테마, 언어, 접기, 확인한 세션 다시 보기, 트레이로 숨기기
- **트레이 아이콘**: 왼쪽 클릭으로 보이기/숨기기. 메뉴에 소리·음성 켜고 끄기, 알림 들어보기, 위치 초기화, Windows 시작 시 실행이 있습니다.
- `ClaudeSessionOverlay.exe --demo`는 지어낸 세션으로 오버레이를 띄웁니다(`--theme light`, `--lang ko`).
- `ClaudeSessionOverlay.exe --dump`는 지금 오버레이가 보는 내용을 `~/.claude/session-hud/dump.txt`에 남깁니다.

## 폴더 구성

| 폴더 | 내용 |
| --- | --- |
| `app/` | Windows 오버레이(C#, WPF, .NET 9) |
| `core/` | 세션 파일 읽기·목록 계산 공용 부분(화면 없음) |
| `avalonia/` | macOS용 여러 OS 오버레이(Avalonia, .NET 9) |
| `macos/` | macOS 메뉴 막대 상태 항목과 위젯(Swift) |
| `scripts/` | `make-macos-app.sh`: macOS `.app`과 `.dmg` 만들기 |
| `mod/session-hud/` | 선택 사항인 Claude Code Mod(TypeScript) |
| `docs/` | 스크린샷과 [소개 페이지](https://claude-overlay.jsuk.cloud/ko/) |

## 라이선스

[MIT](LICENSE)
