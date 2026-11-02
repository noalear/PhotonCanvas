#!/usr/bin/env bash
# Publish a new version by hand: build it and put the builds up as a GitHub release, which
# PhotonCanvas finds and installs by itself. (Normally .github/workflows/release.yml does this on
# every change to master; this is for a numbered version such as 0.2.0, which also moves the
# workflow's numbering on to 0.2.x.) PhotonCanvas is free to use, all rights reserved (LICENSE).
#
#   VERSION=0.2.0 tools/release.sh                  # every platform
#   VERSION=0.2.0 tools/release.sh win-x64 osx-arm64 # just these
#
# What's new: docs/releases/<version>.md if it's there, else the commit subjects since the last release.
# Needs the GitHub CLI (gh), signed in with access to the repository releases go to (RELEASE_REPO).
set -euo pipefail
cd "$(dirname "$0")/.."

: "${VERSION:?Set VERSION, e.g. VERSION=0.2.0 tools/release.sh}"
REPO=${RELEASE_REPO:-noalear/PhotonCanvas}
GH=${GH:-gh}
command -v "$GH" >/dev/null || GH="/c/Program Files/GitHub CLI/gh.exe"
[ -n "$(git status --porcelain)" ] && { echo "Commit or put away your changes first: the release is built from what's committed."; exit 1; }

# The version goes in the build (the update check compares against it) and in a commit and tag here.
sed -i "s|<Version>[^<]*</Version>|<Version>$VERSION</Version>|" Directory.Build.props
if ! git diff --quiet Directory.Build.props; then
  git commit -q -m "Version $VERSION" Directory.Build.props
fi
git tag -a "v$VERSION" -m "PhotonCanvas $VERSION"

VERSION="$VERSION" RELEASE_REPO="$REPO" tools/publish.sh "$@"

notes="$(mktemp)"
if [ -f "docs/releases/$VERSION.md" ]; then cp "docs/releases/$VERSION.md" "$notes"
else
  last=$(git describe --tags --abbrev=0 "v$VERSION^" 2>/dev/null || true)
  { echo "What's new in $VERSION:"; echo; git log --format='- %s' ${last:+"$last..v$VERSION"} | grep -v '^- Version ' | head -60; } > "$notes"
fi
cat >> "$notes" <<'EOF'

PhotonCanvas downloads this version by itself and puts it in when you close it (Settings → Updates). To install it by hand, unpack the file for your computer in place of the old one: your designs, settings and material library are kept in your PhotonCanvas folder, not with the program.
EOF
cat >> "$notes" <<EOF

PhotonCanvas is free to use, for personal or commercial work; all rights reserved. It may not be sold, redistributed or published in a modified form without written permission. The full terms are in the LICENSE file that comes with it.
EOF

files=(dist/PhotonCanvas-*.zip dist/PhotonCanvas-*.tar.gz dist/PhotonCanvas-*.files.txt)
existing=()
for f in "${files[@]}"; do [ -f "$f" ] && existing+=("$f"); done
[ ${#existing[@]} -gt 0 ] || { echo "Nothing was built."; exit 1; }
# The checksums the program checks each download against (it won't install one without them).
(cd dist && sha256sum "${existing[@]#dist/}" > SHA256SUMS)
existing+=(dist/SHA256SUMS)
"$GH" release create "v$VERSION" "${existing[@]}" --repo "$REPO" --title "PhotonCanvas $VERSION" --notes-file "$notes"
git push -q origin HEAD "v$VERSION"
echo "Released $VERSION to https://github.com/$REPO/releases/tag/v$VERSION"
