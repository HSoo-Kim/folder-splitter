#!/usr/bin/env bash
# Builds and runs the Swift Testing suites. Each suite compiles with only the sources it needs.
#   scripts/test.sh                                   # every suite below
#   scripts/test.sh Sources/Core/Search Tests/Search  # one slice (sources + its tests)
#   scripts/test.sh -- --filter SearchQueryTests      # extra args go to the test runner
source "$(dirname "$0")/env.sh"

slice=()
runner_args=()
while (( $# )); do
    if [[ "$1" == "--" ]]; then shift; runner_args=( "$@" ); break; fi
    slice+=( "$1" ); shift
done

run_suite() {   # run_suite <dirs…>
    local list=( "${SHARED_SOURCES[@]}" $(swift_files "$ROOT/Tests/Support") )
    for arg in "$@"; do list+=( $(swift_files "$(cd "$ROOT" && realpath "$arg")") ); done
    local files=( $(printf '%s\n' "${list[@]}" | awk 'NF && !seen[$0]++') )
    local out="$BUILD/tests-$(printf '%s' "$*" | shasum | cut -c1-8)"
    mkdir -p "$out"
    "${SWIFTC[@]}" -Onone -g -parse-as-library -enable-testing \
        -F "$TESTING_FRAMEWORKS" -Xlinker -rpath -Xlinker "$TESTING_FRAMEWORKS" \
        -plugin-path "$TESTING_PLUGINS" \
        -module-name FolderSplitterTests -o "$out/run" "${files[@]}"
    "$out/run" ${runner_args[@]+"${runner_args[@]}"}
}

if (( ${#slice[@]} )); then
    run_suite "${slice[@]}"
    exit
fi

status=0
run_suite Sources/Core/Search Tests/Search || status=1
run_suite Sources/Core/Models Tests/Models || status=1
run_suite Sources/Core/FileOps Tests/FileOps || status=1
run_suite Sources/Core/Update Tests/Update || status=1
run_suite Sources/Core/Cloud Tests/Cloud || status=1
run_suite Sources/App/Contracts Sources/App/Preview Tests/Preview || status=1
exit $status
