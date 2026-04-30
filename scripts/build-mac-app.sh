#!/usr/bin/env bash
# Builds a runnable Conclave.app bundle on macOS.
#
# `dotnet publish` alone produces a flat folder of native binaries; macOS only
# treats it as a real app (icon, dock entry, "Open" via Finder) once it's
# wrapped in the standard .app layout with an Info.plist and CFBundleIconFile
# pointing at icon.icns. This script does that wrapping so locally-built
# bundles match what the Release workflow ships.
#
# Usage:
#   scripts/build-mac-app.sh                 # auto-detects host arch
#   scripts/build-mac-app.sh osx-arm64       # explicit RID
#   VERSION=1.2.3 scripts/build-mac-app.sh   # stamp a real version
#
# Output: ./Conclave.app at the repo root.
set -euo pipefail

cd "$(dirname "$0")/.."

RID="${1:-}"
if [[ -z "$RID" ]]; then
    case "$(uname -m)" in
        arm64)  RID="osx-arm64" ;;
        x86_64) RID="osx-x64" ;;
        *) echo "unsupported arch $(uname -m); pass an RID explicitly" >&2; exit 1 ;;
    esac
fi

VERSION="${VERSION:-0.0.0-dev}"
APP="Conclave.app"

dotnet publish src/Conclave.App \
    -c Release \
    -r "$RID" \
    -o publish \
    -p:Version="$VERSION"

rm -rf "$APP"
mkdir -p "$APP/Contents/MacOS" "$APP/Contents/Resources"
cp -R publish/. "$APP/Contents/MacOS/"
cp assets/icon.icns "$APP/Contents/Resources/icon.icns"
chmod +x "$APP/Contents/MacOS/Conclave.App"

cat > "$APP/Contents/Info.plist" <<EOF
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
  <key>CFBundleName</key><string>Conclave</string>
  <key>CFBundleDisplayName</key><string>Conclave</string>
  <key>CFBundleIdentifier</key><string>com.cellcote.conclave</string>
  <key>CFBundleVersion</key><string>${VERSION}</string>
  <key>CFBundleShortVersionString</key><string>${VERSION}</string>
  <key>CFBundleExecutable</key><string>Conclave.App</string>
  <key>CFBundleIconFile</key><string>icon</string>
  <key>CFBundlePackageType</key><string>APPL</string>
  <key>LSMinimumSystemVersion</key><string>11.0</string>
  <key>NSHighResolutionCapable</key><true/>
</dict>
</plist>
EOF

# Bump the bundle's mtime so Finder/Dock invalidate any cached generic icon
# from a previous unbundled publish.
touch "$APP"

echo "Built $APP ($RID, version $VERSION)"
