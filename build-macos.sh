#!/usr/bin/env bash
set -e

VERSION="${1:-1.3.1}"
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
cd "$SCRIPT_DIR"

echo "==> Running UniLink macOS tests..."
python3 -m unittest discover tests/UniLink.macOS.Tests

echo "==> Building native Swift UniLink.app bundle..."
./src/UniLink.macOS/build-swift.sh

mkdir -p artifacts
APP_PATH="$HOME/Applications/UniLink.app"

if [ ! -d "$APP_PATH" ]; then
  echo "Error: $APP_PATH not found"
  exit 1
fi

echo "==> Packaging macOS Release DMG (UniLink-${VERSION}-macOS.dmg)..."
DMG_STAGE=$(mktemp -d /tmp/unilink_dmg_stage.XXXXXX)
cp -R "$APP_PATH" "$DMG_STAGE/"
ln -s /Applications "$DMG_STAGE/Applications"

DMG_FILE="artifacts/UniLink-${VERSION}-macOS.dmg"
rm -f "$DMG_FILE"
hdiutil create -volname "UniLink" -srcfolder "$DMG_STAGE" -ov -format UDZO "$DMG_FILE"
rm -rf "$DMG_STAGE"
shasum -a 256 "$DMG_FILE" > "${DMG_FILE}.sha256"

echo "==> Packaging macOS Release ZIP (UniLink-${VERSION}-macOS.zip)..."
ZIP_FILE="$SCRIPT_DIR/artifacts/UniLink-${VERSION}-macOS.zip"
rm -f "$ZIP_FILE"
cd "$HOME/Applications"
zip -rq "$ZIP_FILE" "UniLink.app"
cd "$SCRIPT_DIR"
shasum -a 256 "artifacts/UniLink-${VERSION}-macOS.zip" > "artifacts/UniLink-${VERSION}-macOS.zip.sha256"

echo "==> Done! Artifacts generated in artifacts/:"
ls -lh artifacts/UniLink-${VERSION}-macOS.*
