#!/usr/bin/env bash
set -e

echo "==> Running UniLink macOS tests..."
python3 -m unittest discover tests/UniLink.macOS.Tests

echo "==> Building native Swift UniLink.app bundle..."
./src/UniLink.macOS/build-swift.sh

mkdir -p artifacts
echo "==> Packaging macOS Release ZIP..."
cd "$HOME/Applications"
zip -rq "$OLDPWD/artifacts/UniLink-1.3.0-macOS.zip" "UniLink.app"
cd "$OLDPWD"
shasum -a 256 "artifacts/UniLink-1.3.0-macOS.zip" > "artifacts/UniLink-1.3.0-macOS.zip.sha256"

echo "==> Done! Artifact saved to artifacts/UniLink-1.3.0-macOS.zip"
