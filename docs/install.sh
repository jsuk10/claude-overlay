#!/bin/bash
# Claude Session Overlay macOS 설치 / installer
#   curl -fsSL https://claude-overlay.jsuk.cloud/install.sh | bash
# 최신 릴리스의 DMG(이 Mac의 칩에 맞는 것)를 받아 응용 프로그램 폴더에 복사하고 실행한다.
# curl로 받은 파일에는 격리 표시가 붙지 않아 Gatekeeper 경고 없이 열린다(아직 Apple 공증 전인 앱).
set -euo pipefail

APP_NAME="Claude Session Overlay.app"
REPO="jsuk10/claude-overlay"

if [ "$(uname -s)" != "Darwin" ]; then
    echo "macOS 전용 설치 스크립트입니다 / This installer is for macOS only." >&2
    exit 1
fi

# Rosetta로 돈 셸이면 uname -m이 x86_64로 나오므로 칩을 직접 묻는다
if [ "$(sysctl -in hw.optional.arm64 2>/dev/null || echo 0)" = "1" ]; then ARCH=arm64; else ARCH=x64; fi
URL="https://github.com/$REPO/releases/latest/download/ClaudeSessionOverlay-macos-$ARCH.dmg"

# 관리자가 아니라 /Applications에 쓸 수 없으면 ~/Applications에
DEST="/Applications"
if [ ! -w "$DEST" ]; then DEST="$HOME/Applications"; mkdir -p "$DEST"; fi

TMP="$(mktemp -d)"
MNT="$TMP/mnt"
cleanup() {
    hdiutil detach -quiet "$MNT" 2>/dev/null || true
    rm -rf "$TMP"
}
trap cleanup EXIT

echo "내려받는 중 / Downloading: $URL"
curl -fL --progress-bar "$URL" -o "$TMP/overlay.dmg"

mkdir -p "$MNT"
hdiutil attach -quiet -nobrowse -readonly -mountpoint "$MNT" "$TMP/overlay.dmg"

# 실행 중이면 끄고 바꾼다
pkill -f "$APP_NAME/Contents/MacOS/" 2>/dev/null || true
sleep 1
rm -rf "$DEST/$APP_NAME"
ditto "$MNT/$APP_NAME" "$DEST/$APP_NAME"
xattr -dr com.apple.quarantine "$DEST/$APP_NAME" 2>/dev/null || true

echo "설치했습니다 / Installed: $DEST/$APP_NAME"
open "$DEST/$APP_NAME"
