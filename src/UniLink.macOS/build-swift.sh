#!/usr/bin/env bash
set -e

DIR="$(cd "$(dirname "$0")" && pwd)"
APP_NAME="UniLink"
DEST_DIR="$HOME/Applications"
APP_BUNDLE="$DEST_DIR/$APP_NAME.app"

echo "==> [Swift Native] Assembling $APP_BUNDLE..."
mkdir -p "$APP_BUNDLE/Contents/MacOS"
mkdir -p "$APP_BUNDLE/Contents/Resources/gui"

echo "==> [Swift Native] Compiling UniLink binary from Swift sources..."
cd "$DIR/Swift"

swiftc -O \
  -vfsoverlay vfs-overlay.yaml \
  -Xcc -ivfsoverlay -Xcc vfs-overlay.yaml \
  Models.swift ConfigStore.swift NetInfo.swift PortalClient.swift Watcher.swift DashboardWindow.swift MenuBarController.swift main.swift \
  -o "$APP_BUNDLE/Contents/MacOS/$APP_NAME"

chmod +x "$APP_BUNDLE/Contents/MacOS/$APP_NAME"

# 2. Resources (Dashboard HTML & Icon)
if [ -f "$DIR/unilink/gui/index.html" ]; then
  cp "$DIR/unilink/gui/index.html" "$APP_BUNDLE/Contents/Resources/gui/index.html"
fi
if [ -f "$DIR/Resources/AppIcon.icns" ]; then
  cp "$DIR/Resources/AppIcon.icns" "$APP_BUNDLE/Contents/Resources/AppIcon.icns"
fi

# 3. Info.plist
cat << 'EOF' > "$APP_BUNDLE/Contents/Info.plist"
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
  <key>CFBundleName</key>
  <string>UniLink</string>
  <key>CFBundleDisplayName</key>
  <string>UniLink</string>
  <key>CFBundleIdentifier</key>
  <string>ir.unilink.desktop</string>
  <key>CFBundleVersion</key>
  <string>1.3.0</string>
  <key>CFBundleShortVersionString</key>
  <string>1.3.0</string>
  <key>CFBundleExecutable</key>
  <string>UniLink</string>
  <key>CFBundleIconFile</key>
  <string>AppIcon</string>
  <key>CFBundlePackageType</key>
  <string>APPL</string>
  <key>LSMinimumSystemVersion</key>
  <string>12.0</string>
  <key>LSUIElement</key>
  <true/>
  <key>NSHighResolutionCapable</key>
  <true/>
  <key>NSRequiresAquaSystemAppearance</key>
  <false/>
</dict>
</plist>
EOF

# 4. Codesign & Refresh Finder / LaunchServices Icon
codesign --force --deep --sign - "$APP_BUNDLE" 2>/dev/null || true
touch "$APP_BUNDLE"
/System/Library/Frameworks/CoreServices.framework/Frameworks/LaunchServices.framework/Support/lsregister -f "$APP_BUNDLE" 2>/dev/null || true

echo "✔ Successfully built native Swift $APP_BUNDLE"
