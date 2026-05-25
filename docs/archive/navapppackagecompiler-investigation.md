# NavAppPackageCompiler — Investigation Report

**Date:** 2026-05-20  
**Branch:** `spike/navappcompiler-spike`  
**Verdict: B — Setup-replication viable**

---

## 1. Method Signatures

### `NavAppPackageCompiler`

- **Constructor:** `NavAppPackageCompiler()` — parameterless. The class can be instantiated without service-tier context.

### `RecompileFullPackage` (the main entry point)

```csharp
Result<CompilationOutput> RecompileFullPackage(
    NavAppPackageReader reader,                        // the packaged .app stream
    NavAppManifest manifest,                           // parsed from the .app
    CodeModuleOutputter outputter,                     // our capture sink (same type we use!)
    AppTenantId tenantId,                              // service-tier: needed by CreateCompilation
    Compilation compilation,                           // nullable — if non-null, skips CreateCompilation
    ObjectChangeModelDefinition radChanges,            // null for normal publish
    bool skipTargetValidation,
    PartnerDiagnosticsApplicationLifecycleTrace.TracePayloadBuilder partnerDiagnosticsPayload,
    ModuleDefinition cachedModuleDefinition,           // nullable
    IDictionary<Guid, Version> depedencyDescription,   // nullable
    bool isInPlaceCompilation,
    CancellationToken cancellationToken
) : Result<CompilationOutput>
```

**Key finding:** The `compilation` parameter is nullable — if non-null, `RecompileFullPackage` skips `CreateCompilation()` and uses the passed-in `Compilation` directly. This means we can call it with our own pre-built `Compilation`.

`CompilationOutput` wraps `(Compilation, CodeModuleOutputter)` — the same outputter we pass in, populated after `Emit`. This is the same output shape as our `BcEmitOutput`.

---

## 2. Input Contract

### How `RecompileFullPackage` reads source

1. It calls `NavAppPackageReaderExtensions.CreateSyntaxTrees(reader, parseOpts, ct)` — reads `.al` files from the packaged `.app` stream (a NAVX zip), not raw strings.
2. `ParseOptions` are built fresh: `ParseOptions.Default.WithRuntimeVersion(RuntimeVersion.Latest).WithManifestOptions(manifest, includeRuntimeVersion: false)`. Preprocessor symbols come from the manifest, not hardcoded values.
3. If `compilation == null`, it calls `CreateCompilation(manifest, syntaxTrees, fileSystem, tenantId, ...)` which requires **service-tier singletons** (see §4).

### Dependency resolution (inside `CreateCompilation`)

`CreateCompilation` calls:
- `NavGlobal.get_SystemTenant().get_DotNetResolverFactory()` — the system-tenant singleton
- `NavAppReferenceLoaderFactory.CreateReferenceLoader(moduleSpec, tenantId, depDict, ...)` — service-tier ref loader

These are not fakeable without fully initializing the service tier. **This is the primary blocker for a drop-in call.**

---

## 3. Output Contract

`RecompileFullPackage` produces a `Result<CompilationOutput>` where `CompilationOutput` holds the passed-in `CodeModuleOutputter` (now fully populated with emitted C# per-object) and the `Compilation`. It does **not** write to disk — all output is in-memory, in the outputter. Same contract as our `CaptureOutputter`.

---

## 4. The Compilation Setup Divergence — THE KEY FINDING

This is the most important section. Three differences between what MS does and what `BcCompiler.cs` does:

### 4a. `EmitOptions` — confirmed divergence

| Parameter | `EmitOptions.Default` (what we use) | MS `RecompileFullPackage` |
|---|---|---|
| `runtimeMetadataVersion` | 130000 ✓ (same) | 130000 |
| `extensionEmitValues` | **`null`** | **`new ExtensionEmitValues(appId, appVersion)`** |
| `concurrentEmit` | false | false ✓ |
| `nonDebuggableEmit` | false | false ✓ |
| `emitAsync` | **`false`** | **`!ServerUserSettings.DisableAsyncCodeGeneration`** |
| `emitInlineScope` | **`false`** | **`ServerUserSettings.EnableInlinedMethodCodeGeneration`** |

`EmitOptions.Default` itself uses `runtimeMetadataVersion=130000` (confirmed in `.cctor`) — so that's not the gap.

The gaps are:
1. **`extensionEmitValues = null`**: MS always passes `new ExtensionEmitValues(appId, appVersion)`. This embeds the app's GUID + version into the emitter, controlling how extension-type dispatch tables and by-ref argument wrapping are generated. Without it, the emitter uses a null extension identity — a different code path that could produce call-site shapes inconsistent with what Roslyn's C# compiler expects, triggering CS1503.
2. **`emitAsync`** (async code generation): When MS deploys with `DisableAsyncCodeGeneration=false` (the default on a live service tier), the emitter generates async wrapper methods. Our runner always generates sync-only C#. This could produce different by-ref argument call site shapes that `CallSiteArgWrap` is patching.
3. **`emitInlineScope`**: Controls whether method bodies are inlined. Less likely to be the CS1503 cause but still a divergence.

### 4b. `ParseOptions` — confirmed divergence

| | BcCompiler | MS `RecompileFullPackage` |
|---|---|---|
| `runtimeVersion` | `null!` (likely resolves to old default) | `RuntimeVersion.Latest` |
| preprocessor symbols | hardcoded `CLEANSCHEMA1..25` | from `manifest.WithManifestOptions` |

The `RuntimeVersion.Latest` difference matters: different runtime versions may select different emitter code paths for by-ref parameters.

### 4c. `CompilationOptions` — minor divergence

MS uses the 21-arg `CompilationOptions` ctor with `continueBuildOnError` tied to `ServerUserSettings.EnableMultithreadedCompilation`, then calls `.WithManifestOptions(manifest)` to apply the app's target from `app.json`. Our `BcCompiler` hardcodes `target: CompilationTarget.OnPrem` and never calls `WithManifestOptions`. For our test corpus (all `OnPrem` apps) this is probably benign, but for Extension-targeted apps it would matter.

---

## 5. Feasibility Verdict

### Verdict: **B — Setup-replication viable**

**Why not A (drop-in call to `RecompileFullPackage`):**
- `RecompileFullPackage` with `compilation=null` calls `CreateCompilation`, which needs `NavGlobal.SystemTenant` and `NavAppReferenceLoaderFactory` — both require a live service-tier process.
- Even with `compilation` pre-built (bypassing `CreateCompilation`), `RecompileFullPackage` calls `ServerUserSettings.get_Instance()` (for `ExtensionAllowedTargetLevel` and `EmitOptions`) and `DiagnosticsResolver` (for telemetry on failure), both of which may NRE on our skeleton runtime.
- The `tenantId` parameter propagates through the diagnostics path.

**Why B is the right answer:**
The IL analysis reveals exactly which `EmitOptions` fields diverge. We can fix `BcCompiler.cs` without calling any Ncl.dll service-tier code by mirroring the 3 setup changes:

1. **`EmitOptions` fix** (highest-leverage, ~5 lines):
   ```csharp
   // instead of: EmitOptions.Default
   var serverSettings = ...; // already initialized by BcRuntime.EnsureApplied()
   var emitOpts = new NavCA.EmitOptions(
       runtimeMetadataVersion: 130000,
       runtimeMetadataSuffix: null,
       skipStmtHit: false,
       extensionEmitValues: new NavCA.ExtensionEmitValues(appId, appVersion),
       concurrentEmit: false,
       nonDebuggableEmit: false,
       emitAsync: true,    // matches MS default (DisableAsyncCodeGeneration=false)
       emitInlineScope: false  // conservative default
   );
   ```
   The `appId` / `appVersion` already exist: `appId = DeterministicGuid(moduleName)` is computed in `BcCompiler.Emit()`.

2. **`ParseOptions` fix** (~3 lines):
   ```csharp
   // instead of: new ParseOptions(runtimeVersion: null!, ...)
   var parseOpts = NavCA.ParseOptions.Default
       .WithRuntimeVersion(NavCA.RuntimeVersion.Latest);
   // Keep CLEANSCHEMA1..25 — no app manifest, so WithManifestOptions isn't applicable
   ```

3. **`CompilationOptions` fix** (optional for our corpus, ~2 lines):
   Already correct for OnPrem. Add `.WithManifestOptions(manifest)` if/when we add an app.json reader to the compile pipeline.

### Why this likely fixes `CallSiteArgWrap`

`CallSiteArgWrap` patches CS1503 mismatches between the C# the BC emitter generates and what Roslyn's C# compiler expects for by-ref argument call sites. The two most likely causes of those mismatches are:
- `extensionEmitValues=null` → emitter uses a null-identity code path with different ByRef wrapping
- `emitAsync=false` → emitter generates sync-only call sites that differ from the async-enabled ones Roslyn expects when async types are referenced

Enabling `extensionEmitValues` and `emitAsync=true` in `BcCompiler.Emit()` should reproduce the exact C# shapes that MS ships, eliminating the need for `CallSiteArgWrap` to patch them.

---

## 6. Recommended Next Step (not in this spike)

1. Change `EmitOptions.Default` → the custom `EmitOptions` above in `BcCompiler.Emit()`.
2. Change `ParseOptions` → `ParseOptions.Default.WithRuntimeVersion(RuntimeVersion.Latest)`.
3. Run the full test matrix. If CS1503 errors disappear → `CallSiteArgWrap.cs` can be deleted. If they don't, log which specific AL patterns still trigger them — the remaining gap is real BC emitter behavior we cannot replicate without deeper service-tier context.

**Estimated effort:** ~30 lines changed in `BcCompiler.cs`; `CallSiteArgWrap.cs` deletion is the prize if it works.

---

## Appendix: IL Evidence Summary

| Finding | IL evidence |
|---|---|
| `RecompileFullPackage` ctor is parameterless | `.ctor()` with no args at `NavAppPackageCompiler` type |
| `RecompileFullPackage` accepts pre-built `Compilation` | `brtrue` at `IL_0081` skips `CreateCompilation` call |
| MS constructs `EmitOptions` with `ExtensionEmitValues` | `IL_00C3..IL_00FE` in `RecompileFullPackage` body |
| `EmitOptions.Default` uses `null` extensionEmitValues | `.cctor` of `EmitOptions`: `ldnull` for 4th arg |
| `CreateCompilation` needs `NavGlobal.SystemTenant` | `IL_00EA` in `CreateCompilation` |
| `ParseOptions` uses `RuntimeVersion.Latest` | `IL_0052` in `RecompileFullPackage` |
| MS calls `WithManifestOptions` on both Parse and Compile options | `IL_005E` + `IL_0038` respectively |
