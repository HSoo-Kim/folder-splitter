#!/usr/bin/env bash
# End-to-end QA: runs the built app's in-process harness (--qa) against a fixture folder with a
# throwaway session and search index. Results: build/qa/<lang>/report.md and screenshots.
#   scripts/qa.sh                       # build, then run English and Korean
#   scripts/qa.sh --no-build --lang ko  # reuse build/Folder Splitter.app, Korean only
#   scripts/qa.sh --arch x86_64         # run the Intel slice of a universal build (Rosetta)
source "$(dirname "$0")/env.sh"

build=1
languages=( en ko )
arch=""
while (( $# )); do
    case "$1" in
        --no-build) build=0 ;;
        --lang) shift; languages=( "$1" ) ;;
        --arch) shift; arch="$1" ;;
    esac
    shift
done
(( build )) && "$ROOT/scripts/build.sh"

# QA runs a copy with its own bundle identifier, so its preferences, LaunchServices
# registration and privacy grants never touch an installed Folder Splitter.
qa_id="io.github.hsoo-kim.FolderSplitter.qa"
app="$BUILD/Folder Splitter QA.app"
rm -rf "$app"
ditto "$BUILD/Folder Splitter.app" "$app"
plutil -replace CFBundleIdentifier -string "$qa_id" "$app/Contents/Info.plist"
plutil -replace CFBundleName -string "Folder Splitter QA" "$app/Contents/Info.plist"
plutil -replace CFBundleDisplayName -string "Folder Splitter QA" "$app/Contents/Info.plist"
codesign --force --sign - --identifier "$qa_id" --timestamp=none "$app" 2>/dev/null
cleanup() {
    /System/Library/Frameworks/CoreServices.framework/Frameworks/LaunchServices.framework/Support/lsregister -u "$app" >/dev/null 2>&1 || true
    defaults delete "$qa_id" >/dev/null 2>&1 || true
    rm -rf "$app"
}
trap cleanup EXIT
status=0
for lang in "${languages[@]}"; do
    out="$BUILD/qa/$lang"
    rm -rf "$out"
    mkdir -p "$out"
    # A separate instance (-n) so an installed, running copy is left alone; -W waits for it to quit.
    qa_args=( -AppleLanguages "($lang)" --qa "$out" --language "$lang" --session "$out/session.json" --index-root "$out/fixture" )
    if [[ -n "$arch" ]]; then
        arch "-$arch" "$app/Contents/MacOS/FolderSplitter" "${qa_args[@]}" >/dev/null 2>&1 &
    else
        open -n -W "$app" --args "${qa_args[@]}" &
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
