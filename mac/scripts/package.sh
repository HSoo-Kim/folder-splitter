#!/usr/bin/env bash
# Builds the universal release app (or reuses it with --no-build) and zips it for other people:
#   build/folder-splitter-mac.zip
# The app is signed with the release identity (self-signed) but not notarized: on another Mac,
# macOS blocks the first launch until the user allows it (System Settings ▸ Privacy & Security
# ▸ Open Anyway).
source "$(dirname "$0")/env.sh"

[[ "${1:-}" == "--no-build" ]] || "$ROOT/scripts/build.sh"   # --no-build: zip the app QA just tested
app="$BUILD/Folder Splitter.app"
codesign --verify --strict "$app"
# Releases must carry the stable identity, or users lose their privacy grants on update.
codesign -d -r- "$app" 2>&1 | grep -q 'certificate leaf' || { echo "not signed with the release identity (scripts/make-signing-identity.sh)" >&2; exit 1; }
# Release asset name the in-app update check looks for (like folder-splitter-win-x64.zip).
zip="$BUILD/folder-splitter-mac.zip"
rm -f "$zip"
ditto -c -k --sequesterRsrc --keepParent "$app" "$zip"
echo "packaged $zip: version $(cat "$ROOT/VERSION") ($(lipo -archs "$app/Contents/MacOS/FolderSplitter"))"
