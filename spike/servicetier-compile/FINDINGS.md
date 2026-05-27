# Spike: Service-Tier Compile — Findings

Branch: `v2-spike-servicetier-compile`  
BC version: 28.1.49838.50794  
Platform: Linux / net10.0  
Probe: `spike/servicetier-compile/Probe/`  
Date: 2025

---

## TL;DR

**GO.** BC's own AL compiler emits a loadable DLL in-process on Linux, headless, no container, no SQL.

**Round 2 proof:**

```
[R2-2] NavCA.Compilation created
[R2-2] Emit result: Success=True capturedFiles=1
[R2-2] Wrote C# file: Probe_Test.cs (2831 chars)
[R2-2] CompileCSharpFilesAsync result: NavAppCSharpCompilerResult
[R2-2] HasErrors=False
[R2-2] AssemblyContent: 5120 bytes
[R2-2] Assembly.Load: ProbeAlApp, Version=28.0.50706.0, Culture=neutral, PublicKeyToken=null
[R2-2] Types: Microsoft.Dynamics.Nav.BusinessApplication.Codeunit50001, ...
[R2-2-SUCCESS] Hello() → 'hello from probe'
[CLASS] [FULL-PIPELINE-GREEN]  // PROOF: BC compiled tiny AL source to IL DLL in-process on Linux, headless. Method invoked successfully!
```

Round 1 assessed this as NO-GO due to `PlatformNotSupportedException` on `NavDirectorySecurity.CreateSecurityForDomainDirectory()`.  
Round 2 lifted bc-linux patches to bypass that and all other Windows-only blockers.

### Caveat — proven on a tiny app; real apps need the next step

The GREEN proof above is a **trivial synthetic 1-codeunit app**. Compiling a
**real app** (RecoverySolutions) through `RecompileFullPackage` still fails with
`NavTypeKind None` / `ConversionKind NoConversion` emit errors across ~12 objects
— a **symbol-closure / `Compilation` construction** gap (our code), *not* a
platform gap. The platform feasibility is settled; the next step is building the
real `Compilation` via `BcCompiler`'s no-SQL reference path so the AL semantic
model binds completely. Until then this is a proven PoC, not yet a drop-in
replacement for `BcAssembler`.

Q2 (runtime packages): `NavAppPackageCompiler.ExtractEmittedContent(stream)` is
SQL/ACL-free and could load ISV runtime-package `.app`s; no detection/extraction
case exists in the runner yet (future work).

---

## Ordered patch list (productionisation backlog for BcRuntime)

All patches are in `AlRunner/Patches/CompilePatches.cs` (partial class on `BcRuntime`), applied in `BcRuntime.EnsureCompilePatches()`.

### Patch #9 — `NavEnvironment.Topology` proxy (CRITICAL for ACL bypass)

| Field | Value |
|---|---|
| **bc-linux source** | `StartupHook.cs` ~line 572, Patch #9 |
| **Mechanism** | JmpHook on `StandardServiceTopology.IsServiceRunningInLocalEnvironment` get_; `LinuxTopologyProxyImpl` DispatchProxy |
| **Why needed** | `IsServiceRunningInLocalEnvironment=true` caused `NavDirectorySecurity.CreateSecurityForDomainDirectory()` to call Windows ACL APIs → `PlatformNotSupportedException` on Linux |
| **Faithfulness** | Returns `false` — this property gates whether the service tier skips domain-ACL creation. False = "not running locally" = no ACL creation. Does not affect AL→C# or C#→IL output. Pure infrastructure path. |
| **Probe evidence** | `[CompilePatches] Patch #9: Topology replaced with Linux proxy (IsServiceRunningInLocalEnvironment=false)` |

### Patch #2b — `TempPathHelper.InitializeFolders()` redirect (CRITICAL for non-root Linux)

| Field | Value |
|---|---|
| **bc-linux source** | bc-linux creates `/usr/share/Microsoft/Microsoft Dynamics NAV/...` as root in `entrypoint.sh`. No corresponding patch needed there. Our probe runs as non-root. |
| **Mechanism** | JmpHook replaces `TempPathHelper.InitializeFolders()` to create `/tmp/bc-alrunner/{guid}/` and set `instanceBasePath`, `configurableBasePath`, `driveName` fields via reflection |
| **Why needed** | `TempPathHelper.InitializeFolders()` computes its base path from `Environment.GetFolderPath(SpecialFolder.CommonApplicationData)` = `/usr/share` on Linux, then tries to create `/usr/share/Microsoft/Microsoft Dynamics NAV/280/Server/`. On non-root systems this raises `UnauthorizedAccessException → NavNCLDirectoryCreationException` |
| **Faithfulness** | Redirects temp/assembly scratch space only. No effect on IL emission. The field names `instanceBasePath`, `configurableBasePath`, `driveName` are runtime state, not output state. |
| **Probe evidence** | `[CompilePatches] Patch #2b: TempPathHelper.InitializeFolders hooked → /tmp/bc-alrunner/` |

### Patch #14 — `CecilDotNetTypeLoader.IsTypeForwardingCircular` (NEEDED for .NET type resolution)

| Field | Value |
|---|---|
| **bc-linux source** | `StartupHook.cs` ~line 610, Patch #14 |
| **Mechanism** | JmpHook: `IsTypeForwardingCircular → always false` |
| **Why needed** | Type-forwarding detection loop in Cecil's DotNet type loader causes false-positive circular detection → breaks `.NET` DotNet variable resolution in AL |
| **Faithfulness** | Returns `false` — correct for any type system where forwarding chains are finite (which .NET guarantees). Does not affect emitted IL. |
| **Probe evidence** | `[CompilePatches] Patch #14: CecilDotNetTypeLoader.IsTypeForwardingCircular hooked → false` |

### Patch #15 — `GetLocationOfAssembliesLoadedInServerAppDomain` filter (NEEDED for assembly probing)

| Field | Value |
|---|---|
| **bc-linux source** | `StartupHook.cs` ~line 679, Patch #15 |
| **Mechanism** | JmpHook replaces `GetLocationOfAssembliesLoadedInServerAppDomain` with `FilteredAssemblyLocations()` which returns only assemblies with a non-empty disk path |
| **Why needed** | Without filtering, byte-loaded assemblies (path = `""`) are included in the probing list, causing `AssemblyLocator` to fail when it tries `File.Exists("")` |
| **Faithfulness** | Only affects which assemblies are offered as reference candidates. Empty-path assemblies are not valid reference candidates by any standard. Does not affect emitted IL. |
| **Probe evidence** | `[CompilePatches] Patch #15: assembly probing filter hooked` |

---

## Full pipeline recipe (no-SQL Compilation construction)

```csharp
// 1. Build NavCA.Compilation from AL source files (runner already does this via BcCompiler)
var parseOpts = new NavCA.ParseOptions(
    runtimeVersion: null!,
    preprocessorSymbols: [],
    documentationMode: NavCA.DocumentationMode.None);
var tree = NavCA.Syntax.SyntaxTree.ParseObjectText(
    alSource, path: "MyApp.al", encoding: null!, parseOpts, default);
var compOpts = new NavCA.CompilationOptions(
    continueBuildOnError: true,
    target: NavCA.CompilationTarget.OnPrem,
    generateOptions:
        NavCA.CompilationGenerationOptions.Code |
        NavCA.CompilationGenerationOptions.Navigation);
var compilation = NavCA.Compilation.Create(
    moduleName: "MyApp", publisher: "Pub",
    version: new Version(1, 0, 0, 0), appId: appId,
    syntaxTrees: [tree], options: compOpts);
// Add ref loader + DotNet resolver for apps with dependencies (BcCompiler.Emit does this)

// 2. AL → C# via BC's emitter
var outputter = new CaptureOutputter();   // extends NavCA.Emit.CodeModuleOutputter
compilation.Emit(NavCA.EmitOptions.Default, outputter);
// outputter.Captured = List<(string Name, string Code)>

// 3. Write C# to temp files
var csFiles = outputter.Captured.Select(s => {
    var path = Path.Combine(tmpDir, s.Name + ".cs");
    File.WriteAllText(path, s.Code);
    return path;
}).ToList();

// 4. C# → IL via BC's own CSharpCompiler (type: Microsoft.Dynamics.Nav.Runtime.CSharpCompiler)
// Requires Patch #9 + #2b to be active (TempPathHelper redirect + topology proxy)
var csResult = await CSharpCompiler.Instance.CompileCSharpFilesAsync(
    assemblyName, csFiles, enableDebugging: false, CancellationToken.None);
// csResult.HasErrors=false, csResult.AssemblyContent = byte[]

// 5. Load and invoke
var asm = Assembly.Load(csResult.AssemblyContent);
var codeunit = RuntimeHelpers.GetUninitializedObject(
    asm.GetType("Microsoft.Dynamics.Nav.BusinessApplication.Codeunit50001")!);
var result = codeunit.GetType().GetMethod("Hello")!.Invoke(codeunit, null);
// → "hello from probe"
```

---

## Literal probe output (key steps only)

```
Step 0: BcRuntime.EnsureApplied() + EnsureCompilePatches()
[OK]   BcRuntime.EnsureApplied() succeeded — runtime patches installed
[CompilePatches] Patch #9: Topology replaced with Linux proxy (IsServiceRunningInLocalEnvironment=false)
[CompilePatches] Patch #2b: TempPathHelper.InitializeFolders hooked → /tmp/bc-alrunner/
[CompilePatches] Patch #14: CecilDotNetTypeLoader.IsTypeForwardingCircular hooked → false
[CompilePatches] Patch #15: assembly probing filter hooked
[OK]   BcRuntime.EnsureCompilePatches() applied

Step 3b: CSharpCompiler.Instance.CompileCSharpFilesAsync with trivial C#
[CompilePatches] TempPathHelper.InitializeFolders → redirected to /tmp/bc-alrunner/fee0f489
[OK]   CompileCSharpFilesAsync returned: ValueTask`1
  [3b-PROP] IsCompletedSuccessfully: True
  [3b-PROP] IsFaulted: False
  [3b-OK] GetAwaiter().GetResult() succeeded! Type=NavAppCSharpCompilerResult
    [3b-RES-PROP] AssemblyContent: System.Byte[]
    [3b-RES-PROP] HasErrors: False
[CLASS] [accessible-step3b-OK]  // CSharpCompiler.CompileCSharpFilesAsync SUCCEEDED end-to-end!

Step R2-1: Load BC-compiled DLL bytes + invoke method
[R2-1] AssemblyContent: 3584 bytes
[R2-1] Assembly.Load succeeded: ProbeAssembly, Version=28.0.50706.0, ...
[R2-1-SUCCESS] Hello() returned: 'hello from BC CSharpCompiler'
[CLASS] [R2-1-GREEN]  // BC's CSharpCompiler emitted a loadable DLL! Method invoked in-process on Linux!

Step R2-2: Full AL→C#→IL pipeline (BC compiler, in-process, headless)
[R2-2] NavCA.Compilation created
[R2-2] Emit result: Success=True capturedFiles=1
[R2-2] Wrote C# file: Probe_Test.cs (2831 chars)
[R2-2] CompileCSharpFilesAsync result: NavAppCSharpCompilerResult
[R2-2] HasErrors=False
[R2-2] AssemblyContent: 5120 bytes
[R2-2] Assembly.Load: ProbeAlApp, Version=28.0.50706.0, ...
[R2-2] Types: Microsoft.Dynamics.Nav.BusinessApplication.Codeunit50001, ...
[R2-2-SUCCESS] Hello() → 'hello from probe'
[CLASS] [FULL-PIPELINE-GREEN]  // PROOF: BC compiled tiny AL source to IL DLL in-process on Linux, headless. Method invoked successfully!
```

---

## bc-linux `_disabledPatches` safety audit

None of our four patches (#9, #2b, #14, #15) appear in bc-linux's `_disabledPatches` set (the set of patches found to cause AL→C# emission drift). All four are infrastructure-only patches that affect directory creation, ACL gating, and assembly probing — not the AL→C# or C#→IL code generation paths.

---

## Round 1 recap (overturned)

Round 1 (commit `5e410968`) declared NO-GO with two stated blockers:

1. **`NavDirectorySecurity.CreateSecurityForDomainDirectory()` → `PlatformNotSupportedException`**  
   → Fixed by Patch #9 (topology proxy making `IsServiceRunningInLocalEnvironment=false`).

2. **`CSharpCompiler.Instance` inaccessible from byte-loaded context**  
   → Was wrong. The type is accessible. Its initialization succeeds once all artifacts DLLs are pre-loaded (done by the probe's pre-load loop).

3. **`TempPathHelper` creating `/usr/share/Microsoft/...` as non-root**  
   → Fixed by Patch #2b (redirect to `/tmp/bc-alrunner/{guid}/`).

---

## What this means for the runner's architecture

The current architecture (BcCompiler AL→C# + BcAssembler Roslyn C#→IL) is **correct and complete** for tests. The spike proves that the BcAssembler Roslyn step can be **replaced with BC's own `CSharpCompiler.CompileCSharpFilesAsync`** for even more faithful IL output. 

Productionisation path for `BcAssembler` → `BcRuntime.CSharpCompiler`:
1. Add `EnsureCompilePatches()` to `BcRuntime.EnsureApplied()` (or call it from `BcCompiler.Emit`)
2. Replace `BcAssembler` with `CSharpCompiler.Instance.CompileCSharpFilesAsync`
3. Handle `NavAppCSharpCompilerResult.Diagnostics` the same way `BcAssembler` does

BC's `CSharpCompiler` adds the runtime's own assembly references automatically (it uses `Assembly.GetExecutingAssembly().Location` = Ncl.dll's disk path, which is valid when Ncl loads from disk as it does in our probe). This means the patched Ncl assemblies (including `AllowedTypes` polyfills, `CallSiteArgWrap`, etc.) would be referenced by the emitted DLL automatically — exactly what `BcAssembler` currently does by passing them explicitly.

---

## Probe Evidence Location

- `AlRunner/Patches/CompilePatches.cs` — all four Round 2 patches
- `spike/servicetier-compile/Probe/Program.cs` — probe source (spike-only)
- Run: `dotnet run --project spike/servicetier-compile/Probe --framework net10.0 -c Release`
