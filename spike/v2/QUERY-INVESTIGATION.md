# NavQuery investigation — deferred multi-day workstream

**Status (2026-05-10):** deferred. Cluster is small (~7 failing tests), but the work needed to land it is 2–3 days. Full architecture mapped below so a future session can pick up cleanly.

**Why this doc exists:** two Sonnet sessions (~6.5h combined) failed to land NavQuery. The third session paired Opus 4.7 with a GPT-4.1 brainstorm and produced an empirical diagnosis that explains the failure mode and identifies the path forward. This doc captures everything.

---

## §1. Why NavQuery is structurally different from XmlPort/Form/Report

XmlPort, Form, and Report all share a "data-free shell" runtime shape on the BC side:
- Their ctors are essentially empty (`base(parent, meta.ApplicationObjectId)` and not much else).
- The AL-emitted business logic body sits on top of a runtime that doesn't dereference its own metadata to *function* — only to dispatch.
- That's why mirroring `NavXmlPortHandle.CreateTarget` (factory delegate registered on the meta) plus simple ctor bypass works for those three.

NavQuery is fundamentally different: **its runtime is a metadata-driven SQL projection layer**, not a shell. Filter and column logic are how Query *works*, not optional extras.

Concretely from decompiled NCL.dll v27.5:

```csharp
// NCL.dll line ~51201
protected NavQuery(ITreeObject parent, int id, SecurityFiltering sf)
    : base(parent, id)
{
    NCLMetaQuery.MetaQuery.ValidateColumns(...);          // [A] NRE on skeleton — MetaQuery null
    Filters = ExtractDefaultRuntimeFilters(NCLMetaQuery);  // [B] NRE — LazyEx<QueryDefinitionContainer>
    ALTopNumberOfRowsToReturn = NCLMetaQuery.TopNumberOfRowsToReturn;
}
```

`NCLMetaQuery.queryDefinition` is `LazyEx<QueryDefinitionContainer>` and **explicitly throws `"queryDefinition cannot be read before calling ParseMetadata"`** when accessed pre-init. This is BC's own assertion: Query's metadata must be fully parsed before any instance method runs. There is no graceful "empty/null" path the way there is for XmlPort.

Compare to `NavXmlPort` ctor (NCL.dll, similar line range): structurally near-empty, just calls `base(parent, ...)`.

This is GPT-4.1's "category error" diagnosis confirmed empirically: **mirroring XmlPort cannot work for Query because Query's contract requires populated metadata, not a bypass.**

---

## §2. Why both prior Sonnet workers (sessions 3 + 4) failed

Two consecutive workers drafted plausible-looking 200–450-line patches, never produced a green smoke, then collapsed and rewrote from scratch. Opus's empirical probe found the smoking-gun root cause:

```csharp
// In RecordPatches.NclMetadataCachePopulator.cs, populator's reflection cache
var m = nclAsm.GetMethod("CreateEmptyNCLMetaQuery", flags);  // throws AmbiguousMatchException
```

`NCLMetaQuery` (unlike `NCLMetaForm` / `NCLMetaReport`) has **two** `CreateEmptyNCLMetaQuery` overloads. `Type.GetMethod(name, flags)` without an explicit signature filter throws `AmbiguousMatchException`. The populator's outer try/catch silently swallowed it. Workers had **zero feedback signal** that their hook even fired — they were drafting blind, exactly the failure mode GPT-4.1 predicted.

Compounding that: the Query overload of `CreateEmptyNCLMetaQuery` takes `ApplicationObjectId`, not `int` (Form/Report take `int`). Reflection-construct calls passing `int` would silently fail too.

**Lesson for the architecture going forward:** any reflection-based populator must (a) use signature-explicit `GetMethod(name, flags, types)`, (b) log failures eagerly rather than swallowing into a generic catch. The brain should treat "no commits, plausible drafts" from a delegated worker as a strong signal that **diagnostic instrumentation is missing**, not that the worker is incompetent.

---

## §3. Three layered obstacles (empirically verified by Opus probe)

In order of unblocking:

### 3.1. Cache miss — fixable in ~60 LOC (verified)

`PopulateNclMetadataCache` in `spike/v2/Runner/Patches/RecordPatches.NclMetadataCachePopulator.cs` handles ObjectType={Table=1, Report=3, Page=8} but not Query=9. Adding a 4th slot mirroring `BuildNCLMetaReport` does drain the cache miss. Opus verified empirically: `PopulateNclMetadataCache[Query]: added=1, failed=0`.

Two sub-traps caught only with a real run:
- `CreateEmptyNCLMetaQuery` takes `ApplicationObjectId`, not `int`. NCL line ~153226 vs Form/Report at ~152132 / ~155871.
- Two overloads → `Type.GetMethod` ambiguity (see §2). Use `GetMethod(name, flags, null, new[] { typeof(ApplicationObjectId) }, null)`.

### 3.2. `NCLMetaQuery.CreateObjectInstance` NRE — same shape as Form/Report (~80 LOC)

With cache populated, the next failure shifts from "Query not found" to a NRE inside `NCLMetaQuery.CreateObjectInstance(ITreeObject, SecurityFiltering)` at NCL line ~153714, dereferencing `ApplicationObjectConstructor` delegate (null on a skeleton meta).

Solution: standard `NavQueryHandle.CreateTarget` hook mirroring `NavReportHandle_CreateTarget` (`spike/v2/Runner/Patches/CodeunitPatches.cs:154`). The hook locates `Query{ID}` in the loaded test assembly and returns a constructed instance.

### 3.3. The actual blocker — `NavQuery` base ctor metadata deref

Even with §3.1 + §3.2 done, the ctor body (§1 above) deref's `MetaQuery.ValidateColumns(...)` and `ExtractDefaultRuntimeFilters(NCLMetaQuery)`. Both NRE on a skeleton with `MetaQuery == null` and `queryDefinition` un-parsed. JmpHooking the protected ctor is allowed (NCL = runtime engine), but bypassing those calls leaves the instance in a state where `ALOpen` / `ALSetFilter` / `ALRead` / column getters all fail downstream.

### 3.4. AL methods are pervasively MetaQuery-coupled (architectural scope question)

- `ALOpen → OpenAsync` builds a SQL `ResultSetEnumerator` over `NavDatabase`.
- `ALRead` advances that enumerator.
- `ALSetFilter` / `ALSetRangeSafe` / `ALGetFilter` call `NCLMetaQuery.QueryDefinition.GetColumnByNo(columnNo)`. `queryDefinition` is `LazyEx<QueryDefinitionContainer>` that explicitly throws `"queryDefinition cannot be read before calling ParseMetadata"`.
- AL-emitted column getters on `Query{ID}` compile to `GetColumnValueSafe(columnId, NavType)` on the base, dereferencing `currentDataRow` populated by the SQL enumerator.

There is **no shallow patch**. Either we feed BC a real parsed metadata, or we replace the entire BC runtime layer with a runner-side shim.

---

## §4. Two viable mechanisms (both in v2 spec, both 2–3 days)

### Option A (recommended): real `MetaQuery` XML builder + `CreateDynamicQuery`

Parse the AL `query` AST (dataitems, columns, filters, joins, orderby, top) → construct BC's `MetaQuery` XML representation → call `NCLMetaQuery.CreateDynamicQuery(token, metaQuery, clrType, appGroup)` at NCL line ~153240.

**Why this is the right shape:**
- Reuses BC's real query engine unmodified (collation, joins, filters, top-rows, dataitem-link semantics, calculated columns, sorting). All the hard parts of Query stay in BC.
- Narrow runner surface: one parser + one delegate registration. No JmpHook on AL methods.
- Mirrors how Tables already work via `MetaTable` parsing — same architectural shape, same trust boundary.
- Closest to "Option-C" in HANDOFF §5.2.

**Estimated work:**
- AL Query AST → MetaQuery XML mapping (dataitem joins, column types, link-fields, orderby, filtergroup propagation): ~1.5 days.
- Metadata column-type resolution (handles MetaTable.GetFieldByNo dependency): ~0.5 days. Note: this overlaps with the open `RecordImplementation.CalcFieldsAsync` FlowField populator gap, so doing CalcFieldsAsync first is good prep.
- `CreateDynamicQuery` wiring + smoke: ~0.5 days.
- Edge cases (joins, calculated columns, security filters): ~0.5–1 day.

### Option B: runner-side Query executor + 6 JmpHooks

Effectively v1's `MockQuery` ported to v2's JmpHook mechanism: hook `NavQuery.OpenAsync` / `ALRead` / `ALSetFilter` / `ALGetFilter` / `GetColumnValueSafe` / `ALCloseAsync`, route to a runner-side iteration shim reading from `TempTableDataProvider`.

**Why it's the fallback, not the primary:**
- ~6 JmpHooks vs Option A's one delegate. More surface, more places to drift from BC semantics.
- Join semantics, sort semantics, calculated-column semantics all need re-implementation in the shim. That's the work BC's query engine does for free under Option A.
- More likely to subtly disagree with BC behavior — exactly the failure mode v1 hit.

Mechanism is in spec. Use only if Option A turns out to have an unexpected blocker.

---

## §5. Future-session checklist for picking up Query

When picking this up as a 2–3 day workstream:

1. **Read this doc + HANDOFF §1, §2, §5.2, §5.5 + `.claude/rules/precompiled-dll-respect.md`.**
2. **Stash recovery:** `git stash list` → look for "session-3 WIP: QueryPatches investigation". Has 269 lines of plausible draft. Treat it as a sketch only — the underlying approach (constructor JmpHook + null metadata) is wrong per §3.4. Salvage the reflection caches and the `Query{ID}` lookup, discard the rest.
3. **Diagnostic-first.** Before touching `NclMetadataCachePopulator.cs`, add a `Console.Error.WriteLine` line at every reflection failure path. The populator's outer catch must NOT swallow `AmbiguousMatchException` silently — that's what cost two sessions.
4. **Land §3.1 (cache populator) as commit 1.** Verify `PopulateNclMetadataCache[Query]: added=N, failed=0` shows up in the smoke output. No P uplift expected from this commit alone — that's fine, it's a stepping stone.
5. **Land §3.2 (CreateTarget hook) as commit 2.** Should give some uplift on tests that don't exercise filters/columns. Capture which tests remain failing — those are the ones needing §3.3+§3.4 (full Option A).
6. **Begin Option A.** Start with the simplest fixture (`Simple Item Query` in `tests/bucket-2/page-report/96-query-basic` if it exists, otherwise the smallest Query in the corpus). Prototype `MetaQuery.FromAlSource(string)` → `CreateDynamicQuery` → smoke that single test green. Then iterate over the rest.

**Pre-requisite work that doesn't conflict:** `RecordImplementation.CalcFieldsAsync` (FlowField populator, ~23 tests in current classification) and `NCLMetaTable.GetFieldByNo` (~15 tests) are both in `RecordPatches.NclMetadataCachePopulator.cs` territory and pave the way for Option A's column-type resolution. Drain those first.

---

## §6. Worker-delegation lessons from the two failed Sonnet attempts

Strong implications for how to brief future delegated work on deep targets:

- **Diagnostic instrumentation is non-negotiable** before drafting code. Brief must require: atomic counter + side-effect log on hook entry, reflection-method-resolution must log on failure, populator catches must log not swallow.
- **Empirical-feedback gate:** the worker must *run a smoke against a single failing test* before extending the patch beyond ~50 LOC. "Plausible drafts" from a worker stuck at 200+ LOC with no smoke is the failure signature.
- **Hard time cap with stash-and-pivot:** 90-min cap per target. If no green smoke at the cap, stash + pivot to a fresh target. The brain (top-level Opus) reviews stashed work in a later session, not the worker.
- **Sonnet vs Opus on architecturally-deep targets:** Sonnet is excellent on shape-matched fix loops where a known-good analog exists. When the analog is wrong (as here — XmlPort isn't analogous to Query), Sonnet drafts plausibly without empirical pushback. Opus's empirical probe ("don't draft, populate one slot, run, look at the next failure") would have surfaced the AmbiguousMatchException in 30 minutes. **For deep-architectural-shape questions, escalate to Opus before delegating fix-loop work to Sonnet.**

---

## §7. Tactical recommendation for "next session"

**Defer Query.** Cluster is only 7 failing tests vs much larger non-Query clusters in `v2-classification.json`. Pick those first:
- `RecordImplementation.CalcFieldsAsync` — ~23 tests, FlowField populator gap, well-scoped.
- `NCLMetaTable.GetFieldByNo` — ~15 tests, populator gap.
- `NavRecord.ModifyAsync` residual — ~15-20 tests, different sub-shape from the morning's drain.

Re-classify on top of `aa7d1827` and pick the largest non-Query cluster. The Query workstream is a planned 2–3 day investment when corpus pass rate plateaus on the easier wins.
