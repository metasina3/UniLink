#!/usr/bin/env bash
set -e

echo "==> Running UniLink macOS tests..."
python3 -m unittest discover tests/UniLink.macOS.Tests

echo "==> Building UniLink.app bundle..."
python3 src/UniLink.macOS/install.py build

mkdir -p artifacts
echo "==> Packaging macOS Release ZIP..."
cd "$HOME/Applications"
zip -rq "$OLDPWD/artifacts/UniLink-1.2.1-macOS.zip" "UniLink.app"
cd "$OLDPWD"
shasum -a 256 "artifacts/UniLink-1.2.1-macOS.zip" > "artifacts/UniLink-1.2.1-macOS.zip.sha256"

echo "==> Done! Artifact saved to artifacts/UniLink-1.2.1-macOS.zip"
