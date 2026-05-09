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

1. **No type-renaming rewrites.** Renaming `NavX → MockX` breaks R2R compat.
2. **No silent workarounds.** Gaps get fixed or quarantined with a documented
   reason in `tests/excluded/<bucket>/<suite>/`. Never paper over.
3. **Argument-wrap rewriting only.** `Rewriters/CallSiteArgWrap.cs` (121 LOC) is
   the only Roslyn rewrite. It wraps `expr → new ByRef<T>(getter, setter)` at
   call-sites where BC's emitter couldn't statically prove the wrap was needed.
   This produces IL byte-equivalent to BC's own pipeline.
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

## §4. Current state (2026-05-09 — all-bundled, post-CLI-flip)

**Branch:** `spike/bc-abi-identity`. No push, no PR.

| Sub-bucket | Pass | Total | % | Wall |
|---|---:|---:|---:|---:|
| bucket-1/codeunit-runtime | 583 | 991 | 58.8% | 55s |
| bucket-1/record-table | 347 | 896 | 38.7% | 70s |
| bucket-2/data-formats | 1162 | 1559 | 74.5% | 43s |
| bucket-2/page-report | 220 | 617 | 35.7% | 39s |
| **Total** | **2312** | **4063** | **56.9%** | **~207s** |

All four sub-buckets now run in bundled mode by default. Combined corpus wall
went from ~22 min (per-suite mix) to ~3.5 min (**6.4× faster**), parity
preserved everywhere (bundled pass count = per-suite pass count after the
same quarantines).

**Quarantines:** 30 suites total under `tests/excluded/<sub-bucket>/`, each
with a `RUNNER-GAP-*.md` note documenting the cause. Three categories:
- **BC compiler emit bugs** (NavTypeKind 'None', IndexOutOfRange in `BuildUserCallArgumentList`, NRE in `WriteAttributeProperties` on `fileupload`) — real BC defects.
- **Bundled-mode strictness** (AL0680 dataset position, AL0240 ReportHandler signature, AL0264 ID collisions, AL0305 name length, AL0275/AL0217 layout/scope rules) — bundled compilation enforces stricter checks than per-suite. Tagged `BUNDLED` in the gap doc filename. Pattern clusters worth investigating: AL0680 ×4, AL0240 ×3, ID-collision ×2.
- **AL Runner Config codeunit gap** (271-companyproperty, 242-company-name) — al-runner-only config codeunit not yet implemented in v2 (v1 implemented via `MockSession` routing on the renamed `MockCodeunitHandle`).

**Architectural viability: VALIDATED.** Four spikes this session triangulated
the JIT invariants and the working envelope. Verdict: **v2 architecture can
reach v1's full AL scope.** Remaining ~210 blocked tests are an engineering
task, not a research one. See §5 for the toolkit and the per-class fix
recipe (Option C).

---

## §5. Patching toolkit (post-Spike-4 state)

The runner has three dispatch mechanisms, in priority of use:

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

### Spike history (all four committed; rationale in commit messages)

| Spike | Outcome | Key finding |
|---|---|---|
| 1 — Harmony 2.4.2 | RED | MonoMod.Core writes non-PIC native `.so`; glibc 2.43 W^X rejects `dlopen`. Reverted (`f97d4a5f`). |
| 2 — JmpHook 14-byte overwrite of async entry | RED | Corrupts `MOV R10, [MD]` bytes JIT reads when lazy-compiling callers post-hook → SIGSEGV. Diagnostic kept (`c52f0b4c`). |
| 3 — Indirect-cell pointer swap | YELLOW | GREEN on already-hooked sync methods (cell becomes inline pointer). RED on live FixupPrecode async methods — the cell is JIT *state*, not just dispatch. (`68b362c4`) |
| 4 — EventPipe + post-JIT JmpHook | YELLOW (mechanism GREEN) | EventListener subscribes & fires for BC R2R methods. JmpHook applies post-JIT without ABI/JIT crashes. ABI compatible. Replacement reached. Failure was semantic stub, not mechanism. (`47fda6a9`) |

---

## §6. Active work threads (priority order)

### Tier 1 — Drain the async/generic blockers via Option C (~2-3 days)

Per-class metadata-cache extension or sync-underbelly hook. Priority by
test-count impact:

| # | Blocker | Approach | Recovered |
|---|---|---|---:|
| 1 | `NavRecord.ALFieldCaptionAsync` | Extend `RecordPatches.NclMetaTableBuilder` so `FieldCaption` is populated for parsed AL fields. Hot path becomes sync `MetaTable.GetFieldByNo().FieldCaption` and just works. Also unblocks the 32-fail "Expected: must. Actual: NRE." cluster (Rec.TestField cascades through this). **Validated 2026-05-09 by decompile.** | ~40+ |
| 2 | `NavForm.GetAutoFormatStringAsync` | Decompile, find sync underbelly (likely a metadata/format-spec lookup). Same metadata-cache pattern. | ~100 |
| 3 | `NavReport.RunReportAsync` | Decompile. Probably a sync dataset+layout pipeline awaited inside. Hook the inner sync path. | ~53 |
| 4 | `NavMethodScope.RunBehaviorAsync` | Decompile, sync behavior-dispatch underneath. | ~10 |
| 5 | `NavObjectDictionary\`2.get_Target` | Hook the constructor on each closed instantiation (non-generic on closed type → standard JmpHook). Pre-populate backing dict; getter reads naturally. | ~9 |
| 6 | `NavReport.SaveAsAsync` | Decompile, same pattern. | ~9 |
| **Reserve** | If a class resists Option C | EventPipe + post-JIT JmpHook (§5.3) with semantically-faithful replacement. | (fallback) |

Expected total v2 corpus after this tier: **~2700-2800 / 4063 (67-69 %)**.

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
- Per-stack surgical NRE fixes for the `NavDialog.ALError` cluster (~30 min
  per stack, ≤8 tests each). Tier 1 absorbs most of these as side effects.

---

## §7. Files & paths cheat sheet

| Where | What |
|---|---|
| `spike/v2/Runner/Program.cs` | CLI: `<bundle-dir>...` (bundled by default); `--per-suite` (legacy), `--bundled` (no-op alias), `--precompile`, `--package-cache`, `--out`. |
| `spike/v2/Runner/BcCompiler.cs` | In-process `Compilation.Emit` driver. |
| `spike/v2/Runner/Rewriters/CallSiteArgWrap.cs` | The only Roslyn rewriter; 121 LOC. |
| `spike/v2/Runner/Patches/*.cs` | ~78 JMP-hooks, organized by subsystem. |
| `spike/v2/Runner/Patches/EventPipeJitListener.cs` | Spike-4 scaffolding: in-process JIT-event listener (disabled, proven). |
| `spike/v2/Runner/Patches/AsyncStateMachineSpike.cs` | Reflection on async state-machine layout from prior spikes. |
| `spike/v2/Runner/Infrastructure/JmpHook.cs` | x86-64 JMP-patch via mprotect (FixupPrecode-aware). |
| `spike/v2/Runner/AppLoader.cs`, `DependencyResolver.cs`, `DependencyLoader.cs` | R2R `.app` deps loader. |
| `tests/bucket-1/`, `tests/bucket-2/` | Active test corpus. |
| `tests/excluded/<bucket>/<suite>/` | Documented quarantines (not deleted). |
| `scripts/al-inventory.py` | Per-bucket object enumerator + collision detector. Use before bundled migration. |
| `~/.local/share/al-runner/artifacts/27.5.46862.48827/` | BC 27.5 service-tier DLLs (loaded at runtime). |
| `~/.local/share/al-runner/symbols/27.5.46862.48827/` | BC 27.5 `.app` symbols. |
| `/tmp/codeanalysis.cs` | Decompiled `Microsoft.Dynamics.Nav.CodeAnalysis.dll` (16 MB; grep, never cat; never commit). |
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

**Reading order:** §1 → §2 → §3 → §4. ~3 min.

**Working dir:** `/home/stefan/Documents/Repos/community/BusinessCentral.AL.Runner`.
**Branch:** `spike/bc-abi-identity`. Don't push, don't PR.

**Smoke test the pipeline end-to-end:**
```bash
dotnet build spike/v2/Runner/Runner.csproj
BCCOMPILER_DIAG=1 dotnet run --project spike/v2/Runner --no-build -- \
    --bundled tests/bucket-1/codeunit-runtime
# Expect: 583P / 991 in ~55s. Anything else → regression.
```

Then pick the top open thread from §6 and either do it directly or delegate
per §10.

For older session chronology, decisions, and the patch-by-patch arc that got us
here, see `HANDOFF-archive.md`. It's preserved verbatim from the previous
HANDOFF.md (sections §B–§R + the original §3–§10).
