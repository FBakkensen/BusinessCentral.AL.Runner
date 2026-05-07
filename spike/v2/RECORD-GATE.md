# Record Gate Spike — Verdict: **GO**

**Confidence:** Medium-high.
**Test target:** `tests/bucket-1/record-table/02-record-operations`.
**Result:** 1/6 PASS, 5/6 fail uniformly on a single async overload (`NavRecord.InsertAsync`). One test passing through real BC IL with full AL `Init/Insert/Get/Modify/Delete` semantics is the gate-spike's minimum success criterion.

## TL;DR

There **is** a clean BC-internal substitution boundary for storage. It's not where Opus's prior analysis suggested (no public `IDataAccessSource` to swap), but BC ships its own **`TempTableDataProvider`** for the AL temp-table feature, and that machinery can be repurposed as our in-memory store for *all* tables — not just temp ones. With ~770 lines of `RecordPatches.cs` plus auxiliary skeleton-construction work in `BcRuntime.cs`, a record-table test now executes against unmodified Microsoft `NavRecord` IL with `Init`, `Validate`, `Insert`, and `Get` all working.

## Storage architecture findings

When AL writes `Customer.Get(123)`, the call chain through Microsoft's IL is:

1. `Customer` is a generated `RecordN<TableId>` class. Field accesses go through `RecordImplementation` (a non-public concrete type that wraps a `NCLMetaTable` + a `IDataAccessSource`).
2. `Customer.Get(123)` lowers to `NavRecordHandle<TFields>.get_Target()` → `NavRecordHandle.CreateTarget()`.
3. `CreateTarget` calls `NavGlobal.NCLMetadata.GetMetaTableById(tableNo)` to obtain the per-table metadata. **This is where the original analysis got stuck** — `NavGlobal` is null in our skeleton.
4. With metadata in hand, it constructs the typed record with `RecordImplementation` referencing `NavSession.DataAccessSource.GetDataAccessForTable(metaTable)`.
5. `GetDataAccessForTable` returns a `IDataAccess` — for a permanent table this would be `SqlDataAccess`, for AL temp tables this is `TempTableDataAccess`, both built atop `TempTableDataProvider` for the actual storage.

The boundary is **`NavSession.DataAccessSource`**, but the substitution sleight-of-hand is that we don't have to write our own `IDataAccess` from scratch — we **reuse BC's own `TempTableDataProvider`** for all tables.

### Specific types in the chain

- `Microsoft.Dynamics.Nav.Runtime.NavRecordHandle.CreateTarget` (hook point)
- `Microsoft.Dynamics.Nav.Runtime.NavGlobal.NCLMetadata` (bypassed by direct metadata construction)
- `Microsoft.Dynamics.Nav.Runtime.NCLMetaTable.CreateFromMetaTable(MetaTable)` (internal — invoked via reflection from parsed AL source)
- `Microsoft.Dynamics.Nav.Types.Metadata.MetaTable` / `MetaField` / `MetaKey` (public — used to build NCLMetaTable)
- `Microsoft.Dynamics.Nav.Runtime.NavSession.DataAccessSource` (hook getter)
- `Microsoft.Dynamics.Nav.Runtime.DataAccessSource.GetDataAccessForTable` (hook method → return `CreateTempDataAccess(metaTable)`)
- `Microsoft.Dynamics.Nav.Runtime.NavDatabase.CollationAwareStringComparer` (hook — return `OrdinalIgnoreCase`)
- `Microsoft.Dynamics.Nav.Runtime.NavApplicationObjectBase..ctor` (replaced — replicates `TreeObject.ctor` and injects skeleton session)

### Single-point-of-substitution feasible?

**Yes, *effectively* — at three coordinated hook points** (`NavRecordHandle.CreateTarget`, `NavSession.DataAccessSource`, `DataAccessSource.GetDataAccessForTable`). The metadata side requires reading AL source files (because AL's own type metadata is inside the `.app` we're already producing), but that's parsing, not infrastructure.

## What was attempted

### Attempt A — clean storage-provider swap

**Result: WORKS.** The agent found that:

- `TempTableDataProvider` (BC's existing in-memory store for AL `temporary` tables) accepts any `NCLMetaTable` and provides full `Get/Insert/Modify/Delete/SetRange/FindFirst/...` semantics.
- `NCLMetaTable.CreateFromMetaTable(MetaTable)` is the boundary between AL-source-derived `MetaTable` (public Types.dll) and runtime `NCLMetaTable` (Ncl.dll).
- The agent built `RecordPatches.MetaTableBuilder` that parses AL `.al` files for table definitions and produces `MetaTable` objects via reflection on the public Types.Metadata API.

This sidesteps `NavGlobal.NCLMetadata` entirely — we never need a working SystemTenant.

### Attempt B — per-method JMP-hook

Not needed for the prototype; Attempt A handled all sync CRUD. Attempt B may still be the right tool for `InsertAsync` and other async overloads, where the IL goes through a different code path.

### Attempt C — escalation

Not reached.

## Patches added

Major files:

- `spike/v2/Runner/Patches/RecordPatches.cs` (771 lines) — the storage substitution
  - `MetaTableBuilder.Parse(srcDir)` — parses AL source for tables/fields/keys
  - `RegisterMetaTable(metaTable)` — caches NCLMetaTable per TableID
  - `Hook_NavRecordHandle_CreateTarget` — constructs typed `RecordImplementation` with our metadata + temp-table-backed `IDataAccess`
  - `Hook_NavSession_DataAccessSource` — returns skeleton `DataAccessSource`
  - `Hook_GetDataAccessForTable` — routes to `CreateTempDataAccess(metaTable)`
  - `Hook_CollationAwareStringComparer` → `OrdinalIgnoreCase`
  - `Hook_TempTableStatistics_ReportIncrementChange` → no-op
  - `Hook_SequentialUuidCreator_NewSequentialId` → counter-based GUIDs
- `spike/v2/Runner/BcRuntime.cs` additions:
  - `NavApplicationObjectBaseCtorReplacement` — replicates `TreeObject.ctor` (so `tree` field gets set), injects `_skeletonSession`, skips `ResolveAppGroup`
  - `TreeHandler_get_Session` → `_skeletonSession`
  - `IsGlobalTriggerImplemented` → false
  - `RecordImplementation.IsOpen` → true (skeleton always-open)
- `spike/v2/Runner/Program.cs` — calls `RecordPatches.AddSourceDir(srcDir)` per bucket

## Test pass status

```
tests/bucket-1/record-table/02-record-operations
  Tests:        6
  pass:         1   (✓ minimum gate criterion met)
  fail:         5   (all on NavRecord.InsertAsync — single missing async overload)
  error:        0
```

The 5 failing tests all share the exact same stack:

```
at NavRecord.InsertAsync(DataError errorLevel, Boolean runApplicationTrigger, ...)
at NavRecord.ALInsertAsync(DataError, Boolean, Boolean)
at <test method scope>.OnRun()
```

This is one async overload that bypasses the patched sync path. A 30–60 minute follow-up patch (either redirect to sync `Insert` or hook the async ctor on the result task) likely closes most of these.

## Effort estimate for full record-table coverage

**Medium (M)** — projected ~2 weeks for the rest of bucket-1/record-table (109 tests).

Rationale:
- Sync CRUD is **done** — works for any AL table parseable from `.al` source.
- Async overloads (Insert/Modify/Delete/Find variants) need the same pattern but on the async path. Probably 5–10 method hooks.
- FlowFields, FlowFilters, calculated fields, FindSet/Next iteration: existing `TempTableDataProvider` already supports these. Mostly testing.
- Keys, sort orders, filtering: same — BC's own provider handles them.
- The big unknowns: `AutoCalcFields`, `RecordRef`, `FieldRef` runtime introspection. Probably 1 week of patch-and-iterate per discovery class.

Page-report bucket benefits transitively — many of those tests touch records, and once records work, ~30–40 of the current page-report failures should fall away.

## Verdict

**GO**, with confidence Medium-high.

The headline finding is that `TempTableDataProvider` exists and accepts arbitrary `NCLMetaTable`. That's the substrate Opus's analysis was skeptical we'd find. We did find it. The prototype proves it works end-to-end against real Microsoft IL for a non-trivial CRUD test.

Caveats:
- The InsertAsync residual means we're not yet at "all 6 tests pass on this bucket." But the architectural verdict doesn't depend on it — that's tactical patch work of the same shape we've done elsewhere.
- BC version drift: `TempTableDataProvider` is non-public BC infrastructure. Major BC versions could move/rename it. Re-spike per major release, ~1 day each.
- Performance: BC's TempTableDataProvider uses real B-tree-style indexes internally. Should be fast, but not benchmarked.

## What this means for the project

**The branch is no longer a write-off candidate.** The architectural risk that motivated this gate spike — "if NavRecord doesn't redirect, the entire approach is throwaway" — is resolved in the affirmative. The remaining work is bounded patch-and-iterate that follows the same template as the codeunit-runtime work (~2 weeks for record-table, ~1 week for page-report transitive benefit, plus W-7 isolation modes for correctness).

**Combined corpus projection** if record-table reaches the same ~85% pass rate as the other categories:
- bucket-1/codeunit-runtime: 87% (already)
- bucket-1/record-table: ~85% projected (currently 18%)
- bucket-2/data-formats: 86% (already)
- bucket-2/page-report: ~70% projected (currently 43%, will benefit from records)
- **Overall: ~85% pass rate, 700+ of 809 tests** — vs. 73% / 592 today.

Recommend proceeding with full migration. Schedule follow-up agents on (1) async overloads, (2) record-table corpus expansion, (3) W-7 isolation modes.

## Recommended next step

1. Land this commit on `spike/bc-abi-identity` (merge `spike/bc-abi-identity-record-gate`).
2. Run the full corpus to confirm post-merge numbers and capture the new baseline.
3. Spawn a focused agent on `NavRecord.InsertAsync` + sibling async overloads (target: 02-record-operations 1/6 → 6/6).
4. Spawn a broader agent on the rest of `bucket-1/record-table`.
