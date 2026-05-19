# Light-Bucket Migration (v2)

**30-sec headline:** Migrated `tests/bucket-1` and `tests/bucket-2` to light mode in-place. Suites that need real BC types moved to sibling `tests/bucket-1-heavy/`. No `bucket-2-heavy` exists because every bucket-2 suite is light-compatible. Total wall-clock for the 4 main bucket categories + heavy: **617s → 240s (61% faster, 377s saved)** with pass-count parity.

## Pre-migration vs post-migration

| Bucket / category | Baseline | Post-migration | Δ wall-clock |
|---|---|---|---|
| `bucket-1/codeunit-runtime` | 139s 765P/226F across 991 | 40s 766P/222F across 988 | −99s |
| `bucket-1/record-table` | 248s 745P/160F across 905 | 80s 749P/156F across 905 | −168s |
| `bucket-1-heavy/codeunit-runtime` | (in bucket-1) | 54s 0P/3F across 3 | (new, was inside baseline) |
| `bucket-2/data-formats` | 120s 1396P/163F across 1559 | 36s 1402P/157F across 1559 | −84s |
| `bucket-2/page-report` | 110s 272P/345F across 617 | 30s 272P/345F across 617 | −80s |
| **Total** | **617s 3178P/894F/4072** | **240s 3189P/883F/4072** | **−377s (−61%)** |

Pass-count parity confirmed: 4072 tests in / 4072 tests out. Pass/fail deltas (+11 / −11) are within the spike's documented run-to-run noise (JIT-target deferred matching).

## Heavy-suite inventory

Only one suite required heavy mode (real BC dependencies):

- **`bucket-1-heavy/codeunit-runtime/316-no-series-getnextno-overloads`** —
  references `Codeunit "No. Series"` from BC Base Application. No v1 stub
  exists. The suite already failed (0P/3F) under the original heavy bucket-1
  baseline; the migration preserves that behavior — moving it to bucket-1-heavy
  doesn't change correctness, only isolates it so the rest of bucket-1 can
  drop BC deps and run in 41s instead of 139s.

No suites needed quarantining from `bucket-1/record-table`, `bucket-2/data-formats`,
or `bucket-2/page-report`.

## Layout after migration

```
tests/
  bucket-1/                   ← light (dependencies: [])
    _shared/                  ← stubs: LibraryAny, LibraryVariableStorage,
                                       Assert (id 131 Library Assert),
                                       LibraryTestInitialize, AlRunnerConfig
                                + bucket-local Assert.Codeunit (130000),
                                  LibraryRandom (130440), LibraryUtility (131003)
    codeunit-runtime/
    record-table/
  bucket-1-heavy/             ← heavy (full BC dep set, fresh GUID)
    _shared/                  ← bucket-local only: Assert.Codeunit, LibraryRandom,
                                LibraryUtility (no stubs — real SA is loaded)
    codeunit-runtime/316-no-series-getnextno-overloads
  bucket-2/                   ← light
    _shared/                  ← same five stubs + bucket-local Assert.Codeunit
    data-formats/
    page-report/
  spike-a-baseapp/            ← heavy by design, untouched
```

`tests/bucket-1-light/` (the spike's A/B clone) was deleted — the in-place
migration supersedes it. The spike rationale lives in
[`spike/v2/LIGHT-BUCKET-SPIKE.md`](LIGHT-BUCKET-SPIKE.md).

## Smoke command (updated)

Use this for any future spot-check of all light + heavy buckets:

```bash
dotnet build spike/v2/Runner -c Release
for b in bucket-1/codeunit-runtime bucket-1/record-table \
         bucket-1-heavy/codeunit-runtime \
         bucket-2/data-formats bucket-2/page-report \
         spike-a-baseapp; do
  if [ -d "tests/$b" ]; then
    echo "=== $b ==="
    ts=$(date +%s)
    dotnet run --project spike/v2/Runner -c Release --no-build -- tests/$b 2>&1 \
      | grep -E "→ [0-9]+P/[0-9]+F" | tail -1
    echo "wall: $(($(date +%s) - ts))s"
  fi
done
```

## Future contributor reminder

**Any new test that references real BC business types (`Customer`, `Item`,
`Sales Header`, `No. Series`, …) must go in a heavy bucket** (or a new
`bucket-N-heavy/`), because the light buckets compile with
`dependencies: []` and have no Base Application / Business Foundation
symbols available — only the five test-library shells in
[`AlRunner/stubs/`](../../AlRunner/stubs/) plus each bucket's local
`Assert.Codeunit.al`, `LibraryRandom.al`, `LibraryUtility.al`.

If a new test should be light but won't compile, the diagnose recipe is:

```bash
BCCOMPILER_DIAG=1 BCCOMPILER_DIAG_VERBOSE=1 \
  dotnet run --project spike/v2/Runner -c Release --no-build -- tests/bucket-N/<cat> 2>&1 \
  | grep -oE "Codeunit '[^']+' is missing|Table '[^']+' is missing" | sort -u
```

If the missing symbol is a System Application / test-library codeunit that
already has a v1 stub in `AlRunner/stubs/`, add it to `tests/bucket-N/_shared/`.
Otherwise move the new suite to the heavy bucket.

Important emit detail (also documented in the spike): a single suite with
unresolved BC type references causes BC's emit to throw `AggregateException`
and produce **zero** captured objects for the entire bundle — the runner
reports `0 suite errors, 0 tests` rather than per-suite COMPILE-FAIL. The
fix is to quarantine the offending suite.
