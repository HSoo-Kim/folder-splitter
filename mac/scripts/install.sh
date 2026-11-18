#!/usr/bin/env bash
# Builds a release app (or reuses it with --no-build), replaces /Applications/Folder Splitter.app and launches it.
source "$(dirname "$0")/env.sh"

[[ "${1:-}" == "--no-build" ]] || "$ROOT/scripts/build.sh"   # --no-build: install the app QA just tested
app="$BUILD/Folder Splitter.app"
signing_keychain="$HOME/Library/Keychains/folder-splitter-signing.keychain-db"
if [[ -f "$signing_keychain" ]] && keychain_pw="$(security find-generic-password -a "$USER" -s folder-splitter-signing-keychain -w 2>/dev/null)" && security unlock-keychain -p "$keychain_pw" "$signing_keychain" 2>/dev/null; then
    identity="$(security find-identity -p codesigning "$signing_keychain" | awk '/"Folder Splitter Signing \(HSoo-Kim\)"/ { print $2; exit }')"
    if [[ -n "$identity" ]]; then
        codesign --force --sign "$identity" --identifier io.github.hsoo-kim.FolderSplitter --timestamp=none "$app"
    fi
fi
target="/Applications/Folder Splitter.app"

# SIGTERM quits like ⌘Q (the app saves its session); no AppleScript automation prompt.
if pkill -TERM -f "$target/Contents/MacOS/FolderSplitter"; then
    for _ in $(seq 1 40); do pgrep -f "$target/Contents/MacOS/FolderSplitter" >/dev/null || break; sleep 0.25; done
fi
# The app declines to quit while a copy or move runs: never replace it under a running operation.
if pgrep -f "$target/Contents/MacOS/FolderSplitter" >/dev/null; then
    echo "Folder Splitter is still running (a file operation may be in progress). Quit it, then run this again." >&2
    exit 1
fi
rm -rf "$target"
ditto "$BUILD/Folder Splitter.app" "$target"
lsregister=/System/Library/Frameworks/CoreServices.framework/Frameworks/LaunchServices.framework/Support/lsregister
# Only the installed copy may receive "Show in Finder" and foldersplitter:// requests.
"$lsregister" -u "$BUILD/Folder Splitter.app" >/dev/null 2>&1 || true
"$lsregister" -f "$target" >/dev/null 2>&1 || true   # advisory; opening registers it too
open "$target"
echo "installed $target"
