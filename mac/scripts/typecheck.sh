#!/usr/bin/env bash
# Type-check a slice without building the whole app, so parallel work never blocks on
# someone else's half-written files.
#   scripts/typecheck.sh Sources/Core/Search        # Shared + that slice
#   scripts/typecheck.sh Sources/App/FileList       # adds App/Contracts automatically
#   scripts/typecheck.sh --all                      # everything (Shared + Core + App)
source "$(dirname "$0")/env.sh"

list=( "${SHARED_SOURCES[@]}" )
if [[ "${1:-}" == "--all" ]]; then
    list+=( "${CORE_SOURCES[@]}" "${APP_SOURCES[@]}" )
else
    needs_app=0
    for arg in "$@"; do
        abs="$(cd "$ROOT" && realpath "$arg")"
        [[ "$abs" == "$ROOT/Sources/App"* ]] && needs_app=1
        list+=( $(swift_files "$abs") )
    done
    if (( needs_app )); then list+=( $(swift_files "$ROOT/Sources/App/Contracts") ); fi
fi

files=( $(printf '%s\n' "${list[@]}" | awk 'NF && !seen[$0]++') )
# main.swift holds top-level code; library-only slices need -parse-as-library instead.
mode=( -parse-as-library )
printf '%s\n' "${files[@]}" | grep -q '/main\.swift$' && mode=()
"${SWIFTC[@]}" -typecheck ${mode[@]+"${mode[@]}"} "${files[@]}"
echo "typecheck ok (${#files[@]} files)"
