# AL Runner v2 — handoff

**Last updated:** 2026-05-09 (rewrite — earlier sessions archived to `HANDOFF-archive.md`)

For older session-by-session chronology (§B through §R of the previous handoff),
see `HANDOFF-archive.md`. The technical decisions there are still valid; the
narrative was just too long to be load-bearing.

---

## §1. Mission

v2 is a test runner over BC AL code that satisfies one hard constraint:
**the compiled test DLL must be binary-compatible with Microsoft's R2R DLLs.**

The R2R DLLs Microsoft ships inside their `.app` files (System Application,
Base Application, etc.) reference the real BC runtime types: `NavCodeunitHandle`,
`ByRef<NavCodeunitHandle>`, `NavRecord`, etc. If v2 renames or substitutes those
types (as v1 does, e.g. `NavCodeunitHandle → MockCodeunitHandle`), the resulting
DLL cannot link against Microsoft's R2R DLLs and integration tests that touch
System Application code break at load time.

**Therefore v2 does no type-renaming rewrites.** Both AL-source compile and
Microsoft-`.app`-load go through a converging pipeline:

```
                           ┌─ Microsoft .app ──> publishedartifacts/*.dll ─┐
                           │                                                │
      any input ───────────┤                                                ├──> Assembly.Load ──> [NavTest] runner
                           │                                                │
      AL source ──> BC's Compilation.Emit ──> C# ──> Roslyn ──┐             │
                                                              ├─────────────┘
                              CallSiteArgWrap pass ───────────┘
                              (only IL-equivalent rewrite — fills gaps in
                               BC's emitter for `dict.Get(K, fieldOfT)`-shape
                               ByRef<T> wraps it doesn't statically prove)
```

**End state:** `spike/v2/Runner/` becomes the new `AlRunner/`, contains no
compile or rewrite logic of its own beyond the converging pipeline above plus
JMP-hook patches against service-tier runtime. v1's `RoslynRewriter`,
`Runtime/AlScope.cs`, and `--dump-csharp` subprocess are deleted.

---

## §2. Invariants (do not violate)

0. **Precompiled-DLL respect.** The runner's whole point is that integration
   tests run against unmodified MS-AL-compiled DLLs (`SystemApplication.dll`,
   `BaseApplication.dll`, etc.) and ISV-AL-compiled extensions. **Their public
   type surface and method bodies must behave exactly as compiled.** The
   runtime engine (NCL.dll, Types.dll, dispatchers, framework wrappers) and
   skeleton state are ours to modify however we need. See
   `.claude/rules/precompiled-dll-respect.md` for the full table of what's
   allowed vs forbidden and the mental model. Items #1 and #3 below are
   consequences of this rule.
1. **No type-renaming rewrites.** Renaming `NavX → MockX` breaks linking for
   every R2R or AL-precompiled caller in the load chain. Consequence of #0.
2. **No silent workarounds.** Gaps get fixed or quarantined with a documented
   reason in `tests/excluded/<bucket>/<suite>/`. Never paper over.
3. **Argument-wrap rewriting only on our own AL output.**
   `Rewriters/CallSiteArgWrap.cs` (121 LOC) is the only Roslyn rewrite on AL
   we emit. It wraps `expr → new ByRef<T>(getter, setter)` at call-sites
   where BC's emitter couldn't statically prove the wrap was needed. This
   produces IL byte-equivalent to BC's own pipeline. **Never rewrite IL of
   precompiled AL-business-logic DLLs (MS or ISV)** — see #0.
4. **Reuse service-tier code before patching.** Before writing a new JMP-hook in
   `Patches/*.cs`, check whether the real MS service-tier DLLs we already load
   can satisfy the call. Three checks, in order:
   1. Would `NavEnvironment.InstantiateStandaloneNavEnvironment` (or a derived
      ctor) populate the field that's NRE-ing? Fix the population path, not
      the call site.
   2. Is the type reachable via `BcRuntime.SetupAlcResolver` /
      `ResolvingProbeBcArtifactDir` (24 BC service-tier DLLs)? If not yet,
      add it there before patching.
   3. Can reflection-construct a real internal class instead of skeleton-poking
      via `RuntimeHelpers.GetUninitializedObject`?
   Only after all three return "no" do we reach for `Hook(...)` /
   `JmpHook.Install(...)`. Each unnecessary patch is future-version-bump tax.
5. **TDD-style proof on bucket migrations.** Pass-count parity vs per-suite is
   the proof that bundled mode is correct. "It ran" is not enough.
6. **No `CHANGELOG.md` edits.** Generated post-merge. See
   `.claude/rules/no-changelog-edits.md`.
7. **No `coverage.yaml` updates while on the spike branch.**

---

## §3. CLI direction — converging on v1

v1's CLI is `al-runner [opts] <src-dirs>...` — no mode flag, just point at AL.

**Where we are (2026-05-09):**
- ✅ All 4 sub-buckets migrated to bundled and parity-verified.
- ✅ `bundledMode = true` is now the default in `Program.cs`.
- ✅ `--bundled-experiment` removed (footgun).
- ✅ `--per-suite` opts into the legacy path (kept one cycle for diagnostic
  comparisons; will be removed once the bundled dep-load asymmetry is resolved).
- ✅ `--bundled` accepted as a no-op alias for backwards compat.

**Remaining v1-convergence work:**
1. Align flag spellings with v1 (`--package-cache` → `--packages`).
2. Pick up the v1 features v2 lacks: `--run`, `--output-junit`, `--output-json`,
   `--coverage`, `--test-isolation`, `--test-timeout`, `--guide`. One at a time.
3. Eventually delete `AlRunner/` and rename `spike/v2/Runner/` to `AlRunner/`.

---

## §4. Current state (2026-05-11 EOD — W-8 trigger+subscriber dispatch complete)

**Branch:** `spike/bc-abi-identity`. No push, no PR.

| Sub-bucket | Pass | Total | % | Wall |
|---|---:|---:|---:|---:|
| bucket-1/codeunit-runtime | 733 | 991 | 74.0% | ~102s |
| bucket-1/record-table | 655 | 905 | 72.4% | ~199s |
| bucket-2/data-formats | 1380 | 1559 | 88.5% | ~86s |
| bucket-2/page-report | 268 | 617 | 43.4% | ~77s |
| spike-a-baseapp (keystone) | 8 | 8 | 100% | ~56s |
| **Total** | **3044** | **4071** | **74.8%** | **~9 min** |

**Critical fidelity note:** as of 2026-05-11 the trigger and event-subscriber dispatch paths are real (W-8 series: `ae15b158`, `c2df0bcd`, `f8367536`, `29b5acc9`, `c4bce11a`). Insert/Modify/Delete/Rename trigger dispatch runs through unmodified Ncl bodies; `[NavEventSubscriber]`-attributed AL methods are discovered at startup and dispatched by BC's own `NavEventScope.CheckAndFireTriggerEventsAsync`. Pre-W-8 pass counts were misleading on every trigger/subscriber-dependent test; current numbers are honest.

Runtime grew ~40% across all buckets from the pre-W-8 baseline of ~6.4 min — real BC body execution + W-8 reflection cost. Tracked in `feedback project_runtime_parity_backlog.md`.

**Architectural viability: PROVEN END-TO-END.** Two derisking spikes ran
this session:

- **Spike A — integration tests against unmodified MS Base App.** Loaded
  `Microsoft.Dynamics.Nav.BaseApplication.dll` v27.5 (5 R2R DLL chunks,
  ~217 MB) and ran AL tests calling four shapes of Base App business
  logic: cross-chunk dispatch (`Codeunit "Type Helper".GetOptionNo`),
  `Record "Currency".Init()` (after the populator was extended), error
  raising → asserterror channel (`Codeunit "GLN Calculator"`), control
  flow with by-ref outputs (`Codeunit "Type Helper".GetHMSFromTime`).
  All 8 tests pass against unmodified MS DLLs. `tests/spike-a-baseapp/`
  is now a permanent regression check.

- **Spike B — AL-output caching.** Compile bucket once, write `<key>.dll` +
  `<key>.enum-registry.json` sidecar to `--cache <dir>`, reload on
  subsequent runs without recompiling. Verified at full corpus scale:
  warm pass counts match cold for all four buckets, ~1.4–2.4× wall-time
  reduction (compile fully skipped). Now enables AL-DLL-as-dependency
  scenarios alongside MS / ISV precompiled DLLs.

**Quarantines:** 30 suites total under `tests/excluded/<sub-bucket>/`, each
with a `RUNNER-GAP-*.md` note documenting the cause. Three categories:
- **BC compiler emit bugs** (NavTypeKind 'None', IndexOutOfRange in `BuildUserCallArgumentList`, NRE in `WriteAttributeProperties` on `fileupload`) — real BC defects.
- **Bundled-mode strictness** (AL0680 dataset position, AL0240 ReportHandler signature, AL0264 ID collisions, AL0305 name length, AL0275/AL0217 layout/scope rules) — bundled compilation enforces stricter checks than per-suite. Tagged `BUNDLED` in the gap doc filename.
- **AL Runner Config codeunit gap** (271-companyproperty, 242-company-name) — al-runner-only config codeunit not yet implemented in v2 (v1 implemented via `MockSession` routing on the renamed `MockCodeunitHandle`).

Remaining 1286 failures are concentrated in a handful of clusters per bucket;
see `v2-classification.json` (regenerated by every run, gitignored). The
biggest remaining levers are documented in §6.

---

## §5. Patching toolkit (post-2026-05-09 session)

The runner has five mechanisms, in priority of use:

### 1. JmpHook (default — 78 working hooks)

`Infrastructure/JmpHook.cs`: x86-64 `jmp` patch via direct `libc!mprotect`
(bypasses managed W^X enforcement). FixupPrecode-aware. Use for sync,
non-generic methods whose callers are already JIT'd by the time the hook
applies (i.e. methods called from already-JIT'd test setup or from BC code
that runs before our `EnsureApplied()`).

**Do NOT use JmpHook directly on:**
- Async / `ValueTask`-returning method entry points (precode bytes are still
  JIT-reachable — overwriting them corrupts `MOV R10, [MD]` and crashes the
  next caller-JIT).
- Open-generic methods on generic types (each closed instantiation has its
  own code; one hook doesn't cover all).
- Methods whose callers haven't all been JIT'd yet at hook time.

**R2R-internal-call caveat (`bbbc98ff`):** JmpHook on a property getter inside
NCL.dll does NOT reach R2R-precompiled call sites within the same DLL — only
external callers (BusinessApplication.dll, our compiled AL output). For
intra-NCL-R2R call sites, prefer state-population (the §5.2 path) or hook a
*downstream* method that NCL.dll calls into. Boundary not yet fully audited.

### 2. Service-tier polyfill (Option C — preferred for async/generic)

For async/generic methods, the right pattern is **don't patch them — fix
their sync underbelly**. Most async BC methods are thin `await`-around-sync
wrappers. Empirical example (validated 2026-05-09):

```csharp
public async ValueTask<string> ALFieldCaptionAsync(int fieldNo)
{
    if (Session.PushDynamicCaptionStack(...)) {           // already hooked
        try { var nt = await GetDynamicCaptionAsync(...); ... }
        finally { Session.PopDynamicCaptionStack(); }
    }
    return MetaTable.GetFieldByNo(fieldNo).FieldCaption;  // hot path: SYNC
}
```

Fix: extend `Patches/RecordPatches.NclMetaTableBuilder` (per the §O-era cache
populator) so `MetaTable.GetFieldByNo(...)` returns an `NCLMetaField` with a
populated `FieldCaption` for parsed AL tables. The async wrapper resolves
naturally — no JmpHook on the async surface needed.

Same pattern likely applies to:
- `NavReport.RunReportAsync` (sync dataset/layout pipeline underneath)
- `NavMethodScope.RunBehaviorAsync` (sync behavior-dispatch underneath)
- `NavForm.GetAutoFormatStringAsync` (sync format-string lookup underneath)
- `NavObjectDictionary\`2.get_Target` (hook the closed-type constructor —
  which is non-generic on the closed type — and pre-populate the backing
  dict; the open-generic getter then reads our state naturally).

This is the **first resort** for any blocked async/generic test class.

### 3. EventPipe + post-JIT JmpHook (fallback — proven mechanically)

For cases where Option C is infeasible (e.g. an async method does real
async I/O with no thin sync underbelly), Spike 4 (commit `47fda6a9`)
proved the mechanism:

1. Subscribe in-process to `Microsoft-Windows-DotNETRuntime` JIT keyword
   (`0x10` at `EventLevel.Verbose`) via `System.Diagnostics.Tracing.EventListener`.
2. On `MethodLoadVerbose_V2` events, match against a target list. Payload
   gives `MethodStartAddress` + `MethodSize` of the post-JIT compiled body.
3. Apply JmpHook to that compiled-body address (post-JIT, so the precode
   has been promoted past FixupPrecode and the JIT-state-reading-cell
   problem from prior spikes is gone).
4. **Crucial:** the replacement must preserve the original method's
   failure-mode contract (raise the right exception via the right channel,
   not just return a default). Spike 4 confirmed mechanism works; the
   YELLOW was a semantic-fidelity issue with the test stub, not a
   mechanical one.

The scaffolding lives at `spike/v2/Runner/Patches/EventPipeJitListener.cs`.
Patching is currently disabled in committed state (proof-of-concept only).
**Not yet deployed in production** — this is the unlock for the remaining
async-self-dispatch clusters (NavForm.GetAutoFormatStringAsync,
NavReport.RunReportAsync, NavReport.SaveAsAsync, the 12 TFO residuals
JIT-inlining NavTextConstant.get_Value). Spike-4 left mechanism GREEN but
semantic-fidelity replacements unproven; needs ~2–4 days of focused work
across all blocked async surfaces.

### 4. Compile-pipeline rewrites (compile-step Cecil/Roslyn)

For cases where the AL output we emit needs IL-level adjustment that the
runtime engine can't do polyfill-style, we may rewrite IL **inside the
compile pipeline** (before the DLL is finalised + cached). Today's only
compile-side rewrite is `CallSiteArgWrap` (Roslyn). **Compile-side rewrites
are the only safe place to alter our AL output** — load-time IL rewriting
of an already-cached DLL would diverge from the cache and violate the
precompiled-DLL-respect rule (see `.claude/rules/precompiled-dll-respect.md`).
Cecil-rewriting MS-AL or ISV-AL DLL bodies is forbidden — those bodies are
the integration-test contract.

### 5. Skeleton-state population (often the right answer)

Repeatedly the cleanest fix is to populate state on the skeleton
`NavSession` / `NavMethodScope` / `NavCompany` so the real BC method runs
unmodified. Wins this session:
- `NavSession.<ErrorCollection>k__BackingField` (commit `4ad44f0e`)
- `NavSession.cachedEnvironmentDefaultLcid` LazyEx (commit `86072df6`)
- Skeleton `TreeHandler` planted on `_skeletonSession` (`2b52e8a`)
- `NavThreadLocalStorage.Current.Session` wired so static getters resolve
- Per-test reset of the shared `TempTableDataProvider` cache (commit
  `de0bad05` — the +169 PASS lever).

This is HANDOFF §2 invariant 4: **try state-population before patching**.

### Spike history (all six committed; rationale in commit messages)

| Spike | Outcome | Key finding |
|---|---|---|
| 1 — Harmony 2.4.2 | RED | MonoMod.Core writes non-PIC native `.so`; glibc 2.43 W^X rejects `dlopen`. Reverted (`f97d4a5f`). |
| 2 — JmpHook 14-byte overwrite of async entry | RED | Corrupts `MOV R10, [MD]` bytes JIT reads when lazy-compiling callers post-hook → SIGSEGV. Diagnostic kept (`c52f0b4c`). |
| 3 — Indirect-cell pointer swap | YELLOW | GREEN on already-hooked sync methods (cell becomes inline pointer). RED on live FixupPrecode async methods — the cell is JIT *state*, not just dispatch. (`68b362c4`) |
| 4 — EventPipe + post-JIT JmpHook | YELLOW (mechanism GREEN) | EventListener subscribes & fires for BC R2R methods. JmpHook applies post-JIT without ABI/JIT crashes. ABI compatible. Replacement reached. Failure was semantic stub, not mechanism. (`47fda6a9`) |
| 5 — Base App body invocation keystone | GREEN | Loaded MS BaseApplication.dll v27.5 (5 R2R chunks) unmodified, called four shapes of business logic from AL test. Multi-DLL R2R load support added. (`ffecaf8a`, `ae56a513`, `e82a55a8`) |
| 6 — AL-output cache keystone | GREEN | Compile once, cache `<key>.dll` + `<key>.enum-registry.json` sidecar, reload without recompile. Pass-count parity at full corpus scale. Cache-schema-versioned hash key. (`9838d1a7`, `40af00be`) |

---

## §6. Active work threads (priority order)

### Tier 1 — Original async/generic blockers — STATUS

| # | Blocker | Status | Commit / next action |
|---|---|---|---|
| 1 | `NavRecord.ALFieldCaptionAsync` | ✅ DONE +26 P | `896df62` (FieldCaption + TestField throw path polyfill) |
| 2 | `NavForm.GetAutoFormatStringAsync` | ⛔ §5.3 STOP — needs EventPipe | Blocked: async self-dispatches to AL-emitted overrides, no shared sync underbelly. ~18 in bucket-1, ~90 across corpus. |
| 3 | `NavReport.RunReportAsync` | ⛔ §5.3 STOP — needs EventPipe | Blocked: same shape. ~45 across corpus. Needs semantically-faithful replacement that fires registered `[RequestPageHandler]` handlers. |
| 4 | `NavMethodScope.RunBehaviorAsync` | ✅ DONE +78 P | `4ad44f0e` (skeleton ErrorCollection + threadlocal Session + ALError routing) |
| 5 | `NavObjectDictionary\`2.get_Target` | ✅ DONE +15 P | `08987e80` (Option-C hook returning a populated SharedNavObjectDictionary against the skeleton container) |
| 6 | `NavReport.SaveAsAsync` | ⛔ §5.3 STOP (assumed shape) | ~9 tests, untested but expected same row-2/3 shape |

Three of six rows drained; three blocked behind §5.3 EventPipe deployment (rows 2/3/6 — NavForm.GetAutoFormatStringAsync, NavReport.RunReportAsync, NavReport.SaveAsAsync).

### Tier 1B — New high-impact targets surfaced this session

The Tier-1 drains plus state-population work surfaced new top blockers in
the freshly-regenerated `v2-classification.json`. Priority order:

| # | Bucket / Cluster | Tests | Approach |
|---|---|---:|---|
| A | `NavDialog.ALError` catch-all in record-table | ~81 | Multi-root catch-all. Pick a representative subset, classify the underlying NREs. Likely ~2-3 distinct root causes hiding here. |
| B | `NavRecord.ModifyAsync` (record-table) | 19 | ✅ **DONE** 2026-05-11 — `29b5acc9` (Modify drain, predicated on `f8367536` recursion guard). Part of W-8a series. |
| C | `NavSession.GetPermissionSet` | 16 | ✅ **DONE** 2026-05-10 (commit `776eb87e`, +36 P). Hook returns `VirtualDataProvider.PermissionSet` (all-granted). |
| D | `NCLMetaTable.GetFieldByNo` | 15 | ✅ **DONE** 2026-05-11 — `70380611` (tableextension field merging) drained the bulk; residual is the `999`-not-found trap-error case. |
| E | `NavApplicationObjectBaseHandle\`1.get_Target` | 14 | ✅ **CLASSIFIED** 2026-05-11 — `8efcc462` split into 5 default-variant (out-of-scope/NavRecord.CloneForVariant) + 8 system-table-2000000041 (genuine BcAppFallback gap). |
| F | `RecordImplementation.CalcFieldsAsync` | 14 | ✅ **PARTIALLY DONE** — `d337c849` (FlowField populator basics, 2026-05-10) + `ff0d83e7` (unquoted-name parser, 2026-05-11). Residual edge cases remain in the bucket but the cluster is much smaller. |
| G | `NavRecord..ctor` (record-table) | 11 | Ctor on `RecordLink` / `Company` / other system tables; needs BcAppFallback metadata entries. Unblocked but not yet attempted. |
| H | `NavQuery` cluster | ~7 | ⏳ **DEFERRED** to a planned 2–3 day workstream. Not solvable single-session. See `spike/v2/QUERY-INVESTIGATION.md` for the full architectural map. |
| I | `ALDatabase.AL*` NRE cluster | ~32 | ⚠️ **HARDER THAN A SONNET TASK** — 2 attempts segfaulted (`b01c0111` silent stubs, `5dce5c23` ThrowOutOfScope); both reverted. See `feedback_aldatabase_hard.md`. Needs instrumented investigation (per-hook addr logging + coredump) or EventPipe post-JIT body patch. |
| J | Event-subscriber dispatch | — | ✅ **DONE** 2026-05-11 — `c4bce11a` A-prime (populate BC's own `NavEventScope.registeredSubscriptions`, no JmpHook on the dispatch path; R2R-inlining was the blocker for A-base). |

Each row is a candidate single-fix-unlocks-many lever; the
`RecordImplementation.IssueFindRequestAsync` + `InternalFindRecordWithoutCheckingValuesAsync` collapse (commit `de0bad05`, +150 P in record-table)
proved that pattern is real for this codebase.

### Tier 1C — §5.3 EventPipe deployment (highest-leverage remaining work)

Spike-4 (`47fda6a9`) proved the mechanism. Deployment requires:

1. Re-enable `EventPipeJitListener` (currently disabled in committed state).
2. Subscribe targets: `NavForm.GetAutoFormatStringAsync`,
   `NavReport.RunReportAsync`, `NavReport.SaveAsAsync`, plus the 12 TFO
   residuals where `NavTextConstant.get_Value` is JIT-inlined into
   `Codeunit130000.ExpectedTestFieldError_Scope__82543929.OnRun`.
3. For each target write a *semantically-faithful* replacement (not a stub
   that returns default). E.g. `RunReportAsync` must dispatch registered
   `[RequestPageHandler]` AL methods so test handlers fire.
4. Validate JIT-event-ordering for AL-emitted methods (Spike-4 only tested
   BC R2R methods; AL methods JIT lazily — confirm the listener races don't
   miss them).

Estimated effort: 2–4 days. Estimated payoff: ~200 PASS across the corpus.

### Tier 2 — Structural cleanups (~1 day)

1. **Bundled dep-load asymmetry investigation.** 12+ `BUNDLED`-tagged quarantines
   share a common root: bundled `Compilation.Emit` enforces stricter symbol
   resolution / collision detection than per-suite. Hypothesis:
   `ISymbolReferenceLoader` is lazy and per-suite never triggers loading of the
   conflicting symbol because no source references it; bundled does. Validate by
   instrumenting `BcCompiler` to log loaded refs in each mode + diffing. If
   correct, fixing un-quarantines 12+ suites.

2. **AL Runner Config codeunit** — implement v2's equivalent of v1's MockSession
   routing for `131100 "AL Runner Config"`. Un-quarantines 271 + 242.

3. **Patch redundancy audit.** Three patches likely redundant after the real
   `NavEnvironment` ctor + skeleton SystemTenant + cache populator landed:
   - `NavEnvironment.Instance` getter hook
   - `NavEnvironment.instance` skeleton pre-poke
   - `NCLMetaApplicationObject.get_ApplicationObjectConstructor` (`e1ffb0c3`)
   Test by reverting each and re-running the corpus.

4. **R2R-internal-call boundary audit (bbbc98ff caveat).** Audit ~78 existing
   JmpHooks for cases where the patched method is called from within NCL.dll
   itself (R2R-precompiled). Those hooks silently no-op for intra-NCL callers
   even if they work for AL-test callers. Quantifies how many tests are
   masked by this. Likely candidates: any property getter on `NavSession`,
   `NavRecord`, `NCLMetaApplicationObject`.

### Tier 3 — CLI feature parity with v1 (~2-3 days)

Pick up the v1 features v2 lacks, in roughly this order:
1. `--packages` (rename of `--package-cache`, v1 spelling)
2. `--run <name>`, `--run-codeunit <name>`
3. `--output-junit <path>`, `--output-json` (CI integration)
4. `--test-isolation codeunit|method`, `--test-timeout <sec>`
5. `--company-name <name>`, `--user-id <name>` (depends on AL Runner Config landing in Tier 2)
6. `--coverage` (Cobertura output)
7. `--guide` (rewrite for v2 architecture)

Specialized features (lower priority): `--server`, `--dap`, `--extract-deps`,
`--compile-dep`, `--generate-stubs`.

### Tier 4 — v1 → v2 cutover

1. Delete `AlRunner/`.
2. `git mv spike/v2/Runner AlRunner` (preserves history).
3. Update `.claude/agents/`, `.claude/rules/`, `CLAUDE.md`,
   `docs/coverage.yaml` references.
4. Final corpus run on the new `AlRunner/`. Should match v2 numbers
   byte-for-byte.

**Anti-priority (do NOT spend session time on):**
- Per-stack surgical NRE fixes for the `NavDialog.ALError` catch-all
  classification (~30 min per stack, ≤8 tests each). The catch-all
  classification often hides 2-3 distinct root causes; sample, classify,
  then drain by root rather than per-stack. Big wins this session
  (`4ad44f0e` ALError routing, `de0bad05` shared-DataAccess) have already
  consumed most of the easy roots.

---

## §7. Files & paths cheat sheet

| Where | What |
|---|---|
| `spike/v2/Runner/Program.cs` | CLI: `<bundle-dir>...` (bundled by default); `--per-suite` (legacy), `--bundled` (no-op alias), `--precompile`, `--package-cache`, `--out`, `--cache <dir>` (AL-output cache). |
| `spike/v2/Runner/BcCompiler.cs` | In-process `Compilation.Emit` driver. |
| `spike/v2/Runner/BcAssembler.cs` | Final emit step. |
| `spike/v2/Runner/Rewriters/CallSiteArgWrap.cs` | The only Roslyn rewriter on AL output; 121 LOC. |
| `spike/v2/Runner/Patches/*.cs` | ~80 JMP-hooks + skeleton-state populators, organized by subsystem. |
| `spike/v2/Runner/Patches/RecordPatches.cs` (+ partials) | Record / TempTableDataProvider polyfills. |
| `spike/v2/Runner/Patches/RecordPatches.NclMetaTableBuilder.cs` | Builds `NCLMetaTable` from AL source or BC `.app` source fallback. |
| `spike/v2/Runner/Patches/RecordPatches.BcAppFallback.cs` | Falls back to extracting AL source from registered `.app` deps for tables not in the test bundle (commit `f5ae0d9a`). |
| `spike/v2/Runner/Patches/EnumMetadataPatches.cs` | `AlEnumMetadataRegistry` + `AlEnumOptionMetadata` subclass. Persisted across cache via `<key>.enum-registry.json` sidecar. |
| `spike/v2/Runner/Patches/EventPipeJitListener.cs` | Spike-4 scaffolding: in-process JIT-event listener (disabled, mechanism proven). |
| `spike/v2/Runner/Patches/AsyncStateMachineSpike.cs` | Closed-instantiation generic-getter hooks (NavObjectDictionary). |
| `spike/v2/Runner/Infrastructure/JmpHook.cs` | x86-64 JMP-patch via mprotect (FixupPrecode-aware). |
| `spike/v2/Runner/AppLoader.cs`, `DependencyResolver.cs`, `DependencyLoader.cs` | R2R `.app` deps loader. Multi-DLL R2R chunks supported (commit `ffecaf8a`). |
| `spike/v2/Runner/TestExecutor.cs` | Per-test state reset hook (`ResetPerTestState`) — used to mirror BC's transaction isolation contract. |
| `tests/bucket-1/`, `tests/bucket-2/` | Active test corpus. |
| `tests/spike-a-baseapp/` | Permanent regression check that integration tests against unmodified MS Base App work end-to-end. 8 tests. |
| `tests/excluded/<bucket>/<suite>/` | Documented quarantines (not deleted). |
| `scripts/al-inventory.py` | Per-bucket object enumerator + collision detector. |
| `~/.local/share/al-runner/artifacts/27.5.46862.48827/` | BC 27.5 service-tier DLLs (loaded at runtime). |
| `~/.local/share/al-runner/symbols/27.5.46862.48827/` | BC 27.5 `.app` files (Base App, System App, etc.) — extracted on demand. |
| `/tmp/ncl-src/` | Decompiled `Microsoft.Dynamics.Nav.Ncl.dll` (~16 MB; grep, never cat; never commit). Regenerate with `ilspycmd ~/.local/share/al-runner/artifacts/27.5.46862.48827/Microsoft.Dynamics.Nav.Ncl.dll -o /tmp/ncl-src`. |
| `/tmp/types-src/` | Decompiled `Microsoft.Dynamics.Nav.Types.dll`. Same convention. |
| `.claude/rules/precompiled-dll-respect.md` | **The mission rule.** Auto-loaded. Read before any patch. |
| `spike/v2/docs/BcCompiler.reference.cs` | API mapping reference (do not link into build). |

---

## §8. Diagnostic commands

```bash
# Build
dotnet build spike/v2/Runner/Runner.csproj

# Bundled run with full emit diagnostics
BCCOMPILER_DIAG=1 dotnet run --project spike/v2/Runner --no-build -- \
    --bundled tests/bucket-1/codeunit-runtime

# Per-suite (legacy default)
dotnet run --project spike/v2/Runner --no-build -- tests/bucket-1/record-table

# Verbose AggregateException unwinding (50 inner exceptions instead of 5)
BCCOMPILER_DIAG_VERBOSE=1 ...

# Inventory a bucket before migration
python3 scripts/al-inventory.py tests/bucket-1/record-table
```

`BCCOMPILER_DIAG=1` prints:
- `emitSuccess=True/False`
- `EmitResult.Diagnostics: <N> error(s)` with `emit[AL<id>] @ <file>:<line>: <msg>` per error.
- For `AggregateException` paths: `inner[<Type>]: <full-msg>` + top BC.CodeAnalysis stack frames + InnerException chain + a regex-extracted "Object :: Method [Reason]" list.

**Footgun:** never use `--bundled-experiment` to diagnose bundled-mode bugs —
it skips `SetResolvedDeps` so library codeunits look missing and BC's emitter
NREs on unresolved overloads. Always use real `--bundled`.

---

## §9. Operating notes

- **Don't commit decompiled MS IP.** `ilspycmd` output stays in `/tmp/`.
- **Commit-signing flake:** 1Password SSH agent occasionally drops with
  `error: 1Password: failed to fill whole buffer`. Re-run usually works on the
  second try. **Do not bypass signing without explicit user authorization** —
  past authorizations don't carry across sessions.
- **One commit per logical step** so we can bisect.
- The pre-existing v2 file split (`BcRuntime.cs` partials, `RecordPatches.cs`
  partials, `Patches/*.cs`) stays. JMP-hook infrastructure stays.

---

## §10. Subagent delegation pattern

The multi-step iterative work in this spike (patch loops, renumbering, sentinel
investigation, bucket migrations) delegates well to background agents. Brief
shape that worked:

- **Self-contained.** No expected back-and-forth.
- **Hard binary-compat constraint stated up front + STOP conditions.**
- **One commit per logical step** so we can bisect.
- **Final report ≤300 words.**
- **Sonnet by default, Opus only for genuinely architectural / cross-file
  reasoning tasks.** Iterative fix-loop work is Sonnet-shaped.

The "brain" role (this top-level session) reviews every commit before
greenlighting the next bucket: no type renames, no AL source rewrites that hide
runner gaps, no `_shared/` stubs that silently shadow MS-shipped code, per-suite
parity actually holds. If anything drifts, roll back rather than continue.

---

## §11. Pickup guide for a clean session

**Reading order:** §1 → §2 (especially invariant #0 + the linked
`.claude/rules/precompiled-dll-respect.md`) → §4 → §6 → §5. ~5 min.

**Working dir:** `/home/stefan/Documents/Repos/community/BusinessCentral.AL.Runner`.
**Branch:** `spike/bc-abi-identity`. Don't push, don't PR. Commits this
session were made unsigned (`-c commit.gpgsign=false`) under explicit
per-session user authorization — that authorization does NOT carry across
sessions; for new sessions either retry signed or get fresh authorization.

**Smoke test all five buckets:**
```bash
dotnet build spike/v2/Runner/Runner.csproj
for b in tests/bucket-1/codeunit-runtime tests/bucket-1/record-table \
         tests/bucket-2/data-formats tests/bucket-2/page-report \
         tests/spike-a-baseapp; do
  echo "=== $b ==="
  dotnet run --project spike/v2/Runner --no-build -- "$b" 2>&1 | grep -E "→ "
done
```
Expected (post-commit `de0bad05`):
```
bucket-1/codeunit-runtime  → 722P/269F/0E across 991 tests, ~80s
bucket-1/record-table      → 532P/364F/0E across 896 tests, ~133s
bucket-2/data-formats      → 1288P/271F/0E across 1559 tests, ~60s
bucket-2/page-report       → 235P/382F/0E across 617 tests, ~57s
tests/spike-a-baseapp      → 8P/0F/0E across 8 tests, ~40s
total: 2785 / 4071 = 68.4%
```
Any deviation = regression — investigate before adding new patches.

**With AL-output cache (corpus-validated)** — first run populates, second
run hits cache and skips compile (~1.4–2.4× speedup):
```bash
dotnet run --project spike/v2/Runner --no-build -- \
    --cache /tmp/al-cache <bucket-path>
```

Then pick the top open thread from §6 (Tier 1B is the highest-payoff sync
work; Tier 1C — §5.3 EventPipe deployment — is the highest-leverage but
multi-day mechanism investment). Delegate per §10.

For older session chronology, decisions, and the patch-by-patch arc that got us
here, see `HANDOFF-archive.md`. It's preserved verbatim from the previous
HANDOFF.md (sections §B–§R + the original §3–§10).
