# SearchBench

Windows-only benchmark harness for Folder Splitter's independent path index and Everything's native IPC interface. It does not install, launch or configure Everything. Use a separately configured instance when comparing accessible folder traversal with Everything's privileged NTFS index.

```powershell
dotnet run --project tools/SearchBench -c Release -- build --root C:\ `
  --store C:\Bench\paths.fsix --report C:\Bench\build.json
dotnet run --project tools/SearchBench -c Release -- coverage `
  --store C:\Bench\paths.fsix --instance fsbench --report C:\Bench\coverage.json
dotnet run --project tools/SearchBench -c Release -- parity `
  --store C:\Bench\paths.fsix --instance fsbench --seed 42 --report C:\Bench\parity.json
dotnet run --project tools/SearchBench -c Release -- snapshot-parity `
  --store C:\Bench\paths.fsix --instance fsbench --seed 42 --report C:\Bench\snapshot-parity.json
dotnet run --project tools/SearchBench -c Release -- compare `
  --store C:\Bench\paths.fsix --instance fsbench --seed 42 --limit 100 `
  --warmup 3 --passes 5 --order abba `
  --priority-root C:\Work --priority-root C:\Users --priority-root C:\ `
  --report C:\Bench\compare.json
dotnet run --project tools/SearchBench -c Release -- timing `
  --store C:\Bench\paths.fsix --seed 42 --limit 100 --warmup 3 --passes 5 `
  --priority-root C:\Work --priority-root C:\Users --priority-root C:\ `
  --report C:\Bench\timing.json
dotnet run --project tools/SearchBench -c Release -- update `
  --root C:\Bench --instance fsbench --report C:\Bench\update.json
dotnet run --project tools/SearchBench -c Release -- restart --report C:\Bench\restart.json
dotnet run --project tools/SearchBench -c Release -- smoke `
  --instance fsbench --report C:\Bench\smoke.json
```

Both APIs receive the same query, ordered priority roots and limit. Queries include seeded leaf fragments, case variants, partial paths, Korean, broad matches and a verified absent term. `--queries` accepts a private UTF-8 file with one query per line, optionally prefixed by a category and tab.

`timing` runs the same queries, contexts and passes as `compare` on the independent engine alone, for re-measuring after engine changes without an Everything instance.

`compare` filters the Everything candidates to the independently accessible corpus **before** ranking and truncation. This removes privileged-only entries and namespace aliases, without pretending that the independent scanner has equal privileged coverage. Filtering and IPC materialization costs remain in the reference timing. Every operation must produce the same ordered path/type digest or the command fails. A comparison report is not emitted on failure.

`coverage` reports unique path/type counts and denied/excluded scopes. `parity` checks complete result sets before truncation. These are distinct from ranked-candidate parity and must be read separately. A live filesystem can change between observations.

`snapshot-parity` is a separate test: freeze the reconciled independent index and one native corpus, report initial coverage differences, then validate actual `FileIndex.Search` full results on their common typed paths. Coverage differences above 1% fail qualification. This avoids confusing matching correctness with files changing during a long test. It does not replace strict live `parity` or prove identical raw coverage; native live query and update behavior need separate evidence.

Cold build, snapshot decode, memory and watcher-update measurements are separate from warmed query timings. Snapshot decode excludes the startup reconciliation that runs after cached readiness. Build/load private bytes are process measurements: the load measurement still holds the build's object graph and is not steady-state application memory. Parallel-query current-thread allocation measurements in the owner tests do not include worker allocations.

Keep benchmark output private: coverage reports contain missing-path samples and failed parity runs retain mismatch details. Scratch data is removed on success; retained mismatch paths are printed for diagnosis and must be removed after inspection. Point `TMP`, `TEMP` and `TMPDIR` at an owned run directory. Never commit snapshots, path inventories or raw research queries.
