#!/usr/bin/env bash
# End-to-end QA: runs the built app's in-process harness (--qa) against a fixture folder with a
# throwaway session and search index. Results: build/qa/<lang>/report.md and screenshots.
#   scripts/qa.sh                       # build, then run English and Korean
#   scripts/qa.sh --no-build --lang ko  # reuse build/Folder Splitter.app, Korean only
#   scripts/qa.sh --arch x86_64         # run the Intel slice of a universal build (Rosetta)
#   scripts/qa.sh --onscreen            # windows on screen and the app activated (takes focus)
# By default the app's windows stay outside every display and it never becomes active, so the run
# leaves the user's screen and keyboard focus alone (separate windows such as Settings may flash).
source "$(dirname "$0")/env.sh"

build=1
languages=( en ko )
arch=""
placement=( --offscreen )
while (( $# )); do
    case "$1" in
        --no-build) build=0 ;;
        --lang) shift; languages=( "$1" ) ;;
        --arch) shift; arch="$1" ;;
        --onscreen) placement=() ;;
    esac
    shift
done
(( build )) && "$ROOT/scripts/build.sh"

# QA runs a copy with its own bundle identifier, so its preferences, LaunchServices
# registration and privacy grants never touch an installed Folder Splitter. A fresh copy per
# language: the update check replaces it with a fake newer release.
qa_id="io.github.hsoo-kim.FolderSplitter.qa"
app="$BUILD/Folder Splitter QA.app"
make_app() {   # make_app <destination> <version>
    rm -rf "$1"
    ditto "$BUILD/Folder Splitter.app" "$1"
    plutil -replace CFBundleIdentifier -string "$qa_id" "$1/Contents/Info.plist"
    plutil -replace CFBundleName -string "Folder Splitter QA" "$1/Contents/Info.plist"
    plutil -replace CFBundleDisplayName -string "Folder Splitter QA" "$1/Contents/Info.plist"
    plutil -replace CFBundleShortVersionString -string "$2" "$1/Contents/Info.plist"
    codesign --force --sign - --identifier "$qa_id" --timestamp=none "$1" 2>/dev/null
}
cleanup() {
    /System/Library/Frameworks/CoreServices.framework/Frameworks/LaunchServices.framework/Support/lsregister -u "$app" >/dev/null 2>&1 || true
    defaults delete "$qa_id" >/dev/null 2>&1 || true
    rm -rf "$app"
    # The update check moves the replaced QA app to the Trash.
    rm -rf "$HOME/.Trash/Folder Splitter QA"*.app 2>/dev/null || true
}
trap cleanup EXIT
version="$(cat "$ROOT/VERSION")"
# The fake release is the next patch version (0.1.1 → 0.1.2).
next_version="$(echo "$version" | awk -F. '{ printf "%d.%d.%d", $1, $2, $3 + 1 }')"
status=0
for lang in "${languages[@]}"; do
    out="$BUILD/qa/$lang"
    rm -rf "$out"
    mkdir -p "$out/update"
    make_app "$app" "$version"
    # A fake newer Mac release beside a Windows-only one, served from a file:// feed.
    make_app "$out/update/Folder Splitter QA.app" "$next_version"
    ditto -c -k --sequesterRsrc --keepParent "$out/update/Folder Splitter QA.app" "$out/update/folder-splitter-mac.zip"
    rm -rf "$out/update/Folder Splitter QA.app"
    sha="$(shasum -a 256 "$out/update/folder-splitter-mac.zip" | cut -d' ' -f1)"
    cat > "$out/update/releases.json" <<JSON
[
  {"tag_name": "v0.9.0", "html_url": "https://github.com/HSoo-Kim/folder-splitter/releases/tag/v0.9.0",
   "draft": false, "prerelease": false,
   "assets": [{"name": "folder-splitter-win-x64.zip", "browser_download_url": "https://github.com/HSoo-Kim/folder-splitter/releases/download/v0.9.0/folder-splitter-win-x64.zip", "digest": "sha256:$(printf '%064d' 0)"}]},
  {"tag_name": "mac-v$next_version", "html_url": "https://github.com/HSoo-Kim/folder-splitter/releases/tag/mac-v$next_version",
   "draft": false, "prerelease": false,
   "assets": [{"name": "folder-splitter-mac.zip", "browser_download_url": "file://$out/update/folder-splitter-mac.zip", "digest": "sha256:$sha"}]}
]
JSON
    # A separate instance (-n) so an installed, running copy is left alone; -W waits for it to quit.
    qa_args=( -AppleLanguages "($lang)" --qa "$out" --language "$lang" --session "$out/session.json" --index-root "$out/fixture"
              --update-feed "file://$out/update/releases.json" --dropbox-root "$out/fixture/Dropbox" ${placement[@]+"${placement[@]}"} )
    if [[ -n "$arch" ]]; then
        arch "-$arch" "$app/Contents/MacOS/FolderSplitter" "${qa_args[@]}" >"$out/stdout.log" 2>"$out/stderr.log" &
    else
        # -g: launch in the background (off screen runs never take focus).
        open -n -W ${placement[@]+-g} "$app" --args "${qa_args[@]}" &
    fi
    opener=$!
    for _ in $(seq 1 480); do kill -0 "$opener" 2>/dev/null || break; sleep 0.5; done
    if kill -0 "$opener" 2>/dev/null; then
        echo "QA ($lang) timed out after 240 s" >&2
        pkill -f "Folder Splitter QA.app/Contents/MacOS/FolderSplitter" 2>/dev/null || true
        status=1
        continue
    fi
    if [[ ! -f "$out/report.md" ]]; then echo "QA ($lang) produced no report" >&2; status=1; continue; fi
    sed -n 3p "$out/report.md"
    grep '\*\*FAIL\*\*' "$out/report.md" && status=1
done
# Duplicates undone during QA land in the user's Trash; remove those fixture copies.
rm -f "$HOME/.Trash/report 2026 final copy"*.pdf "$HOME/.Trash/report 2026 final 복사본"*.pdf 2>/dev/null || true
exit $status
