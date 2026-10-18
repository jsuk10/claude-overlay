#!/bin/bash
# macOS에서 실행: Avalonia 앱을 빌드해 메뉴 막대 앱(.app)으로 묶고, 위젯 확장과 메뉴 막대용 네이티브 라이브러리를 넣고,
# 서명한 뒤 설치용 디스크 이미지(.dmg)까지 만든다.
#   ./scripts/make-macos-app.sh            Apple Silicon (osx-arm64)
#   ./scripts/make-macos-app.sh osx-x64    Intel Mac
# 결과: out/Claude Session Overlay.app, out/ClaudeSessionOverlay-macos-<arm64|x64>.dmg
#
# 환경 변수
#   SIGN_IDENTITY   codesign 인증서(기본 "-" = 임시 서명). 예: "Developer ID Application: …"
#   SKIP_DMG=1      .dmg를 만들지 않는다
# Xcode(또는 Command Line Tools)의 swiftc가 없으면 위젯·네이티브 메뉴 막대 없이 묶는다(Avalonia 트레이 아이콘을 쓴다).
set -euo pipefail

RID="${1:-osx-arm64}"
case "$RID" in
    osx-arm64) ARCH=arm64 ;;
    osx-x64) ARCH=x64 ;;
    *) echo "지원하지 않는 RID: $RID (osx-arm64 또는 osx-x64)" >&2; exit 1 ;;
esac
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
PUBLISH="$ROOT/out/$RID"
NATIVE="$ROOT/macos/build"
APP="$ROOT/out/Claude Session Overlay.app"
DMG="$ROOT/out/ClaudeSessionOverlay-macos-$ARCH.dmg"
SIGN="${SIGN_IDENTITY:--}"
BUNDLE_ID="local.claude-session-overlay"
VERSION="$(sed -n 's:.*<Version>\(.*\)</Version>.*:\1:p' "$ROOT/avalonia/ClaudeSessionOverlay.Avalonia.csproj")"

# ── 1. 네이티브(Swift): 메뉴 막대 라이브러리와 위젯. 두 아키텍처를 묶은 universal로 만든다 ──
HAS_NATIVE=0
if command -v swiftc >/dev/null 2>&1 && xcrun --show-sdk-path >/dev/null 2>&1; then
    HAS_NATIVE=1
    SDK="$(xcrun --show-sdk-path)"
    rm -rf "$NATIVE" && mkdir -p "$NATIVE"
    for arch in arm64 x86_64; do
        swiftc -swift-version 5 -O -sdk "$SDK" -target "$arch-apple-macos11.0" \
            -emit-library -module-name ClaudeOverlayMac \
            -Xlinker -install_name -Xlinker @rpath/libClaudeOverlayMac.dylib \
            -o "$NATIVE/libClaudeOverlayMac-$arch.dylib" \
            "$ROOT/macos/Shared/Board.swift" "$ROOT/macos/Bridge/StatusBar.swift"
        swiftc -swift-version 5 -O -sdk "$SDK" -target "$arch-apple-macos14.0" \
            -parse-as-library -application-extension -module-name ClaudeSessionWidget \
            -o "$NATIVE/ClaudeSessionWidget-$arch" \
            "$ROOT/macos/Shared/Board.swift" "$ROOT/macos/Widget/SessionsWidget.swift" "$ROOT/macos/Widget/SessionsControl.swift"
    done
    lipo -create -output "$NATIVE/libClaudeOverlayMac.dylib" "$NATIVE"/libClaudeOverlayMac-{arm64,x86_64}.dylib
    lipo -create -output "$NATIVE/ClaudeSessionWidget" "$NATIVE"/ClaudeSessionWidget-{arm64,x86_64}
else
    echo "swiftc가 없어 위젯·네이티브 메뉴 막대 없이 묶습니다 (xcode-select --install)" >&2
fi

# 앱 아이콘: assets/icon의 PNG(scripts/make-icon.swift가 그린 것)를 .icns로 묶는다
ICONSET="$ROOT/out/AppIcon.iconset"
rm -rf "$ICONSET" && mkdir -p "$ICONSET"
for s in 16 32 128 256 512; do
    cp "$ROOT/assets/icon/icon-$s.png" "$ICONSET/icon_${s}x${s}.png"
    cp "$ROOT/assets/icon/icon-$((s * 2)).png" "$ICONSET/icon_${s}x${s}@2x.png"
done
iconutil -c icns -o "$ROOT/out/AppIcon.icns" "$ICONSET"

# ── 2. .NET 앱 ─────────────────────────────────────────────────────────
dotnet publish "$ROOT/avalonia" -c Release -r "$RID" --self-contained -o "$PUBLISH"

rm -rf "$APP"
mkdir -p "$APP/Contents/MacOS" "$APP/Contents/Resources"
cp -R "$PUBLISH/" "$APP/Contents/MacOS/"
rm -f "$APP/Contents/MacOS/"*.pdb
cp "$ROOT/out/AppIcon.icns" "$APP/Contents/Resources/"

if [ "$HAS_NATIVE" = 1 ]; then
    cp "$NATIVE/libClaudeOverlayMac.dylib" "$APP/Contents/MacOS/"
    WIDGET="$APP/Contents/PlugIns/ClaudeSessionWidget.appex"
    mkdir -p "$WIDGET/Contents/MacOS"
    cp "$NATIVE/ClaudeSessionWidget" "$WIDGET/Contents/MacOS/"
    sed "s/__VERSION__/$VERSION/g" "$ROOT/macos/Widget/Info.plist" > "$WIDGET/Contents/Info.plist"
fi

# LSUIElement: Dock·⌘Tab에 나오지 않는 메뉴 막대 앱
# CFBundleURLTypes: 위젯에서 누른 claude-overlay:// 주소를 이 앱이 받는다
# NSAppleEventsUsageDescription: 터미널 탭을 고를 때 묻는 자동화 허용 창의 설명
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
    <string>${BUNDLE_ID}</string>
    <key>CFBundleExecutable</key>
    <string>ClaudeSessionOverlay</string>
    <key>CFBundleIconFile</key>
    <string>AppIcon</string>
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
    <key>NSAppleEventsUsageDescription</key>
    <string>Claude Code 세션이 열린 Terminal·iTerm 탭을 앞으로 가져옵니다. / Brings the Terminal or iTerm tab running a Claude Code session to the front.</string>
    <key>CFBundleURLTypes</key>
    <array>
        <dict>
            <key>CFBundleURLName</key>
            <string>${BUNDLE_ID}</string>
            <key>CFBundleURLSchemes</key>
            <array>
                <string>claude-overlay</string>
            </array>
        </dict>
    </array>
</dict>
</plist>
EOF

# ── 3. 서명: 안쪽부터. 위젯은 샌드박스 권한(entitlements)을 따로 붙인다 ──────────
# Apple Silicon은 서명 없는 실행 파일을 돌리지 않는다. 인증서가 없으면 임시 서명(-)
# 실제 인증서로 서명하면 공증(notarization)에 필요한 강화 런타임을 켜고, .NET JIT이 돌 수 있게 권한을 준다
OPTS=(--force --sign "$SIGN")
APP_OPTS=()
if [ "$SIGN" != "-" ]; then
    OPTS+=(--options runtime --timestamp)
    APP_OPTS=(--entitlements "$ROOT/macos/App.entitlements")
fi
codesign "${OPTS[@]}" ${APP_OPTS[@]+"${APP_OPTS[@]}"} --deep "$APP"
if [ "$HAS_NATIVE" = 1 ]; then
    codesign "${OPTS[@]}" --entitlements "$ROOT/macos/Widget/Widget.entitlements" "$APP/Contents/PlugIns/ClaudeSessionWidget.appex"
fi
codesign "${OPTS[@]}" ${APP_OPTS[@]+"${APP_OPTS[@]}"} "$APP"
codesign --verify --deep --strict "$APP"
echo "만들었습니다: $APP"

# ── 4. 디스크 이미지: 앱과 /Applications 바로 가기 ─────────────────────────
if [ "${SKIP_DMG:-0}" != 1 ]; then
    STAGE="$ROOT/out/dmg-$ARCH"
    rm -rf "$STAGE" "$DMG" && mkdir -p "$STAGE"
    cp -R "$APP" "$STAGE/"
    ln -s /Applications "$STAGE/Applications"
    hdiutil create -quiet -volname "Claude Session Overlay" -srcfolder "$STAGE" -fs HFS+ -format UDZO -ov "$DMG"
    rm -rf "$STAGE"
    if [ "$SIGN" != "-" ]; then codesign --force --sign "$SIGN" "$DMG"; fi
    echo "만들었습니다: $DMG"
fi
