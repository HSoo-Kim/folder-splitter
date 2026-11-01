#!/usr/bin/env bash
# Builds build/Folder Splitter.app.
#   scripts/build.sh            # optimized, universal (Apple silicon + Intel)
#   scripts/build.sh --debug    # -Onone -g, this Mac's architecture only, faster to compile
source "$(dirname "$0")/env.sh"

config="release"
[[ "${1:-}" == "--debug" ]] && config="debug"
if [[ "$config" == "release" ]]; then
    opt=( -O -whole-module-optimization )
    archs=( arm64 x86_64 )
else
    opt=( -Onone -g -D DEBUG )
    archs=( "$ARCH" )
fi

app="$BUILD/Folder Splitter.app"
contents="$app/Contents"
rm -rf "$app"
mkdir -p "$contents/MacOS" "$contents/Resources"

slices=()
for arch in "${archs[@]}"; do
    slice="$BUILD/FolderSplitter-$arch"
    # The later -target overrides the native one in SWIFTC.
    "${SWIFTC[@]}" "${opt[@]}" -target "$arch-apple-macosx$DEPLOYMENT_TARGET" -module-name FolderSplitter \
        -o "$slice" "${SHARED_SOURCES[@]}" "${CORE_SOURCES[@]}" "${APP_SOURCES[@]}"
    slices+=( "$slice" )
done
lipo -create "${slices[@]}" -output "$contents/MacOS/FolderSplitter"
rm -f "${slices[@]}"
rm -rf "$BUILD"/FolderSplitter-*.dSYM
version="$(cat "$ROOT/VERSION")"
build_number="$(date +%Y%m%d%H%M)"
sed -e "s/__VERSION__/$version/g" -e "s/__BUILD__/$build_number/g" \
    "$ROOT/Resources/Info.plist" > "$contents/Info.plist"
plutil -lint -s "$contents/Info.plist"

"$ROOT/scripts/make-icon.sh"
cp "$BUILD/AppIcon.icns" "$contents/Resources/AppIcon.icns"
for lproj in "$ROOT"/Resources/*.lproj; do cp -R "$lproj" "$contents/Resources/"; done

# macOS keeps privacy grants per designated requirement. The release identity (created once by
# scripts/make-signing-identity.sh) makes it the same for every build, so updates keep Full Disk
# Access and folder access; an ad-hoc signature changes it with every build.
signing_keychain="$HOME/Library/Keychains/folder-splitter-signing.keychain-db"
identity=""
if [[ -f "$signing_keychain" ]]; then
    security unlock-keychain -p "$(security find-generic-password -a "$USER" -s folder-splitter-signing-keychain -w)" "$signing_keychain"
    identity="$(security find-identity -p codesigning "$signing_keychain" | awk '/"Folder Splitter Signing \(HSoo-Kim\)"/ { print $2; exit }')"
fi
if [[ -n "$identity" ]]; then
    codesign --force --sign "$identity" --identifier io.github.hsoo-kim.FolderSplitter --timestamp=none "$app"
else
    echo "note: no release signing identity (scripts/make-signing-identity.sh); signing ad hoc" >&2
    codesign --force --sign - --identifier io.github.hsoo-kim.FolderSplitter --timestamp=none "$app"
fi
echo "built $app ($config)"
