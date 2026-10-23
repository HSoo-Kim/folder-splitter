# Partial-path search backend decision

Measured October 21, 2026 on Windows 11 build 26100, Ryzen 9 9950X (16 physical cores), .NET SDK 10.0.402. Product decision: **independent index**, without an Everything runtime dependency.

## Re-measurement for 0.7.17 (2026-10-23)

After 0.7.15 and 0.7.16 changed the engine (index exclusions for Windows, ProgramData, each profile's AppData, the recycle bin and System Volume Information; one packed string per folder; compacting collection after full scans), the independent engine was re-timed with the new `SearchBench timing` command. It uses the same query generator, seed 42, limit 100, six priority roots, three warmup passes and five measured passes as `compare`, but times only the independent engine. No isolated Everything instance was running, so **no new Everything comparison was made**; the table below must not be compared ratio-for-ratio with the October 21 table, whose corpus was larger (3.72 million entries, no exclusions).

Corpus: 2,298,670 entries, 6 denied directories. Fresh build 25.8 s, persisted snapshot load 0.25 s.

| Query category | Samples | p50, ms | p95, ms |
|---|---:|---:|---:|
| Leaf fragments | 60 | 9.42 | 22.16 |
| Case variants | 60 | 9.36 | 22.01 |
| Partial paths | 40 | 11.54 | 12.74 |
| Korean path | 5 | 16.08 | 16.47 |
| Broad terms | 15 | 32.80 | 95.32 |
| Absent term | 5 | 8.00 | 8.10 |
| **Overall** | **185** | **9.62** | **31.02** |

Overall p99 was 94.6 ms. Broad terms remain the slowest category; their p95 is a single maximum among 15 samples.

## Gate declared before measurement

Keep the independent engine when warm-query p95 is at most 100 ms and at most `max(3 * Everything p95, Everything p95 + 25 ms)`, with correct shared-scope results. Report coverage, cold startup, memory and updates separately.

## Qualified comparison

37 seeded queries: 12 leaf fragments, 12 case variants, 8 partial paths, 1 Korean path, 3 broad terms and 1 absent term. Six ordered current/open/favorite/global contexts, limit 100, three warmup passes, five measured passes, ABBA order. There were 370 measured calls per backend; every operation, including warmups, checked ordered path/type digest equality.

The accessible shared corpus had **3,724,094 entries**. Both actual `QueryAsync` implementations received the same input, priority roots and limit, including materialization and ranking. Everything used native name/path literal searches, not forced full-path regex matching. Reference candidates were filtered to the independently accessible corpus **before** ranking/truncation; this filtering cost remains in its timing.

| Query category | Independent p95, ms | Everything adapter p95, ms |
|---|---:|---:|
| Leaf fragments | 22.37 | 168.24 |
| Case variants | 22.28 | 169.71 |
| Partial paths | 30.36 | 162.67 |
| Korean path | 40.31 | 141.53 |
| Broad terms | 68.79 | 7.13 |
| Absent term | 18.97 | 40.57 |
| **Overall** | **62.13** | **164.62** |

Overall p50 was 21.10/48.38 ms and p99 was 68.08/220.79 ms, respectively. The independent engine passed both declared gates. Its broad searches were slower; this result does not claim universal superiority over Everything or measure Everything's intrinsic matcher in isolation. The adapter performs ordered scope queries, IPC transfer and shared ranking.

The independent index uses immutable directory/name arrays, bounded top-K ranking and four scan partitions on sufficiently large corpora. It does not recover or reproduce Everything's proprietary matcher. See [actual REA analysis](rea-analysis.md) for the binary findings and explicit unknowns.

## Coverage, startup and memory

An isolated **Everything 1.4.1.1032** folder-index instance scanned the same C volume under ordinary user permissions. Its configuration and database were separate from the user's running installed instance and service. Privileged NTFS coverage is not equivalent to managed accessible traversal.

A qualifying coverage observation counted 3,721,709 independent and 3,721,696 reference entries after exclusions: 13 live-filesystem differences (0.00035%), below the declared 1% qualification bound. Raw counts were 3,724,279 and 4,606,589, with 524 denied directories and 2,571 excluded scopes. Raw reference counts include namespace aliases and must not be compared as unique accessible coverage.

The corrected cold build contained 3,724,083 entries with 524 denied directories:

- Fresh managed traversal: **50.44 seconds**.
- Persisted snapshot IO/decode: **740.33 ms**, excluding background reconciliation.
- Process private bytes after build: **499,347,456**.
- After decoding a second index in the same benchmark process: **839,970,816**. Both object graphs were retained; this is not steady-state application memory.

Directory-name notifications update snapshots without blocking the UI. Startup exposes saved results first, then reconciles offline changes. A dedicated restart fixture verified persisted readiness plus offline create, rename and delete; reconciliation took 9.99 ms for three entries, not for the full volume.

Actual create/rename/delete visibility took 6.91/5.63/1.38 ms for the independent fixture and 690.82/1268.71/851.25 ms for the folder-index reference. Independent completion used `IndexChanged`; Everything completion used completed IPC observations, without fixed sleeps. These are three observations, not latency percentiles.

Cloud-directory traversal was corrected after a real full-volume comparison exposed an omitted subtree. Directory enumeration can report cloud reparse attributes without a namespace link target. Such directories are traversed; actual junctions and symbolic links are not followed. Existing real junction, denied-ACL, long-path and mutation regressions still pass.

## Reproduction and rejected measurements

Commands and output/privacy handling are documented in [SearchBench](../../tools/SearchBench/README.md). The qualified command used `compare --seed 42 --limit 100 --warmup 3 --passes 5 --order abba` and six `--priority-root` values. Private inventories and raw research queries are not committed.

An earlier sequential prototype measured p95 191.05 ms, but its reference query results included excluded namespace aliases. It was rejected as acceptance evidence. The harness now fails on ranked result mismatch. The final run above used the partitioned implementation and corrected reference queries; no concurrent heavy tests ran during timing.

A strict live full-result run failed with 716 aggregate differences: initial coverage differed by 1 missing/55 extra entries, and files changed during subsequent broad queries. Inspection found active application caches, new repository objects/documents, temporary files and OS resources. This run is not reported as zero-error. `snapshot-parity` separately freezes both corpora to test matching on their common typed paths, while continuing to report and qualify initial coverage differences. Exact-ranked live parity and real mutation visibility remain separate checks.

The frozen full-corpus run passed all 37 queries with **zero matching differences on 3,721,493 common entries**. Initial independent/reference coverage was 3,721,509/3,721,498, with 5 reference-only and 16 independent-only entries (524 denied directories, 2,570 excluded scopes). Those coverage differences remain reported; the test does not label them matching failures or claim identical raw coverage.
