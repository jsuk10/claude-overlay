#!/bin/bash
# macOS에서 실행: Avalonia 앱을 빌드해 메뉴 막대 앱(.app)으로 묶고 임시 서명(ad-hoc)한다.
#   ./scripts/make-macos-app.sh            Apple Silicon (osx-arm64)
#   ./scripts/make-macos-app.sh osx-x64    Intel Mac
# 결과: out/Claude Session Overlay.app  →  /Applications로 옮겨 쓴다
set -euo pipefail

RID="${1:-osx-arm64}"
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
PUBLISH="$ROOT/out/$RID"
APP="$ROOT/out/Claude Session Overlay.app"
VERSION="$(sed -n 's:.*<Version>\(.*\)</Version>.*:\1:p' "$ROOT/avalonia/ClaudeSessionOverlay.Avalonia.csproj")"

dotnet publish "$ROOT/avalonia" -c Release -r "$RID" --self-contained -o "$PUBLISH"

rm -rf "$APP"
mkdir -p "$APP/Contents/MacOS" "$APP/Contents/Resources"
cp -R "$PUBLISH/" "$APP/Contents/MacOS/"

# LSUIElement: Dock·⌘Tab에 나오지 않는 메뉴 막대 앱
cat > "$APP/Contents/Info.plist" <<EOF
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
    <key>CFBundleName</key>
    <string>Claude Session Overlay</string>
    <key>CFBundleDisplayName</key>
    <string>Claude Session Overlay</string>
    <key>CFBundleIdentifier</key>
    <string>io.github.jsuk10.claude-session-overlay</string>
    <key>CFBundleExecutable</key>
    <string>ClaudeSessionOverlay</string>
    <key>CFBundlePackageType</key>
    <string>APPL</string>
    <key>CFBundleShortVersionString</key>
    <string>${VERSION}</string>
    <key>CFBundleVersion</key>
    <string>${VERSION}</string>
    <key>LSMinimumSystemVersion</key>
    <string>11.0</string>
    <key>LSUIElement</key>
    <true/>
    <key>NSHighResolutionCapable</key>
    <true/>
</dict>
</plist>
EOF

# Apple Silicon은 서명 없는 실행 파일을 돌리지 않는다: 임시 서명
codesign --force --deep --sign - "$APP"
echo "만들었습니다: $APP"
