#!/usr/bin/env bash
# Build PhotonCanvas ready to run, for Windows, macOS and Linux. Each build carries its own
# .NET runtime, so nothing needs installing on the computer that runs it.
#
#   tools/publish.sh                 # every platform
#   tools/publish.sh linux-x64       # just one (win-x64, win-arm64, osx-arm64, osx-x64, linux-x64, linux-arm64)
#
# Output: dist/<platform>/ (a PhotonCanvas.app bundle on macOS), plus dist/PhotonCanvas-<platform>.zip on
# Windows and .tar.gz on macOS and Linux (which keeps the program's run permission).
# VERSION=1.2.0 tools/publish.sh sets the version shown in the Mac bundle.
set -euo pipefail
cd "$(dirname "$0")/.."

ALL=(win-x64 win-arm64 osx-arm64 osx-x64 linux-x64 linux-arm64)
TARGETS=("${@:-${ALL[@]}}")
VERSION=${VERSION:-0.1.0}
# Built on Windows, files don't carry Unix run permission: the archives set it instead.
case "${OSTYPE:-}" in msys*|cygwin*) ON_WINDOWS=1 ;; *) ON_WINDOWS=0 ;; esac
pack_tar() { # folder to pack, archive
  if [ "$ON_WINDOWS" = 1 ]; then tar -C "$(dirname "$1")" --mode=755 -czf "$2" "$(basename "$1")"
  else tar -C "$(dirname "$1")" -czf "$2" "$(basename "$1")"; fi
}
pack_zip() { # folder to pack, archive
  if command -v zip >/dev/null; then local dest; dest="$(cd "$(dirname "$2")" && pwd)/$(basename "$2")"; (cd "$(dirname "$1")" && zip -qry "$dest" "$(basename "$1")")
  else powershell -NoProfile -Command "Compress-Archive -Force -Path '$(cygpath -w "$1")' -DestinationPath '$(cygpath -w "$2")'"; fi
}

for rid in "${TARGETS[@]}"; do
  out="dist/$rid"
  rm -rf "$out"
  echo "== $rid"
  dotnet publish src/PhotonCanvas.App -c Release -r "$rid" --self-contained true -p:Version="$VERSION" \
    -p:PublishSingleFile=false -p:DebugType=none -o "$out/bin" -nologo -v q
  # The remote server for the phone, in its own folder beside the program (Laser → Remote access starts it).
  dotnet publish src/PhotonCanvas.Server -c Release -r "$rid" --self-contained true -p:Version="$VERSION" \
    -p:PublishSingleFile=false -p:DebugType=none -o "$out/bin/server" -nologo -v q
  # The add-ons that come with the program, each in addons/<id> beside it (docs/addons.md).
  for proj in src/Addons/*/PhotonCanvas.Addon.*.csproj; do
    [ -f "$proj" ] || continue
    dotnet build "$proj" -c Release -p:Version="$VERSION" -p:AppOutDir="$PWD/$out/bin/" -nologo -v q
  done

  case "$rid" in
    osx-*)
      # A proper app bundle: Contents/MacOS holds the program, Info.plist describes it.
      app="$out/PhotonCanvas.app"
      mkdir -p "$app/Contents/MacOS" "$app/Contents/Resources"
      cp -R "$out/bin/." "$app/Contents/MacOS/"
      rm -rf "$out/bin"
      cat > "$app/Contents/Info.plist" <<PLIST
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
  <key>CFBundleName</key><string>PhotonCanvas</string>
  <key>CFBundleDisplayName</key><string>PhotonCanvas</string>
  <key>CFBundleIdentifier</key><string>org.photoncanvas.app</string>
  <key>CFBundleVersion</key><string>$VERSION</string>
  <key>CFBundleShortVersionString</key><string>$VERSION</string>
  <key>CFBundleExecutable</key><string>PhotonCanvas</string>
  <key>CFBundlePackageType</key><string>APPL</string>
  <key>LSMinimumSystemVersion</key><string>11.0</string>
  <key>NSHighResolutionCapable</key><true/>
  <key>NSLocalNetworkUsageDescription</key><string>PhotonCanvas talks to your laser over the local network.</string>
  <key>CFBundleDocumentTypes</key>
  <array>
    <dict>
      <key>CFBundleTypeName</key><string>PhotonCanvas project</string>
      <key>CFBundleTypeExtensions</key><array><string>pcanvas</string></array>
      <key>CFBundleTypeRole</key><string>Editor</string>
    </dict>
  </array>
</dict>
</plist>
PLIST
      chmod +x "$app/Contents/MacOS/PhotonCanvas" "$app/Contents/MacOS/server/PhotonCanvas.Server"
      # Not signed: on first open, right-click → Open (or System Settings → Privacy & Security → Open anyway).
      pack_tar "$app" "dist/PhotonCanvas-$rid.tar.gz"
      ;;
    linux-*)
      mv "$out/bin" "$out/PhotonCanvas"
      chmod +x "$out/PhotonCanvas/PhotonCanvas" "$out/PhotonCanvas/server/PhotonCanvas.Server"
      cat > "$out/PhotonCanvas/photoncanvas.desktop" <<DESKTOP
[Desktop Entry]
Type=Application
Name=PhotonCanvas
Comment=Design and laser jobs for the xTool F2 Ultra, offline
Exec=PhotonCanvas %f
Terminal=false
Categories=Graphics;Engineering;
MimeType=application/x-photoncanvas;
DESKTOP
      pack_tar "$out/PhotonCanvas" "dist/PhotonCanvas-$rid.tar.gz"
      ;;
    win-*)
      mv "$out/bin" "$out/PhotonCanvas"
      pack_zip "$out/PhotonCanvas" "dist/PhotonCanvas-$rid.zip"
      ;;
  esac
done
echo "Done: see dist/"
