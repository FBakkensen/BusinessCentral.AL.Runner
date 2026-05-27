# Spike: Service-Tier Compile — Findings

Branch: `v2-spike-servicetier-compile`  
BC version: 28.1.49838.50794  
Platform: Linux / net10.0  
Probe: `spike/servicetier-compile/Probe/`  
Date: 2025

---

## TL;DR

**NO-GO.** Two independent hard blockers prevent `NavAppPackageCompiler.RecompileFullPackage` from replacing the runner's compile pipeline on Linux:

1. **Without pre-built Compilation** → SQL hard dependency (`NavGlobal.AppDatabase`).
2. **With pre-built Compilation (SQL bypass)** → `CSharpCompiler.Instance.CompileCSharpFilesAsync` fails on Linux: `PlatformNotSupportedException: ACL APIs not supported on this platform`.

The runner's current two-stage pipeline (BcCompiler AL→C# + BcAssembler C#→IL) remains correct and is the right architecture for Linux.

---

## Q1 — Reachability Verdict

### Architecture: two-stage, not one

Decompilation confirms `NavAppPackageCompiler.Recompile` is itself two-stage:

1. **AL → C#**: `compilation.Emit(outputter)` — produces C# source files
2. **C# → IL**: `CSharpCompiler.Instance.CompileCSharpFilesAsync(assemblyName, filePaths, ...)` — Roslyn compile via BC's own C# compiler

The runner's existing BcCompiler + BcAssembler is already an exact mirror of this. The spike question was: can we drive stage 1+2 as a single `Recompile` call instead?

### Probe steps and literal output

#### Step 0 — BC runtime bootstrap (SUCCESS)
```
[OK]   BcRuntime.EnsureApplied() succeeded — runtime patches installed
[INFO] Ncl loaded from: '...spike/servicetier-compile/Probe/bin/Release/net10.0/Microsoft.Dynamics.Nav.Ncl.dll'
```
Runner bootstrap works as expected.

#### Step 2 — NavAppPackageCompiler reflected (SUCCESS)
```
[OK]   Found method: Recompile
  [METHOD] Recompile(NavAppCompileArguments compilationAgruments, NavCancellationToken cancellationToken, Compilation compilation) → Result`1
  [METHOD] RecompileFullPackage(NavAppPackageReader reader, NavAppManifest manifest, CodeModuleOutputter outputter, AppTenantId tenantId, Compilation compilation, ...) → Result`1
  [METHOD] CreateCompilation(NavAppManifest manifest, ..., AppTenantId tenantId, ...) → Result`1
  [METHOD] ExtractEmittedContent(Stream packageStream) → Result`1
```
All target methods visible and callable via reflection.

#### Step 3 — CSharpCompiler.Instance (SUCCESS with caveat)
```
[OK]   Found CSharpCompiler type: Microsoft.Dynamics.Nav.Runtime.CSharpCompiler in Microsoft.Dynamics.Nav.Ncl
[OK]   CSharpCompiler.Instance obtained: CSharpCompiler
[INFO] CompileCSharpFilesAsync(String assemblyName, IList`1 sourceFiles, Boolean enableDebugging, CancellationToken cancellationToken)
```
`CSharpCompiler.Instance` IS accessible once all 480 BC artifacts DLLs are pre-loaded. The earlier static-analysis assessment that it was inaccessible was wrong. However:

#### Step 3b — CompileCSharpFilesAsync invoked (HARD BLOCKER)
```
[3b-TASK-FAIL] PlatformNotSupportedException: Access Control List (ACL) APIs are part of resource management on Windows and are not supported on this platform.
[3b-TASK-STACK]
   at System.Security.AccessControl.ObjectSecurity..ctor()
   at System.Security.AccessControl.CommonObjectSecurity..ctor(Boolean isContainer)
   at System.Security.AccessControl.NativeObjectSecurity..ctor(Boolean isContainer, ResourceType resourceType)
   at System.Security.AccessControl.FileSystemSecurity..ctor()
   at System.Security.AccessControl.DirectorySecurity..ctor()
   at Microsoft.Dynamics.Nav.Runtime.NavDirectorySecurity.CreateSecurityForDomainDirectory()
   at Microsoft.Dynamics.Nav.Runtime.TempPathHelper.InitializeFolders()
   at Microsoft.Dynamics.Nav.Runtime.NavEnvironment.get_TemporaryPathHelper()
   at Microsoft.Dynamics.Nav.Runtime.NavEnvironment.<.ctor>b__320_15()
```
`CSharpCompiler.CompileCSharpFilesAsync` calls `NavEnvironment.get_TemporaryPathHelper()` which calls `NavDirectorySecurity.CreateSecurityForDomainDirectory()` which uses Windows ACL APIs. **Not fixable on Linux without rewriting NavDirectorySecurity.**

#### Step 6b — Recompile(real app, compilation=null) (SQL hard dependency confirmed)
```
[6b-RESULT] Success=False Failure=True
[6b-ERROR-PROP] Message: Object reference not set to an instance of an object.
[6b-ERROR-PROP] StackTrace:
   at Microsoft.Dynamics.Nav.Runtime.Apps.NavAppPackageCompiler.CreateCompilation(...)
   at Microsoft.Dynamics.Nav.Runtime.Apps.NavAppPackageCompiler.RecompileFullPackage(...)
   at Microsoft.Dynamics.Nav.Runtime.Apps.NavAppPackageCompiler.Recompile(...)
```
When `compilation=null`, `RecompileFullPackage` calls `CreateCompilation` → `NavAppReferenceLoaderFactory.CreateReferenceLoader` → `NavSqlConnectionScope.Create(NavGlobal.AppDatabase, ...)`. `NavGlobal.AppDatabase` is null in the runner's headless context (not a service tier). NullReferenceException at first SQL access.

#### Step 7c — Recompile(real app, pre-built Compilation) (SQL bypass WORKS, but hits ACL)
```
[7c-RESULT] Success=False Failure=True
[7c-ERROR] Message: One or more errors occurred.
  (Failure while emitting method. Object:'Table Customization.RSA.PayrollDimensionMappingRSABG'
   Method:'OnValidate()' (Unexpected value 'None' of type 'Microsoft.Dynamics.Nav.CodeAnalysis.NavTypeKind'))
  ...
[7c-ERROR] StackTrace:
   at Microsoft.Dynamics.Nav.CodeAnalysis.MethodCompiler.CompileModule(ModuleSymbol moduleSymbol)
```
**Important**: Providing a non-null `Compilation` to `Recompile` DOES bypass `CreateCompilation` (the SQL path). We reach `MethodCompiler.CompileModule`. The errors here are because we passed a minimal/mismatched Compilation (tiny test AL, not the real app's sources) — not a fundamental barrier. 

However, even if the Compilation were correct, the next stage (`CSharpCompiler.Instance.CompileCSharpFilesAsync`) would hit the ACL blocker from Step 3b.

#### Shutdown evidence of service-tier path coupling
```
Unhandled exception. System.IO.DirectoryNotFoundException:
  Could not find a part of the path '/usr/share/Microsoft/Microsoft Dynamics NAV/280/Server/MicrosoftDynamicsNavServer$_PID_1'.
   at Microsoft.Dynamics.Nav.Runtime.TempPathHelper.Dispose(Boolean disposing)
   at Microsoft.Dynamics.Nav.Runtime.TempPathHelper.Finalize()
```
Even finalizer code expects a real BC service tier installation directory. BC's runtime is deeply coupled to its service tier install path.

---

## Q1 — Context-Requirement Table

| Requirement | Classification | Evidence / Notes |
|---|---|---|
| Valid `.app` stream with 40-byte NAVX header + OPC zip | **[fakeable]** | Step 6b: real app read successfully by `NavAppPackageReader.Create` |
| `NavAppCompileArguments` construction | **[fakeable]** | Step 4: constructed via reflection |
| `NavCancellationToken` | **[fakeable]** | Not found in any assembly; null → default value type accepted |
| `NavAppPackageCompiler` type access | **[already-faked]** | Accessible via reflection once BC DLLs loaded |
| `CSharpCompiler.Instance` accessibility | **[already-faked]** | Accessible after pre-loading all 480 artifacts DLLs (Step 3) |
| `CreateCompilation` (when compilation=null) → SQL via `NavGlobal.AppDatabase` | **[hard-dependency]** | Step 6b: NullReferenceException; requires live SQL database |
| `CSharpCompiler.CompileCSharpFilesAsync` → `NavEnvironment.TemporaryPathHelper` → Windows ACL | **[hard-dependency]** | Step 3b: `PlatformNotSupportedException` on Linux |
| BC service tier installation path (`/usr/share/Microsoft/...`) | **[hard-dependency]** | Shutdown stack: `TempPathHelper.Finalize` hardcodes expected service path |
| Correct `Compilation` matching the app being compiled | **[already-faked]** | SQL bypass confirmed: providing non-null Compilation skips CreateCompilation; runner already builds Compilation in BcCompiler |

---

## GO / NO-GO Recommendation

**NO-GO.**

**Deciding reasons (two independent, either alone is fatal on Linux):**

1. **SQL (`NavGlobal.AppDatabase`)**: The reference-loader path (`compilation=null`) requires a live SQL database. This is a true hard dependency — no in-process fake is possible for the database itself.

2. **Windows ACL (`NavDirectorySecurity`)**: The C#→IL stage (`CSharpCompiler.CompileCSharpFilesAsync`) calls `NavEnvironment.get_TemporaryPathHelper()` → `NavDirectorySecurity.CreateSecurityForDomainDirectory()` → `DirectorySecurity..ctor()`. `DirectorySecurity` uses `System.Security.AccessControl` which is Windows-only. **This runs on every C#→IL compile, even with a pre-built Compilation bypassing SQL.** Fixing it would require rewriting BC's `NavDirectorySecurity` and `TempPathHelper` — a violation of the precompiled-DLL contract.

**Side discovery (no GO implication, just a clarification):** The SQL bypass via pre-built Compilation IS real. When `Compilation` is non-null, `RecompileFullPackage` skips `CreateCompilation` entirely and proceeds to `MethodCompiler.CompileModule`. This is the same pattern the runner already exploits (building a Compilation via `NavCA.Compilation.Create` + `WithReferenceLoader`). The barrier is not in the AL→C# stage but in the C#→IL stage.

**The runner's current architecture is correct for Linux.** The two-stage pipeline (BcCompiler with `Compilation.Emit` for AL→C#, plus BcAssembler with Roslyn for C#→IL) is the right approach, not a workaround. BC's own `CSharpCompiler` cannot run on Linux.

---

## Q2 — Runtime-Package Load Path

### What the runner already does

| Package type | Detection | Runner handling |
|---|---|---|
| **MS R2R pre-compiled** | `publishedartifacts/*.dll` in zip | `AppLoader.ExtractAllDlls` → load all DLL chunks |
| **Service-tier toolkit DLLs** | BC service tier `apps/assembly/release/<ver>/<sha>.dll` | `ServiceTierDllIndex` — indexes by object type name, loads on demand |
| **AL source packages** | `src/*.al` in zip | `BcCompiler` → `Compilation.Emit` → `BcAssembler` → IL |

### Gap: runtime packages (compiled-IL `.app`)

A **runtime package** is a `.app` whose payload is compiled IL (not AL source, not R2R). BC's publish pipeline produces these when a dev extension is compiled server-side. The compiled IL is stored inside the `.app` zip at a path managed by `NavAppPackageMetadataOutputter` (BC's own packaging layer, distinct from the `publishedartifacts/` R2R path).

**BC's extraction path**: `NavAppPackageCompiler.ExtractEmittedContent(Stream packageStream) → Result<byte[]>` — a single method call that extracts the compiled DLL bytes from a runtime package. This method IS accessible in the runner's context (shown in Step 2 reflection output).

**What's missing in the runner**:
1. Detection: no code checks whether an `.app` is a runtime package (has embedded IL but no AL source). A simple heuristic: AL source app has `src/*.al`; R2R has `publishedartifacts/*.dll`; runtime package has neither.
2. Extraction path: `AppLoader` has no case for calling `NavAppPackageCompiler.ExtractEmittedContent`.
3. No test coverage for this package type.

**Sketch for a clean runtime-package path** (not part of this spike's deliverable):
```csharp
// In DependencyLoader or AppLoader:
if (AppLoader.ExtractAl(appPath).Count == 0           // no AL source
    && AppLoader.ExtractAllDlls(appPath).Count == 0)  // no R2R DLLs
{
    // likely a runtime package — extract compiled IL via BC's own extractor
    var compiler = (NavAppPackageCompiler)Activator.CreateInstance(navAppPackageCompilerType)!;
    using var stream = File.OpenRead(appPath);
    var result = ExtractEmittedContentMethod.Invoke(compiler, [stream]);
    // Result<byte[]> → load from bytes
}
```
This would NOT require SQL or `CSharpCompiler` — only `NavAppPackage.Open` + `ExtractEmittedContent`. Viable, but out of scope for this spike.

---

## What This Spike Rules Out (and In)

### Ruled OUT

- Replacing the runner's BcCompiler + BcAssembler with a single `NavAppPackageCompiler.Recompile` call on Linux.
- Eliminating `CallSiteArgWrap`, `SymbolJson`, `JsonSymbolReferenceLoader`, or the Roslyn C#→IL step via BC's own compiler (BC's CSharpCompiler is Windows-only).

### Ruled IN (side discoveries)

- `CSharpCompiler.Instance` IS accessible on Linux (accessible type, callable via reflection). Only `CompileCSharpFilesAsync` itself fails due to `NavDirectorySecurity`.
- The SQL bypass via pre-built `Compilation` is real and confirmed. This is what `BcCompiler.Emit` already exploits correctly — the current architecture is sound.
- `NavAppPackageCompiler.ExtractEmittedContent` is accessible and could enable runtime-package support without SQL or CSharpCompiler dependencies (Q2 gap, future work).

---

## Probe Evidence Location

- `spike/servicetier-compile/Probe/Program.cs` — probe source (throwaway, spike only)
- `spike/servicetier-compile/Probe/Probe.csproj` — probe project
- Run: `dotnet run --project spike/servicetier-compile/Probe --framework net10.0 -c Release`
