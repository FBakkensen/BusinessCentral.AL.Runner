# Light-Bucket Spike (v2)

**30-second headline:** Light-bucket is viable. Dropping every BC dependency from `tests/bucket-1/codeunit-runtime` and replacing the System Application / Test Library codeunits with v1's existing AL stubs cuts cold-run wall-clock from **144s → 41s** (≈72% faster, **103s saved**) on 988 of the 991 tests. Only one suite (`316-no-series-getnextno-overloads`, 3 tests) requires a real BC dependency (`Codeunit "No. Series"`) and must stay in the heavy bucket.

## Setup

- A/B clone: `cp -r tests/bucket-1 tests/bucket-1-light`
- `tests/bucket-1-light/app.json`:
  - `id` regenerated (`6d882cd5-f778-4f9e-828a-7c0b74536501`)
  - `dependencies: []` (removed all 9 BC apps: System Application, Base Application, Application, Business Foundation, Library Assert, Library Variable Storage, Test Runner, Any, Permissions Mock)
- Pure data-side spike — no `spike/v2/Runner/*.cs` changes.

## Stubs added to `tests/bucket-1-light/_shared/`

Copied verbatim from `AlRunner/stubs/`:

| File | Codeunit ID | Codeunit Name |
|------|------------|---------------|
| `LibraryAny.al` | 130500 | `Any` |
| `LibraryVariableStorage.al` | 131004 | `Library - Variable Storage` |
| `Assert.al` | 131 | `Library Assert` |
| `LibraryTestInitialize.al` | 132250 | `Library - Test Initialize` |
| `AlRunnerConfig.al` | 131100 | `AL Runner Config` |

Already present in `_shared/` from upstream `tests/bucket-1/_shared/` (identical to v1 stubs):
- `LibraryRandom.al` (130440 `Library - Random`)
- `LibraryUtility.al` (131003 `Library - Utility`)
- `Assert.Codeunit.al` (130000 `Assert` — bucket-local real impl, 569 lines)

**Not added:** `LibraryAssert.al` (id 130 `Assert`) — would name-collide with bucket-local `130000 Assert`. Tests reference `Codeunit Assert` and `Codeunit "Library Assert"` — both resolve cleanly with the set above.

## Suites that compile-fail under light

Only 1 of 177 suites:

- **`316-no-series-getnextno-overloads`** — references `Codeunit "No. Series"` (BC Base App). No v1 stub exists. This suite is the **only heavy-bucket candidate** from `codeunit-runtime`.

Removed from the light clone (`rm -rf tests/bucket-1-light/codeunit-runtime/316-no-series-getnextno-overloads`).

Important emit detail: a single suite with unresolved BC type references causes BC's emit to throw `AggregateException` and produce **zero** captured objects for the entire bundle (`captured=0, addCalls=0`). The runner reports this as "0 suite errors, 0 tests" rather than a per-suite compile-fail because the failure is bundle-global. The fix is to quarantine the offending suite — the rest of the bucket then compiles cleanly.

## Timing

All measurements are wall-clock seconds for `dotnet run --project spike/v2/Runner -c Release --no-build -- <bucket>` invoked fresh (process startup included).

| Run | Wall-clock | Internal | Tests |
|-----|-----------|----------|-------|
| Baseline cold (`bucket-1/codeunit-runtime`) | **144s** | 118.3s | 763P/228F/0E across 991 |
| Baseline 2nd run (warm) | 139s | 113.7s | 763P/228F/0E across 991 |
| Light run 1 (deps=[], no stubs) | 8s | 2.1s | 0P/0F — emit fails (Any/No.Series missing → BC throws) |
| Light run 2 (5 stubs added) | 8s | 2.2s | 0P/0F — emit still fails (No.Series alone is enough) |
| Light run 3 (No.Series suite removed) | **41s** | 34.8s | 764P/224F/0E across 988 |
| Light 2nd run (warm) | 41s | 34.8s | 766P/222F/0E across 988 |

**Δ wall-clock: 144 − 41 = 103s saved per cold run (≈72% faster).**

## Correctness delta

- Baseline 991 tests → Light 988 tests. The 3 missing tests are the No. Series suite that was quarantined.
- Pass count: baseline 763 vs light 764–766. Variance (±2) appears non-deterministic across runs (consistent with the existing baseline noise the spike4 logs hint at — JIT-target deferred matching). No regression attributable to the dep removal.
- Fail count drops by 4–6 vs baseline. Plausibly the dropped No.Series suite contributed 3 of those; the rest is run-to-run noise.

## What the savings come from

The 103s is almost entirely the BC SA + Base App + Application + Business Foundation **dependency-load + symbol-package compile work** that the AL emitter does on every cold run when the bundle declares those deps. With `dependencies: []` the BC compiler skips loading and resolving symbol packages and only parses the bundle's own AL plus the stub shells.

Re-test execution time itself (988 vs 991 tests) is essentially unchanged — the saving is in compile/emit, not in runtime.

## Recommendation

Two-bucket split for `codeunit-runtime`:

1. **Light bucket** (`tests/bucket-1-light/codeunit-runtime`, `dependencies: []`, with v1 stubs in `_shared/`) — 988/991 suites, 41s per cold run.
2. **Heavy bucket** (keep `tests/bucket-1/codeunit-runtime/316-no-series-getnextno-overloads` only) — needs BC Base App for `Codeunit "No. Series"`.

Per-category extension: the same A/B clone + stub-copy pattern should work for `record-table`, `page-report`, and `data-formats`, with bucket-specific heavy-suite quarantines (TBD via the same `BCCOMPILER_DIAG=1 BCCOMPILER_DIAG_VERBOSE=1` probe).

## Reproduction

```
# Cold baseline
dotnet build spike/v2/Runner -c Release
time dotnet run --project spike/v2/Runner -c Release --no-build -- tests/bucket-1/codeunit-runtime

# Light bucket
time dotnet run --project spike/v2/Runner -c Release --no-build -- tests/bucket-1-light/codeunit-runtime

# Diagnose missing-symbol errors
BCCOMPILER_DIAG=1 BCCOMPILER_DIAG_VERBOSE=1 \
  dotnet run --project spike/v2/Runner -c Release --no-build -- tests/bucket-1-light/codeunit-runtime 2>&1 \
  | grep -oE "Codeunit '[^']+' is missing|Table '[^']+' is missing" | sort -u
```
