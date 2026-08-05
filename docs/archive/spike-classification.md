# AlRunner v2 — failure classification & parallelizable work plan

> **STATUS UPDATE — 2026-05-07 session.** This document is partly historical. The
> sections marked below are SUPERSEDED by findings from the 2026-05-07 work; treat
> this header as the source of truth and the older sections as preserved context.
>
> ## Architectural pivot — superseding W-5 (and large parts of v2's premise)
>
> The original v2 premise was "AL→C# via `--dump-csharp` → unmodified Roslyn compile
> against real BC DLLs → JMP-hook patches". That was validated for pure-compute AL
> code, but breaks for AL code that uses `var` parameters with non-handle types
> (`var Foo: Code`, `var Bar: Boolean`, `var Baz: RecordRef`, …). The reason: the
> AL compiler's `--dump-csharp` taps an **intermediate** form. BC's service tier
> applies a final post-emit rewriter at extension install time — adding `ByRef<T>`
> wraps for value-typed `var` parameters, rewriting `OnInvoke` dispatch, and
> wrapping call-site arguments as `new ByRef<T>(getter, setter)` where the callee
> expects `ByRef<T>`.
>
> Verified by inspecting Microsoft-shipped pre-compiled DLLs: 8,373+ `ByRef<>`
> occurrences, ZERO `[NavByReferenceAttribute]` survives into the final DLL for
> non-handle types. The rule encoded in BC's emitter (`ParameterSymbol.ShouldBe-
> PassedByRef`): wrap iff `IsVar && !IsArray && !IsUserType` (UserType = Codeunit /
> Page / Report / Record / Table / Query / Interface / XmlPort / TestPage). Handle
> types preserve `[NavByReference] T` as-is.
>
> ### The pivot v2 is taking
>
> Replace the `AlRunner --dump-csharp` subprocess + custom Roslyn compile with a
> **direct in-process call to `Microsoft.Dynamics.Nav.CodeAnalysis.Compilation.Emit()`**.
> That is the same API the BC service tier uses at extension install time — it
> performs the rewrites natively and emits a final DLL. v2 then loads the DLL bytes
> and runs tests under JMP-hook-patched skeleton sessions.
>
> Net: v2 does **zero C# rewriting itself**. BC's compiler does it all. The earlier
> `ByRefWrapRewriter` (committed in `Rewriters/ByRefWrapRewriter.cs`) is preserved
> as documentation of what BC does, but is NOT in the current pipeline path. It
> will be deleted once `Compilation.Emit` integration lands.
>
> This is the same direction W-5 anticipated, but elevated from "post-stabilisation
> performance polish" to "the architectural answer for correctness AND speed".
>
> ## Suite-discovery fix (2026-05-07 morning)
>
> `Program.cs::ExpandBucket` no longer requires `al-runner.json`. Suites are
> discovered by `src/`+`test/` directory presence (matches the existing CI loop in
> `.github/workflows/test-matrix.yml`). Visible tests across `tests/bucket-1/` +
> `tests/bucket-2/` jumped from 809 (al-runner.json-only) to **3,628** (full
> corpus). Pre-discovery measurements in this document below are on the smaller
> visible set; they understate corpus coverage but do not affect classifications.
>
> ## Bundled-bucket compile (in flight)
>
> Aligning v2's compilation scope to v1's: every top-level arg (typically
> `tests/bucket-N/`) becomes ONE compilation unit — every suite's `src`/`test`/
> `app*` dirs collected and fed in one pass. Confirmed against v1: v1 builds ONE
> Roslyn `Compilation` per multi-folder invocation (`Pipeline.cs:1626`/`:772`).
> `--test-isolation method` is just a runtime state-reset flag, not a compilation
> boundary.
>
> Once `Compilation.Emit` lands, this becomes "one BC compilation per top-level
> arg → one DLL → one in-process load + run".
>
> ## Patches landed in this session
>
> All under `BcRuntime.cs` / `Patches/*.cs` (now per-concern partial files):
> - `NavRecord.InsertAsync` (4-arg) → bypass triggers/events, route to
>   `RecordImpl.InsertRecordAsync` directly.
> - `RecordImpl.InternalFindRecordWithoutCheckingValuesAsync` → thin
>   TryGetByPrimaryKey passthrough; bypasses NRE-prone fallback branch.
> - `NavServerEventSource.WritePermissionUncheckedEvent` + `get_Log` →
>   no-op + skeleton EventSource singleton.
> - `RecordImpl.VerifySecurityFiltersOnRecordAsync` / `VerifySecurityFiltersAsync`
>   → completed-`ValueTask` no-ops.
> - `NavSession.IsLocalLanguage` → `false`.
> - `NavSession.GetSecurityFilters` → `null` (matches IsPermissionSystemEnabled=false branch).
> - `NavMethodScope.AssertError(Action)` → run body, invert pass/fail; skip session.Rollback.
> - `NavSession.PushDynamicCaptionStack` → no-op.
> - `NavSession.SortingProperties` + skeleton DB `sqlSortingProperties` poke →
>   unblocks `RecordBufferComparer.Compare` inside `TempTableDataProvider.Insert`.
>
> ## Latest measured pass rate (per-suite-subprocess, OLD architecture pre-bundle)
>
> | Bucket | Suites | Ran | Compile-fail | Exec-fail | Tests | Pass | Fail | % |
> |---|---|---|---|---|---|---|---|---|
> | bucket-1 | 349 | 278 | 6 | 65 | 1,485 | 730 | 755 | 49% |
> | bucket-2 | 312 | 297 | 2 | 13 | 2,143 | 1,401 | 742 | 65% |
> | **Total** | **661** | **575** | **8** | **78** | **3,628** | **2,131** | **1,497** | **59%** |
>
> Existing v1 AlRunner reports 4,281 passing across the same corpus. v2 covers
> ~50% of that today; gap is split between visible-but-failing (~1,497) and
> suites whose AL emit subprocess fails entirely (~78 of 661 suites).
>
> ## File layout (post-2026-05-07 split)
>
> `BcRuntime.cs` 1541 → 597 lines; `Patches/RecordPatches.cs` 804 → 487; replacement
> methods extracted into per-concern partials (`Patches/HelperShims.cs`,
> `EnvironmentPatches.cs`, `SessionPatches.cs`, `MethodScopePatches.cs`,
> `ApplicationObjectBasePatches.cs`, `CodeunitPatches.cs`, `RecordWritePatches.cs`,
> `TelemetryPatches.cs`, `MiscPatches.cs`); `RecordPatches.AlSourceParser.cs` and
> `RecordPatches.NclMetaTableBuilder.cs` separate. JMP-hook + field-poke moved to
> `Infrastructure/`. See `HANDOFF.md` for the full layout.
>
> ## Where to read for current state
> - `spike/v2/HANDOFF.md` — the live entry point for any future agent.
> - This file's "## Architecture summary" and the work-item bodies (W-1…W-8) are
>   STALE for the patch-count and file-layout details, but the work-item INTENT
>   (what each W-N covers and why) remains valid as a roadmap.
>
> ──────────────────────── original document below ────────────────────────

## Status snapshot (post W-1, W-1.5, W-2 + Opus subsystem analysis)

| Mode | Pass | Fail | Total | Notes |
|---|---|---|---|---|
| Baseline (no patches) | 0 | 791 | 791 | original v2 corpus run |
| After W-2 only | 4 | 161 | 165 | bucket-1/codeunit-runtime |
| After W-1 + W-2 | 6 | 155 | 161 | bucket-1/codeunit-runtime, single process |
| After W-1.5 + W-1 + W-2, **single process** | 21 | 140 | 161 | state pollution caps progress |
| After W-1.5 + W-1 + W-2, **per-bucket subprocess** | **95** | **66** | **161** | **59% pass rate** |

The subprocess-isolated run is the current effective mode. State-pollution isolation (W-1.6) is the next bottleneck — solving it lifts the single-process number toward 95.

## Executive summary

Ran the new v2 pipeline (AL→C# via `--dump-csharp` → unmodified Roslyn compile against real BC DLLs → JMP-hook patches → load + execute) across most of the existing AL test corpus.

| Bucket | Buckets ran | Tests run | Pass | Fail | Compile-fail buckets |
|---|---|---|---|---|---|
| `bucket-1/codeunit-runtime` | 35 | 161 | 0 | 161 | 2 |
| `bucket-1/record-table` | 25 | 109 | 0 | 109 | 0 |
| `bucket-2` | 57 | 521 | 0 | 521 | 1 |
| **Total** | **117** | **791** | **0** | **791** | **3** |

**The headline number is the failure classification: 99.6% of the 791 test failures are one of two root causes.** The pipeline itself works — compilation succeeds, tests load, execution starts, code runs. Failures concentrate at two specific BC service-tier APIs that need targeted patching.

## Root-cause distribution

```
790 / 791   runtime failures classified into 2 layers:
  697       runtime/method-scope          (NavMethodScope..ctor null deref)
   93       runtime/navglobal-systemtenant (NavGlobal.get_SystemTenant returns null)
    4       process-error                 (AL emit subprocess transient)

 3 / 117   buckets fail at compile time:
   3       compile/signature-mismatch    (ConvertToDotNetFormatString overload)
```

These three classifications are entirely independent of each other. **Three parallel agents could work them simultaneously with zero coordination.**

## Performance

```
v2 wall-clock per bucket — measured today: ~2.0s
  AL→C# emit subprocess: ~1.8s (the bottleneck — `dotnet AlRunner.dll --dump-csharp` startup + emit)
  Roslyn compile:        ~0.2s   (C# → IL against real BC DLLs)
  test execution:        ~0.02s  (every Test method in the bucket combined)

v2 wall-clock per bucket — projected after in-process AL emit (W-5): **~0.3s**
  AL→C# emit (in-process):    ~0.08s
  Roslyn compile:             ~0.2s
  test execution:             ~0.02s
```

**Projection: a single test bucket (one al-runner.json directory, e.g. `01-pure-function` with its 6 test methods) runs in ~0.3 seconds.** That's everything for one bucket: AL→C# emission, Roslyn compile against real BC DLLs, JMP-hook-patched runtime, and per-test invocation. Test execution itself is already ~0.02s/bucket; the only meaningful work left is the compile pipeline. The 0.3s projection is grounded in measured numbers — the only changing variable is replacing a subprocess (`dotnet AlRunner.dll`, ~1.8s of which ~1.6s is JIT/init overhead) with an in-process call to `Microsoft.Dynamics.Nav.CodeAnalysis.Compilation.Emit`. That subprocess is currently the only reason a bucket takes seconds rather than fractions of a second.

For reference, the existing AL Runner takes ~5–10s per single bucket end-to-end. The v2 approach is on track for **~30× faster per bucket** post-W-5. (The full corpus of ~117 buckets remains a sequential aggregate — running everything in parallel is a separate scaling discussion.)

## Work-item breakdown for parallel execution

### W-1: Patch `NavMethodScope..ctor` chain (~697 tests, 88% of failures)

**Symptom:** `System.NullReferenceException at NavMethodScope..ctor(NavApplicationObjectBase, MethodScopeFlags, Boolean)`

**Stack pattern:**
```
at NavMethodScope..ctor(NavApplicationObjectBase, MethodScopeFlags, Boolean)
at NavMethodScope..ctor(NavApplicationObjectBase, Boolean eventSource)
at NavMethodScope`1..ctor(TParent, Boolean)
at NavMethodScope`1..ctor(TParent)
at <Codeunit>.<TestMethod>_Scope_<n>..ctor(<Codeunit> βparent)
at <Codeunit>.<TestMethod>()
```

**Existing partial patches** (in `BcRuntime.cs`):
- `NavApplicationObjectBase.get_Session` → skeleton NavSession ✓
- `NavSession.get_CurrentMethodScope` → root tree stub ✓
- `NavSession.VerifyExecutePermission` → no-op ✓
- `NavMethodScope.ThrowStackOverflow` → no-op ✓
- `NavCancellationToken.Throw{If,}OperationCanceledException` → no-op ✓

**Why some test paths still NRE:** the spike's Discount Calculator test reaches OnRun successfully, but ~88% of test corpus tests use `NavApplicationObjectBaseHandle<T>` (codeunit-as-variable pattern) which hits a different NavMethodScope ctor path that dereferences another field we haven't initialized yet.

**Investigation prompt for agent:** decompile `NavMethodScope..ctor(NavApplicationObjectBase, MethodScopeFlags, Boolean)` (use ILSpy locally — read-only reference). Identify EVERY field/property dereference between method entry and the existing `applicationObject.Session.CurrentMethodScope` read. Add JMP hooks for any that return null on a skeleton instance. Test against `tests/bucket-1/codeunit-runtime/14-assert-130000`.

**Done when:** `tests/bucket-1/codeunit-runtime/14-assert-130000` runs to assertion outcome (not NRE).

### W-2: Patch `NavGlobal.SystemTenant` / `NCLMetadata` (~93 tests, 12%)

**Symptom:** `NullReferenceException at NavGlobal.get_SystemTenant() → NavGlobal.get_NCLMetadata() → NavCodeunitHandle.CreateTarget()`

**Stack pattern:**
```
at NavGlobal.get_SystemTenant()
at NavGlobal.get_NCLMetadata()
at NavCodeunitHandle.CreateTarget()
at NavApplicationObjectBaseHandle`1.get_Target()
at <Codeunit>.<TestMethod>_Scope_<n>.OnRun()
```

This fires when the test body calls a method on a codeunit-as-variable (e.g. `DiscountCalc.ApplyDiscount(...)`). The handle's lazy `Target` property goes through `NavGlobal.NCLMetadata` to look up codeunit metadata.

**Approach options for agent:**
- (a) Hook `NavGlobal.get_NCLMetadata` (or get_SystemTenant) to return a populated stub.
- (b) Hook `NavCodeunitHandle.CreateTarget` to construct codeunits directly (bypass NavGlobal). This is closer to what AL Runner's existing AlScope already does — likely the fastest win.

**Done when:** `tests/bucket-1/codeunit-runtime/01-pure-function/test/CalculatorTest:TestApplyDiscount_10Percent` returns `Pass`.

### W-3: Compile-time `ConvertToDotNetFormatString` overload mismatch (3 buckets)

**Symptom (compile error):**
```
ConvertToDotNetFormatString takes 2 arguments
```

The AL compiler 17.0.34 emits `ConvertToDotNetFormatString(arg1, arg2)` but the BC 27.5 service-tier method only has 1- and 3-arg overloads (or similar mismatch).

**Investigation prompt for agent:** check `Microsoft.Dynamics.Nav.*.dll` for `ConvertToDotNetFormatString` overloads at BC 27.5. If a 2-arg overload doesn't exist, add a polyfill in `BcAssembler.PolyfillSource` that delegates to whichever overload does exist.

**Done when:** `tests/bucket-1/codeunit-runtime/141-add-dataset` and `tests/bucket-2/page-report/82-pageextension` compile.

### W-4 (parallel-friendly, after W-1/W-2): tighten Pass/Fail/Error classification

Currently all thrown exceptions in `TestExecutor` are categorized as `Fail`. Many of the W-1/W-2 NREs would actually be `Error` (test infrastructure didn't run) rather than `Fail` (assertion failed). Once W-1/W-2 land, expect to see a third category: actual `Assert.AreEqual` failures or successes.

Discovery hook: BC's `Assert.*` helpers throw `NavAssertionException` (or similar). Mapping from exception type to outcome should live in a small lookup in `TestExecutor.RunOne`.

### W-5 (post-success): in-process AL→C# emit

Replace the `AlRunner --dump-csharp` subprocess with direct `Compilation.Emit` calls. Eliminates ~1.8s per bucket. Requires referencing `Microsoft.Dynamics.Nav.CodeAnalysis` from v2 directly. Estimated 100 lines of new code in a `AlEmitter` rework.

## Architecture summary

```
spike/v2/Runner/
├── Runner.csproj         references real BC service-tier DLLs
├── Program.cs            CLI: bucket discovery + orchestration (~100 lines)
├── AlEmitter.cs          subprocess → AlRunner --dump-csharp + parse (~75 lines)
├── BcAssembler.cs        Roslyn compile against real BC DLLs (~80 lines)
├── BcRuntime.cs          JMP-hook patches at process start (~250 lines)
├── Win32Stubs.cs         P/Invoke resolver for kernel32/user32/... (~70 lines)
├── TestExecutor.cs       discover [NavTest], invoke, capture (~80 lines)
└── Reporter.cs           classification + JSON output (~120 lines)
```

**Total: ~775 lines.** Replaces (eventually) `AlRunner/RoslynRewriter.cs` (~3500 lines) and `AlRunner/Runtime/AlScope.cs` (~3500 lines) plus the rewriter-related portions of `Pipeline.cs` and `Program.cs`. Net reduction in production AL Runner: estimated **~6000 lines** once W-1/W-2 land and the migration completes.

## W-8 (DONE 2026-05-11): trigger dispatch + event-subscriber dispatch

Landed across five commits:

- `ae15b158` — Insert drain (drop bypass + walk inherited `objectId` field + populate `NavCompany.trackChanges`).
- `c2df0bcd` — Delete + Rename drain (+ `RecordLink.MoveLinksAsync` no-op + `NavRecord.UpdateReferencesOnRenameAsync` no-op).
- `f8367536` — `NavMethodScope` 500-frame bounded-depth recursion guard (`NavNCLDialogException("Maximum recursion depth")` matching BC's runtime-error contract).
- `29b5acc9` — Modify drain (predicated on recursion guard catching `Codeunit108002`).
- `c4bce11a` — Event-subscriber dispatch via A-prime (populate BC's own `NavTableTriggerEventHandler.eventScopes[evt].registeredSubscriptions` with real `NavEventSubscription` instances; BC's own dispatcher fires them — no JmpHook on the dispatch path because of the R2R-inlining trap that killed A-base, see `feedback_r2r_inlining_traps.md`).

Outcomes:
- Canary `tests/bucket-1/record-table/100-uninit-field-fix` 1P/2F → **3P/0F**.
- Bucket-1/record-table 600P (day start) → **655P** (+55).
- Corpus 2987P → **3044P / 4071 = 74.8%**.
- Pre-W-8 corpus pass counts were misleading on every trigger/subscriber-dependent test; post-W-8 numbers are honest.

The original W-8 design from this doc (`IsTemporary` lie + trigger dispatch confirmation + Commit no-op) understated the work — actual scope included building a v2-side subscriber-discovery + registry + injection layer because v2 had no event-subscriber dispatch at all (`AlCompat.FireEvent` was v1-only). Net effort ~1 day of brain + delegated implementation, ~600 LOC across `EventSubscriberPatches.cs` (NEW), `RecordWritePatches.cs`, `RecordPatches.NclMetaTableBuilder.cs`, `RecordPatches.NclMetadataCachePopulator.cs`, `MethodScopePatches.cs`, `BcRuntime.cs`, `TestExecutor.cs`.

### What's still open after W-8

- **ALDatabase NRE cluster** — two attempts segfaulted. See `feedback_aldatabase_hard.md`.
- **Manual-binding subscribers** (`BindSubscription`) — auto-binding works; manual deferred as a follow-on.
- **W-7 test isolation** — still pending (the other half of the "honest pass count" story; per-test write-log rollback + isolation-mode CLI flag).

## W-7 (deferred design): test isolation modes + init handlers + write-log rollback

**Status:** designed, not built. Track here so it's not forgotten.

BC's standard test framework supports configurable isolation per test suite — each AL test file declares (or the runner is configured for) one of:

| BC mode | What BC does | v2 equivalent |
|---|---|---|
| `Test Runner` (codeunit 130450, default) | Wraps each `[Test]` method in a transaction scope; rolls back at end | `--isolation per-test` — reset BC singleton fields between every Test method |
| `Test Runner - Isol. Disabled` (codeunit 130451) | No isolation, all tests share state, used in BCApps CI with `renewClientContextBetweenTests` | `--isolation disabled` — current single-process behavior |
| `Test Suite` (codeunit 130459) | Per-suite isolation (looser than per-test) | `--isolation per-codeunit` — reset between buckets |

**AL tests are written against a specific mode.** A test that assumes per-function rollback will leak state to the next test under `disabled`. A test that depends on a previous test's setup will fail under `per-test` reset.

### What v2 needs

1. **CLI flag:** `--isolation per-test|per-codeunit|disabled`. Default to `per-test` to match BC's standard runner.
2. **Per-bucket override** read from `al-runner.json` (same knob the existing AL Runner already exposes — should propagate verbatim).
3. **Write-log rollback in `RecordPatches`.** Every `Insert/Modify/Delete` through our hooked `IDataAccess` appends to a per-test `(table, key, prev-value-or-null)` log. At test boundary (per `IsolationLevel`), replay log in reverse to restore state. ~150 lines. Same algorithm `AlScope.cs` uses today.
4. **`BcRuntime.ResetState(IsolationLevel)`** — re-pokes the appropriate field set:
   - `PerTest`: smallest set — `_skeletonSession.<CurrentMethodScope>k__BackingField`, `_skeletonRootScope.<StackDepth>k__BackingField`, any error/diagnostic state, replay write log
   - `PerCodeunit`: superset — also reset accumulated child-scope linked lists, codeunit type cache, `NavEnvironment.instance` mutable fields
   - `Disabled`: no-op
5. **Init handler discovery and invocation in `TestExecutor`.** AL test buckets register codeunits via `[EventSubscriber]` against `Codeunit::"Test Runner"` events:
   - `OnBeforeTestSuiteRun` — invoked once before the bucket's tests run; this is where master-data seed lives
   - `OnBeforeTestRun` / `OnAfterTestRun` — per-test setup/teardown
   - `OnAfterTestSuiteRun` — final cleanup
   Plus the existing AL Runner's `tests/init-events/` codeunit-list mechanism for older tests. Both must be honored. ~200 lines + small event dispatcher.
6. **`TestExecutor`** invokes init handlers and `BcRuntime.ResetState(level)` at the right boundaries:
   - bucket start: `OnBeforeTestSuiteRun` handlers
   - per `[Test]` method: `OnBeforeTestRun` → run test → `OnAfterTestRun` → reset state if isolation requires
   - bucket end: `OnAfterTestSuiteRun`
7. **Subprocess fallback** stays available as `--isolation subprocess` for buckets that genuinely need full process isolation (e.g. tests that touch BC types we haven't yet identified as polluters).

### Why this is deferred

W-1.6 found the actual cross-bucket *runtime-state* pollution root cause was `<TieredCompilation>false</TieredCompilation>`, not field mutation. With tiered comp off, the JMP-hooks stay live for the full run and most pollution disappears.

But W-7 / W-8 cover a **different** pollution problem: **AL-test-level state pollution.** Tests writing records that bleed into the next test produce wrong outcomes, sometimes silently passing tests that would fail under BC's default `Test Runner`. This is the contract AL test authors actually depend on, and v2 today is silently in `Isol. Disabled` mode. **It's a correctness gap that affects pass-rate accuracy, not pass-rate volume.** A test passing in v2 today doesn't necessarily mean the AL code is correct under standard BC test semantics.

### Done criteria when implemented

- `--isolation per-test` runs every test in the same effective state as the existing AL Runner's per-function isolation
- AL tests with `// IsolationLevel = Disabled` comments / config respect that
- Pass rate parity with the existing AL Runner on a representative bucket sample

### Estimated effort

M (1 week, combined with W-8). Components:
- Write-log + rollback in `RecordPatches`: ~150 LOC, well-defined algorithm.
- Init-handler discovery + invocation: ~200 LOC, follows BC's `[EventSubscriber]` pattern.
- ResetState: ~100 LOC of field-poke wiring, fields already enumerated in BcRuntime.cs.
- CLI flag + `al-runner.json` propagation: ~50 LOC.
- W-8's `IsTemporary` lie + trigger dispatch: ~50–100 LOC, depends on what `runTrigger=true` actually triggers in BC IL when the provider is temp-backed.

**Critical for production-readiness, not pass-rate alone.** Without it, v2's pass numbers are misleading — they include tests that would fail if isolated correctly.

## Suggested work cadence

1. **Now → W-1 + W-2 in parallel** (two impl agents). Each is bounded by ILSpy-aided introspection of one BC type. ~1 day each.
2. **After W-1/W-2 land:** rerun the full corpus, capture new classification. Likely surfaces a third tier of issues — record-table tests will hit `NavRecord` operations needing in-memory storage. That's W-6+ territory.
3. **W-3** is one-off, low-priority, queue at any point.
4. **W-4** can proceed alongside W-1/W-2 — pure post-processing logic.
5. **W-5** post-stabilization, performance polish.

## Files to read first (for any agent picking up W-1/W-2)

- `spike/v2/Runner/BcRuntime.cs` — existing patches, JMP-hook plumbing
- `spike/v2/Runner/TestExecutor.cs` — how tests are discovered and invoked
- `spike/bc-abi-identity/FINDINGS.md` — original 18-layer trace from the spike (context for why each existing patch exists)
- `~/Documents/Repos/community/bc-linux/src/StartupHook/StartupHook.cs` — reference for the JMP-hook technique and many specific patch ideas
