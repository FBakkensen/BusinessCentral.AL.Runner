# AL Runner v2 — handoff (2026-05-07)

This is the live entry point for any agent or human picking up the v2 spike.
Read this first. Other docs in this folder (`CLASSIFICATION.md`,
`RECORD-GATE.md`, `SUBSYSTEMS.md`, `../bc-abi-identity/FINDINGS.md`) are
historical analysis with status-update prefaces; their bodies are preserved
context and may be partly superseded — trust the prefaces and this file.

## Mission

Replace the existing `AlRunner/` (43,087 lines, depends on `RoslynRewriter.cs` +
`Runtime/AlScope.cs` for AL→C# rewriting and a v1 runtime substitution) with a
small v2 that:

- compiles AL via Microsoft's own `Microsoft.Dynamics.Nav.CodeAnalysis.Compilation.Emit()`
  (same API the BC service tier uses at extension install time),
- loads the resulting DLL directly into the test process,
- runs `[Test]` methods against unmodified Microsoft IL under JMP-hook-patched
  skeleton sessions (no v1 runtime substitution).

End state: `spike/v2/Runner/` becomes the new `AlRunner/`. v1's rewriter and
runtime are deleted. Migration plan in §6 below.

## Current state

- **Branch:** `spike/bc-abi-identity` of `~/Documents/Repos/community/BusinessCentral.AL.Runner`.
- **Recent commits:** `git log --oneline -15` for full history. Key commits this session:
  - `df15edd3` — discovery fix (`al-runner.json` optional) + Infrastructure split
  - `55ca1f5b` — top failure-class patches (IsLocalLanguage, GetSecurityFilters, AssertError, PushDynamicCaptionStack)
  - `ee77cc68` / `2fb4030c` / `9b73a958` — file split (`BcRuntime.cs` 1541→597, per-concern partials, `RecordPatches.cs` three-way split)
  - `c7a6f2e6` — W-6 record-write redirect (`NavRecord.InsertAsync` bypass)
  - `4115821` — bundled emit/compile experiment + `ByRefWrapRewriter` (now superseded; see §3)
- **Pass rate (per-suite-subprocess, OLD architecture pre-bundle):** **2,131 / 3,628 (59%)** corpus-wide; v1 reports 4,281 passing on the same corpus.
- **Wall time (corpus-wide):** ~24 min in parallel (2 buckets × ~12 min). 88 % of that is AL emit subprocess overhead. With `Compilation.Emit` integration this drops to ~2 min.

## File layout

```
spike/v2/Runner/
├── Runner.csproj         references Microsoft.Dynamics.Nav.* DLLs from artifacts
├── Program.cs            CLI: bundle discovery + orchestration
├── BcAssembler.cs        ★ TO BE REPLACED by direct Compilation.Emit (see §3)
├── AlEmitter.cs          ★ TO BE REPLACED — currently a --dump-csharp subprocess wrapper
├── TestExecutor.cs       discover [NavTest], invoke, capture
├── Reporter.cs           failure classification + JSON output
├── Win32Stubs.cs         P/Invoke resolver for kernel32/user32/...
├── BcRuntime.cs          patch-registration dispatcher (ApplyAllPatches)
├── Infrastructure/
│   ├── JmpHook.cs            x86-64 JMP-patch via mprotect (FixupPrecode-aware)
│   ├── FieldPoke.cs          reflection-set on private/readonly fields
│   └── RootTreeObject.cs     skeleton ITreeObject for the root scope
├── Patches/
│   ├── HelperShims.cs                    NoOp/ReturnX/ReturnValueTask shims
│   ├── EnvironmentPatches.cs             NavEnvironment cctor + ServiceAccount
│   ├── SessionPatches.cs                 NavSession property/method NREs
│   ├── MethodScopePatches.cs             NavMethodScope ctor + AssertError
│   ├── ApplicationObjectBasePatches.cs   NavApplicationObjectBase ctor + TryInvoke
│   ├── CodeunitPatches.cs                NavCodeunit.DoRunAsync + Handle.CreateTarget + cache
│   ├── RecordWritePatches.cs             NavRecord.InsertAsync + InternalFind +
│   │                                     ApplyRecordPatches dispatcher
│   ├── TelemetryPatches.cs               NavServerEventSource + NavDialog + diagnostics
│   ├── MiscPatches.cs                    ALSession + NCLEnumMetadata
│   ├── RecordPatches.cs                  data-access plumbing (skeleton DAS,
│   │                                     TempTableDataProvider ctor, hook impls)
│   ├── RecordPatches.AlSourceParser.cs   AL .al regex parser → ParsedTable
│   └── RecordPatches.NclMetaTableBuilder.cs  ParsedTable → NCLMetaTable
└── Rewriters/
    └── ByRefWrapRewriter.cs              ★ DOCUMENTATION ARTIFACT — see §3
```

★ **The marked files (BcAssembler.cs, AlEmitter.cs, Rewriters/ByRefWrapRewriter.cs)
are scheduled for removal once the in-process `Compilation.Emit` integration lands.**
The rewriter is preserved as documentation of what BC's compiler does internally.

`BcRuntime` is a `public static partial class` whose pieces fold across all
files in `Patches/`. `RecordPatches` is similarly partial across its three
files. The hook installer (`Hook(method, replacementName, ...)` in `BcRuntime.cs`)
looks up the replacement by name on `typeof(BcRuntime)` — every partial file
contributes to that lookup.

## §3. Architectural pivot — direct Compilation.Emit (in flight)

### Why

The original premise "AL→C# via `--dump-csharp` → Roslyn compile against real BC
DLLs → no C# rewriting" works for pure-compute AL but fails for AL var-params
with non-handle types. Concrete: `--dump-csharp` taps the AL compiler at an
intermediate stage where parameters are still annotated `[NavByReferenceAttribute] T`
instead of being typed `ByRef<T>`. BC's service tier completes that rewrite at
extension install time:

- `[NavByReference] T` → `ByRef<T>` for value types (NavCode, NavText, bool, int,
  NavDate, NavRecordRef, NavList<T>, NavDictionary<...>, NavOption, …).
- `[NavByReference] *Handle` types (NavCodeunitHandle, INavRecordHandle, NavInterfaceHandle,
  NavStream, NavTestPageHandle, NavReportHandle, NavQueryHandle, NavPageHandle)
  preserved as-is (Handle types are already indirect references).
- `OnInvoke` dispatch sites change from `ALCompiler.ObjectToExactT(args[i])` to
  `(ByRef<T>)ALCompiler.SafeCastCheck<ByRef<T>>(args[i])` for wrapped params.
- Call-site arg unboxing wraps in `new ByRef<T>(getter-lambda, setter-lambda)`
  when the called method expects `ByRef<T>` and the arg is plain `T`.

The exact rule lives at `Microsoft.Dynamics.Nav.CodeAnalysis.dll`,
`ParameterSymbol.ShouldBePassedByRef = IsVar && !IsArray && !IsUserType`. The
emitter at `EmitParameterType` / `EmitMethodScopeFieldType` / `EmitArgumentExpression`
applies the rewrites. Microsoft's pre-compiled System Application 27.5 DLL
proves the convention: 8,373+ `ByRef<>` occurrences, 1,166 `[NavByReference] *Handle`
occurrences, ZERO `ByRef<*Handle>` and ZERO `[NavByReference] T` for non-handle types.

### What the pivot looks like

Replace this:
```
AL source folders → AlRunner --dump-csharp (subprocess) → pre-rewrite C# →
                    BcAssembler (Roslyn compile + ByRefWrapRewriter +
                    PolyfillSource) → DLL bytes
```

With this:
```
AL source folders → BC's Compilation.Emit (in-process) → DLL bytes
                    (BC's compiler applies the rewrites natively;
                     no v2 rewriter, no polyfill source-redirect needed
                     for cases BC handles, no Roslyn compile in v2)
```

v2 then loads the DLL bytes and runs tests under JMP-hook patches as today.

### Why this is in scope

Per the user's directive: "the only thing that we are changing here is that
we're not rewriting C# anymore" — going through BC's own compiler means BC does
the rewriting (which is in scope as "what the service tier does at install time"),
v2 does none. This matches v2's purity goal and the "consume Microsoft's DLLs as-is"
intent.

### Status

A research sub-agent is mapping BC's `Compilation.Emit` API surface — public
entry points, syntax-tree creation, options, references. When that report
lands, the implementation is concretely:

1. Reference `Microsoft.Dynamics.Nav.CodeAnalysis.dll` in `Runner.csproj`.
2. Replace `AlEmitter.cs` and `BcAssembler.cs` with a single ~50-line
   `BcCompiler.cs` that calls `Compilation.Create(...)` + `AddSyntaxTrees` +
   `Emit(stream)` for each top-level bundle.
3. Delete `Rewriters/ByRefWrapRewriter.cs` (BC compiler does this natively).
4. Reduce `BcAssembler.PolyfillSource` to only the cases BC's emitter doesn't
   handle (likely zero or near-zero).
5. Verify smoke test on the previously-broken 1239-dict-codeunit-value bundle.
6. Re-run corpus, capture new pass rate.

## §4. Patches landed in this session

All under `BcRuntime.cs` / `Patches/*.cs`:

- `NavRecord.InsertAsync` (4-arg) → bypass triggers/events, route to
  `RecordImpl.InsertRecordAsync` directly.
- `RecordImpl.InternalFindRecordWithoutCheckingValuesAsync` → thin
  `TryGetByPrimaryKey` passthrough; bypasses NRE-prone fallback branch
  (Session.CurrentMethodScope.ApplicationObject is null on root scope).
- `NavServerEventSource.WritePermissionUncheckedEvent` + `get_Log` →
  no-op + skeleton EventSource singleton.
- `RecordImpl.VerifySecurityFiltersOnRecordAsync` / `VerifySecurityFiltersAsync`
  → completed-`ValueTask` no-ops.
- `NavSession.IsLocalLanguage` → `false`.
- `NavSession.GetSecurityFilters` → `null` (matches IsPermissionSystemEnabled=false branch).
- `NavMethodScope.AssertError(Action)` → run body, invert pass/fail; skip session.Rollback.
- `NavSession.PushDynamicCaptionStack` → no-op.
- `NavSession.SortingProperties` + skeleton DB `sqlSortingProperties` poke →
  unblocks `RecordBufferComparer.Compare` inside `TempTableDataProvider.Insert`.

All confirmed against the failure classification from the corpus run; collectively
these moved bucket-1 from earlier baselines toward the current 730/1485 (49 %).

## §5. Top remaining failure classes

From the most recent corpus classification (per-suite-subprocess, OLD architecture);
re-classify after the `Compilation.Emit` pivot lands.

| Count | Class | Notes |
|---|---|---|
| 114 | `NavGlobal.get_NCLMetadata` | Triggered from Form/Report/Query handle's `CreateTarget`. Per-handle-type `CreateTarget` hook (same shape as existing `NavCodeunitHandle_CreateTarget` and `NavRecordHandle_CreateTarget`). |
| 65 | `process-error` | Bucket-level AL-emit subprocess failures; surface `BucketResult.ProcessError` in classification. May resolve automatically post-pivot when AL emit becomes in-process. |
| 63 | `NavSession.GetSecurityFilters` | LANDED this session — re-run will show drop. |
| 59 | `NavApplicationObjectBaseHandle\`1.get_Target` | Same root cause as NCLMetadata #1. |
| 57 | `NavSession.get_IsLocalLanguage` | LANDED this session — re-run will show drop. |
| 33 | `NavRecordRef.get_Target` | NavRecordRef is a value type that gets ByRef-wrapped; `get_Target` calls into NCLMetadata. May resolve post-pivot. |
| 28 | `NavMethodScope.AssertError` | LANDED this session — re-run residuals are tests where the body itself NREs. |
| 25 | `NavTestPageHandle.CreateTarget` | TestPage support is non-trivial; tag as "headless-incompatible" or scope out. |
| 17 | `NCLMetaApplicationObject.get_ApplicationObjectConstructor` | Related to Handle.CreateTarget family. |
| 17 | `NavIntegerFormatter.FormatWithFormatNumber` | Format chain NREs through session culture; hook to `int.ToString(Invariant)`. |
| 14 | `NCLMetaTable.GetFieldByNo` | 2-arg overload checks `extensionObjectsByObjectId`; hook to delegate to 1-arg. |
| 13 | `NavStringValue.CompareTo` | Needs CollationAwareStringComparer; fall back to `string.CompareOrdinal`. |
| 12 | `ALDatabase.get_ALCompanyName` | Static getter chains through Session.Tenant; hook to `""`. |
| 12 | `NavStream.get_Target` | Same family as Handle.CreateTarget. |

## §6. Migration plan (Phase 4 — eventual replacement of v1)

The user has chosen the staged path: stay in `spike/v2/` until tests work,
then move. Migration prerequisites that must be satisfied before
`spike/v2/Runner/` can become `AlRunner/`:

1. **`Compilation.Emit` integration** (§3) — landed and stable.
2. **App-dep handling** — v1's `DepCompiler.cs` / `DepExtractor.cs` for
   suites with `app1/`, `app2/` dirs. v2 has no equivalent today.
3. **Stubs / auto-stubbing** — v1's `StubGenerator.cs` / `StubIndex.cs` /
   `AutoStub*`. Required for many tests that depend on Microsoft system app
   codeunits as auto-stubs.
4. **Coverage / JUnit reporting** — v1's `CoverageReport.cs` / `JUnitReport.cs`
   / `TelemetryReporter.cs`. Required for CI workflows.
5. **W-7 isolation modes** (`--isolation per-test|per-codeunit|disabled`) —
   matches v1's `--test-isolation method` flag. Critical for correctness.
6. **W-8 IsTemporary semantics + trigger dispatch** on top of TempTableDataProvider.
7. **CLI parity flags** — `--strict`, `--coverage`, `--stubs`, `--out-junit`,
   etc. CI workflows require these.

After all (1)–(7):
1. Side-by-side run in CI for one PR cycle, compare outputs.
2. Switch CI workflows (`.github/workflows/test-matrix.yml`, `pr-check.yml`,
   `coverage-demo.yml`, `publish.yml`) to v2.
3. Delete `AlRunner/` and `AlRunner.Tests/`. Rename `spike/v2/Runner/` →
   `AlRunner/`. v2 becomes the single runner.

## §7. v1 architecture reference (confirmed 2026-05-07)

When v1 is invoked as `AlRunner --strict --test-isolation method <suite1-src>
<suite1-test> <suite2-src> <suite2-test> ...` (the CI pattern):

- Builds **ONE Roslyn `Compilation`** per multi-folder invocation
  (`Pipeline.cs:1626 / :772`). All folders feed one merged BC compilation
  → one C# source list → one Roslyn compilation → one assembly.
- `--test-isolation method` is a **runtime state-reset flag**, NOT a
  compilation/process boundary (`Pipeline.cs:85` enum + `Program.cs:3539`
  `doTableReset = testIsolation == TestIsolation.Method`).
- All tests run in one process, one assembly, one ALC.
- v1 RoslynRewriter performs: identifier renames (NavCodeunitHandle →
  MockCodeunitHandle, etc), narrow ByRef call-site fixups, IterationInjector,
  ValueCaptureInjector. The `ByRef<T>` wrap on parameter types is BC-native
  (the AL compiler emits it during Compilation.Emit), NOT a rewriter pass.

v2 should mimic v1's compilation/execution scope exactly. Bundled emit + bundled
compile per top-level arg (typically `tests/bucket-N/`) is the right shape.

## §8. Running the corpus

```bash
cd spike/v2/Runner
dotnet build --nologo
# Per-bundle (one subprocess per top-level arg). Run buckets in parallel:
dotnet run --no-build -- --out /tmp/v2-bucket1.json ../../../tests/bucket-1 &
dotnet run --no-build -- --out /tmp/v2-bucket2.json ../../../tests/bucket-2 &
wait
# Tally:
for f in /tmp/v2-bucket*.json; do
  python3 -c "
import json,sys
d = json.load(open(sys.argv[1]))
print(sys.argv[1], '— total_failures:', d['total_failures'])
print('Top classes:')
for c in sorted(d['classifications'], key=lambda x: -x['count'])[:10]:
    print(f'  {c[\"count\"]:5d}  {c[\"classification\"]}')" "$f"
done
```

## §9. Operating notes

- **Don't commit decompiled MS IP.** Use `ilspycmd` locally; decomp output stays
  in `/tmp/`. Microsoft DLLs come from `/home/stefan/.local/share/al-runner/artifacts/`.
- **No `CHANGELOG.md` edits in any PR** (project rule, see `.claude/rules/no-changelog-edits.md`).
- **No `coverage.yaml` updates** while still on the spike branch.
- **agentprism for parallel work** — self-contained tasks (e.g. "implement these N
  hooks against BC API X; signature must match the original; smoke-test with bucket Y")
  fit Copilot Sonnet via `agentprism` well, preserving this session's context.

## §10. Files to read first

- This file (`HANDOFF.md`).
- `spike/v2/CLASSIFICATION.md` header — architectural pivot details.
- `spike/v2/RECORD-GATE.md` header — record-table architecture verdict.
- `spike/bc-abi-identity/FINDINGS.md` header — original 18-layer trace clarification.
- `spike/v2/Runner/BcRuntime.cs` + `Patches/` — current patch surface.
- `~/Documents/Repos/community/bc-linux/src/StartupHook/StartupHook.cs` — JMP-hook
  technique reference.
