#!/usr/bin/env bash
# Generates platform icon bundles from assets/logo.png.
#
# Outputs:
#   assets/icon.ico         — Windows multi-size icon (16..256)
#   assets/icon.icns        — macOS icon bundle (16..1024 + retina)
#   assets/icon-256.png     — Linux app icon (also a generic fallback)
#   assets/icon-512.png     — Linux app icon (hicolor 512)
#
# Requires: ImageMagick (`magick`), and on macOS: `sips`, `iconutil`.
set -euo pipefail

cd "$(dirname "$0")/.."

SRC="assets/logo.png"
[[ -f "$SRC" ]] || { echo "missing $SRC" >&2; exit 1; }

# Windows .ico — magick handles all sizes in one call.
magick "$SRC" -define icon:auto-resize=256,128,64,48,32,16 assets/icon.ico

# Linux PNGs at common hicolor sizes.
magick "$SRC" -resize 256x256 assets/icon-256.png
magick "$SRC" -resize 512x512 assets/icon-512.png

# macOS .icns — only build on macOS, since iconutil is Apple-only.
if [[ "$(uname)" == "Darwin" ]]; then
    TMP="$(mktemp -d)/icon.iconset"
    mkdir -p "$TMP"
    sips -z 16 16     "$SRC" --out "$TMP/icon_16x16.png"      >/dev/null
    sips -z 32 32     "$SRC" --out "$TMP/icon_16x16@2x.png"   >/dev/null
    sips -z 32 32     "$SRC" --out "$TMP/icon_32x32.png"      >/dev/null
    sips -z 64 64     "$SRC" --out "$TMP/icon_32x32@2x.png"   >/dev/null
    sips -z 128 128   "$SRC" --out "$TMP/icon_128x128.png"    >/dev/null
    sips -z 256 256   "$SRC" --out "$TMP/icon_128x128@2x.png" >/dev/null
    sips -z 256 256   "$SRC" --out "$TMP/icon_256x256.png"    >/dev/null
    sips -z 512 512   "$SRC" --out "$TMP/icon_256x256@2x.png" >/dev/null
    sips -z 512 512   "$SRC" --out "$TMP/icon_512x512.png"    >/dev/null
    cp "$SRC"                  "$TMP/icon_512x512@2x.png"
    iconutil -c icns "$TMP" -o assets/icon.icns
    rm -rf "$TMP"
fi

echo "Generated:"
ls -la assets/icon.* assets/icon-*.png
