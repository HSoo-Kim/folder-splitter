## Chrome-style group pills, editor and pinned tabs

Group chips and saved-sidebar entries share pale same-hue pills, visible mid-tone borders and outlined folder glyphs. Chip text is checked at 4.5:1 against normal, hover and pressed fills for all 15 named palettes and both generated custom-palette fixtures; vivid membership underlines retain their independent marker contrast. The editor uses nine circular colors, the selected color's ring, an immediately focused borderless name field, icon-bearing commands and muted shortcut hints. Windows 11 DWM owns popup corners/shadows; earlier Windows retains the standard popup shadow.

Pinned tabs occupy a compact, icon-only prefix outside the ordinary overflow window. Pin/unpin applies to multi-selection, preserves pane/window moves, excludes pins from Close other tabs, and interacts with groups by removing either pin or group membership. Ctrl+W and native middle-click still close pins. Sessions and workspaces store the normalized saved-tab prefix length as `PaneState.PinnedCount`; successful memory-restart replacement is checked across three peer windows.

Release application/UI-test builds report **0 warnings / 0 errors**. Model tests pass **89/89**. Private-desktop runs pass **1,456 tab-group checks**, **493 multi-window checks** and **766 existing tab-strip/help checks**. Logs: `artifacts/local-tabgroups/chrome-tab-groups-2.log`, `chrome-multi-window-1.log`, `chrome-tab-chrome-1.log`. The first tab-group run found the expected sidebar pixel contract change from neutral text to the group's readable hue; that existing assertion now checks the new hue and its contrast instead. No pixel tolerance was relaxed.

The final `--chrome-groups` follow-up passes **40/40** (`artifacts/local-tabgroups/chrome-capture-4.log`) after checking the actual rendered focused placeholder pixels at 4.5:1 in light and dark modes. The native EDIT control remains responsible for text editing, selection, caret and accessibility; only its empty-state cue is painted in the theme's readable muted color because the system cue uses a fixed gray and normally disappears on focus. The broader suites above preceded that localized cue-paint follow-up.

The subsequent editor-alignment review uses one **14-DIP content inset**, scaled to **18 physical pixels at 120 DPI**, for the name pill, first color dot and command icons; the field shares the matching right inset. The menu overrides WinForms' image-gutter-derived default padding rather than relying on a padding assignment that native layout replaces. Icons come from the installed app icon font, are normalized to the same 16-DIP extent, and use grayscale vector edge coverage in exactly the enabled/disabled text color. The final focused run passes **82/82** (`artifacts/local-tabgroups/chrome-alignment-6.log`), including both theme captures, full-width shortcut/submenu columns, every icon's position and monochrome pixel coverage, disabled icons, saved-state check and delete icons.

The final post-refinement `--tab-groups` run passes **1,500 checks / 0 failures** (`artifacts/local-tabgroups/chrome-aligned-tab-groups-1.log`), with exit code 0, no timeout and successful private-desktop/job cleanup. The recorded geometry is identical in light and dark captures: field left **18 px**, first dot left **18 px**, every command icon left **18 px**, and matching **18 px** right inset at **120 DPI**. Release application/UI-test builds report **0 warnings / 0 errors**.

The final color-row correction assigns nine full-width hit-target slots and spaces the circles evenly from the first left edge to the last right edge. Each circle's antialiasing and selected-ring stroke are inset inside its painted bounds rather than extending into a dot-sized final button. Light and dark pixel checks cover every full circle/ring, its owning hit target, the content bounds and equal gaps; the last orange color is selected through the real editor before the dark capture. The editor inherits the pane's actual text font, and the checks exercise distinct **9 / 9.5 / 10.5 pt** app sizes at **120 DPI**. The focused run passes **147/147** (`artifacts/local-tabgroups/chrome-color-row-2.log`), retaining the alignment, monochrome-icon and shortcut-column checks.

The final font-aware `--tab-groups` rerun passes **1,565 checks / 0 failures** (`artifacts/local-tabgroups/chrome-color-row-tab-groups-2.log`), following the earlier **1,561 / 0** color-row run before the explicit app-font inheritance assertions. Both exit with code 0, without timeout, and report successful private-desktop/job cleanup. The corresponding Release application/UI-test build has **0 warnings / 0 errors**.

The full no-argument UI suite is deliberately left to the integration owner after merging. All launches use `PrivateDesktopQa`, keep the input desktop unchanged, and report successful job/desktop cleanup. Strip captures are `tests/QuadExplorer.UiTests/bin/Release/net10.0-windows10.0.19041.0/artifacts/tabgroups-polish/chrome-strip-{light,dark}.png`; each includes two pins plus expanded and collapsed groups. Popup `PrintWindow` returns black on the non-input desktop, so editor layout captures use the real control's WM_PRINT/DrawToBitmap paint path instead.

The requested light/dark editor captures are in the same capture directory as `chrome-editor-light.png` and `chrome-editor-dark.png`; the hosted EDIT HWND is printed separately into the real menu bitmap so its focused placeholder is included. WM_PRINT captures client layout, not the DWM compositor's outer shadow.

## Final release-readiness review fixes

Workspace tab replacement and Close other tabs suppress saved-group synchronization until the complete operation settles. Nested close, undo and transfer batches preserve any outer suppression; closing a group completely keeps its saved copy. Undo, workspace/session restore and direct saved-group insertion check every open pane in every window before restoring a saved ID; a conflicting restored group retains its folders and metadata but is unlinked.

Batch close, multi-selection moves and redo choose the next visible tab, then the previous visible tab, or create an ungrouped blank when only collapsed groups remain. The established single-tab close behavior is unchanged. A Windows shutdown query saves without terminating the application; native WM_QUERYENDSESSION followed by WM_ENDSESSION(FALSE) tests exercise cancellation, continued tray/hotkey/new-window use, and a later confirmed end-session. Update notices distinguish explicit user dismissal from owner closure, allowing a surviving window to notify the same release when its former owner closes.

The README now accurately describes silent backup-copy failure and warns that 0.8.20 drops tab groups, saved groups and extra windows on save. The one-time migration backup contains the pre-upgrade file, not groups created afterward.

The owner's display changed during verification: one multi-window attempt observed 120 DPI with working areas changing from 2560×1380 to 6200×1860; later attempts used 96 DPI and 1920×1032. Two pre-existing 96-DPI test assumptions were reproduced on an isolated 5758ff8 export. The pane-undo fixture now reloads its real captured workspace after the appearance/resize sweep before capturing its exact size expectation, avoiding a one-pixel WinForms resize-rounding discrepancy. Native sort-highlight pixels are sampled at the actual native Name/Date header centers instead of hard-coded x coordinates; at 96 DPI the Date column spans x=272–416, excluding the former x=430 sample. Exact geometry comparisons, pixel tolerances and assertion labels are unchanged. Temporary diagnostics and the disposable baseline export are removed after comparison; baseline logs remain under `artifacts/cross-window-drag/final-baseline96-*.log`.

The complete 96-DPI run is not a release pass: `artifacts/cross-window-drag/final-full-ui-04.log` records **7,280 passed checks** before the unchanged native-open selection pixel assertion fails (`dark-sTrue-before row 3`, **562/568** matching pixels against the existing >99% threshold). The isolated 5758ff8 `--native-open-selection` run reproduces the same label and exact pixel count (`final-baseline96-native-open-01.log`, 190 checks passed before failure). No native-rendering code or pixel threshold was changed. The integration owner explicitly deferred the complete suite and its 11,060-baseline PASS-multiset proof until the 120-DPI display configuration returns; focused feature acceptance remains separate from that pending full-run gate.

Completed evidence: final Release application/UI-test build **0 warnings / 0 errors** (`final-build-06.log`); model tests **88/88**, search **192 assertions**, favorites import passed. The 120-DPI `--tab-groups` run passes **1,416 checks** (`final-tab-groups-02.log`; working area was not logged by that earlier runner). The final successful 96-DPI `--multi-window` run passes **490 checks** with primary working area **1920×1032** (`final-multi-window-05.log`). Update-notice checks pass **39/39** (`final-update-notice-01.log`) and the published single-pane-per-window smoke passes **15/15** (`final-published-smoke-01.log`); those earlier runners did not log their display metrics. All completed private-desktop launchers report successful cleanup.

`final-focused-pass-multiset-proof.json` preserves every normalized PASS label and multiplicity from the prior focused baselines: **1,287 → 1,416 tab-group checks, zero missing, 129 added**, and **464 → 490 multi-window checks, zero missing, 26 added**. It explicitly does not claim a completed full-suite proof. A later 96-DPI tab-group attempt records **1,252 passes / one failure** in the unchanged light multi-selection appearance pixel sample (`final-tab-groups-03.log`); that appearance check had passed in the complete 120-DPI feature run, but no isolated baseline comparison was run for this later failure. No appearance threshold or production paint code was changed. The integration owner requested stopping further 96-DPI reruns; an in-progress repeat batch was cancelled rather than reported as a pass.

## Cross-window tab, selection and group dragging

The application context owns one drag session containing the source browser, pane and ordered tab identities or group. It installs one owned, non-activating overlay for each visible, non-minimized browser and refreshes that set during the OLE message loop. Native top-level z-order hit-testing ignores those transport overlays, skips cloaked windows and lets foreign windows or application dialogs occlude browser targets. The receiving browser computes its own pane, split and strip marker. Commit rechecks live identities, pane capacity, the four-pane limit and the twelve-member strip-join limit after OLE has pumped messages.

Cross-window drops reconstruct the destination batch before removing source tabs, preserve whole-group saved links, expand moved groups, retain the active dragged member, and do not add visits or close-undo entries. Batch insertion rolls back its additions on synchronous creation failure. Source cleanup uses the existing transfer-only close path; empty splits disappear, and the last emptied source browser closes without tray hiding or retiring shared services. The context suppresses intermediate saves and writes the complete browser registry once after a successful transfer.

Outside release is not inferred from `DragDropEffects.None` alone. `OnQueryContinueDrag` records the physical screen point only for an explicit left-button release with `DragAction.Drop`; Escape, an explicit cancellation, or a vanished source marks the session cancelled. After OLE returns, tear-off requires that recorded release, a `None` transport result, and a point outside every visible/non-minimized browser's full bounds and any application dialog. Thus browser chrome/full targets never become accidental new windows, and an accepting foreign target's non-`None` result owns the drop. No new browser is created during hover. The source's normal size is converted between source and release-monitor DPI, positioned under the drop point, and clamped to that monitor's working area.

Limits: OLE exposes acceptance through its returned effect; a foreign target that incorrectly performs a drop but returns `None` cannot be distinguished from rejection. The private desktop cannot move a hardware cursor or exercise a physical mixed-DPI monitor arrangement. UI checks therefore intercept only OLE transport, keep the real button/chip → pane → context → overlay → commit chain, feed the same continuation handler an explicit screen point, and cover native HWND z-order, monitor clamping, cancellation and accepted foreign-effect results. Pure geometry tests additionally exercise 96/120/144-DPI transitions, negative monitor coordinates and working areas smaller than the nominal minimum.

The final Release application/UI-test and model-test builds have **0 warnings / 0 errors** (`artifacts/cross-window-drag/build-ui-final-02.log`, `build-unit-01.log`). Model tests pass **88/88**, path search passes **192 assertions**, and favorites import passes (`unit-01.log`, `path-unit-01.log`, `favorites-unit-01.log` in that directory). All UI execution uses `PrivateDesktopQa`; no real desktop, default session or registry registration is changed.

The final focused runs pass **1,287 tab-group checks** and **464 multi-window checks**, and the full native UI suite passes **11,060 checks**, all with exit code 0, no timeout, and successful private-desktop/job cleanup (`tab-groups-final-02.log`, `multi-window-final-02.log`, `full-ui-01.log`). The new scenario contributes exactly **220 checks** to each runner: tab/selection/group center, edge and strip moves; whole-payload capacity and four-pane rejection; exact/twelve-plus-one strip joins; capacity/group/pane changes between overlay acceptance and commit; saved-link retention; empty source split/window cleanup; native overlap ordering and hidden/minimized eligibility; Escape/cancel/foreign acceptance/browser-chrome refusal; all three outside tear-offs with tray both on and off; clamped geometry; and aggregate-session restore. Existing redo checks exposed an over-broad source-history invalidation during integration; cleanup now removes only redo entries referencing transferred tabs, preserving unrelated reopened tabs.

The lossless byte-wise normalized PASS-multiset proof (`artifacts/cross-window-drag/pass-multiset-proof.json`) compares against `artifacts/tabgroups-c2/full-ui-02.log`: **10,840 baseline checks, zero missing labels or multiplicities, 220 additional checks**. It applies only the established count-summary, scratch-GUID and variable help-layout suffix normalization. No pre-existing test was removed, weakened or relabeled, and the known native-appearance flake did not require a full-run retry.

The self-contained Windows x64 single-file publish succeeds (`artifacts/cross-window-drag/publish-01.log`), and `published/` contains only `FolderSplitter.exe`. Its private-desktop published smoke passes **15/15 checks**, exit code 0, with no timeout and successful cleanup (`published-smoke-01.log`). Two real launches restore independent browser groups and the saved catalog, open/focus a saved sidebar group through native input, save through Settings → Exit, and retain both browsers and stable group links across relaunch. The scratch session is `published-smoke-session/published-groups-session.json`; the existing smoke confirms the real Run registration's kind and data remain byte-for-byte unchanged.

## Tab groups C2: multi-selection and saved groups

The pane keeps an ordered selection separate from its viewed tab. Ctrl-click toggles inactive members, Shift-click uses the last plain/Ctrl-click anchor and skips collapsed members, and the viewed tab cannot be deselected. Inactive selected tabs use an inset, accent-edged wash blended from the theme's selected and canvas colors; the viewed tab retains its raised content-surface shape. Each selected tab exposes the native accessible `Selected` state. Plain tab clicks, strip Escape, keyboard tab switching and tab removal clear the multi-selection.

Context menus capture the current selection when the clicked tab belongs to it. Grouping gathers members in strip order, preserving the contiguity of partially selected source groups; whole groups and loose tabs can merge. When the first selected tab lies inside a partially selected source group, the new run is placed at that remaining group's boundary rather than splitting its surviving members. The 12-member group and 32-tab pane limits are preflighted before any batch mutation. Duplicate remains tied to the clicked tab. Selected-tab strip drags, pane/split drops and new-window moves use the same atomic batch paths. Single-tab moves remain ungrouped unless a strip drop explicitly joins a target group.

A selection close stores one history entry containing each path (including blank tabs), former index, original group identity and active-member flag. One undo restores positions and rejoins the surviving original group, or reconstructs a fully closed group; an overfull former group falls back to an ungrouped tab. Redo tracks all reopened tab identities and closes that batch in one action. Existing single-tab and whole-group undo entries populate that same redo identity list. Removed or moved tabs invalidate stale redo targets.

Saved groups have stable app-wide IDs, a newest-first catalog capped at 30 (the thirty-first save is refused without evicting data), and optional open-group links in pane/session/workspace state. Names, colors, member order and folder navigation synchronize from live linked groups to the shared catalog and every sidebar. Bulk close, undo and transfer operations suppress transient partial snapshots; a settled partial close updates its surviving members, whereas a vanished group leaves its saved entry intact. Partial group transfers do not duplicate the saved link; whole-group transfers retain it. Delete/unsave clears live links across all windows while leaving open tabs intact, and undo checks the catalog before restoring a deleted link. Opening an existing saved group focuses/expands its owning pane, including hidden panes or peer windows, rather than duplicating it. Saved sections follow the complete Favorites section; the member count has its own right-aligned space so name ellipsis cannot hide it.

Release app/UI-test build: **0 warnings / 0 errors** (`artifacts/tabgroups-c2/build-10.log`). Model tests: **87/87** (81 baseline plus six saved-group persistence/compatibility cases), path search: **192 assertions**, favorites import: passed (`unit-01.log`, `path-unit-01.log`, `favorites-unit-01.log` in the same directory). Final focused private-desktop suites pass **1,067 tab-group checks** and **244 multi-window checks**, with no failures/timeouts and successful launcher cleanup (`tab-groups-06.log`, `multi-window-04.log`). No baseline assertions were removed or weakened. A 14-tab real-input fixture is widened to fit its minimum-width tabs at the private desktop's DPI.

An earlier multi-window attempt timed out after the hidden tray-Exit fixture (`multi-window-01.log`); its launcher still released the private desktop and killed only its own job. No production exit guard or tray test was changed to hide this observation. The later completed run also exercised that unchanged fixture. Intermediate integration failures exposed unsubscribed legacy single-tab move events; all menu callers now use the batch contracts, and obsolete events/branches were removed.

The first full run exposed a real integration regression in the existing sidebar splitter assertion: WinForms counted the empty, invisible saved section's 40-pixel bottom docking height when limiting the folder-tree split. The empty section now has no dock and an empty rectangle, while a populated section returns to bottom docking. The unchanged splitter and persistence assertions pass in the focused `--appearance` runner (**75 checks**, `appearance-final.log`); no size expectation was weakened. Diagnostic scaffolding and the isolated managed-stack tool installation were removed.

The final complete private-desktop suite passes **10,840 checks**, exit code 0, no timeout, and successful desktop/job cleanup (`artifacts/tabgroups-c2/full-ui-02.log`). The lossless byte-wise normalized PASS-multiset proof (`pass-multiset-proof.json`) retains every label and multiplicity from `artifacts/merged-window/full-ui-final-02.log`: **10,641 baseline, zero missing, 199 additional checks**. The additions comprise **175 explicit feature checks** and **24 additional assertions from the existing custom-theme traversal discovering the three new sidebar controls**. Only the documented count-summary, scratch-GUID and variable help-layout suffix normalization is applied; mixed-encoding native output is not replacement-decoded for comparison.

Final self-contained Windows x64 single-file executable: `artifacts/tabgroups-c2/published/FolderSplitter.exe` (`publish-02.log`; the output directory contains only that executable). Published smoke passes **15/15 checks** on the private desktop (`published-saved-smoke-03.log`). A separate scratch session (`published-smoke-session-03/published-groups-session.json`) holds a saved entry alongside two independent browser groups. Both launches expose the Saved groups sidebar entry; real native clicks on its exact ListBox HWND open/focus one linked group, the real Settings → Exit saves normally, and the second launch retains both the saved catalog and stable open link. The test verifies the real Run value's registry kind and unexpanded data are byte-for-byte unchanged. Early smoke-driver attempts using accessibility selection or out-of-band native selection were replaced with ordinary ListBox mouse input; no production exception handler or fallback was added.

Feature captures are under `artifacts/tabgroups-c2/artifacts/`: `multi-select-tabs/multi-select-light-strip.png`, `multi-select-tabs/multi-select-dark-strip.png` (plus complete windows and custom palettes), and `saved-groups/saved-groups-light-sidebar.png`, `saved-groups/saved-groups-dark-sidebar.png` (plus complete windows). Pixel assertions cover light/dark/custom tab selection, group underlines, saved color dots and the separately visible counts. Private-desktop captures show managed chrome; the uncomposed native Shell area is not a claim about real-session file-list pixels. No real desktop, display setting, default session or registry registration was modified.

## Tab groups and peer browser windows

Merge commit `4116d0c` combines `feature/multi-window` into `feature/tab-groups`. The unit-test invocation conflict retains both model suites. Group docking now suppresses intermediate layout saves with the existing browser-local rebuilding guard; the old form-level save flag moved to the application context. The automatic-memory lifecycle check follows that context and validates saved per-window hidden presentation rather than the superseded process-wide `--hidden` argument.

The merged baseline builds with **0 warnings / 0 errors**, passes **80/80** model tests, **192** search assertions and the favorites-import checks, and passes **10,567** native UI checks on the private desktop. Evidence: `artifacts/merged-window/native-ui-fe4d2aed86114dcc81e251cc2fdb327f.log`. Its normalized PASS multiset exactly equals the union of the tab-groups baseline (**10,481**) and multi-window baseline (**9,675**), whose shared intersection is **9,589**: no assertion is missing and no baseline-only extra exists. Normalization removes only the final count summary, scratch fixture GUIDs and variable help-layout diagnostic suffixes; it retains assertion labels and multiplicities.

The chip and tab menus now transfer groups or individual tabs into a cascaded, one-pane browser. The application context creates and populates the destination before showing it and before removing source members, including unsaved blank members. Group transfers retain order, name, color and selected member and expand the destination group. Source removal does not count visits or add close history. Empty splits collapse; a browser with no remaining visible or cached tabs closes through a transfer-only path while its new peer keeps the context alive. The synchronous operation performs one aggregate save. Menu commands and completion statuses are translated in Korean, English, Simplified Chinese and Japanese.

The focused suites pass **892** tab-group checks (`tab-groups-native-final.log`) and **160** multi-window checks (`multi-window-native-final.log`), including **74 new checks**. Transfers are invoked by native mouse messages to the visible menu, and keyboard checks use `Application.FilterMessage` so every registered browser filter participates. New checks cover both windows' chip colors and translated menus, secondary-window workspace and close history, 12-member and blank-member transfers, collapsed-group selection retention, one synchronous aggregate save observed through the real temporary session file, source undo/redo preservation, empty-split cleanup and last-source-window closure with tray both enabled and disabled. Model tests pass **81/81**.

A full run encountered the suite's existing compact/comfortable native-icon repaint race before reaching the new feature scenario. A bounded comparison ran `--native-appearance` three times on each build: the feature build failed the unchanged icon-size pixel assertion **1/3** times, and the clean `4116d0c` merge build failed it **2/3** times (`appearance-comparison.json`, `appearance-feature-*.log`, `appearance-merge-*.log`). No assertion, readiness condition or production rendering code was changed to hide it. The temporary merge source/build export was removed. The full-suite private-desktop deadline is now 900 seconds to accommodate the additional native-window scenarios.

The final complete native run passes **10,641 checks = 10,567 merged baseline + 74 new checks**, with exit code 0, no timeout and successful private-desktop cleanup (`artifacts/merged-window/full-ui-final-02.log`). `artifacts/merged-window/pass-multiset-proof.json` records both input baselines, the exact 10,567-check merge union, the final 74-check added multiset and **zero missing assertions from either branch**. The app was then published as a self-contained Windows x64 single-file executable at `artifacts/FolderSplitter/FolderSplitter.exe`.

Published single-file smoke passes **9 checks** on the private desktop (`artifacts/merged-window/published-group-smoke-03.log`). A scratch session starts with one named, colored group in each of two browsers. Both browser HWNDs and their live accessible group names are observed on launch, the real Settings → Exit command closes the application normally, and a second launch restores both groups in their original browsers. Both exits save the expected tab paths and group metadata. The test snapshots the real Run value's kind and unexpanded data before and after both launches, with startup enabled in the scratch session, and confirms it is unchanged. The smoke uses native accessibility enumeration for each child HWND because the top-level client accessibility object alone does not enumerate the managed strips.

Reproduce the published smoke through `tools/PrivateDesktopQa` with a fresh log path and `--timeout 180`, launching the UI-test DLL with `--published-group-smoke artifacts/FolderSplitter/FolderSplitter.exe <scratch-directory>`. No window is shown on the real desktop, and neither the default session nor the real Run/StartupApproved values are modified. Physical monitor-to-monitor DPI transitions remain outside this private-desktop setup; the existing native-DPI restoration checks and both windows' native-DPI chip/font layouts are covered.

## Tab groups: stability and Chrome conveniences

Collapsed groups retain their state during session, workspace and memory-restart restores. Collapsing the active group skips other collapsed groups when selecting a replacement; a new blank tab is inserted after the group when needed. Closing a group selects outside it before removing members, without displaying an intermediate member. A full 32-tab pane refuses a collapse if every alternative is hidden and no blank tab can fit.

The strip reserves the all-tabs button whenever shown, caps chip natural width at 180 logical pixels and ellipsizes long names. Chip text measurement is cached by text/font/DPI; layout counts each contiguous group's members once. Ungrouped insert/close paths do not rebuild the grouped strip. Tooltips retain full group names and list up to 12 member titles, and accessible state/name information includes collapse state and member group names.

Creating a group opens its editor below the chip. Duplicates and folder-tree new tabs open next to the originating grouped tab, falling back to an ungrouped tab after a full group; favorites and places retain their old behavior. A single reopened tab rejoins its original group only while that exact group remains in the pane with room. Cross-pane strip drops retain insertion/join information; expanded-chip right-side drops insert the first member; overflow trailing drops agree with the marker. Move-group menu targets expose capacity failures as disabled items with reason tooltips.

`Ctrl+Shift+F6` reaches the active tab strip without changing existing F6 pane navigation. Arrows reach tabs/chips, Apps or Shift+F10 opens the focused item's anchored menu, and Ctrl+Shift+PageUp/PageDown moves the active tab across group boundaries. New shortcut help and grouped accessible names are localized in Korean, English, Simplified Chinese and Japanese. Unknown group fields load without error; pre-groups record copies read current sessions and omit group metadata on resave.

Focused verification: Release app/UI-test build reports 0 warnings and 0 errors; unit tests pass 65/65 (63 baseline + 2 compatibility cases), path-search tests pass 192 assertions, and favorites-import tests pass. The private-desktop `--tab-groups` scenario passes 892 checks, including chip/underline pixels on all 15 named themes and two custom palettes. The new-group editor and 260-pixel collapsed-chip captures are in `artifacts/tabgroups-polish/new-group-editor-open.png` and `artifacts/tabgroups-polish/260px-long-collapsed-group.png`.

Final full native suite: **10,481 checks passed** (9,912 baseline + 569), with exit code 0, no timeout, and private-desktop cleanup successful. Evidence: `artifacts/tabgroups-polish/full-ui-03.jsonl`; final focused evidence: `artifacts/tabgroups-polish/tab-groups-09.jsonl` (`checks=892 failures=0`). The language scenario now opens the real tab menu with native right-click messages instead of reflecting the changed private menu signature. Temporary baseline source/build exports were removed; comparison logs remain.

DPI evidence uses the real 120-DPI private desktop plus 144/192-DPI-equivalent final font metrics, including large text and the child DPI notification. It does not claim physical monitor-DPI transitions: changing the real display/session or registry is outside this QA setup. The blank native Shell content in private-desktop window captures is also not evidence about composed Shell pixels; the requested editor/tab-strip chrome is rendered and inspected.

The committed b1e4c6f baseline and the changed build both reproduced intermittent all-black `PrintWindow` captures of the layered drag overlay (`baseline-tab-groups-01.jsonl`, `tab-groups-06.jsonl`). The marker test now captures the actual overlay's managed paint path with `DrawToBitmap`/`WM_PRINT`, retaining the same marker-pixel assertion and real insertion/drop path. No production workaround, retry or suppressed assertion was added.

## Folder Splitter 0.8.20: 선택한 파일의 이름 칸 색

자세히 보기에서 파일을 선택하면 이름 칸만 선택 색 없이 목록 배경색으로 남는 경우가 있었습니다. 이름 칸은 기본 정렬 열입니다. Windows 파일 목록은 정렬 열을 따로 칠하는 기능이 있고, 이 색이 선택 색을 덮습니다. 앱은 정렬 열을 바꾸는 `LVM_SETSELECTEDCOLUMN` 메시지를 가로채 이 기능을 꺼 두었습니다. 그런데 Shell이 목록 인터페이스로 이 값을 직접 바꾸면 메시지가 오지 않습니다. 실제 사용 중인 0.8.19 창의 파일 목록 세 개가 모두 정렬 열 0(이름)을 갖고 있었고, 같은 메시지를 보내면 앱이 -1로 바꿔 놓는 것으로 가로채기는 붙어 있음을 확인했습니다. 이제 자세히 보기를 그릴 때마다 행을 그리기 전에 정렬 열이 켜져 있는지 보고 끕니다. 정렬 열 강조 설정은 앱이 직접 칠하므로 영향이 없습니다.

새 `--selection-fill` 검사는 사용자 지정 색 #626525·#F6F7F9, 세로 구분선 없음, 넓은 행 간격으로 두 줄을 선택해 처음 연 상태, 파일 추가, 정렬 메뉴, 다른 폴더에 다녀온 뒤, 그리고 목록의 원래 창 프로시저로 정렬 열을 켠 뒤(메시지 없이 바뀐 상태)에 선택 행 전체가 선택 색인지 픽셀로 확인합니다. 수정 전에는 마지막 단계에서 이름 칸이 목록 배경으로 남아(722픽셀 중 424픽셀만 선택 색) 실패했고, 수정 뒤에는 31개가 모두 통과합니다. 비공개 데스크톱에서 전체 네이티브 UI 검사 9,589개가 모두 통과했습니다.

단위 검사 54개, 경로 검색 192 assertion, 탐색기 즐겨찾기 가져오기 검사가 통과했습니다. 배포 파일은 커밋한 코드로 만든 단일 실행 파일입니다. ZIP에는 `FolderSplitter.exe`, README 네 개, LICENSE만 들어 있고 모두 원본과 같습니다. 실행 파일은 Folder Splitter 0.8.20 Windows x64이며 CET 호환 플래그는 꺼져 있습니다. 업데이트 코드는 0.8.17 이후 바뀌지 않았고, 0.8.19에 들어 있는 그 코드로 실제 0.8.19 실행 파일을 이 ZIP으로 교체했습니다. SHA-256이 다른 패키지는 거부했습니다. 배포 실행 파일은 격리된 세션에서 바탕 화면 탭과 실제 최근 항목 탭으로 각각 3번, 업데이트 직후처럼 `--wait-pid`로 1번 실행·종료했고, 실제 자동 실행 항목은 바뀌지 않았습니다.

## Folder Splitter 0.8.19: 고른 사용자 지정 색이 실제로 보이도록 수정, 최근 조합

0.8.18의 `사용자 지정 색`은 적용 자체는 됐지만 고른 색이 거의 남지 않았습니다. 배경은 OKLCH 밝기 0.925~0.975(밝은 테마)·0.23~0.32(어두운 테마)로 옮기고 채도를 0.03까지 낮췄으며, 무채색에 가까운 배경은 강조 색 기운으로 바꿨습니다. 그래서 흰색·기본 배경(#F6F7F9)·스톤·회색이 모두 같은 테마가 됐고, 선명한 파랑(#0080FF)은 거의 흰색(#DAE8FB)이 됐습니다. 비공개 데스크톱에서 강조 색을 정한 뒤 배경만 흰색으로 바꿔 적용했을 때 컨트롤 73개, 도구 모음 항목 98개, 파일 목록 색 6가지가 하나도 바뀌지 않았습니다. 강조 색도 읽을 수 있는 색인데 기본 범위로 옮겼습니다(검정 → #4D4D4D).

이제 고른 배경이 글자를 7:1 이상으로 읽을 수 있고 선택·마우스 올림 표시를 둘 여유가 있으면 그대로 쓰고, 아니면 색상과 채도를 유지한 채 밝기만 필요한 만큼 옮깁니다(8비트 최종 색 기준). 파일 목록은 같은 색을 밝은 테마에서 45% 흰색과 섞어 차분하게, 어두운 테마에서는 조금 밝게 칠합니다. 강조 색은 읽을 수 있으면 그대로 씁니다. 대화상자 배경 견본 12개는 모두 그대로 재현되고, 측정한 배경 34개 중 23개가 그대로 재현됐습니다(이전 6개). 유채색 배경의 채도는 평균 34%에서 91%가 남습니다. 강조·배경 조합 12,544개에서 글자·보조 글자·강조·오류 대비, 상태 순서, 밝은·어두운 판정 위반은 없었습니다.

대화상자도 고쳤습니다. 화면 배율이 125%일 때 견본은 32×32 픽셀 그대로였고 미리보기 크기도 고정이라, 배율에 맞춰 그린 `새 탭에서 열기`가 파일 행 위에 겹쳤습니다. 이제 견본·여백·미리보기 크기를 현재 DPI로 정하고, 미리보기 배치를 한 곳에서 계산해 96·120·144 DPI에서 겹치지 않는지 검사합니다. `다른 색…`으로 고른 색은 견본 줄 끝에 선택된 견본으로 보이고, 이름에 16진수 값이 들어갑니다. 미리보기는 앱처럼 선택한 사이드바 항목과 활성 영역 테두리에 강조 색을 씁니다. 배경이 테마 전체와 밝은·어두운 글자를 정하므로 배경 색 줄을 강조 색 줄보다 먼저 둡니다.

최근에 적용한 사용자 지정 색 조합은 최신순으로 6개까지 `session.json`의 `RecentCustomColors`에 남습니다. 같은 조합은 한 번만 남고, 다시 고르면 맨 앞으로 옵니다. 색 테마 메뉴에서는 프리셋 아래에 배경·강조 두 색을 반씩 칠한 그림과 16진수 값으로 보이며 누르면 바로 적용됩니다. 대화상자에서는 배경 색 줄 위의 `최근 조합` 줄에 반반 원으로 보이며, 누르면 두 색을 미리보기에만 넣습니다. 이전 세션은 저장된 사용자 지정 색 한 쌍으로 목록을 시작합니다.

`다른 색…`이 여는 Windows 색 선택 창은 처음부터 펼친 상태라 `사용자 지정 색 만들기(D) >>` 단추가 늘 꺼진 채 보였습니다. 이 단추를 숨깁니다. 또 창을 열 때마다 새로 만들어 `사용자 지정 색에 추가(A)`로 넣은 색이 다음에 사라졌습니다. 이제 16칸을 `session.json`의 `PickerColors`에 저장해 두 줄(배경·강조)이 함께 쓰고, 다시 실행해도 남습니다. 비공개 데스크톱에서 색 선택 창의 해당 단추가 숨겨진 것, RGB 값을 넣어 추가한 색이 다음에 다시 열 때 첫 칸에 있는 것을 실제 창으로 확인했습니다.

단위 검사 54개(색 생성 3개, 최근 조합 3개, 색 선택 창 칸 2개 추가), 경로 검색 192 assertion이 통과했습니다. 비공개 데스크톱(125%)에서 사용자 지정 색 UI 검사 578개가 통과했습니다. 실제 견본·최근 조합 단추 클릭, 테마 메뉴의 최근 조합 적용과 순서, 6개 제한, 4개 언어의 접근성 이름, Windows 색 선택 창을 확인합니다. 0.8.18과 나란히 실행한 전체 네이티브 UI 검사는 0.8.18 9,411개가 모두 통과했습니다. 변경본은 첫 실행에서 명령 막대 시나리오의 세부 정보 행 측정이 한 번 늦어 2개가 실패했고, 이 시나리오만 두 버전에서 각각 3번씩 다시 실행하면 모두 통과했습니다. 최종 변경본 전체는 9,558개가 모두 통과했습니다. 같은 배율에서 대화상자, 테마 메뉴, 밝은 하늘색·모래색·남색 사용자 지정 색을 적용한 창을 PrintWindow로 캡처해 확인했습니다.

배포 파일은 커밋한 코드로 만든 단일 실행 파일입니다. ZIP에는 `FolderSplitter.exe`, README 네 개, LICENSE만 들어 있고 모두 원본과 같습니다. 실행 파일은 Folder Splitter 0.8.19 Windows x64이며 CET 호환 플래그는 꺼져 있습니다. 업데이트 코드는 0.8.17 이후 바뀌지 않았고, 0.8.18에 들어 있는 그 코드로 실제 0.8.18 실행 파일을 이 ZIP으로 교체했습니다. SHA-256이 다른 패키지는 거부했습니다. 배포 실행 파일은 격리된 세션에서 바탕 화면 탭과 실제 최근 항목 탭(140개)으로 각각 3번, 업데이트 직후처럼 `--wait-pid`로 1번 실행·종료했습니다. `--session`으로 연 세션은 이미 등록돼 있던 실제 자동 실행 항목을 바꾸지 않았고, `--hidden` 시작은 알림 영역 옵션에 따라 숨김·최소화로 시작했습니다.

## Folder Splitter 0.8.18: Windows 시작 시 자동 실행과 사용자 지정 색

`설정`의 `Windows 시작 시 자동 실행`은 기본으로 켜져 있습니다. 기본 세션으로 앱을 열면 현재 사용자 레지스트리의 `Run`에 `"<실행 파일>" --hidden` 값 하나를 등록합니다. 로그인하면 알림 영역에 숨긴 채 시작하고, 알림 영역 옵션이 꺼져 있으면 최소화해 시작합니다. 작업 관리자의 시작 앱에서 끈 상태(`StartupApproved`)는 앱을 실행해도 되돌리지 않고 `설정`에서 다시 켤 때만 지웁니다. `--session`으로 연 별도 세션은 스스로 등록하지 않습니다. 실행만으로는 아직 존재하는 다른 사본의 등록을 가져오지 않고(예: ZIP 안에서 바로 연 임시 사본), 등록된 실행 파일이 사라진 경우(앱을 옮긴 경우)에만 새 위치로 바꿉니다.

색 테마 메뉴 맨 아래 `사용자 지정 색…`은 강조 색과 배경 색 두 가지로 테마 전체를 만듭니다. OKLCH 색 공간에서 배경의 밝기·채도를 기존 테마와 비슷한 범위로 맞추고, 글자·보조 글자·오류·강조 색을 8비트 최종 색 기준 WCAG 대비를 만족하도록 조정하며, 강조 색의 색상(hue)은 유지합니다. 같은 입력은 항상 같은 테마가 되고, 고른 두 색만 `session.json`에 저장합니다. 미리보기는 `적용`을 누르기 전에는 앱에 반영되지 않습니다.

테마를 바꿀 때 각 컨트롤의 색은 이전 테마의 같은 역할 색으로 대응시킵니다. 두 팔레트가 서로 다른 역할에 같은 RGB를 쓰면(짙은 파랑 배경으로 만든 사용자 지정 색의 표면 색과 Nord의 정렬 열 색이 같습니다) 테마를 바꾼 뒤 영역 배치를 바꿀 때 주소 표시줄·상태 표시줄 등이 배경 색으로 바뀌었습니다. 전환을 모두 적용한 뒤에는 이전 테마 대응을 끝내고, 도구 모음 안의 컨트롤(폴더 내 찾기 상자)은 한 번만 다시 칠하도록 고쳤습니다. 이 순서를 되돌린 빌드에서는 새 검사가 주소 표시줄을 포함한 4개 컨트롤의 색 변화로 실패했고, 수정 후 통과했습니다.

단위 검사 46개(색 생성 4개, 자동 실행 기본값과 사용자 지정 색 저장 2개 추가)와 비공개 데스크톱의 사용자 지정 색 UI 검사 431개가 통과했습니다. 자동 실행 UI 검사는 시험용 레지스트리 루트에서 등록 명령, 작업 관리자 설정 보존, 다른 사본 유지, 창 표시 시 등록과 설정 해제 저장을 확인합니다. 단일 실행 파일로도 실제 사용자 레지스트리에서 확인했습니다. `--session`으로 연 세션은 `Run` 값을 만들지 않았고, `--hidden`으로 시작하면 알림 영역 옵션이 켜져 있을 때 창이 숨겨지고 꺼져 있을 때 최소화됐습니다. 시험 전후로 `Run`·`StartupApproved`에 앱 값이 없었습니다. 같은 실행 파일로 밝은·어두운 사용자 지정 색을 띄워 파일 목록까지 색이 맞는지 화면으로 확인했고, 색 생성기는 견본 조합 120개와 극단 입력을 렌더링해 확인했습니다. 기본 세션의 실행 시 자동 등록은 사용 중인 앱이 같은 세션을 쓰고 있어 실제 실행 파일로는 확인하지 않았습니다.

이 PC는 원격 데스크톱(1280×797) 세션이어서 배포된 0.8.17 코드에서도 전체 네이티브 UI 검사 중 4개 시나리오의 5개 검사(상단 도구 모음 2배 글꼴, 세부 정보 어두운 픽셀, 그룹 테마, 탭 줄)가 실패합니다. 시나리오별로 이어서 실행하는 임시 실행기로 비교하면 0.8.17은 6,951개, 변경본은 7,397개가 통과했고 실패한 검사는 같았습니다.

배포 파일은 커밋한 코드로 만든 단일 실행 파일입니다. ZIP에는 `FolderSplitter.exe`, README 네 개, LICENSE만 들어 있고 모두 원본과 같습니다. 실행 파일은 Folder Splitter 0.8.18 Windows x64이며 CET 호환 플래그는 이전처럼 꺼져 있습니다. 업데이트 코드는 0.8.17 이후 바뀌지 않았고, 0.8.17에 들어 있는 그 코드로 실제 0.8.17 실행 파일을 이 ZIP으로 교체했습니다. SHA-256이 다른 패키지는 거부했습니다. 배포 실행 파일은 격리된 세션에서 바탕 화면 탭과 실제 최근 항목 탭(140개)으로 각각 3번, 업데이트 직후처럼 `--wait-pid`로 1번 실행·종료했고, 그동안 실제 자동 실행 항목은 바뀌지 않았습니다.

## Folder Splitter 0.8.17: 복사 중 종료 보호와 최근 항목 누락 수정

Mac의 파일 작업·권한·검색·업데이트 안정화와 Windows 코드를 비교했습니다. Windows의 복사·이동·이름 변경은 자체 구현 대신 Windows `IFileOperation`을 그대로 사용합니다. 원본이 작업 시작 전에 사라져 `SHCreateItemFromParsingName`이 `FileNotFoundException`을 반환하면 이전에는 작업 스레드의 미처리 예외로 앱이 종료될 수 있었습니다. 이제 오류를 작업 결과로 전달하고, 결과를 창에 전달하는 중 창이 닫혀도 폐기된 컨트롤에 콜백을 실행하지 않습니다.

앱의 다른 영역으로 복사·이동과 일괄 이름 변경이 진행 중이면 일반 종료를 막고 기다리거나 작업을 취소하라는 안내를 표시합니다. 알림 영역으로 창을 숨기는 동작은 계속 허용하고, 메모리 정리를 위한 재시작은 작업이 끝날 때까지 미룹니다. 업데이트 다운로드 중 작업이 시작된 경우에는 새 실행 파일을 설치해도 진행 중인 프로세스를 재시작하지 않으며, 작업이 끝난 뒤 사용자가 다시 시작해야 합니다. 이 보호는 앱이 직접 시작한 해당 작업에 적용됩니다. Windows 종료나 작업 관리자 강제 종료까지 막지는 못하며, 탐색기 자체 명령의 파일 작업은 별도로 추적하지 않습니다.

검색 색인을 디스크에 저장하지 못하더라도 이미 만든 메모리 색인과 파일 변경 감시는 계속 사용합니다. 접근 권한이 없어진 폴더의 하위 항목은 재검사에서 제외하고, 권한이 돌아오면 보안 변경 알림이나 1분 간격 재시도로 다시 색인합니다. 저장된 색인은 시작 직후 먼저 표시되므로 백그라운드 재검사가 끝나기 전에는 오래된 결과가 잠시 보일 수 있습니다. 일시적인 I/O 오류는 기존 결과를 유지하고 재시도합니다. 감시자를 열 수 없던 드라이브가 복구되면 다른 드라이브의 감시를 끊지 않고 다시 연결한 뒤 누락된 변경을 재검사합니다.

자동 업데이트는 기존 SHA-256 확인에 더해, 교체 전 ZIP 최상위에 `FolderSplitter.exe`가 정확히 하나인지와 해당 파일의 Windows x64 실행 파일 형식·제품 이름·릴리스 버전이 맞는지 검사합니다. 잘못된 패키지는 원본을 그대로 둡니다. 이 검사는 패키징 실수를 막기 위한 것으로 코드 서명을 검증하지 않습니다.

Windows 11의 비공개 데스크톱과 임시 파일로 확인했습니다. 수정 전과 같은 네이티브 작업 코드를 사용하는 별도 창에서 512MiB 복사가 완료 콜백을 보내기 전에 프로세스를 종료하자 대상은 원본과 크기가 같아도 SHA-256이 달랐습니다. 수정 후 실제 메인 창을 이용한 같은 크기의 복사 중 종료를 시도했을 때 안내가 나타나고 창이 유지됐으며, 작업 완료 후 원본과 대상 SHA-256이 일치했습니다. 다운로드 중 시작한 작업의 완료 콜백을 보류한 상태에서 실제 0.8.16 ZIP을 설치하는 경우에도 기존 실행 파일이 보존되고 앱은 열린 채로 남았습니다. 비공개 데스크톱에서 변경 관련 UI 검사 34개와 파일 작업 스트레스 검사 27개, 업데이트 패키지 검사 10개가 통과했습니다.

SHA-256이 맞는 일반 텍스트도 이전에는 `FolderSplitter.exe`로 설치됐으나 수정 후에는 거부됐습니다. 실제 0.8.16 배포 ZIP은 정상 설치됐으며 버전을 다르게 제시하면 원본을 보존했습니다. 저장 경로가 디렉터리여서 색인을 쓸 수 없는 경우 수정 전에는 검색 시작이 실패했고, 수정 후에는 검색과 이후 파일 감시가 동작했습니다. 경로 검색 192개 assertion, 세션·레이아웃 40개와 즐겨찾기 검사도 통과했습니다. 별도 세션에서 단일 파일 실행 파일을 4번 실행·종료하고 탭 경로 보존을 확인했습니다.

`최근 항목`을 연 직후 목록 일부나 전체가 사라지는 문제도 고쳤습니다. 0.8.16부터 있던 문제로, 빠른 필터 검사 실패와 전체 UI 검사 실패의 원인이었습니다. 최근 항목은 탐색기 결과 폴더로 이동한 뒤 대상 파일을 직접 넣어 보여 줍니다. 그런데 이동 직후 탐색기 보기도 이 결과 폴더를 따로 읽습니다. 앱이 대상을 넣은 뒤에 이 읽기가 끝나면 보기가 그때까지 넣은 행을 지웠습니다. 비공개 데스크톱에서 보기의 행 수와 읽기 완료 알림(`DShellFolderViewEvents.EnumDone`)을 함께 기록했고, 행 수가 줄어든 시점은 매번 이 알림과 겹쳤습니다. 결과 폴더의 `RemoveAll`과 `AddIDList`는 모두 성공을 반환했습니다. 이전 기록에 "결과 폴더 자체가 비었다"고 적은 것은 잘못이었습니다. 이 결과 폴더는 목록이 정상일 때도 `EnumObjects`로 항목을 돌려주지 않았습니다. 이제 이 알림이 왔을 때 보기의 행 수가 방금 넣은 수와 다르거나 넣는 중이었으면 목록을 다시 채웁니다. 목록이 그대로 남아 있으면 아무것도 하지 않습니다. 다시 채우는 동안 목록이 잠깐(이 PC에서 0.1~0.3초) 새로 그려질 수 있습니다.

최근 항목 검사에는 목록을 연 뒤 1초를 기다려 행이 남아 있는지 확인하는 단계를 추가했습니다. 수정 전 코드로는 단독 실행 4번과 명령 모음 설정 검사 뒤 실행 4번이 모두 이 단계에서 실패했고, 수정 후에는 8번 모두 통과했습니다. 일반 폴더에서 필터를 쓴 뒤 최근 항목을 열고 바로 필터하는 진단은 수정 전 4번 모두 결과가 비었고, 수정 후 4번 모두 결과가 남았습니다. 이 PC의 실제 최근 항목 폴더(바로 가기 286개, 읽기만 함)를 격리된 세션으로 열었을 때, 수정 전 실행 파일은 5번 중 2번 133개 중 101개만 남았습니다. 행 수 변화로 보면 먼저 넣은 32개, 즉 가장 최근에 연 항목이 빠진 것으로 보입니다. 수정 후에는 5번 모두 133개가 표시됐습니다. 상위 폴더에서 `Recent` 폴더를 선택하는 검사는 폴더 목록을 다 읽은 뒤 선택하고, 선택에 성공했는지도 확인하도록 고쳤습니다. 수정 후 전체 네이티브 UI 검사는 두 번 모두 8,965개가 통과했습니다.

배포 파일은 커밋한 코드로 만든 단일 실행 파일입니다. ZIP에는 `FolderSplitter.exe`, README 네 개, LICENSE만 들어 있고 모두 원본과 같습니다. 실행 파일은 Folder Splitter 0.8.17 Windows x64이며 CET 호환 플래그는 이전처럼 꺼져 있습니다. 0.8.16에 들어 있던 업데이트 코드를 그대로 컴파일해, GitHub 최신 릴리즈 응답과 같은 형식의 정보로 실제 0.8.16 실행 파일을 새 ZIP으로 교체해 봤습니다. 새 실행 파일이 설치되고 이전 파일은 `.old`로 남았으며, SHA-256이 다른 패키지는 거부하고 원래 파일을 그대로 뒀습니다. 0.8.17의 업데이트 코드로도 결과가 같았습니다. 배포 실행 파일은 격리된 세션에서 바탕 화면 탭과 실제 최근 항목 탭으로 각각 3번 열고 닫았고, 최근 항목은 매번 133개가 모두 남았습니다. 업데이트 직후처럼 `--wait-pid`로 시작한 경우도 정상적으로 열리고 닫혔으며, 모든 실행에서 탭 경로가 저장됐습니다.

비공개 데스크톱의 실제 두 영역 시작은 수정 전 0.8.16과 수정본 모두 WinForms `SplitContainer`의 GDI+ 오류로 중단되어, 해당 실행 방식으로는 두 영역을 검증하지 못했습니다. UI 검사 하네스는 이 환경의 그리기 오류를 처리한 상태로 여러 영역을 검사합니다.

## Folder Splitter 0.8.16: 최근 항목이 실제 파일을 넘김

사이드바의 `최근 항목`은 `%APPDATA%\Microsoft\Windows\Recent` 폴더를 그대로 열었습니다. 이 폴더에는 Windows가 파일을 열 때마다 만든 바로 가기(`.lnk`)만 있고 확장자는 항상 숨겨집니다. 그래서 텔레그램 같은 프로그램으로 끌어다 놓거나 복사하면 문서 대신 1KB 남짓한 `.lnk` 파일이 넘어갔습니다. 개발 PC에서 Windows가 각 항목에 만들어 주는 끌기 데이터(`CF_HDROP`)를 직접 읽어 `...\Recent\김현수_4대보험.pdf.lnk`가 들어 있는 것을 확인했고, 탐색기 홈의 최근 파일은 실제 경로를 넘기는 것도 확인했습니다.

이제 `최근 항목`은 ExplorerBrowser의 결과 폴더(`IResultsFolder`)에 바로 가기의 대상 파일과 폴더를 채워 보여 줍니다. 끌어다 놓기, 복사, 경로 복사, 미리보기, 선택 크기 합계가 모두 실제 파일을 대상으로 합니다. 순서는 이전과 같이 마지막으로 연 순서이고, 같은 대상은 한 번만, 로컬 고정 드라이브에서 지워진 파일과 앱 실행 기록(`ms-photos:` 등 파일 경로가 없는 항목)은 빼고 보여 줍니다. 네트워크·이동식 드라이브의 대상은 확인하지 않고 그대로 둬서 연결이 끊긴 공유 폴더 때문에 목록이 멈추지 않습니다. 바로 가기 파일은 [MS-SHLLINK] 형식대로 데이터로만 읽습니다. 이 PC의 바로 가기 286개 모두에서 `IShellLink`와 같은 ID 목록과 경로를 얻었고, 읽는 시간은 약 370ms에서 25~36ms로 줄었습니다. 화면에는 32개씩 나눠 넣어 첫 행이 바로 보이고 그 사이 입력이 막히지 않습니다(이 PC 131개 전체 약 0.5초). 빠른 필터, 새로 고침, 뒤로·앞으로, 탭 복원, `shell:recent` 같은 다른 경로 표기, 창 안에서 `Recent` 폴더를 직접 여는 경우도 같은 목록을 엽니다. 위로는 이전처럼 `Recent`를 담은 폴더로 갑니다. 다른 프로그램이 파일을 열면 목록이 따라 바뀌지만, 탭이 숨겨져 있거나 무언가 선택돼 있거나 아래로 스크롤된 동안에는 기다립니다. 목록은 폴더가 아니므로 파일을 끌어다 놓을 수 없고, 다른 영역으로 복사·이동하거나 비교하는 대상이 되지 않습니다. 항목이 실제 파일이므로 삭제·이름 바꾸기·잘라내기는 탐색기 검색 결과처럼 원본 파일에 적용됩니다.

새 UI 검사(`--recent-items`, 15개)는 임시 폴더를 최근 항목 폴더로 써서 사용자의 최근 항목을 바꾸지 않습니다(`shell:recent` 확인만 실제 폴더를 읽습니다). 이전처럼 바로 가기 폴더를 그대로 열도록 되돌리면 실패하고 고친 코드에서 통과합니다. 손상된 바로 가기 두 개(크기 필드가 비정상인 것과 중간에 잘린 것)도 목록을 깨지 않고 건너뜁니다. 별도 코드 검토에서 나온 다섯 가지(손상된 바로 가기에서 예외, 창 안에서 `Recent` 폴더를 열면 예전 화면, 다른 영역 복사·비교 대상, 위로 이동, 자동 갱신 때 스크롤 초기화)는 모두 고치고 검사에 넣었습니다.

이 검증은 원격 데스크톱(1280×798) 세션에서 했습니다. 전체 네이티브 UI 검사 중 상단 도구 모음(글꼴 2배), 어두운 테마 픽셀, 그룹 테마 픽셀, 탭 줄 검사에서 5개가 실패했는데, 배포된 0.8.15 커밋도 같은 세션에서 같은 5개가 똑같이 실패해 화면 환경 때문입니다. 실패 뒤에도 모든 검사를 이어서 돌리도록 임시로 바꿔 두 버전을 비교했고, 나머지 검사는 두 버전 모두 통과했습니다.

## Folder Splitter 0.8.15: 주소창 검색 결과 아이콘

문서 보안(DRM) 프로그램이 설치된 회사 PC에서 주소창 검색 결과의 파일과 폴더가 모두 흰 기본 문서 아이콘으로 나오고, 파일 목록에는 제대로 나온다는 보고가 있었습니다. 주소창은 아이콘을 스레드 풀(MTA)에서 `SHGetFileInfo`로 읽었는데, 이 함수는 STA로 초기화된 스레드를 요구합니다. `ThreadingModel=Apartment`로 등록된 아이콘 처리기는 MTA에서 쓸 수 없어 Windows가 기본 아이콘을 돌려줍니다. 보고자 PC에서 이런 처리기가 등록된 것을 확인했고, 개발 PC에서는 같은 방식으로 등록된 인터넷 바로 가기(`.url`, `ieframe.dll`)로 재현했습니다. 이제 주소창 아이콘은 파일 목록처럼 STA인 전용 백그라운드 스레드에서 읽고, 아이콘 사이마다 그 스레드의 창 메시지를 처리하며, 동기화 상태나 문서 보안 자물쇠 같은 덧그림 표시도 함께 요청합니다.

새 UI 검사(아이콘 처리기가 STA에서만 동작하는 `.url` 결과)는 0.8.14 코드에서 실패하고 0.8.15에서 세 번 모두 통과했습니다. 바탕화면·다운로드·문서 등의 일상 파일과 폴더 264개는 0.8.14와 아이콘이 모두 같았습니다. 아이콘 하나를 읽는 시간은 평균 1.7ms에서 4.6ms로 늘었고, 대부분은 덧그림 표시 확인 시간입니다(이 PC에는 OneDrive·Dropbox·Adobe의 덧그림 처리기가 20개 있습니다). 전용 스레드 시작은 약 0.2ms입니다. 아이콘은 목록 위쪽부터 백그라운드에서 채워지며 검색 결과 글자는 기다리지 않습니다. `.\build.ps1 -UiTests -Publish`는 종료 코드 0이었고, Release 빌드 경고·오류 없이 세션·레이아웃 40/40, 경로 검색 180 assertion, Explorer 즐겨찾기 검사, 비공개 데스크톱 네이티브 UI 8,935개가 통과했습니다. 별도 코드 검토에서 제품 코드 결함은 없었고, 테스트의 두 가지 지적(폐기 확인 대상, 고정 파일 전제 확인)은 고친 뒤 다시 통과했습니다. 게시 실행 파일의 CET 호환 비트는 0.8.13부터와 같이 해제돼 있습니다. 문서 보안 프로그램이 설치된 PC에서 아이콘과 자물쇠가 실제로 나오는지는 직접 확인하지 못했습니다.

0.8.14 검증에서 게시 실행 파일이 가끔 닫히지 않던 현상의 원인도 확인했습니다. 검사 스크립트는 `Process.MainWindowHandle`로 창을 찾았는데, 이 값이 앱 창 대신 같은 프로세스에 만들어진 Windows 입력 표시기 창(`UAC_InputIndicatorOverlayWnd`, `UAC Input Indicator`)을 가리키는 경우가 있었습니다. 이 창은 `WM_CLOSE`를 받아도 앱을 닫지 않습니다. 그동안 앱의 UI 스레드는 메시지 루프에서 대기하고 있었고, 같은 프로세스의 실제 `Folder Splitter` 창에 닫기 요청을 보내면 0.8.14와 0.8.15 모두 종료 코드 0으로 끝났습니다. 앱 문제가 아니므로 앱 코드는 바꾸지 않았고, 검사 스크립트가 클래스와 제목으로 앱 창을 찾도록 고쳤습니다. 고친 스크립트로 저장된 검색 색인이 있는 격리 세션에서 0.8.15 게시 실행 파일을 8번 실행해 8번 모두 정상 종료했습니다.

## Folder Splitter 0.8.14: 영역 이동, 주소창 결과 더 보기, 자주 연 폴더

`Shift+F6`은 이전 영역으로, `Alt+1`~`Alt+4`는 해당 번호의 영역으로 이동합니다. 열려 있지 않은 영역이면 상태 표시줄이 알립니다. `Ctrl+Alt+O`와 영역 옵션의 같은 항목은 현재 폴더를 다른 영역(바로 전에 쓰던 영역, 없으면 번호가 가장 낮은 다른 보이는 영역)에서 열고 그 영역으로 이동합니다. 이미 그 폴더를 보는 탭이 있으면 그 탭을 선택하고, 선택된 탭이 비어 있으면 채우며, 아니면 새 탭을 추가합니다. 오른쪽 Alt(AltGr)가 눌린 `Ctrl+Alt` 조합은 단축키가 아니라 글자 입력으로 처리하므로 AltGr+O(ó)를 입력할 수 있습니다. 기존 `Ctrl+Alt+C`/`Ctrl+Alt+X`도 같은 규칙을 따르고, 왼쪽 Alt로는 모두 그대로 동작합니다.

주소창 검색 결과는 50개로 시작하며 `결과 더 보기` 줄을 Enter 또는 클릭하면 200개, 500개로 늘어납니다. 앱은 보여 줄 개수보다 하나 더 요청해 더 있는지 판단하므로, 결과가 정확히 50·200·500개일 때는 이 줄이 나타나지 않습니다. 새로 늘어난 결과의 첫 줄이 선택되고, 이미 읽은 아이콘은 다시 읽지 않으며, 결과 목록을 눌러도 키보드 포커스는 주소창에 남습니다. 더블클릭은 한 번만 동작합니다.

폴더를 연 횟수는 폴더 경로와 횟수만 세션 파일의 `FrequentFolders`에 저장합니다(최대 100개, 합계가 1000을 넘으면 모든 횟수를 절반으로 줄임). 3번 이상 연 폴더 최대 24개가 현재 폴더, 열린 탭, 즐겨찾기, 최근 닫은 탭 다음 순위로 검색 결과에 반영됩니다. 시작할 때 복원한 탭, 작업 구성 불러오기, 탭 이동은 세지 않으며, 드라이브·공유 루트와 폴더 경로가 아닌 위치는 기록하지 않습니다. `설정` → `자주 연 폴더 기록 지우기`로 지웁니다. 이전 버전은 이 항목을 무시합니다.

Windows 11 build 26100, .NET SDK 10.0.402에서 `.\build.ps1 -UiTests -Publish`가 종료 코드 0으로 완료됐습니다. `TreatWarningsAsErrors`를 켠 Release 빌드에 경고·오류가 없었고, 세션·레이아웃 등 40/40(폴더 방문 기록 8개 추가), 경로 검색 180 assertion(더 큰 쪽이 앞쪽의 연장인지 검사 추가), Explorer 즐겨찾기 필터·정규화·중복 제거·상한 검사(실제 폴더 20개), 비공개 데스크톱 네이티브 UI 8,933개가 통과했습니다. 네이티브 UI는 기존 8,655개에 새 기능 278개(영역 이동·다른 영역에서 열기 159, 결과 더 보기 64, 자주 연 폴더 55)를 더한 수입니다. 새 UI 검사는 구현과 따로 작성했고, 결과 목록을 한 번 누르면 주소창이 키보드 포커스를 잃어 `결과 더 보기`가 동작하지 않는 문제를 이 검사가 찾았습니다. 별도 코드 검토에서 나온 결함(더블클릭, AltGr, 닫은 뒤 쪽수 초기화, 복원에 실패한 탭의 첫 방문 누락, 아이콘 재읽기)은 고친 뒤 검사를 다시 통과시켰습니다. 순수 로직인 방문 기록은 일부러 틀리게 만든 7가지 변형을 모두 잡아냈습니다. 화면 배율이 다른 실제 모니터와 AltGr 키가 있는 실제 키보드 배열에서는 직접 확인하지 못했고, 글자 크기 100%·150% 환경은 검사로, AltGr은 키 상태를 흉내 내는 검사로 확인했습니다.

이 PC에서는 전체 UI 검사가 모든 항목을 통과해 결과를 출력한 직후 프로세스가 종료되는 단계에서 그래픽 드라이버(`amdxx64.dll`)가 `0xC0000409`로 죽어 실행이 실패했습니다. 변경하지 않은 0.8.13 커밋으로도 같은 일이 재현되어, 검사 호스트가 결과를 출력한 뒤 판정 값을 종료 코드로 바로 끝내도록 바꿨습니다(앱 코드는 아님).

게시 실행 파일의 PE 디버그 디렉터리에서 `IMAGE_DLLCHARACTERISTICS_EX_CET_COMPAT` 비트가 해제된 상태(0.8.13과 같음)를 확인했습니다. 새 실행 파일의 SHA-256은 `9a2444201a6fafa6eec13d159cd8e9723880ea9267075d2ef901fa2e4507d769`이며 ZIP 안의 실행 파일과 일치합니다. ZIP의 SHA-256은 `a630435784ec1cec917b97931e55e8e5a939acb5b58123a26a68b0e427fdf5b8`입니다. 게시 실행 파일은 `PrivateDesktopQa`의 비공개 데스크톱에서 격리 `--session`으로 실행해 창이 뜨고 응답하며 `WM_CLOSE`로 닫히면 세션에 `FrequentFolders`가 저장되는 것을 확인했습니다. 창을 닫는 요청은 8번 중 6번 30초 안에 정상 종료됐고 2번은 종료되지 않았습니다. 같은 방법으로 0.8.13 실행 파일도 8번 중 1번 종료되지 않았습니다. 0.8.15 검증에서 이는 검사 스크립트가 앱 창 대신 Windows 입력 표시기 창에 닫기 요청을 보낸 탓으로 확인됐습니다(위 0.8.15 항목 참고). 기존 앱 프로세스나 사용 중인 데스크톱에는 입력을 보내지 않았습니다. 증거와 배포 파일은 `artifacts/release-0.8.14/`에 보관합니다.

## Folder Splitter 0.8.13: CET 완화

[#13](https://github.com/HSoo-Kim/folder-splitter/issues/13)의 `FAST_FAIL_SET_CONTEXT_DENIED` 보고에 따라 앱 프로젝트에 `<CETCompat>false</CETCompat>`를 지정합니다. .NET SDK의 `CreateAppHost`가 일반 apphost와 단일 파일 게시 호스트에 이 설정을 적용합니다. 게시 뒤 바이너리를 직접 수정하지 않습니다. 이 변경은 프로세스에 CET 섀도 스택 보호를 적용하지 않는 완화책이며, 셸 확장 또는 RPC 오류 자체를 고치지 않습니다. 보고자는 같은 0.8.12에서도 재부팅 후 증상이 사라졌다고 했으므로, 보고자의 환경에서 해결됐다고 단정하지 않습니다.

`build.ps1 -Publish`는 `artifacts/folder-splitter-win-x64.zip`을 생성합니다. ZIP 내용은 실행 파일, 네 언어 README와 LICENSE 여섯 개입니다. 릴리즈에는 이 ZIP 하나만 첨부하며 파일명에 버전을 넣지 않습니다. 앱의 업데이트 검사기가 이 이름의 자산을 찾으므로, 이름이 바뀌면 이미 설치된 앱이 앱 안에서 자동으로 업데이트하지 못합니다.

Windows 11 build 26100, .NET SDK 10.0.402에서 `.\build.ps1 -UiTests -Publish`가 종료 코드 0으로 완료됐습니다. `TreatWarningsAsErrors`를 켠 Release 빌드에 경고·오류가 없었고, 세션·레이아웃 32/32, 경로 검색 168 assertion, Explorer 즐겨찾기 필터·정규화·중복 제거·상한 검사(실제 폴더 20개), 비공개 데스크톱 네이티브 UI 8,660개와 주소창 아이콘 집중 검사 23개가 통과했습니다.

이전 0.8.12와 새 게시 단일 파일 실행 파일의 PE 디버그 디렉터리를 직접 파싱했습니다. `IMAGE_DEBUG_TYPE_EX_DLLCHARACTERISTICS`(20), 크기 4, 파일 오프셋 7,818,980의 값이 `0x1`에서 `0x0`으로 바뀌어 `IMAGE_DLLCHARACTERISTICS_EX_CET_COMPAT` 비트가 해제됐습니다. 새 실행 파일의 SHA-256은 `1064b57084635c7d7ea9bdfa504cc1a3906a6de0b7e0665287409f6a474816a0`이며 ZIP 안의 실행 파일과 일치합니다. ZIP의 SHA-256은 `476536b8d28371f3fff3c1eee7afe1a5abda18c20282ef5c030c2afa669bacaf`입니다.

게시 실행 파일은 `PrivateDesktopQa`의 비공개 데스크톱에서 고유한 임시 `--session`으로 실행했습니다. 전역 단축키·트레이 유지·업데이트 확인·자동 업데이트·자동 재시작을 끄고 별도 검색 색인을 미리 복사했습니다. 새로 만든 프로세스의 창만 `WM_CLOSE`로 닫아 종료 코드 0을 확인했습니다. 기존 앱 프로세스나 사용 중인 데스크톱에는 입력을 보내지 않았습니다. 증거와 최종 배포 파일은 `artifacts/release-0.8.13/`에 보관합니다. 보고자의 Windows 11 build 26300과 해당 셸 확장 조합에서의 재현·해결 여부는 검증하지 못했습니다.

## Folder Splitter 0.8.11: 상단 도구 모음과 업데이트 알림

사이드바 버튼 바로 왼쪽의 압정으로 **전역 도구 모음**의 고정을 전환합니다. 고정하면 항상 표시되고, 고정을 해제하면 숨겨집니다. 창 제목 표시줄의 드래그 가능한 영역에 마우스를 약 0.35초 머물면 같은 도구 모음이 작업 영역 위에 일시적으로 나타나며, 그 전에 벗어나면 나타나지 않습니다. 작업 영역 상단이나 최소화·최대화·닫기 버튼 위에서는 나타나지 않습니다. 표시된 도구 모음의 압정을 누르면 다시 고정합니다. 메뉴나 입력 컨트롤을 조작하는 동안에는 자동 숨김이 조작을 방해하지 않아야 합니다. `Ctrl+Shift+F11`로도 고정을 전환할 수 있습니다. 영역별 탭·명령 막대, 분할·선택·사이드바·미리보기 상태는 바꾸지 않습니다.

선택은 실행 시 지정한 세션의 `GlobalToolbarPinned`에 저장됩니다. 이전 세션에서 값이 없으면 기존처럼 표시·고정합니다. 아래 검사는 임시 폴더와 격리 세션만 사용하며 기본 앱 세션을 쓰지 않습니다.

새 버전이 확인되고 앱의 알림 위치가 화면에 보이면 창 오른쪽 아래에 알림이 부드럽게 올라옵니다. 알림은 자동으로 사라지지 않으며, `닫기`를 누르면 부드럽게 내려갑니다. 닫은 뒤에도 아래쪽 버전 링크는 유지됩니다. 업데이트 또는 다운로드는 해당 버튼을 누를 때 기존 설정에 따라 진행합니다. 같은 버전은 실행 중 한 번만 알리고, 창이 숨겨졌거나 최소화됐거나 알림 위치가 다른 창에 가려졌으면 다시 보일 때 알립니다. 이미 표시된 알림이 다른 창에 가려져도 닫히지 않습니다. Windows의 애니메이션 효과가 꺼져 있으면 이동 없이 표시하고 닫습니다. 업데이트 확인을 끄면 요청과 알림이 중단됩니다.

집중 회귀 검사: Windows 11 build 26100에서 상단 도구 모음 검사 196개와 업데이트 알림 검사 35개 통과. 제목 표시줄 호버 지연과 취소, 고정·해제, 메뉴·포커스 보호, 알림의 지속 표시와 수동 닫기, 숨김·최소화·가림 상태 복귀를 확인했습니다. U+E840을 채운 압정은 Windows 11의 Segoe Fluent Icons 외곽선을 사용하고, Windows 10에서는 Segoe MDL2 Assets로 돌아갑니다. 0.8.11 전체 `.\build.ps1 -UiTests -Publish`에서도 설정·분할 32개, 검색 168개 assertion, 네이티브 UI 8,632개 검사가 통과했으며 빌드 경고와 오류는 없었습니다.

```powershell
dotnet build tests/QuadExplorer.UiTests/QuadExplorer.UiTests.csproj -c Release --nologo
dotnet build tools/PrivateDesktopQa/PrivateDesktopQa.csproj -c Release --nologo
$env:FOLDERSPLITTER_QA_LOG_DIR = Join-Path $env:TEMP ("FolderSplitter-toolbar-" + [guid]::NewGuid().ToString("N"))
[IO.Directory]::CreateDirectory($env:FOLDERSPLITTER_QA_LOG_DIR) | Out-Null
$log = Join-Path $env:FOLDERSPLITTER_QA_LOG_DIR "global-toolbar.log"
dotnet tools/PrivateDesktopQa/bin/Release/net10.0-windows/PrivateDesktopQa.dll --log $log --timeout 180 -- (Get-Command dotnet).Source tests/QuadExplorer.UiTests/bin/Release/net10.0-windows10.0.19041.0/QuadExplorer.UiTests.dll --global-toolbar
Get-Content $log
$log = Join-Path $env:FOLDERSPLITTER_QA_LOG_DIR "update-notice.log"
dotnet tools/PrivateDesktopQa/bin/Release/net10.0-windows/PrivateDesktopQa.dll --log $log --timeout 120 -- (Get-Command dotnet).Source tests/QuadExplorer.UiTests/bin/Release/net10.0-windows10.0.19041.0/QuadExplorer.UiTests.dll --update-notice
Get-Content $log
```

`--global-toolbar`는 실제 WinForms 도구 모음과 제목 표시줄에 창 메시지를 전달해 고정·숨김·지연 표시와 취소·재고정, 작업 영역 위 오버레이와 분할·탭·선택·사이드바·미리보기 상태, 메뉴·포커스 보호, 세션 재실행 및 키보드 조작을 검사합니다. 비입력 데스크톱에는 실제 포인터가 없으므로 지속 호버 검사에서는 Windows의 자동 제목줄 이탈 메시지만 걸러 냅니다. 네 언어와 글꼴 크기, 밝은/어두운 테마 및 보통/최소 창에서 압정 배치도 확인합니다. DPI 관련 검사는 글꼴 크기 변화에 따른 배치 회귀로, 실제 모니터 간 DPI 전환을 대신 검증했다고 주장하지 않습니다. 환경 변수를 지정하면 밝은/어두운 테마의 고정·숨김·호버 표시·재고정 화면과 압정 확대 이미지를 `global-toolbar-*.png`로 저장합니다. `--update-notice`는 격리된 가상 버전으로 알림 위치와 이동, 지속 표시·닫기·중복 방지·언어·숨김 및 가림 상태를 검사합니다. 물리 마우스 이동이나 전역 입력은 사용하지 않습니다.

전체 기존 회귀 검사는 `.\build.ps1 -UiTests`로 실행합니다. 단축키 조합은 전역 키 입력 대신 앱의 단축키 처리기를 호출하며, Enter는 실제 도구 모음의 키 처리 경로로 전달합니다. 실제 모니터 간 100/150/200% DPI 이동, 물리 입력을 통한 단축키와 보조 기술의 읽기 순서는 별도 수동 QA 대상입니다. 아래는 이 기능과 관계없는 기존 버전 검증 이력입니다.

# Folder Splitter 0.6.0 검증 기록

Windows 11 build 26100, .NET SDK 10.0.402에서 다음 명령이 종료 코드 0으로 완료됐습니다.

```powershell
.\build.ps1 -UiTests -Publish
```

- 엄격한 Release 빌드와 자체 런타임 포함 게시 성공.
- 설정·분할 모델 31개, 자체 색인 158개 assertion, 네이티브 UI 145개 검사 통과.
- 실제 Explorer 즐겨찾기 20개 읽기 및 정규화·중복 제거 검사 통과.
- 실제 게시 앱에서 한글 부분 경로 후보와 전체 경로 표시, Enter 폴더 이동, 파일 후보 클릭 후 부모 폴더의 실제 파일 선택을 확인.
- 초기 색인 준비, 슬래시·공백 변형, 결과 없음, 지연된 이전 응답, 삭제된 후보, 비활성 백엔드, 빈 영역 첫 파일 선택 검사 통과.
- 실제 관련 없는 파일 생성 후에도 선택 중인 후보 목록과 선택 행이 유지되는 회귀 검사 통과. 수정 전 같은 검사는 실패했습니다.
- 기존 네 언어·탭·분할·즐겨찾기·버전 푸터 검사를 유지.

자체 엔진은 372만 공통 후보 비교에서 p95 62.13ms로 사전 기준을 통과했습니다. Everything IPC는 벤치마크 도구에만 있고 제품은 Everything 설치를 요구하지 않습니다. 실제 REA 분석과 성능·커버리지·실시간 비교 한계는 [측정 기록](search-evidence/benchmark.md), 수치는 [JSON](search-evidence/benchmark-results.json)에 있습니다. 실시간 전체 결과 비교 중 파일 변동으로 실패한 실행도 해당 기록에 명시했습니다.

실제 화면 증거는 비공개 `qa-workspace/search-ui-evidence/`에 보관합니다. `published-candidates.png`, `published-folder-enter.png`, `published-file-selected.png`는 게시 실행 파일을 OS 자동화로 조작한 화면입니다. 파일 선택은 화면의 실제 선택 행과 네이티브 `CF_HDROP` 회귀 검사 양쪽으로 확인했습니다.

아래는 이전 0.5.2 기준선의 검증 이력입니다.

## 0.5.2 기준선

검증 환경: Windows 11 x64, OS build 26100, .NET SDK 10.0.402.

## 완료

실행 명령:

```powershell
.\build.ps1 -UiTests
.\build.ps1 -Publish
```

- Release 빌드: 성공. 컴파일 오류와 경고 없음.
- 설정·분할 모델 회귀 테스트: 31/31 통과.
- 실제 Windows Shell 통합 검사: 125/125 통과.
- 탐색기 즐겨찾기 가져오기 검사: 고정 항목 필터, 정규화, 중복 제거, 상한과 실제 읽기 확인.
- 독립 실행형 Windows x64 패키지 생성: 성공.
- 배포용 `FolderSplitter.exe` 직접 실행 및 실제 화면 확인: 성공.
- 제품명 `Folder Splitter`와 앱 아이콘 확인: 성공.

통합 검사 범위:

- 네 영역의 Shell 보기 생성과 독립적인 초기 폴더.
- 초기 네 영역과 새 탭에서 네이티브 드롭 대상 등록.
- 한글 폴더 탐색, 뒤로/앞으로/상위 폴더 이동.
- 새 탭, 탭 닫기, 닫은 탭 복원.
- 보기 방식 전환과 새로고침.
- 1/2/3/4분할과 숨긴 영역의 탭 보존.
- 3영역 좌우·상하 2:1 배치, 가장자리 분할, 가운데 이동, 빈 영역 접기.
- 5번째 영역 거부 시 탭과 배치 보존.
- 희소 영역 ID, 분할 트리와 비율 저장·복원, 숨긴 Shell 보기 해제.
- 즐겨찾기 접기·펼치기와 접힌 상태 저장.
- 빈 분할 영역, 마지막 탭 닫기, 빈 영역 주소 열기와 중복 없는 탭 이동.
- 사이드바 전체 접기, 폭 보존, 즐겨찾기 전체 제거와 선택 복원.
- 도구 모음 항목의 실제 글자 높이와 컨트롤 높이 비교.
- 주소창·상태줄의 선호 높이와 탐색 아이콘 열의 선호 너비 검사.
- 아래쪽 Pane 3·4 상태줄이 화면 작업 영역 안에 들어오는지 검사.
- 한국어·영어·중국어 간체·일본어 전환과 다음 실행 언어 저장.
- 언어 전환 시 Shell 보기·탭·이동 이력·입력 중인 경로 보존.
- 각 언어의 탐색 툴팁, 문맥 메뉴, 도움말과 번역 키 포맷 검사.
- 언어 선택이 탭 닫기 왼쪽에 놓이고 최소 창 크기에서도 겹치지 않는지 검사.
- 즐겨찾기와 빠른 접근 항목을 선택한 상태에서 실제 드롭다운 키 입력으로 언어를 전환하고 선택 상태를 보존하는지 검사.
- 창 왼쪽 아래의 버전이 실행 어셈블리와 일치하고 사이드바를 숨겨도 잘리지 않는지 검사.

- 최소 창 크기 900×600에서 영역 배치.
- 종료 시 설정 저장과 비활성 영역 경로 보존.

언어 변경 오류는 수정 전 동일한 회귀 검사에서 `ItemArray.EntryEnumerator.MoveNext → MainForm.ReloadPlaces` 예외로 재현했습니다. 선택 복원 시 목록 열거자를 사용하지 않도록 수정했습니다.

수동 화면 확인:

- 실제 마우스로 탭을 오른쪽 가장자리로 드래그하여 1영역에서 2영역으로 분할.
- 즐겨찾기 제목을 눌러 목록만 숨기는 동작.
- 새 세션에서 탐색기 고정 폴더 20개를 가져오는 동작.
- 주소창 옆 폴더 버튼으로 기본 선택 창을 열고 고른 샘플 폴더가 빈 영역에 열리는 동작.
- 한국어 `1 분할 / 2 분할 / 3 분할 / 4 분할` 표기와 언어 선택 위치 확인.
- 최신 실행 파일의 작업표시줄 고정 및 바로가기 대상 경로 확인.
- 배포 ZIP에 한국어·영어·중국어 간체·일본어 README 포함 확인.
- 로컬 배포 출력은 `artifacts/FolderSplitter`와 최신 ZIP만 유지.
- 네 영역 모두 실제 파일 목록, 주소창, 독립 탭 표시.
- 활성 영역 표시, 탭 닫기 버튼, 빠른 접근과 즐겨찾기 표시.
- 패키지 실행 시 별도 런타임 설치 요구 없이 시작.
- Ctrl+C/Ctrl+V로 영역 간 파일 복사, 원본과 대상 내용 일치 확인.
- F2로 복사한 파일 이름 변경, 변경 후 내용 보존 확인.
- Ctrl+T, Ctrl+W, Ctrl+Shift+T로 탭 생성·닫기·복원 확인.
- 실제 마우스 드래그로 영역 간 파일 이동: 사용자가 직접 성공을 확인했고,
  이후 대상 영역에 파일이 나타나고 원본 영역에서 사라진 화면을 확인.

## 수정한 문제

초기 Shell 보기 생성 전에 OLE가 초기화되지 않아 드롭 대상 등록이 누락됐습니다.
`Application.OleRequired()`를 보기 생성 전에 호출하도록 수정했습니다.
같은 등록 검사가 수정 전 실패하고 수정 후 통과하는 것을 확인했습니다.

자동 입력 도구의 드래그 결과는 성공 근거로 사용하지 않았습니다.
정식 회귀 검사는 실제 보기의 등록 상태를 확인하고, 드래그 파일 이동은 위의
실사용 결과로 검증했습니다.

## 별도 검증하지 않은 환경

Dropbox/OneDrive 동기화 상태, 외부 Shell 확장과 네트워크 공유는 별도 검증하지 않았습니다.
탐색기 고정 여부는 Windows 11 build 26100의 `System.Home.IsPinned` 속성으로 확인합니다.
이 속성을 제공하지 않는 항목은 경고와 함께 건너뛰며 최근 사용 파일을 즐겨찾기로 잘못 가져오지 않습니다.
탐색기 가져오기는 읽기 전용이며 탐색기의 고정·해제 동작을 호출하지 않습니다.

## 테스트 데이터

자동 검사는 독립적인 임시 폴더를 만들고 종료 시 해당 폴더만 정리합니다.
수동 검사에는 Git에서 제외된 `qa-workspace/`의 합성 파일만 사용합니다.
사용자의 기존 문서나 실제 업무 파일을 변경하지 않습니다.
