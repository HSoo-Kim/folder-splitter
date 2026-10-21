# Everything executable analysis with REA

Observed October 21, 2026. Actual released `rea-agents@6.7.0` inspected real PE files and decompiled functions through its native Windows Ghidra provider. This is executable evidence, not a reconstruction from documentation or strings alone.

## Targets and prerequisites

The live [official downloads page](https://www.voidtools.com/downloads/) reported stable **1.4.1.1032** and beta **1.5.0.1424b**. The downloaded beta PE's `FileVersion` and `ProductVersion` both reported `1.5.0.1424b`. This supersedes the earlier 1423b observation.

| Role | Target | SHA256 |
| --- | --- | --- |
| Primary, isolated beta x64 | `Everything-1.5.0.1424b.x64.zip` -> `Everything.exe`, 5,146,280 bytes | `4c994da9c376ce264fb4f0b8a46d76abad3ce160d0cb8aa85883867ebc725fd4` |
| Stable x64 control, not running user instance | `C:/Program Files/Everything/Everything.exe`, 1.4.1.1032 | `f191f756996a14a11e5445fa7103d302efd510cf2fbf920e6c0c8ed51d512e36` |
| Running stable x86, identity only | `C:/Program Files (x86)/Everything/Everything.exe`, 1.4.1.1032 | `d38f48dd60de6b10f9a132718ccbfddc171a7a7d1cc88ef2e4ce73c0c43dcd5f` |

Beta archive: [official portable download](https://www.voidtools.com/Everything-1.5.0.1424b.x64.zip). Archive SHA256 `f8e5d907305fc1aff288ad4d722548f36a1ef8c3389933b1fbc935dba7083084` matched the [publisher checksum file](https://www.voidtools.com/Everything-1.5.0.1424b.sha256). Full URLs, roles, sizes, versions and prerequisite checksums are in `rea-targets.json`.

Portable Ghidra 12.1.4 and full Temurin JDK 21.0.12.1+1 were downloaded into an owned `fs.mkdtemp` root; Node 24.14.0 was already present. REA was installed with `npm install --prefix <OWNED_ROOT>/rea rea-agents@6.7.0`, not globally. Its actual shipped CLI help and live MCP input schemas determined invocation syntax.

`rea doctor --format json` reported Ghidra available, version/JDK/native decompiler healthy, and verified native Job Object, private DACL and reparse-safe handle controls. Audit-wide doctor remained unhealthy only for unrelated unconfigured providers, agent registration and REA skill installation. No setup registration was performed.

## Actual inspection

Both commands completed with exit 0:

```powershell
& $node $rea inspect "$root/beta/Everything.exe" --provider ghidra --format json
& $node $rea inspect 'C:/Program Files/Everything/Everything.exe' --provider ghidra --format json
```

| Target | REA source Evidence ID | Image base | Analyzed procedures | Defined strings |
| --- | --- | --- | --- | --- |
| Beta x64 | `ev_16afda598247b7b93fd2c005673f15529bdc938d985e03271b5b12ddab90205f` | `0x140000000` | 11,446 | 12,875 |
| Stable x64 | `ev_74e9639a77ca5de26a5d9d56aae907351a7040b85fb4e3dfc2e9e9399ddb954e` | `0x140000000` | 3,070 | 4,840 |

REA reported `import_mode: ephemeral-source-immutable`, `analyzer_preset: ghidra-default` and completed default auto-analysis before serving results. No expensive analyzer was disabled. Counts include the provider's local/external function inventory and defined-string inventory; they are not source-code function counts or a measure of performance.

## Proven native indexing and update mechanisms

All addresses below belong to the beta hash above. `FUN_*` names are Ghidra names, not recovered source symbols. `rea-functions.json` preserves actual complete pseudocode for six focused dossiers, procedure body ranges and direct callers/callees. `rea-calls.json` preserves exact reference queries and results.

1. **Real filesystem controls pass through a common wrapper.** `analyze_function({"procedure":"0x1401c1470"})` produced a 232-byte function whose pseudocode calls imported `KERNEL32.DLL::DeviceIoControl` (`EXTERNAL:0x6c`). It checks input/output lengths against `0x100000000`, forwards the control code and buffers, reports `GetLastError`, and copies the returned byte count. `procedure_callers` found 18 direct callers of this wrapper.

2. **NTFS volume geometry is queried.** Function `0x1401a6710` calls `0x1401c1470` with control code `0x90064`, a `0x60`-byte output buffer, and reads offset `0x30`, logged as `BytesPerFileRecordSegment`. It logs `FSCTL_GET_NTFS_VOLUME_DATA failed %u` on failure. String `0x140435000` has code xref `0x1401a6775`, inside this function. Microsoft `winioctl.h` identifies `0x90064` as `FSCTL_GET_NTFS_VOLUME_DATA`.

3. **USN journal creation, querying and deletion exist in executable code.** Function `0x1401b72d0` constructs two 64-bit input values and passes a 16-byte buffer with `0x900e7` (`FSCTL_CREATE_USN_JOURNAL`) to the wrapper. Function `0x1401b7330` queries with `0x900f4` (`FSCTL_QUERY_USN_JOURNAL`, `0x38`-byte output), then on success passes the returned journal identifier and a flag value of 1 in a 16-byte input to `0x900f8` (`FSCTL_DELETE_USN_JOURNAL`). This second function is journal deletion management, not the journal-reading loop.

4. **A USN-associated update handoff is visible.** Code xref `0x140097a91` to string `_db_monitor_ntfs_process_usn_records` resolves to function `0x140097a50`. Its decompilation acquires/releases a critical section, tests a state pointer, calls `0x14016dc90` and `0x1400a4bc0` with callback `0x1400959e0`, then initializes another path labelled `_db_monitor_ntfs_process_fd_update_events_thread_proc` through `0x1401b61f0` and callback `0x1400958d0`. Synchronization and callback transitions are observed; the callbacks' record-processing semantics were not decompiled.

Observed static call chain:

```text
0x1401a6710 --0x90064--> 0x1401c1470 --> KERNEL32.DLL::DeviceIoControl
0x1401b72d0 --0x900e7--> 0x1401c1470 --> KERNEL32.DLL::DeviceIoControl
0x1401b7330 --0x900f4,0x900f8--> 0x1401c1470 --> DeviceIoControl
```

Control-code names were cross-checked against Microsoft's [winioctl.h](https://raw.githubusercontent.com/microsoft/win32metadata/main/generation/WinSDK/RecompiledIdlHeaders/um/winioctl.h), not inferred from nearby strings. These observations support a privileged NTFS/USN indexing/update component. They do not prove a complete indexing algorithm, its startup cost, or the caller's permission boundary.

## Search matching: what the binary establishes

The provider import search found `FindFirstFileW`, `FindNextFileW`, `GetVolumeInformationW`, `LCMapStringW/A`, `ReadFile` and `DeviceIoControl`. Imports establish available dependencies, not which path supplies each search result.

String `match_path` at `0x14043b740` has xrefs `0x140241da0`, `0x1402425e7` and data reference `0x1404a0a70`; `match_case` at `0x14043b768` has corresponding code/data references. Containment queries resolved code to `0x1402411d0` and `0x140242550`.

Decompiled `0x140242550` serializes separate fields from a query/tab state structure:

```c
FUN_140241100(param_1,"search",*(undefined8 *)(param_2 + 8));
FUN_1402410c0(param_1,"match_case",(int)*(char *)(param_2 + 0x17c));
FUN_1402410c0(param_1,"match_path",(int)*(char *)(param_2 + 0x17e));
FUN_1402410c0(param_1,"match_whole_word",(int)*(char *)(param_2 + 0x17d));
FUN_1402410c0(param_1,"match_regex",(int)*(char *)(param_2 + 0x17f));
```

The same function also writes diacritic, prefix, suffix, punctuation and whitespace flags. **This is serialization/state evidence, not the matcher.** Exact substring comparison, slash normalization, Unicode case folding, wildcard/regex execution, ranking and query-index data structures remain unproven. An independent implementation must establish those semantics and performance with its own tests and benchmark, rather than claim they were recovered here.

## Precise limits and failures

- The NTFS file-record diagnostic at `0x1404355a0` has code xref `0x1401a7fd9`. Its containing candidate `0x1401a6f70` was actually analyzed, but the complete MCP dossier exceeded the 10,484,736-byte response budget; REA reported at least 10,538,891 bytes. This was a transport resource constraint, not absence of decompilation.
- REA retained Evidence `ev_62d46c324e8dcafbbcede9eaacdfaf2b206801b533dc02ad226935edba73f0ac` and supplied an `inspect_analysis_view` continuation. The invoked first page returned only 64 UTF-16 code units of 26,027. It does not substantiate the file-record algorithm. Exact error and continuation are retained in `rea-inspect.json`; no claim is based on unread pages.
- The `db_journal_file_read` and automatic-path-setting seed strings had empty provider xrefs. This does not establish unused or absent behavior.
- The binary references `Everything.pdb`; Ghidra reported PDB load state unknown. Original types/names may be missing. Decompiled signatures, locals and pointer offsets are observations, not source declarations.
- Static direct calls can omit unresolved indirect flow. High-pcode/value-flow output may be truncated; this report uses preserved pseudocode and direct reference evidence, not exhaustive value-flow claims.
- Windows P0 reported no-return repair disabled. Provider function-body/boundary limitations remain applicable.
- No target executable was launched for this lane. No dynamic USN read, MFT enumeration, permission test, correctness comparison or performance benchmark was performed here. Stable x64 was an inventory control, not a function-by-function comparison with beta; running x86 stable was identified but not decompiled.

## Evidence, invocations and teardown

- `rea-targets.json`: versions, roles, URLs, SHA256 and prerequisite receipt.
- `rea-inspect.json`: selected real CLI inspection results, imports, exact large-response error and partial-view coverage.
- `rea-functions.json`: six real focused decompilations, body ranges and call edges.
- `rea-calls.json`: exact MCP operation parameters and normalized xref/caller/containment results.
- `rea-invocations.txt`: installation, environment, CLI and MCP protocol invocations, including setup corrections.
- `rea-teardown.json`: owned resource inventory, process readback, root removal and installed-binary hash readback.

JSON artifacts are explicitly selected/sanitized projections, not importable canonical REA Evidence envelopes. Source Evidence IDs identify the original responses. Duplicate transport text, full assembly/high-pcode, downloaded binaries, installers and broad string inventories were not retained.

All package, temp, Java home, appdata and compilation caches were directed into the newly owned root. Finalization observed no matching owned process. The MCP terminal was no longer registered, so there is no explicit `close_binary` response receipt; the remaining owned ephemeral-project directory was removed with the rest of the root. `rea-teardown.json` records the actual final readback rather than assuming provider cleanup succeeded. Installed stable binaries were not updated or modified, because their version already matched latest stable.
