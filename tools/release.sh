#!/usr/bin/env bash
# Publish a new version for friends: build it, and put the builds up as a release on the public,
# releases-only GitHub repository (the source stays private). Their PhotonCanvas sees it at start.
#
#   VERSION=0.2.0 tools/release.sh                  # every platform
#   VERSION=0.2.0 tools/release.sh win-x64 osx-arm64 # just these
#
# What's new: docs/releases/<version>.md if it's there, else the commit subjects since the last release.
# Needs the GitHub CLI (gh), signed in with access to both repositories.
set -euo pipefail
cd "$(dirname "$0")/.."

: "${VERSION:?Set VERSION, e.g. VERSION=0.2.0 tools/release.sh}"
REPO=${RELEASE_REPO:-noalear/PhotonCanvas-releases}
GH=${GH:-gh}
command -v "$GH" >/dev/null || GH="/c/Program Files/GitHub CLI/gh.exe"
[ -n "$(git status --porcelain)" ] && { echo "Commit or put away your changes first: the release is built from what's committed."; exit 1; }

# The version goes in the build (the update check compares against it) and in a commit and tag here.
sed -i "s|<Version>[^<]*</Version>|<Version>$VERSION</Version>|" Directory.Build.props
if ! git diff --quiet Directory.Build.props; then
  git commit -q -m "Version $VERSION" Directory.Build.props
fi
git tag -a "v$VERSION" -m "PhotonCanvas $VERSION"

VERSION="$VERSION" tools/publish.sh "$@"

notes="$(mktemp)"
if [ -f "docs/releases/$VERSION.md" ]; then cp "docs/releases/$VERSION.md" "$notes"
else
  last=$(git describe --tags --abbrev=0 "v$VERSION^" 2>/dev/null || true)
  { echo "What's new in $VERSION:"; echo; git log --format='- %s' ${last:+"$last..v$VERSION"} | grep -v '^- Version ' | head -60; } > "$notes"
fi
cat >> "$notes" <<'EOF'

To update: unpack the file for your computer in place of the old one. Your designs, settings and material library are kept in your PhotonCanvas folder (in your home folder), not with the program.
EOF

files=(dist/PhotonCanvas-*.zip dist/PhotonCanvas-*.tar.gz)
existing=()
for f in "${files[@]}"; do [ -f "$f" ] && existing+=("$f"); done
[ ${#existing[@]} -gt 0 ] || { echo "Nothing was built."; exit 1; }
"$GH" release create "v$VERSION" "${existing[@]}" --repo "$REPO" --title "PhotonCanvas $VERSION" --notes-file "$notes"
git push -q origin HEAD "v$VERSION"
echo "Released $VERSION to https://github.com/$REPO/releases/tag/v$VERSION"
