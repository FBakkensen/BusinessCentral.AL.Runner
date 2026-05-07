# AlRunner v2 — failure classification & parallelizable work plan

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
