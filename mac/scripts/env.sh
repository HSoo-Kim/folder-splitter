# Shared compiler setup. Source from the other scripts.
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
BUILD="$ROOT/build"
DEPLOYMENT_TARGET="15.0"
ARCH="$(uname -m)"
mkdir -p "$BUILD"

SWIFTC=(xcrun swiftc -swift-version 5 -target "$ARCH-apple-macosx$DEPLOYMENT_TARGET")

# Some CommandLineTools installs ship a stale usr/include/swift/module.modulemap next to
# bridging.modulemap, so every `import AppKit` fails with "redefinition of module
# 'SwiftBridging'". Hide the stale file through a VFS overlay instead of touching /Library.
CLT_SWIFT_INCLUDE="$(dirname "$(xcrun --find swiftc)")/../include/swift"
if [[ -f "$CLT_SWIFT_INCLUDE/module.modulemap" && -f "$CLT_SWIFT_INCLUDE/bridging.modulemap" ]]; then
    CLT_SWIFT_INCLUDE="$(cd "$CLT_SWIFT_INCLUDE" && pwd -P)"
    : > "$BUILD/empty.modulemap"
    cat > "$BUILD/clt-overlay.yaml" <<EOF
{ "version": 0, "case-sensitive": "false", "roots": [ { "type": "directory",
  "name": "$CLT_SWIFT_INCLUDE",
  "contents": [ { "type": "file", "name": "module.modulemap", "external-contents": "$BUILD/empty.modulemap" } ] } ] }
EOF
    SWIFTC+=(-vfsoverlay "$BUILD/clt-overlay.yaml" -Xcc -ivfsoverlay -Xcc "$BUILD/clt-overlay.yaml")
fi

TESTING_FRAMEWORKS="$(xcode-select -p)/Library/Developer/Frameworks"
TESTING_PLUGINS="$(xcode-select -p)/usr/lib/swift/host/plugins/testing"

# Sources shared by every target. Shared/ must stay Foundation-only (tests run headless).
swift_files() { find "$@" -name '*.swift' -type f 2>/dev/null | sort; }
SHARED_SOURCES=( $(swift_files "$ROOT/Sources/Shared") )
CORE_SOURCES=( $(swift_files "$ROOT/Sources/Core") )
APP_SOURCES=( $(swift_files "$ROOT/Sources/App") )
