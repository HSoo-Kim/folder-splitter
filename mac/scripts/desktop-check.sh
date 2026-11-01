#!/usr/bin/env bash
# UI check that leaves the user's desktop alone, driven by agent-desktop
# (github.com/vercel-labs/agent-desktop, `npm install -g agent-desktop`). A copy of the app runs with
# its windows outside every display (--offscreen), is never activated, and is read and operated only
# through accessibility: no pointer, keyboard or focus changes, nothing appears on screen.
# The app draws its own windows to PNG on SIGUSR1 (--capture-dir), since screencapture cannot see
# windows off screen. Results: build/desktop-check/<lang>/report.md and PNGs.
#   scripts/desktop-check.sh                  # Korean, using build/Folder Splitter.app as built
#   scripts/desktop-check.sh --build --lang en
source "$(dirname "$0")/env.sh"

build=0
lang=ko
while (( $# )); do
    case "$1" in
        --build) build=1 ;;
        --lang) shift; lang="$1" ;;
    esac
    shift
done
command -v agent-desktop >/dev/null || { echo "agent-desktop not found: npm install -g agent-desktop" >&2; exit 1; }
(( build )) && "$ROOT/scripts/build.sh"
[[ -d "$BUILD/Folder Splitter.app" ]] || { echo "no build: run scripts/build.sh" >&2; exit 1; }

# Own bundle id and name: never touches (or gets confused with) an installed, running Folder Splitter.
check_id="io.github.hsoo-kim.FolderSplitter.check"
check_name="Folder Splitter Check"
app="$BUILD/desktop-check/$check_name.app"
rm -rf "$app"
mkdir -p "$BUILD/desktop-check"
ditto "$BUILD/Folder Splitter.app" "$app"
plutil -replace CFBundleIdentifier -string "$check_id" "$app/Contents/Info.plist"
plutil -replace CFBundleName -string "$check_name" "$app/Contents/Info.plist"
plutil -replace CFBundleDisplayName -string "$check_name" "$app/Contents/Info.plist"
for strings in "$app"/Contents/Resources/*.lproj/InfoPlist.strings; do
    [[ -f "$strings" ]] && sed -i '' "s/\"CFBundleDisplayName\" = \"[^\"]*\";/\"CFBundleDisplayName\" = \"$check_name\";/" "$strings"
done
codesign --force --sign - --identifier "$check_id" --timestamp=none "$app" 2>/dev/null
"/System/Library/Frameworks/CoreServices.framework/Frameworks/LaunchServices.framework/Support/lsregister" -f "$app"

out="$BUILD/desktop-check/$lang"
rm -rf "$out"
fixture="$out/fixture/DropboxFixture"
mkdir -p "$fixture"
# Items marked the way Dropbox marks them.
head -c 2048 /dev/zero > "$fixture/synced.txt";   xattr -w com.dropbox.attrs 1 "$fixture/synced.txt"
: > "$fixture/online-only.pdf";                   xattr -w com.dropbox.attrs 1 "$fixture/online-only.pdf"
                                                  xattr -w com.dropbox.placeholder 1 "$fixture/online-only.pdf"
echo hi > "$fixture/ignored.tmp";                 xattr -w com.dropbox.ignored 1 "$fixture/ignored.tmp"
echo hi > "$fixture/not-yet-synced.txt"
# No global hot key, no update check, no whole-disk index, no welcome window.
cat > "$out/session.json" <<JSON
{"settings": {"language": "$lang", "hasShownWelcome": true, "globalHotKey": "off", "checkForUpdates": false,
              "searchIndexEnabled": false, "restoreSessionOnLaunch": false}}
JSON

python3 - "$out" "$lang" "$check_id" "$check_name" "$fixture" <<'PY'
import json, os, signal, subprocess, sys, time
out, lang, check_id, name, fixture = sys.argv[1:6]
ko = lang == "ko"
results = []

def ad(*args):
    run = subprocess.run(["agent-desktop", *args], capture_output=True, text=True)
    try:
        return json.loads(run.stdout)
    except json.JSONDecodeError:
        return {"ok": False, "error": {"message": run.stdout + run.stderr}}

def frontmost():
    asn = subprocess.run(["lsappinfo", "front"], capture_output=True, text=True).stdout.strip()
    info = subprocess.run(["lsappinfo", "info", "-only", "bundleid", asn], capture_output=True, text=True).stdout
    return info.split("=")[-1].strip().strip('"')

def capture(file):
    """The app draws its own windows on SIGUSR1 (screencapture cannot see them off screen)."""
    apps = (ad("list-apps").get("data") or {}).get("apps", [])
    pid = next((a["pid"] for a in apps if a.get("bundle_id") == check_id), None)
    target = f"{out}/capture/window-1.png"
    if pid is None:
        return 0
    if os.path.exists(target):
        os.remove(target)
    os.kill(pid, signal.SIGUSR1)
    for _ in range(20):
        if os.path.exists(target):
            os.replace(target, f"{out}/{file}")
            return os.path.getsize(f"{out}/{file}")
        time.sleep(0.25)
    return 0

def record(title, ok, detail):
    results.append((title, ok, detail))
    print(("PASS " if ok else "FAIL ") + title + " | " + detail)

def walk(node):
    yield node
    for child in node.get("children") or []:
        yield from walk(child)

def snapshot():
    snap = ad("snapshot", "--app", name, "--max-depth", "40")
    return snap["data"]["tree"] if snap.get("ok") else None

def file_rows(tree):
    """{name: (badge, size)} for the file list: the outline that has the Name column."""
    for node in walk(tree):
        children = node.get("children") or []
        columns = [c.get("native_id", {}).get("value") for c in children if c.get("role") == "column"]
        if node.get("role") != "outline" or not {"name", "size"} <= set(columns):
            continue
        rows = {}
        for row in (c for c in children if c.get("role") == "treeitem"):
            cells = [c for c in row.get("children") or [] if c.get("role") == "cell"]
            if not cells:
                continue
            badge = next((n.get("name") for n in walk(cells[0])
                          if n.get("native_id", {}).get("value") == "fs.cloudBadge"), None)
            size = cells[columns.index("size")].get("name") if "size" in columns and len(cells) > columns.index("size") else None
            rows[cells[0].get("name")] = (badge, size)
        return rows
    return {}

def wait_rows(predicate, seconds=10):
    deadline = time.time() + seconds
    rows = {}
    while time.time() < deadline:
        tree = snapshot()
        rows = file_rows(tree) if tree else {}
        if predicate(rows):
            return rows, tree
        time.sleep(0.5)
    return rows, None

check_bundle = check_id
front_before = frontmost()
real = None
try:
    info = json.load(open(os.path.expanduser("~/.dropbox/info.json")))
    real = next((v["path"] for v in info.values() if isinstance(v, dict) and os.path.isdir(v.get("path", ""))), None)
except Exception:
    pass

args = ["--arg=--offscreen", f"--arg=--capture-dir", f"--arg={out}/capture", "--arg=-AppleLanguages", f"--arg=({lang})", f"--arg=--session", f"--arg={out}/session.json",
        "--arg=--dropbox-root", f"--arg={fixture}", "--arg=--index-root", f"--arg={os.path.dirname(fixture)}", f"--arg={fixture}"]
if real:
    args.append(f"--arg={real}")
launched = ad("launch", check_id, *args)
if not launched.get("ok"):
    print("launch failed:", launched.get("error")); sys.exit(1)

frontmost_seen = set()
try:
    windows = []
    for _ in range(20):
        listed = ad("list-windows", "--app", name)
        windows = listed.get("data") or [] if listed.get("ok") else []
        if windows:
            break
        time.sleep(0.5)
    frontmost_seen.add(frontmost())
    on_screen = []
    displays = ad("list-displays").get("data") or []
    for window in windows:
        b = window["bounds"]
        for d in displays if isinstance(displays, list) else displays.get("displays", []):
            db = d.get("bounds", {})
            if b["x"] < db.get("x", 0) + db.get("width", 0) and b["x"] + b["width"] > db.get("x", 0) \
               and b["y"] < db.get("y", 0) + db.get("height", 0) and b["y"] + b["height"] > db.get("y", 0):
                on_screen.append(window["title"])
    record("windows stay off every display", bool(windows) and not on_screen,
           f"windows={[(w['title'], w['bounds']['x'], w['bounds']['y']) for w in windows]} onScreen={on_screen}")

    if real:
        rows, _ = wait_rows(lambda r: len(r) > 0 and "online-only.pdf" not in r and any(b for b, _ in r.values()))
        counts = {}
        for badge, _ in rows.values():
            counts[badge or "none"] = counts.get(badge or "none", 0) + 1
        record("real Dropbox folder shows status badges", any(k != "none" for k in counts), f"{len(rows)} items: {counts}")
        capture("real-dropbox.png")
        # Switch to the fixture tab through accessibility only.
        tree = snapshot() or {}
        tab = next((n for n in walk(tree) if n.get("name") == "DropboxFixture" and n.get("ref_id")
                    and "Click" in (n.get("available_actions") or []) and n.get("role") not in ("treeitem", "cell")), None)
        clicked = ad("click", tab["ref_id"]) if tab else {"ok": False, "error": {"message": "fixture tab not found"}}
        record("switch tab headlessly (accessibility press)", clicked.get("ok", False),
               f"tab={tab and (tab.get('role'), tab.get('name'))} result={clicked.get('ok') or clicked.get('error')}")

    expected = {
        "synced.txt": ("최신 상태" if ko else "Up to date", None),
        "online-only.pdf": ("온라인 전용" if ko else "Online-only", "—"),
        "ignored.tmp": ("Dropbox에서 무시됨" if ko else "Ignored by Dropbox", None),
        "not-yet-synced.txt": (None, None),
    }
    rows, _ = wait_rows(lambda r: set(expected) <= set(r))
    frontmost_seen.add(frontmost())
    good = all(rows.get(k, (None, None))[0] == badge and (size is None or rows.get(k, (None, None))[1] == size)
               for k, (badge, size) in expected.items())
    record("Dropbox fixture: badges and online-only size", good, json.dumps(rows, ensure_ascii=False))
    size = capture("fixture.png")
    record("window captured while off screen", size > 20000, f"{out}/fixture.png ({size} bytes)")
finally:
    ad("close-app", name)
    for _ in range(20):
        if not any(a.get("bundle_id") == check_bundle for a in (ad("list-apps").get("data") or {}).get("apps", [])):
            break
        time.sleep(0.3)
    front_after = frontmost()
    frontmost_seen.add(front_after)
    record("never became the frontmost app", check_bundle not in frontmost_seen,
           f"front before={front_before} during/after={sorted(frontmost_seen)}")

with open(f"{out}/report.md", "w") as report:
    report.write("| check | result | detail |\n|---|---|---|\n")
    for title, ok, detail in results:
        report.write(f"| {title} | {'pass' if ok else '**FAIL**'} | {detail.replace('|', '/')} |\n")
passed = sum(ok for _, ok, _ in results)
print(f"{passed}/{len(results)} checks passed. {out}/report.md")
sys.exit(0 if passed == len(results) else 1)
PY
