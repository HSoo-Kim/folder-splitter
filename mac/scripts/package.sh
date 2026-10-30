#!/usr/bin/env bash
# Builds the universal release app (or reuses it with --no-build) and zips it for other people:
#   build/FolderSplitter-<version>.zip
# The app is signed ad hoc, not notarized: on another Mac, macOS blocks the first launch
# until the user allows it (System Settings ▸ Privacy & Security ▸ Open Anyway).
source "$(dirname "$0")/env.sh"

[[ "${1:-}" == "--no-build" ]] || "$ROOT/scripts/build.sh"   # --no-build: zip the app QA just tested
app="$BUILD/Folder Splitter.app"
codesign --verify --strict "$app"
zip="$BUILD/FolderSplitter-$(cat "$ROOT/VERSION").zip"
rm -f "$zip"
ditto -c -k --sequesterRsrc --keepParent "$app" "$zip"
echo "packaged $zip ($(lipo -archs "$app/Contents/MacOS/FolderSplitter"))"
