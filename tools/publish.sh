#!/usr/bin/env bash
# Build PhotonCanvas ready to run, for Windows, macOS and Linux. Each build carries its own
# .NET runtime, so nothing needs installing on the computer that runs it.
#
#   tools/publish.sh                 # every platform
#   tools/publish.sh linux-x64       # just one (win-x64, win-arm64, osx-arm64, osx-x64, linux-x64, linux-arm64)
#
# Output: dist/<platform>/ (a PhotonCanvas.app bundle on macOS), plus dist/PhotonCanvas-<platform>.zip on
# Windows and .tar.gz on macOS and Linux (which keeps the program's run permission), and
# dist/PhotonCanvas-<platform>.files.txt (every file's SHA-256, for updates that fetch only what changed).
# VERSION=1.2.0 tools/publish.sh sets the version shown in the Mac bundle.
# RELEASE_REPO=owner/name (default noalear/PhotonCanvas): the GitHub repository whose releases the
# published copy updates itself from (written to update.json beside the program, with
# update-files.txt listing the files it's made of; see src/PhotonCanvas.Core/UpdateInstall.cs).
set -euo pipefail
cd "$(dirname "$0")/.."

ALL=(win-x64 win-arm64 osx-arm64 osx-x64 linux-x64 linux-arm64)
TARGETS=("${@:-${ALL[@]}}")
VERSION=${VERSION:-0.1.0}
RELEASE_REPO=${RELEASE_REPO:-noalear/PhotonCanvas}
# Built on Windows, files don't carry Unix run permission: the archives set it instead.
case "${OSTYPE:-}" in msys*|cygwin*) ON_WINDOWS=1 ;; *) ON_WINDOWS=0 ;; esac
pack_tar() { # folder to pack, archive
  if [ "$ON_WINDOWS" = 1 ]; then tar -C "$(dirname "$1")" --mode=755 -czf "$2" "$(basename "$1")"
  else tar -C "$(dirname "$1")" -czf "$2" "$(basename "$1")"; fi
}
pack_zip() { # folder to pack, archive
  if command -v zip >/dev/null; then local dest; dest="$(cd "$(dirname "$2")" && pwd)/$(basename "$2")"; (cd "$(dirname "$1")" && zip -qry "$dest" "$(basename "$1")")
  # No zip command (Windows): .NET packs it, with "/" between folder names as the updater expects
  # (PowerShell's Compress-Archive can write "\" and skips hidden files).
  elif [ "$ON_WINDOWS" = 1 ]; then dotnet run tools/zipdir.cs -- "$(cygpath -w "$1")" "$(cygpath -w "$2")"
  else dotnet run tools/zipdir.cs -- "$1" "$2"; fi
}
# A full path the build tools understand: C:/Users/… on Windows, not Git Bash's /c/Users/….
full_path() { if [ "$ON_WINDOWS" = 1 ]; then cygpath -m "$PWD/$1"; else echo "$PWD/$1"; fi; }
write_hashes() { # the version's folder, where to write: each file's SHA-256, so an update can fetch only the files that changed
  (cd "$1" && find . -type f -print0 | LC_ALL=C sort -z | xargs -0 sha256sum) > "$2"
}
write_list() { # the version's folder, where the program is in it (. or Contents/MacOS): every file, so the next update knows what to replace or drop
  (cd "$1" && { find . -type f ! -name update-files.txt | LC_ALL=C sort; echo "./$2/update-files.txt"; } | sed 's|^\./\./|./|') > "$1/$2/update-files.txt"
}

build_addons() { # the published program's folder (ending in /)
  local tmp=dist/.addons; rm -rf "$tmp"
  dotnet new sln -n Addons -o "$tmp" >/dev/null
  local sln; sln=$(ls "$tmp"/Addons.sln*)
  dotnet sln "$sln" add src/Addons/*/PhotonCanvas.Addon.*.csproj >/dev/null
  dotnet build "$sln" -c Release -p:Version="$VERSION" -p:AppOutDir="$1" -nologo -v q
  rm -rf "$tmp"
}

# --force: restore every time. The add-ons' build (below) restores the program again without a
# platform, so in a folder that has built before, its restore record can lack win-x64 and the next
# publish stops with NETSDK1047 (seen on Windows) unless restore runs afresh.
for rid in "${TARGETS[@]}"; do
  out="dist/$rid"
  rm -rf "$out"
  echo "== $rid"
  dotnet publish src/PhotonCanvas.App -c Release -r "$rid" --force --self-contained true -p:Version="$VERSION" \
    -p:PublishSingleFile=false -p:DebugType=none -o "$out/bin" -nologo -v q
  # The remote server for the phone, in its own folder beside the program (Laser → Remote access starts it).
  dotnet publish src/PhotonCanvas.Server -c Release -r "$rid" --force --self-contained true -p:Version="$VERSION" \
    -p:PublishSingleFile=false -p:DebugType=none -o "$out/bin/server" -nologo -v q
  # The add-ons that come with the program, each in addons/<id> beside it (docs/addons.md). All in one
  # build (a throwaway solution listing them), so the program they compile against is checked once and
  # the add-ons build side by side, not one after another.
  build_addons "$(full_path "$out/bin")/"
  # Where this copy gets new versions from, and which build it is.
  printf '{\n  "repo": "%s",\n  "platform": "%s",\n  "version": "%s"\n}\n' "$RELEASE_REPO" "$rid" "$VERSION" > "$out/bin/update.json"

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
      write_list "$app" Contents/MacOS
      write_hashes "$app" "dist/PhotonCanvas-$rid.files.txt"
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
      write_list "$out/PhotonCanvas" .
      write_hashes "$out/PhotonCanvas" "dist/PhotonCanvas-$rid.files.txt"
      pack_tar "$out/PhotonCanvas" "dist/PhotonCanvas-$rid.tar.gz"
      ;;
    win-*)
      mv "$out/bin" "$out/PhotonCanvas"
      write_list "$out/PhotonCanvas" .
      write_hashes "$out/PhotonCanvas" "dist/PhotonCanvas-$rid.files.txt"
      pack_zip "$out/PhotonCanvas" "dist/PhotonCanvas-$rid.zip"
      ;;
  esac
done
echo "Done: see dist/"
