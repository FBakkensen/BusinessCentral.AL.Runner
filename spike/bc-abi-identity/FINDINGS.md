# Spike: BC ABI identity — findings (Phase 0 in progress)

**Branch:** `spike/bc-abi-identity`
**Test target:** `tests/bucket-1/codeunit-runtime/01-pure-function/` (pure decimal math)
**BC version on disk:** 27.5.46862.48827 / AL compiler 17.0.34.45391

## TL;DR

The "drop the rewriter and run unmodified BC IL" path is **viable**. Verified live: a Codeunit50100 (Discount Calculator) compiled directly by Microsoft's AL compiler instantiates successfully on Linux against the real BC service-tier DLLs, using a ~200-line subset of bc-linux's StartupHook technique. Method invocation reaches the next mechanical-patch layer (`NavMethodScope..ctor`) — the path is "more patches of the same shape," not a wall.

## Layers cleared (live, this branch)

| # | Layer | Outcome | Fix |
|---|---|---|---|
| 1 | Compile emitted C# against real MS DLLs | ✅ 0 errors | 1 polyfill: `RuntimeShim.ThrowIfWrongArgumentCount` |
| 2 | Assembly load | ✅ | none |
| 3 | Codeunit ctor null parent | ✅ | Stub `ITreeObject` |
| 4 | `TreeHandler` ctor reads `parent.Tree` | ✅ | Parameterless protected ctor |
| 5 | `IsDisposed` true → child registration fails | ✅ | Reflection-poke `hostObject` field |
| 6 | `kernel32.dll!LCIDToLocaleName` | ✅ | bc-linux's `kernel32_stubs.c` |
| 7 | `NavEnvironment..cctor` calls `WindowsIdentity.GetCurrent()` | ✅ | **JMP-hook .cctor with Linux-friendly replacement** |
| 7a | `NavEnvironment.get_ServiceAccount` / `_Name` deref serviceAccount | ✅ | JMP-hook getters |
| 7b | `NavEnvironment.EmitServerStartupTraceEvents` System.Drawing fonts | ✅ | JMP-hook → no-op |
| 8 | `user32.dll!OemToCharBuff` (called from `NCLManagedAdapter..cctor`) | ✅ | bc-linux's user32 stubs (same .so) |
| 9 | `NavEnvironment..ctor(flags)` instance ctor — null deref | ✅ | Pre-populate `instance` field with `GetUninitializedObject` skeleton + JMP-hook `get_Instance` to return it |
| 10 | **Codeunit fully instantiated** | ✅ | `Instantiated: 50100` — `Codeunit50100..ctor(StubTreeObject)` succeeds |
| 11 | `ApplyDiscount.Invoke` → `NavMethodScope..ctor` NRE | 🟡 next layer | Same shape — JMP-hook or pre-populate the static the ctor reads |

## What this proves

1. **JMP-hook technique works on BC IL on Linux .NET 8.** Function-pointer overwrite via `RuntimeHelpers.PrepareMethod` + `mprotect` (verbatim from bc-linux). Patches both precode and compiled-code addresses; replacements run reliably. No Harmony, no MonoMod, no IL rewriter.

2. **The earlier "wall at layer 7" was wrong.** With the JMP-hook + skeleton-instance pattern, BC's hardest static-init paths fall in 1–2 small replacement methods each. We cleared 4 more layers in ~30 minutes after correcting the assessment.

3. **bc-linux is the right reference, not the right scope.** Out of bc-linux's 23 patches and 2615 lines, this spike used:
   - Patch #2 (NavEnvironment.cctor) — 1 replacement method
   - Patch #3 (kernel32 + user32) — entire `kernel32_stubs.c` lifted as-is
   - JMP-hook plumbing (`ApplyJmpHook`, `WriteJmp`, `mprotect` P/Invoke, `SetStaticField`) — ~150 lines verbatim
   Total adopted code: ~250 lines + ~380 lines of C stubs. AL Runner doesn't need the cluster/topology/SQL/AAD/reporting patches because it has no service tier to keep alive.

4. **Each remaining layer is concrete.** The pattern is: read the stack trace → identify the static cctor or instance ctor that NREs → write a JMP-hook replacement that initializes the fields it reads → repeat. Mechanical, not research.

## Spike runner outline (live, runs)

```
Program.cs:           kernel32/user32/... shim (bc-linux stubs.c)
                      → force-load BC DLLs
                      → LinuxBootstrap.Apply(navNcl)
                      → Assembly.LoadFrom(SpikeBuild.dll)
                      → instantiate Codeunit50100
                      → invoke ApplyDiscount(200, 10)

LinuxBootstrap.cs:    JMP-hook helper (~150 lines, verbatim from bc-linux)
                      Apply():
                        - hook NavEnvironment..cctor → Linux-safe init
                        - hook get_ServiceAccount / _Name → safe defaults
                        - pre-populate instance field with skeleton
                        - hook get_Instance → return skeleton
                        - hook EmitServerStartupTraceEvents → no-op

shims/win32_stubs.c:  bc-linux's stubs file (381 lines), compiled to .so at runtime
shims/RuntimeShim.cs: 1 polyfill method (compile-time only)

generated/*.cs:       Microsoft's AL compiler output, unmodified, 10 codeunits
SpikeBuild.csproj:    compiles generated/*.cs against real MS DLLs
```

## Honest revised estimate

| Phase | Wall clock |
|---|---|
| 0 — Single test method runs end-to-end (currently at layer 11/~?) | **another 1–3 days** to chase NavMethodScope and any remaining ctor chain |
| 1 — Migrate `bucket-1/codeunit-runtime` (~200 tests, no DB) | **2–3 weeks** — each new code path may surface 0–3 new patches |
| 2 — Migrate `bucket-1/record-table` (~150 tests, DB needed) | **3–5 weeks** — biggest unknown, needs in-memory `Record` storage that BC's `NavRecord` IL binds to (existing AlScope.cs already has the semantics — migration, not net-new) |
| 3 — Migrate remaining + delete RoslynRewriter | **1–2 weeks** |
| **End-to-end** | **6–10 weeks** |

Down from my earlier 6–11. Risk now concentrates in Phase 2 (`Record` redirection) — the rest is a known-shape patch loop.

## Remaining concrete work to finish Phase 0

1. JMP-hook `NavMethodScope..ctor` (or initialize the static it reads). Probably reads `NavSession` or accesses a metadata provider — need to chase the NRE to the exact field.
2. Then per-method invocation may pull more layers: `NavScope.Run`, `CStmtHit` (statement-hit tracking), value-stack management. Each one a small replacement.
3. End condition: `ApplyDiscount(200, 10)` returns `Decimal18(180)`. That's the binary success criterion for Phase 0.

## Repo artifacts

- `runner/Program.cs` — bootstrap + spike test harness
- `runner/LinuxBootstrap.cs` — minimal JMP-hook + NavEnvironment patches (~200 lines)
- `runner/SpikeRun.csproj` — ties it all together
- `shims/win32_stubs.c` — bc-linux's Win32 stubs file
- `shims/WindowsPrincipalStub/` — copied for reference (not used in spike — JMP-hook approach avoids needing BCL overlay)
- `shims/RuntimeShim.cs` — single compile-time polyfill
- `generated/` — captured AL→C# emission from the AL compiler, unmodified
- `SpikeBuild.csproj` — proves clean compile against real BC DLLs

## Source attribution

Code in `runner/LinuxBootstrap.cs` and `shims/win32_stubs.c` is copied or adapted from `github.com/StefanMaron/bc-linux` (MIT, by the same author). The JMP-hook technique, replacement-method pattern, and Win32 stub set are all bc-linux's design.
