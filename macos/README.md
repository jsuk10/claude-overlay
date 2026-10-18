# macOS: 메뉴 막대와 위젯 설계

macOS판은 Avalonia 오버레이(`avalonia/`) 하나의 프로세스에 작은 Swift 코드 두 덩어리를 붙인 구조입니다.
세션을 읽고 목록을 정하는 일은 전부 C#(`core/`)이 하고, Swift는 받은 내용을 **그리기만** 합니다.
그래서 규칙(정렬, 색, 문구, 한국어/영어)은 Windows·macOS가 한곳(`core/`)에서 같이 바뀝니다.

```
 ~/.claude/sessions, projects/*.jsonl, 데스크톱 앱 파일
                │  1초마다 읽기
                ▼
 ┌──────── Claude Session Overlay.app (한 프로세스) ─────────────────┐
 │  core/  SessionStore → Overview.BuildRows → StatusBoard.Build    │
 │                                   │ BoardDoc                     │
 │          ┌────────────────────────┼──────────────────────┐       │
 │          ▼                        ▼                      ▼       │
 │   Avalonia 오버레이 창      MacBridge (P/Invoke)    StatusFeed      │
 │   (항상 위 카드)            libClaudeOverlayMac      status.json    │
 │                            .dylib (Swift)          쓰기(바뀔 때·1분) │
 │                            ├ NSStatusItem + NSMenu       │       │
 │                            └ WidgetCenter.reload ────┐   │       │
 │                                                      │   │       │
 │  Contents/PlugIns/ClaudeSessionWidget.appex ◀────────┘   │       │
 │  (WidgetKit, 샌드박스, status.json 읽기 전용) ◀────────────┘       │
 └──────────────────────────────────────────────────────────────────┘
        위젯에서 누름 → claude://code/continue?session=…   (데스크톱 앱 세션)
                     → claude-overlay://open?session=…    (터미널 세션: 오버레이가 받아 그 탭·앱을 앞으로)
```

## 파일

| 파일 | 하는 일 |
| --- | --- |
| `core/StatusBoard.cs` | `BoardDoc`(요약판) 만들기, 위젯용 지문, `StatusFeed`(status.json 쓰기·위젯 새로 고침 시점), `Links`(주소) |
| `avalonia/Platform/MacBridge.cs` | dylib를 `NativeLibrary`로 찾아 `cso_*` 함수를 부른다. 없으면 `IsAvailable = false` |
| `avalonia/Tray.cs` | 명령 목록 하나로 두 가지를 만든다: macOS+dylib면 네이티브 메뉴 막대, 아니면 Avalonia 트레이 |
| `avalonia/App.axaml.cs` | `claude-overlay://` 주소 받기(`IActivatableLifetime`) |
| `macos/Shared/Board.swift` | `BoardDoc`과 같은 모양의 Swift 모델. 메뉴 막대와 위젯이 같이 쓴다 |
| `macos/Bridge/StatusBar.swift` | `libClaudeOverlayMac.dylib`: 상태 항목, 메뉴, 위젯 새로 고침 |
| `macos/Widget/` | 위젯 확장(`.appex`): SwiftUI 화면, Info.plist, 샌드박스 권한 |
| `scripts/make-icon.swift` | 앱 아이콘 그리기(assets/icon의 PNG·ico, Windows와 같이 씀). make-macos-app.sh가 이 PNG로 .icns를 묶는다 |
| `scripts/make-macos-app.sh` | Swift 빌드(universal) → .NET 게시 → `.app` 조립 → 서명 → `.dmg` |
| `docs/install.sh` | 웹에서 한 줄 설치(curl로 DMG를 받아 Applications에) |
| `.github/workflows/release.yml` | `v*` 태그에서 Windows zip·exe와 두 DMG를 만들어 릴리스에 붙인다 |

## 메뉴 막대

- C#이 1초마다 `{ board, commands }` JSON을 `cso_status_update`로 넘긴다. 같은 프로세스 안이라 파일을 거치지 않는다.
- 메뉴 막대 글자는 개수·5시간 %가 바뀔 때만 다시 그린다. `✻ ●1 ●2 ●1` — 점 색은 `core/Palette.cs`의 상태 색, 숫자는 시스템 글자색(밝은·어두운 메뉴 막대 모두 읽힌다). 아이콘은 템플릿 이미지라 메뉴 막대가 칠한다.
- 메뉴는 열 때(`menuNeedsUpdate`) 최신 내용으로 다시 채운다: 머리 줄 요약 → 세션(최대 15개, 두 줄: 제목 / 상태·모델·폴더·컨텍스트 %) → 주간·5시간 사용량 → 오버레이 명령.
- 고른 항목은 id 문자열로 C# 콜백(`[UnmanagedCallersOnly]`)에 돌아온다. `open:<세션 id>`면 그 세션을 열고 확인 처리, 아니면 명령 id(`toggleHidden`, `voice`, `quit` …).
- dylib가 없으면(예: `dotnet run`만 하고 Swift를 빌드하지 않았을 때) 예전처럼 Avalonia 트레이 아이콘과 명령 메뉴만 나온다.

## 위젯

- **데이터**: 위젯 확장은 샌드박스여야 해서 오버레이와 직접 말하지 못한다. 오버레이가 `~/.claude/session-hud/status.json`을 쓰고, 위젯은 `temporary-exception.files.home-relative-path.read-only`(`/.claude/session-hud/`) 권한으로 그 폴더만 읽는다. 샌드박스 안에서는 `NSHomeDirectory()`가 컨테이너를 가리키므로 `getpwuid`로 실제 홈을 찾는다.
  - App Group을 쓰지 않은 까닭: 팀 ID가 있는 인증서가 필요하고, macOS 15부터는 다른 앱 컨테이너 접근에 확인 창이 뜬다. 임시 서명으로도 동작하는 쪽을 골랐다.
- **새로 고침**: `StatusFeed`가 지문(상태·제목·컨텍스트 % 등, 경과 시간은 뺀다)이 바뀔 때만 파일을 쓰고 `WidgetCenter.reloadAllTimelines()`를 부른다(최소 5초 간격). 시스템이 하루 새로 고침 횟수를 제한하므로 매초 부르지 않는다.
  - 경과 시간("작업 중 12:03")은 `Text(date, style: .timer)`라 위젯이 스스로 센다.
  - 오버레이가 1분마다 파일을 다시 써서 `updatedAt`을 갱신한다. 타임라인에 6분 뒤 항목을 하나 더 넣어, 그때까지 새 내용이 없으면 "오버레이가 꺼져 있어요"로 바뀐다.
- **크기**: 작게(상태별 개수·5시간 한도) / 중간(세션 3개 + 한 줄 사용량) / 크게(세션 7개 + 사용량 바) / 아주 크게(바탕화면용: 왼쪽 개수·요약·사용량, 오른쪽 세션 7개와 폴더). 같은 위젯을 알림 센터와 바탕화면 어디에나 둘 수 있다.

## 제어 센터 컨트롤 (macOS 26+)

- `macos/Widget/SessionsControl.swift`: `ControlWidgetButton` 하나. 값은 위젯과 같은 status.json에서 읽어 "입력 대기 N" / "작업 중 N" / "완료 N"을 보이고, 누르면 `OpenURLIntent`로 첫 입력 대기 세션(없으면 `claude-overlay://show`)을 연다.
- 컨트롤 API는 macOS 26 SDK에만 있어 파일 전체와 위젯 묶음의 `if #available(macOS 26.0, *)`를 `#if compiler(>=6.2)`로 감쌌다. Xcode 16(macOS 15 SDK)으로 빌드하면 컨트롤만 빠지고 나머지는 그대로다.
- 릴리스는 macOS 26 실행기(Xcode 26)에서 빌드하므로 컨트롤이 들어가고, 위젯 확장의 최소 버전은 그대로 14라 macOS 14·15에서도 위젯은 돈다.
- 오버레이가 위젯을 다시 불러 달라고 할 때 `ControlCenter.shared.reloadAllControls()`도 함께 부른다.
- **누르기**: 세션 줄은 `Link`. 데스크톱 앱 세션은 `claude://` 딥 링크로 앱이 바로 열고, 터미널 세션은 `claude-overlay://open?session=…`을 오버레이가 받아(Info.plist `CFBundleURLTypes`) 그 터미널을 앞으로 가져온다. Terminal·iTerm2면 세션 프로세스의 tty로 AppleScript가 그 탭까지 고른다(처음 한 번 자동화 허용, `NSAppleEventsUsageDescription`). 작은 위젯과 빈 화면은 `claude-overlay://show`(오버레이 보이기).

## 서명과 배포

- 기본은 임시 서명(`-`). 순서: 전체 `--deep` → 위젯만 샌드박스 권한으로 다시 → 앱 겉면 다시(그래야 봉인이 맞는다).
- `SIGN_IDENTITY="Developer ID Application: …"`이면 강화 런타임과 `macos/App.entitlements`(.NET JIT용)를 붙인다. 공증(`notarytool`)은 아직 스크립트에 없다.
- 임시 서명 앱을 브라우저로 받으면 격리 표시 때문에 첫 실행이 막힌다(macOS 15부터는 오른쪽 클릭 → 열기도 안 되고 시스템 설정에서 "그래도 열기"). 웹 페이지는 그 방법과 함께 `install.sh` 한 줄 설치를 안내한다. curl로 받은 파일엔 격리 표시가 붙지 않는다.
- 웹 페이지(`docs/download.js`)는 맥으로 접속하면 DMG 버튼을 앞세운다. CPU는 Chrome·Edge의 `userAgentData`로 알아내고, Safari는 알려 주지 않아 WebGL 그래픽 칩 이름으로 짐작하다 모르면 Apple Silicon을 고른다(다른 칩 링크도 함께 보인다).
- 페이지와 `install.sh`가 `releases/latest/download/ClaudeSessionOverlay-macos-<arm64|x64>.dmg`를 가리키므로 DMG 이름에는 버전을 넣지 않는다.

## 확인한 것 (Apple Silicon, macOS 15.1)

- 세션·사용량 읽기(`--dump`), `.app` 실행, 오버레이 창이 맨 위 층에 뜸, 메뉴 막대 상태 항목 표시
- `status.json` 쓰기, 위젯 확장이 시스템에 등록됨(`pluginkit -m -p com.apple.widgetkit-extension`)
- 위젯 갤러리에서 작게·중간·크게·아주 크게 모두 그려짐, 샌드박스 거부 없음(임시 서명 그대로). 위젯 확장은 Xcode처럼 `-e _NSExtensionMain`으로 링크해야 한다: 아니면 `@main`만 돌고 바로 끝나 위젯 시스템(chronod)이 위젯 목록을 못 받아(`getAllDescriptors` Code=4099) 갤러리에 나오지 않는다
- 위젯 화면(작게·중간·크게, 다크·라이트)을 SwiftUI `ImageRenderer`로 그려 확인
- 음성: 한국어 Yuna, 영어 Daniel을 고름. 세션 프로세스의 tty와 터미널 앱 찾기, Terminal용 AppleScript 컴파일
- DMG 만들기, `install.sh`로 DMG 마운트·복사 후 서명 유지

## 남은 일

- Apple Developer ID 서명과 공증: 경고 없이 열리게(연 $99 개발자 프로그램 필요)
- Homebrew cask(`brew install --cask …`)
- 위젯에서 바로 압축·확인 처리 버튼(AppIntents, macOS 14+)
- 전체 화면 앱 위에도 오버레이 띄우기 옵션(`fullScreenAuxiliary`)
