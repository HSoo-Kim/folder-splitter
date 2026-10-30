#!/usr/bin/env bash
# Builds build/AppIcon.icns from the Windows app's icon artwork, inset to Apple's icon grid
# (the source has its own margin; artwork lands at ~80% of the canvas) so it sits at the same visual size as other Dock icons.
source "$(dirname "$0")/env.sh"

src="$ROOT/../src/FolderSplitter/Assets/app.png"
icns="$BUILD/AppIcon.icns"
[[ -f "$icns" && "$icns" -nt "$src" ]] && exit 0

set="$BUILD/AppIcon.iconset"
rm -rf "$set"
mkdir -p "$set"
master="$BUILD/AppIcon-1024.png"
sips -s format png -z 930 930 "$src" --out "$BUILD/AppIcon-inset.png" >/dev/null
sips -p 1024 1024 "$BUILD/AppIcon-inset.png" --out "$master" >/dev/null
for size in 16 32 128 256 512; do
    sips -z $size $size "$master" --out "$set/icon_${size}x${size}.png" >/dev/null
    double=$(( size * 2 ))
    sips -z $double $double "$master" --out "$set/icon_${size}x${size}@2x.png" >/dev/null
done
iconutil -c icns "$set" -o "$icns"
