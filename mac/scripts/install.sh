#!/usr/bin/env bash
# Builds a release app, replaces /Applications/Folder Splitter.app and launches it.
source "$(dirname "$0")/env.sh"

"$ROOT/scripts/build.sh"
target="/Applications/Folder Splitter.app"

# SIGTERM quits like ⌘Q (the app saves its session); no AppleScript automation prompt.
if pkill -TERM -f "$target/Contents/MacOS/FolderSplitter"; then
    for _ in $(seq 1 40); do pgrep -f "$target/Contents/MacOS/FolderSplitter" >/dev/null || break; sleep 0.25; done
fi
rm -rf "$target"
ditto "$BUILD/Folder Splitter.app" "$target"
lsregister=/System/Library/Frameworks/CoreServices.framework/Frameworks/LaunchServices.framework/Support/lsregister
# Only the installed copy may receive "Show in Finder" and foldersplitter:// requests.
"$lsregister" -u "$BUILD/Folder Splitter.app" >/dev/null 2>&1 || true
"$lsregister" -f "$target" >/dev/null 2>&1 || true   # advisory; opening registers it too
open "$target"
echo "installed $target"
