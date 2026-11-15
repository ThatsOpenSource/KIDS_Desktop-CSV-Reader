#!/usr/bin/env bash
set -euo pipefail

HERE="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PROJ="$HERE/KidsXmlToCsv.csproj"
OUT="$HERE/dist/macos"
RID="${1:-osx-arm64}"

rm -rf "$OUT"
mkdir -p "$OUT"

echo "── Publishing $RID → $OUT"
dotnet publish "$PROJ" \
  -c Release \
  -r "$RID" \
  --self-contained true \
  -p:PublishSingleFile=false \
  -p:DebugType=None \
  -o "$OUT/app"

APP="$HERE/dist/KIDS.app"
rm -rf "$APP"
mkdir -p "$APP/Contents/MacOS" "$APP/Contents/Resources"

cp -R "$OUT/app/." "$APP/Contents/MacOS/"

# macOS app-bundle icon (user supplies Assets/icon.icns)
if [ -f "$HERE/Assets/icon.icns" ]; then
  cp "$HERE/Assets/icon.icns" "$APP/Contents/Resources/icon.icns"
fi

cat > "$APP/Contents/Info.plist" <<'PLIST'
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN"
 "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
  <key>CFBundleName</key><string>KIDS</string>
  <key>CFBundleDisplayName</key><string>KIDS — Desktop CSV Reader</string>
  <key>CFBundleIdentifier</key><string>de.egov.kids.csvreader</string>
  <key>CFBundleVersion</key><string>0.1.0</string>
  <key>CFBundleShortVersionString</key><string>0.1.0</string>
  <key>CFBundleExecutable</key><string>KIDS</string>
  <key>CFBundleIconFile</key><string>icon</string>
  <key>CFBundlePackageType</key><string>APPL</string>
  <key>LSMinimumSystemVersion</key><string>11.0</string>
  <key>NSHighResolutionCapable</key><true/>
  <key>NSHumanReadableCopyright</key><string>© eGovernment · Lenny Kunz</string>
</dict>
</plist>
PLIST

chmod +x "$APP/Contents/MacOS/KIDS"
echo "Done → $APP"
