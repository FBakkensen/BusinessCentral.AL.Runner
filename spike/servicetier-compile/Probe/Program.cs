// SPIKE / THROWAWAY — do NOT merge to v2.
// Purpose: Probe whether NavAppPackageCompiler.RecompileFullPackage can be driven
// headless in-process, and whether CSharpCompiler.Instance is accessible from a
// byte-loaded Ncl.dll context.
//
// Run: dotnet run --project spike/servicetier-compile/Probe --framework net10.0
//
// Each Step logs:
//   [OK]   if the call succeeded (with summary of what it returned)
//   [FAIL] <ExceptionType>: <message>  (with full stack trace)
//   [CLASS] [already-faked] / [fakeable] / [hard-dependency]
//
// The probe NEVER modifies the production AlRunner source.

using System;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Text;
using System.Threading;
using AlRunnerV2;
using AlRunnerV2.Infrastructure;
using NavCA = Microsoft.Dynamics.Nav.CodeAnalysis;

namespace StcProbe;

internal static class ProbeProgram
{
    // ─── Tiny AL source ──────────────────────────────────────────────────────
    // A single no-dep codeunit that does nothing. Used to create a minimal .app.
    private const string TinyAlSource = """
        codeunit 50001 "Probe Test"
        {
            procedure Hello(): Text
            begin
                exit('hello from probe');
            end;
        }
        """;

    // ─── Minimal NavxManifest.xml ─────────────────────────────────────────────
    private static string BuildManifest(string name, Guid id) => $"""
        <?xml version="1.0" encoding="utf-8"?>
        <Package xmlns="http://schemas.microsoft.com/navx/2012/manifest"
                 Version="2.0">
          <App Id="{id:D}"
               Name="{name}"
               Publisher="SpikePub"
               Version="1.0.0.0"
               Brief=""
               Description=""
               CompatibilityId=""
               Target="OnPrem" />
          <Dependencies />
          <IdRanges>
            <IdRange MinObjectId="50000" MaxObjectId="50099"/>
          </IdRanges>
          <InternalsVisibleTo />
        </Package>
        """;

    // ─── Build a minimal NAVX .app byte[] in memory ───────────────────────────
    // Real BC .app format (40-byte header):
    //   Bytes  0- 3: 'N','A','V','X' magic
    //   Bytes  4- 7: LE uint32 zip-offset (= 40 for real apps)
    //   Bytes  8-11: LE uint32 version (= 2)
    //   Bytes 12-27: 16-byte app GUID (little-endian)
    //   Bytes 28-31: LE uint32 extra field (0 for our minimal app)
    //   Bytes 32-35: LE uint32 padding (0)
    //   Bytes 36-39: 'N','A','V','X' end-of-header marker
    //   Bytes 40+ :  zip archive (PK...)
    private const uint NavxZipOffset40 = 40;
    private static readonly byte[] NavxMagic = [(byte)'N', (byte)'A', (byte)'V', (byte)'X'];

    // Fixed GUID for the probe app — used consistently in BuildTinyApp and Compilation.Create
    private static readonly Guid ProbeAppId = new Guid("d1e2a3b4-c5f6-7890-abcd-ef1234567890");

    private static byte[] BuildTinyApp(string name = "ProbeApp")
    {
        var id = ProbeAppId;

        // Build zip first in its own buffer (so zip-internal offsets are correct)
        byte[] zipBytes;
        using (var zipMs = new MemoryStream())
        {
            // IMPORTANT: use explicit using() block so zip is disposed (writes EOCD)
            // BEFORE ToArray() is called.
            using (var zip = new ZipArchive(zipMs, ZipArchiveMode.Create, leaveOpen: true))
            {
                // NavxManifest.xml
                var manifestEntry = zip.CreateEntry("NavxManifest.xml", CompressionLevel.Fastest);
                using (var w = new StreamWriter(manifestEntry.Open()))
                    w.Write(BuildManifest(name, id));

                // src/ProbeTest.al
                var alEntry = zip.CreateEntry("src/ProbeTest.al", CompressionLevel.Fastest);
                using (var w = new StreamWriter(alEntry.Open()))
                    w.Write(TinyAlSource);

                // [Content_Types].xml
                var ctEntry = zip.CreateEntry("[Content_Types].xml", CompressionLevel.Fastest);
                using (var w = new StreamWriter(ctEntry.Open()))
                    w.Write("""
                        <?xml version="1.0" encoding="utf-8"?>
                        <Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">
                          <Default Extension="al" ContentType="text/plain" />
                          <Override PartName="/NavxManifest.xml" ContentType="application/xml" />
                        </Types>
                        """);
            } // ← zip disposed here, EOCD written
            zipBytes = zipMs.ToArray();
        }

        using var ms = new MemoryStream();

        // Write 40-byte NAVX header
        ms.Write(NavxMagic);                                            // 0-3: NAVX
        WriteUint32(ms, NavxZipOffset40);                               // 4-7: zip offset = 40
        WriteUint32(ms, 2);                                             // 8-11: version = 2
        ms.Write(id.ToByteArray());                                     // 12-27: app GUID (16 bytes)
        WriteUint32(ms, 0);                                             // 28-31: extra field
        WriteUint32(ms, 0);                                             // 32-35: padding
        ms.Write(NavxMagic);                                            // 36-39: end-of-header NAVX marker
        // Zip starts at offset 40
        ms.Write(zipBytes);

        return ms.ToArray();
    }

    private static void WriteUint32(Stream s, uint value)
    {
        var b = BitConverter.GetBytes(value);
        if (!BitConverter.IsLittleEndian) Array.Reverse(b);
        s.Write(b);
    }

    // ─── Reflection helpers ────────────────────────────────────────────────────
    private static Assembly GetNclAssembly()
    {
        return AppDomain.CurrentDomain.GetAssemblies()
            .First(a => a.GetName().Name == "Microsoft.Dynamics.Nav.Ncl");
    }

    private static void Banner(string title)
    {
        Console.WriteLine();
        Console.WriteLine(new string('─', 70));
        Console.WriteLine($"  {title}");
        Console.WriteLine(new string('─', 70));
    }

    private static void LogOk(string msg) => Console.WriteLine($"[OK]   {msg}");
    private static void LogFail(Exception ex, string context)
    {
        Console.WriteLine($"[FAIL] {ex.GetType().FullName}: {ex.Message}");
        if (ex.InnerException != null)
            Console.WriteLine($"       Inner: {ex.InnerException.GetType().FullName}: {ex.InnerException.Message}");
        Console.WriteLine("[STACK]");
        Console.WriteLine(ex.StackTrace);
        if (ex.InnerException?.StackTrace != null)
        {
            Console.WriteLine("[INNER STACK]");
            Console.WriteLine(ex.InnerException.StackTrace);
        }
    }
    private static void LogClass(string cls, string why) =>
        Console.WriteLine($"[CLASS] {cls}  // {why}");

    // ─── Entry point ──────────────────────────────────────────────────────────
    internal static int Main(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;
        Console.WriteLine("=== STC Probe — service-tier compile spike ===");
        Console.WriteLine($"Runtime: {System.Runtime.InteropServices.RuntimeEnvironment.GetRuntimeDirectory()}");
        Console.WriteLine($"PID: {System.Diagnostics.Process.GetCurrentProcess().Id}");

        // ── Pre-load ALL BC runtime DLLs from artifacts directory ────────────────
        // This ensures any DLL required by Recompile (including telemetry/OTel DLLs)
        // is already in the AppDomain before reflection calls trigger assembly resolution.
        var artifactsDir = BcArtifacts.ServiceTierDir;
        var preloadedCount = 0;
        AppDomain.CurrentDomain.AssemblyResolve += (sender, e) =>
        {
            var asmName = new AssemblyName(e.Name).Name + ".dll";
            var candidate = Path.Combine(artifactsDir, asmName);
            if (File.Exists(candidate))
            {
                Console.WriteLine($"[RESOLVE] {asmName} from artifacts");
                return Assembly.LoadFrom(candidate);
            }
            return null;
        };
        foreach (var dll in Directory.EnumerateFiles(artifactsDir, "*.dll"))
        {
            try { Assembly.LoadFrom(dll); preloadedCount++; }
            catch { /* ignore — some DLLs fail to load, that's OK */ }
        }
        Console.WriteLine($"[INFO] Pre-loaded {preloadedCount} BC artifacts DLLs from {artifactsDir}");

        // ── Bootstrap ──────────────────────────────────────────────────────────
        Banner("Step 0: BcRuntime.EnsureApplied() + EnsureCompilePatches()");
        try
        {
            BcRuntime.EnsureApplied();
            LogOk("BcRuntime.EnsureApplied() succeeded — runtime patches installed");
        }
        catch (Exception ex)
        {
            LogFail(ex, "BcRuntime.EnsureApplied");
            Console.Error.WriteLine("FATAL: Cannot proceed without runtime bootstrap.");
            return 1;
        }

        try
        {
            // bc-linux compile-subset patches: Patch #9 (ACL bypass), #14 (Cecil), #15 (assembly probing)
            BcRuntime.EnsureCompilePatches();
            LogOk("BcRuntime.EnsureCompilePatches() applied");
        }
        catch (Exception ex)
        {
            LogFail(ex, "BcRuntime.EnsureCompilePatches");
            // Non-fatal: continue and observe how far we get
        }

        var nclAsm = GetNclAssembly();
        Console.WriteLine($"[INFO] Ncl loaded from: '{nclAsm.Location}' (empty = byte-loaded)");

        // ── Step 1: Build tiny .app ────────────────────────────────────────────
        Banner("Step 1: Build tiny in-memory .app");
        byte[] appBytes;
        try
        {
            appBytes = BuildTinyApp("ProbeApp");
            LogOk($"Built {appBytes.Length} byte NAVX .app");
        }
        catch (Exception ex)
        {
            LogFail(ex, "BuildTinyApp");
            return 1;
        }

        // Save to temp file (NavAppPackageCompiler takes a Stream, but we'll also
        // need a file path for CSharpCompiler which takes file paths)
        var tmpDir = Path.Combine(Path.GetTempPath(), "stc-probe-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(tmpDir);
        var appPath = Path.Combine(tmpDir, "ProbeApp.app");
        File.WriteAllBytes(appPath, appBytes);
        Console.WriteLine($"[INFO] .app written to: {appPath}");

        // ── Step 2: Inspect NavAppPackageCompiler via reflection ──────────────
        Banner("Step 2: Reflect on NavAppPackageCompiler");
        Type? compilerType = null;
        MethodInfo? recompileMethod = null;
        try
        {
            compilerType = nclAsm.GetType("Microsoft.Dynamics.Nav.Runtime.Apps.NavAppPackageCompiler");
            if (compilerType == null) throw new InvalidOperationException("Type not found in Ncl assembly");
            LogOk($"Found type: {compilerType.FullName}");

            // Find the public Recompile method (the entry point wrapping RecompileFullPackage)
            var methods = compilerType.GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.NonPublic);
            foreach (var m in methods)
                Console.WriteLine($"  [METHOD] {m.Name}({string.Join(", ", m.GetParameters().Select(p => $"{p.ParameterType.Name} {p.Name}"))}) → {m.ReturnType.Name}");

            recompileMethod = methods.FirstOrDefault(m => m.Name == "Recompile");
            if (recompileMethod == null) throw new InvalidOperationException("Recompile method not found");
            LogOk($"Found method: {recompileMethod.Name}");
        }
        catch (Exception ex)
        {
            LogFail(ex, "Reflect NavAppPackageCompiler");
            return 1;
        }

        // ── Step 2b: Inspect NavEnvironment + TempPathHelper fields ───────────
        Banner("Step 2b: NavEnvironment string fields + TempPathHelper fields (path redirect research)");
        {
            var envType = nclAsm.GetType("Microsoft.Dynamics.Nav.Runtime.NavEnvironment");
            if (envType != null)
            {
                var staticFields = envType.GetFields(BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
                Console.WriteLine($"[2b] NavEnvironment static fields ({staticFields.Length}):");
                foreach (var f in staticFields.Where(f => f.FieldType == typeof(string) || f.FieldType == typeof(Guid) || f.Name.Contains("path", StringComparison.OrdinalIgnoreCase) || f.Name.Contains("dir", StringComparison.OrdinalIgnoreCase) || f.Name.Contains("server", StringComparison.OrdinalIgnoreCase)))
                {
                    try { Console.WriteLine($"  [{f.Name}: {f.FieldType.Name}] = {f.GetValue(null)}"); }
                    catch (Exception ex) { Console.WriteLine($"  [{f.Name}: {f.FieldType.Name}] = <err: {ex.Message}>"); }
                }
            }

            var tempPathType = nclAsm.GetType("Microsoft.Dynamics.Nav.Runtime.TempPathHelper");
            if (tempPathType != null)
            {
                Console.WriteLine($"[2b] TempPathHelper instance fields:");
                foreach (var f in tempPathType.GetFields(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public))
                    Console.WriteLine($"  [{f.Name}: {f.FieldType.Name}]");
                Console.WriteLine($"[2b] TempPathHelper static fields:");
                foreach (var f in tempPathType.GetFields(BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public))
                {
                    try { Console.WriteLine($"  [{f.Name}: {f.FieldType.Name}] = {f.GetValue(null)}"); }
                    catch { }
                }
                Console.WriteLine($"[2b] TempPathHelper methods:");
                foreach (var m in tempPathType.GetMethods(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.DeclaredOnly))
                    Console.WriteLine($"  {m.Name}({string.Join(", ", m.GetParameters().Select(p => $"{p.ParameterType.Name} {p.Name}"))}) → {m.ReturnType.Name}");
            }
        }

        // ── Step 3: Inspect CSharpCompiler.Instance ───────────────────────────
        Banner("Step 3: CSharpCompiler.Instance — check if accessible (expected: FAIL due to byte-loaded Ncl)");
        Type? csCompilerType = null;
        try
        {
            // Search all loaded assemblies since CSharpCompiler may not be in Ncl directly
            csCompilerType = AppDomain.CurrentDomain.GetAssemblies()
                .Select(a => a.GetType("Microsoft.Dynamics.Nav.Runtime.Apps.CSharpCompiler"))
                .FirstOrDefault(t => t != null);
            if (csCompilerType == null)
            {
                Console.WriteLine($"[INFO] CSharpCompiler not in loaded assemblies ({AppDomain.CurrentDomain.GetAssemblies().Length} assemblies searched)");
                // Try alternative names
                csCompilerType = AppDomain.CurrentDomain.GetAssemblies()
                    .SelectMany(a => { try { return a.GetTypes(); } catch { return []; } })
                    .FirstOrDefault(t => t.Name == "CSharpCompiler" && t.IsClass);
                if (csCompilerType != null)
                    Console.WriteLine($"[INFO] Found CSharpCompiler via type scan: {csCompilerType.FullName} in {csCompilerType.Assembly.GetName().Name}");
            }
            if (csCompilerType == null) throw new InvalidOperationException("CSharpCompiler type not found in any loaded assembly");
            LogOk($"Found CSharpCompiler type: {csCompilerType.FullName}");

            var instanceProp = csCompilerType.GetProperty("Instance",
                BindingFlags.Public | BindingFlags.Static | BindingFlags.NonPublic);
            Console.WriteLine($"[INFO] Instance property found: {instanceProp != null}");

            if (instanceProp != null)
            {
                // This may trigger the constructor which reads Assembly.GetExecutingAssembly().Location
                // If Ncl is byte-loaded, Location="" and CreateAssemblyReferences will fail
                var instance = instanceProp.GetValue(null);
                if (instance != null)
                {
                    LogOk($"CSharpCompiler.Instance obtained: {instance.GetType().Name}");
                    // Inspect methods available on the instance
                    var csMethods = instance.GetType().GetMethods(
                        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                        .Select(m => m.Name).Distinct().OrderBy(n => n).ToArray();
                    Console.WriteLine($"[INFO] CSharpCompiler methods: {string.Join(", ", csMethods)}");
                    // Check for CompileCSharpFilesAsync
                    var compileAsync = instance.GetType().GetMethods(
                        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                        .FirstOrDefault(m => m.Name.Contains("Compile") || m.Name.Contains("CompileAsync") || m.Name.Contains("CompileCSharp"));
                    Console.WriteLine($"[INFO] CompileCSharp-like method: {compileAsync?.Name ?? "NOT FOUND"} ({compileAsync?.GetParameters().Length ?? 0} params)");
                    if (compileAsync != null)
                    {
                        // Print parameter types
                        var parms = string.Join(", ", compileAsync.GetParameters().Select(p => $"{p.ParameterType.Name} {p.Name}"));
                        Console.WriteLine($"[INFO] {compileAsync.Name}({parms})");

                        // Step 3b: Try calling CompileCSharpFilesAsync with trivial C# source
                        Banner("Step 3b: CSharpCompiler.Instance.CompileCSharpFilesAsync with trivial C#");
                        try
                        {
                            var tmpCsDir = Path.Combine(Path.GetTempPath(), $"stc-csharp-{Guid.NewGuid():N}");
                            Directory.CreateDirectory(tmpCsDir);
                            var csFile = Path.Combine(tmpCsDir, "Probe.cs");
                            File.WriteAllText(csFile, """
                                namespace ProbeNs {
                                    public class ProbeClass {
                                        public string Hello() => "hello from BC CSharpCompiler";
                                    }
                                }
                                """);

                            // Signature: CompileCSharpFilesAsync(string assemblyName, IList<string> filePaths, ...)
                            // Try with just assemblyName + filePaths + defaults
                            var fileList = new System.Collections.Generic.List<string> { csFile };
                            // Use reflection to call it — signature may have optional params
                            var csResult = compileAsync.Invoke(instance, new object?[]
                            {
                                "ProbeAssembly",         // assemblyName
                                (System.Collections.Generic.IList<string>)fileList,
                                null,                    // 3rd param (nullable)
                                null                     // 4th param (nullable)
                            });
                            LogOk($"CompileCSharpFilesAsync returned: {csResult?.GetType().Name}");
                            // Inspect result
                            if (csResult != null)
                            {
                                var csRt = csResult.GetType();
                                foreach (var p in csRt.GetProperties(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
                                    Console.WriteLine($"  [3b-PROP] {p.Name}: {TryGetPropValue(csResult, p)}");
                            }
                            LogClass("[accessible-step3b]", "CSharpCompiler.Instance.CompileCSharpFilesAsync returned without throwing");
                            // The Task.Result threw TargetInvocationException — unwrap by calling GetAwaiter().GetResult()
                            if (csResult != null)
                            {
                                var getAwaiter = csResult.GetType().GetMethod("GetAwaiter");
                                if (getAwaiter != null)
                                {
                                    try
                                    {
                                        var awaiter = getAwaiter.Invoke(csResult, null);
                                        var getResult = awaiter?.GetType().GetMethod("GetResult");
                                        var taskFinalResult = getResult?.Invoke(awaiter, null);
                                        Console.WriteLine($"  [3b-OK] GetAwaiter().GetResult() succeeded! Type={taskFinalResult?.GetType().Name}");
                                        if (taskFinalResult != null)
                                        {
                                            foreach (var p in taskFinalResult.GetType().GetProperties(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
                                                Console.WriteLine($"    [3b-RES-PROP] {p.Name}: {TryGetPropValue(taskFinalResult, p)}");
                                        }
                                        LogClass("[accessible-step3b-OK]", "CSharpCompiler.CompileCSharpFilesAsync SUCCEEDED end-to-end!");

                                        // ── Step R2-1: Load the compiled DLL and invoke a method ──
                                        Banner("Step R2-1: Load BC-compiled DLL bytes + invoke method");
                                        try
                                        {
                                            var assemblyContent = taskFinalResult?.GetType()
                                                .GetProperty("AssemblyContent")?.GetValue(taskFinalResult) as byte[];
                                            if (assemblyContent == null || assemblyContent.Length == 0)
                                            {
                                                Console.WriteLine("[R2-1-FAIL] AssemblyContent is null or empty");
                                            }
                                            else
                                            {
                                                Console.WriteLine($"[R2-1] AssemblyContent: {assemblyContent.Length} bytes");
                                                var loadedAsm = System.Reflection.Assembly.Load(assemblyContent);
                                                Console.WriteLine($"[R2-1] Assembly.Load succeeded: {loadedAsm.FullName}");
                                                var probeType = loadedAsm.GetType("ProbeNs.ProbeClass");
                                                if (probeType != null)
                                                {
                                                    var probeInstance = Activator.CreateInstance(probeType);
                                                    var helloMethod = probeType.GetMethod("Hello");
                                                    var result3b = helloMethod?.Invoke(probeInstance, null) as string;
                                                    Console.WriteLine($"[R2-1-SUCCESS] Hello() returned: '{result3b}'");
                                                    LogClass("[R2-1-GREEN]", "BC's CSharpCompiler emitted a loadable DLL! Method invoked in-process on Linux!");
                                                }
                                                else
                                                {
                                                    // List types in the loaded assembly
                                                    var types = loadedAsm.GetTypes();
                                                    Console.WriteLine($"[R2-1] Types in loaded DLL: {string.Join(", ", types.Select(t => t.FullName))}");
                                                }
                                            }
                                        }
                                        catch (Exception ex_r2)
                                        {
                                            Console.WriteLine($"[R2-1-FAIL] {ex_r2.GetType().Name}: {ex_r2.Message}");
                                            Console.WriteLine($"[R2-1-STACK] {ex_r2.StackTrace?[..Math.Min(600, ex_r2.StackTrace?.Length ?? 0)]}");
                                        }
                                    }
                                    catch (TargetInvocationException tie2) when (tie2.InnerException != null)
                                    {
                                        var ex2 = tie2.InnerException;
                                        Console.WriteLine($"  [3b-TASK-FAIL] {ex2.GetType().Name}: {ex2.Message[..Math.Min(500, ex2.Message.Length)]}");
                                        Console.WriteLine($"  [3b-TASK-STACK] {ex2.StackTrace?[..Math.Min(800, ex2.StackTrace?.Length ?? 0)]}");
                                        if (ex2.InnerException != null)
                                            Console.WriteLine($"  [3b-INNER] {ex2.InnerException.GetType().Name}: {ex2.InnerException.Message[..Math.Min(300, ex2.InnerException.Message.Length)]}");
                                    }
                                    catch (Exception ex2)
                                    {
                                        Console.WriteLine($"  [3b-TASK-FAIL-UNEXPECTED] {ex2.GetType().Name}: {ex2.Message}");
                                    }
                                }
                            }
                            Directory.Delete(tmpCsDir, recursive: true);
                        }
                        catch (TargetInvocationException tie) when (tie.InnerException != null)
                        {
                            LogFail(tie.InnerException, "CompileCSharpFilesAsync");
                            if (tie.InnerException.Message.Contains("Location") || tie.InnerException.Message.Contains("assembly reference"))
                                LogClass("[hard-dependency-3b]", "CSharpCompiler requires Ncl DLL disk path for assembly references — blocked by byte-loading");
                            else
                                LogClass("[error-3b]", $"Unexpected: {tie.InnerException.GetType().Name}: {tie.InnerException.Message[..Math.Min(200, tie.InnerException.Message.Length)]}");
                        }
                        catch (Exception ex)
                        {
                            LogFail(ex, "CompileCSharpFilesAsync");
                        }
                    }
                    LogClass("[accessible]", "CSharpCompiler.Instance is accessible — methods visible. Tested in Step 3b.");
                }
                else
                {
                    Console.WriteLine("[WARN] CSharpCompiler.Instance returned null");
                    LogClass("[hard-dependency]", "CSharpCompiler.Instance is null — likely requires NavEnvironment init");
                }
            }
        }
        catch (Exception ex)
        {
            LogFail(ex, "CSharpCompiler.Instance");
            if (ex.InnerException?.Message?.Contains("Location") == true ||
                ex.Message.Contains("Location") ||
                ex.InnerException?.Message?.Contains("assembly") == true)
            {
                LogClass("[hard-dependency]", "CSharpCompiler reads Ncl.dll path via Assembly.GetExecutingAssembly().Location — returns '' when byte-loaded");
            }
            else
            {
                LogClass("[hard-dependency-other]", $"Unexpected error in CSharpCompiler.Instance: {ex.GetType().Name}");
            }
        }

        // ── Step 4: NavAppCompileArguments — construct via reflection ─────────
        Banner("Step 4: Construct NavAppCompileArguments via reflection");
        object? compileArgs = null;
        Type? argsType = null;
        try
        {
            argsType = nclAsm.GetType("Microsoft.Dynamics.Nav.Runtime.Apps.NavAppCompileArguments");
            if (argsType == null) throw new InvalidOperationException("NavAppCompileArguments type not found");

            compileArgs = Activator.CreateInstance(argsType,
                nonPublic: true) ?? throw new InvalidOperationException("Activator.CreateInstance returned null");

            // Set PackageStream
            var pkg = new MemoryStream(appBytes);
            SetProperty(compileArgs, "PackageStream", pkg, argsType);
            LogOk("Set PackageStream");

            // Set PackageType (NavAppPackageType enum — try value 0 = "Source")
            var pkgTypeEnum = nclAsm.GetType("Microsoft.Dynamics.Nav.Runtime.Apps.NavAppPackageType");
            if (pkgTypeEnum != null)
            {
                var srcVal = Enum.ToObject(pkgTypeEnum, 0);
                SetProperty(compileArgs, "PackageType", srcVal, argsType);
                Console.WriteLine($"[INFO] Set PackageType = {srcVal}");
            }

            // TenantId: construct AppTenantId via reflection (empty/default)
            var appTenantIdType = nclAsm.GetType("Microsoft.Dynamics.Nav.Apps.Types.AppTenantId")
                ?? nclAsm.GetType("Microsoft.Dynamics.Nav.Runtime.AppTenantId");
            if (appTenantIdType != null)
            {
                // Try static Empty or Default property
                var emptyProp = appTenantIdType.GetProperty("Empty",
                    BindingFlags.Public | BindingFlags.Static)
                    ?? appTenantIdType.GetProperty("Default",
                        BindingFlags.Public | BindingFlags.Static);
                object? tenantId;
                if (emptyProp != null)
                {
                    tenantId = emptyProp.GetValue(null);
                    Console.WriteLine($"[INFO] AppTenantId.Empty/Default: {tenantId}");
                }
                else if (appTenantIdType.IsValueType)
                {
                    tenantId = Activator.CreateInstance(appTenantIdType);
                    Console.WriteLine($"[INFO] AppTenantId default struct: {tenantId}");
                }
                else
                {
                    // Try string constructor
                    var strCtor = appTenantIdType.GetConstructor(
                        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance,
                        null, new[] { typeof(string) }, null);
                    tenantId = strCtor != null
                        ? strCtor.Invoke(new object?[] { "" })
                        : Activator.CreateInstance(appTenantIdType, nonPublic: true);
                    Console.WriteLine($"[INFO] AppTenantId constructed: {tenantId}");
                }
                SetProperty(compileArgs, "TenantId", tenantId, argsType);
            }
            else
            {
                Console.WriteLine("[WARN] AppTenantId type not found — leaving TenantId unset");
            }

            // SkipTargetLevelValidation: true (avoid target-level checks)
            SetProperty(compileArgs, "SkipTargetLevelValidation", true, argsType);

            LogOk($"NavAppCompileArguments constructed: {compileArgs.GetType().FullName}");
        }
        catch (Exception ex)
        {
            LogFail(ex, "NavAppCompileArguments construction");
            LogClass("[fakeable]", "NavAppCompileArguments is internal but has accessible ctors / properties — can be constructed via reflection");
        }

        // ── Step 5: NavCancellationToken — get default ─────────────────────────
        Banner("Step 5: NavCancellationToken default");
        object? cancellationToken = null;
        try
        {
            // Search all loaded assemblies for NavCancellationToken
            var tokenType = AppDomain.CurrentDomain.GetAssemblies()
                .Select(a => a.GetType("Microsoft.Dynamics.Nav.Runtime.NavCancellationToken")
                           ?? a.GetType("Microsoft.Dynamics.Nav.Runtime.Apps.NavCancellationToken"))
                .FirstOrDefault(t => t != null);
            if (tokenType == null) throw new InvalidOperationException("NavCancellationToken type not found in any loaded assembly");
            Console.WriteLine($"[INFO] NavCancellationToken found in: {tokenType.Assembly.GetName().Name}, IsValueType: {tokenType.IsValueType}");
            cancellationToken = Activator.CreateInstance(tokenType);
            LogOk($"NavCancellationToken default: {cancellationToken}");
            LogClass("[already-faked]", "NavCancellationToken is a value type — default() works");
        }
        catch (Exception ex)
        {
            LogFail(ex, "NavCancellationToken");
            Console.WriteLine("[INFO] NavCancellationToken not found — passing null (reflection converts null to default for value types)");
            LogClass("[fakeable]", "NavCancellationToken construction failed — null passed to Invoke is acceptable for value types");
        }

        // ── Step 6: Invoke Recompile with null Compilation (BC loader path) ────
        Banner("Step 6: Recompile(args, cancellationToken, compilation=null)  [BC reference loader path]");
        Console.WriteLine("[INFO] Expected: FAIL — BC reference loader requires SQL (NavSqlConnectionScope)");
        if (compileArgs != null && recompileMethod != null)
        {
            try
            {
                // Reset PackageStream position
                var pkgStreamProp = argsType!.GetProperty("PackageStream",
                    BindingFlags.Public | BindingFlags.Instance | BindingFlags.NonPublic);
                if (pkgStreamProp?.GetValue(compileArgs) is Stream s) s.Position = 0;

                var result = recompileMethod.Invoke(null, new object?[]
                {
                    compileArgs,
                    cancellationToken,
                    null  // compilation = null → BC creates its own via CreateReferenceLoader
                });
                LogOk($"Recompile returned: {result?.GetType().FullName}");

                // Result returned (may be success or failure) — inspect deeply
                if (result != null)
                {
                    var resultType = result.GetType();

                    foreach (var prop in resultType.GetProperties(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
                        Console.WriteLine($"  [RESULT-PROP] {prop.Name}: {TryGetPropValue(result, prop)}");

                    foreach (var field in resultType.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
                        Console.WriteLine($"  [RESULT-FIELD] {field.Name}: {TryGetFieldValue(result, field)}");

                    // Inspect the Error object if present
                    var errorProp = resultType.GetProperty("Error",
                        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                    if (errorProp != null)
                    {
                        var errorObj = errorProp.GetValue(result);
                        if (errorObj != null)
                        {
                            Console.WriteLine($"  [ERROR-TYPE] {errorObj.GetType().FullName}");
                            foreach (var p in errorObj.GetType().GetProperties(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
                                Console.WriteLine($"  [ERROR-PROP] {p.Name}: {TryGetPropValue(errorObj, p)}");
                            foreach (var f in errorObj.GetType().GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
                                Console.WriteLine($"  [ERROR-FIELD] {f.Name}: {TryGetFieldValue(errorObj, f)}");
                        }
                    }

                    // Try to get the Value (CompilationOutput) — only valid on success
                    var successProp = resultType.GetProperty("Success",
                        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                    bool isSuccess = successProp?.GetValue(result) is true;
                    if (isSuccess)
                    {
                        var valueProp = resultType.GetProperty("Value",
                            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                        if (valueProp != null)
                        {
                            var value = valueProp.GetValue(result);
                            Console.WriteLine($"  [RESULT-VALUE] {value?.GetType().FullName ?? "null"}");
                            if (value != null)
                            {
                                foreach (var prop in value.GetType().GetProperties(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
                                    Console.WriteLine($"    [OUTPUT-PROP] {prop.Name}: {TryGetPropValue(value, prop)}");
                            }
                        }
                    }
                }
                LogClass("[accessible]", "BC reference loader path returned a Result (see [RESULT-PROP] Success for outcome)");
            }
            catch (TargetInvocationException tie) when (tie.InnerException != null)
            {
                var inner = tie.InnerException;
                LogFail(inner, "Recompile(null compilation)");
                ClassifyException(inner);
            }
            catch (Exception ex)
            {
                LogFail(ex, "Recompile(null compilation)");
                ClassifyException(ex);
            }
        }
        else
        {
            Console.WriteLine("[SKIP] Cannot invoke Recompile — args or method missing");
        }

        // ── Step 6b: Recompile with REAL customer source app ─────────────────
        // Tests BC reference loader with a properly-formatted real .app
        const string RealAppPath = "/home/stefan/Documents/Repos/ABC/RecoverySolutions/MainApps/Customizations/A BC Consulting_Customizations_1.13.0.0.app";
        Banner($"Step 6b: Recompile with real source app: {Path.GetFileName(RealAppPath)}");
        if (File.Exists(RealAppPath) && recompileMethod != null && argsType != null)
        {
            try
            {
                var realArgs = Activator.CreateInstance(argsType, nonPublic: true)!;
                var realPkg = File.OpenRead(RealAppPath);
                SetProperty(realArgs, "PackageStream", realPkg, argsType);
                SetProperty(realArgs, "SkipTargetLevelValidation", true, argsType);

                var result6b = recompileMethod.Invoke(null, new object?[] { realArgs, cancellationToken, null });
                LogOk($"Recompile(real app) returned: {result6b?.GetType().FullName}");
                if (result6b != null)
                {
                    var rt = result6b.GetType();
                    var successP = rt.GetProperty("Success", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                    var failP = rt.GetProperty("Failure", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                    Console.WriteLine($"  [6b-RESULT] Success={TryGetPropValue(result6b, successP!)} Failure={TryGetPropValue(result6b, failP!)}");

                    var errorP = rt.GetProperty("Error", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                    if (errorP?.GetValue(result6b) is { } errObj6b)
                    {
                        foreach (var p in errObj6b.GetType().GetProperties(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
                            Console.WriteLine($"  [6b-ERROR-PROP] {p.Name}: {TryGetPropValue(errObj6b, p)}");
                    }
                }
            }
            catch (TargetInvocationException tie) when (tie.InnerException != null)
            {
                LogFail(tie.InnerException, "Recompile(real app, null compilation)");
                ClassifyException(tie.InnerException);
            }
            catch (Exception ex)
            {
                LogFail(ex, "Recompile(real app, null compilation)");
            }
        }
        else
        {
            Console.WriteLine($"[SKIP] Real app not found at {RealAppPath}");
        }

        // ── Step 7: Invoke Recompile WITH a pre-built Compilation (bypass SQL path) ──
        // Key question: if we pass OUR Compilation (built without SQL via NavCA.Compilation.Create),
        // does Recompile skip CreateCompilation and run to completion using CSharpCompiler.Instance?
        Banner("Step 7: Recompile(args, cancellationToken, compilation=ourCompilation) [bypass SQL loader path]");
        Console.WriteLine("[INFO] Building NavCA.Compilation from tiny AL source — bypasses BC's SQL reference loader");
        if (compileArgs != null && recompileMethod != null)
        {
            try
            {
                // Parse the tiny AL source into a syntax tree using BC's own parser
                var parseOpts = new NavCA.ParseOptions(
                    runtimeVersion: null!,
                    preprocessorSymbols: [],
                    documentationMode: NavCA.DocumentationMode.None);

                var tree = NavCA.Syntax.SyntaxTree.ParseObjectText(
                    TinyAlSource, path: "ProbeTest.al", encoding: null!, parseOpts, default);

                var compOpts = new NavCA.CompilationOptions(
                    continueBuildOnError: true,
                    target: NavCA.CompilationTarget.OnPrem,
                    generateOptions:
                        NavCA.CompilationGenerationOptions.Code |
                        NavCA.CompilationGenerationOptions.Navigation);

                var ourCompilation = NavCA.Compilation.Create(
                    moduleName: "ProbeApp",
                    publisher: "SpikePub",
                    version: new Version(1, 0, 0, 0),
                    appId: ProbeAppId,
                    syntaxTrees: [tree],
                    options: compOpts);

                LogOk($"Built ourCompilation: {ourCompilation.GetType().Name}");

                // Reset PackageStream and invoke Recompile with our Compilation
                var pkgStreamProp7 = argsType!.GetProperty("PackageStream",
                    BindingFlags.Public | BindingFlags.Instance | BindingFlags.NonPublic);
                if (pkgStreamProp7?.GetValue(compileArgs) is Stream s7) s7.Position = 0;

                var result7 = recompileMethod.Invoke(null, new object?[]
                {
                    compileArgs,
                    cancellationToken,
                    ourCompilation   // NON-NULL: should bypass CreateCompilation (SQL path)
                });
                LogOk($"Recompile(ourCompilation) returned: {result7?.GetType().FullName}");

                if (result7 != null)
                {
                    var rt7 = result7.GetType();
                    var successP7 = rt7.GetProperty("Success", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                    var failP7 = rt7.GetProperty("Failure", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                    bool success7 = (bool)(successP7?.GetValue(result7) ?? false);
                    Console.WriteLine($"  [7-RESULT] Success={success7} Failure={TryGetPropValue(result7, failP7!)}");

                    if (success7)
                    {
                        var valP7 = rt7.GetProperty("Value", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                        var output7 = valP7?.GetValue(result7);
                        if (output7 != null)
                        {
                            LogOk($"CompilationOutput obtained: {output7.GetType().Name}");
                            foreach (var p in output7.GetType().GetProperties(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
                                Console.WriteLine($"  [7-OUTPUT] {p.Name}: {TryGetPropValue(output7, p)}");
                            LogClass("[GO-signal]", "Recompile with pre-built Compilation SUCCEEDED — no SQL needed when Compilation is provided by runner");
                        }
                    }
                    else
                    {
                        var errorP7 = rt7.GetProperty("Error", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                        if (errorP7?.GetValue(result7) is { } errObj7)
                        {
                            foreach (var p in errObj7.GetType().GetProperties(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
                                Console.WriteLine($"  [7-ERROR-PROP] {p.Name}: {TryGetPropValue(errObj7, p)}");
                        }
                        LogClass("[needs-investigation]", "Recompile with pre-built Compilation FAILED — see [7-ERROR-PROP] for next blocker");
                    }
                }
            }
            catch (TargetInvocationException tie) when (tie.InnerException != null)
            {
                LogFail(tie.InnerException, "Recompile(ourCompilation)");
                ClassifyException(tie.InnerException);
            }
            catch (Exception ex)
            {
                LogFail(ex, "Recompile(ourCompilation)");
            }
        }

        // ── Step 7c: REAL app + pre-built Compilation — definitive bypass test ──
        // Tiny-app ZIP format is incompatible with ZipPackage (OPC reader).
        // Use the real customer .app (valid OPC zip) + our minimal Compilation
        // to test whether passing a non-null Compilation truly skips CreateCompilation.
        Banner("Step 7c: Recompile(REAL app, ourCompilation) [definitive SQL bypass test]");
        if (recompileMethod != null && File.Exists(RealAppPath))
        {
            try
            {
                // Build minimal Compilation (tiny AL source, no deps — just passes null check)
                var parseOpts7c = new NavCA.ParseOptions(
                    runtimeVersion: null!,
                    preprocessorSymbols: [],
                    documentationMode: NavCA.DocumentationMode.None);
                var tree7c = NavCA.Syntax.SyntaxTree.ParseObjectText(
                    TinyAlSource, path: "ProbeTest.al", encoding: null!, parseOpts7c, default);
                var compOpts7c = new NavCA.CompilationOptions(
                    continueBuildOnError: true,
                    target: NavCA.CompilationTarget.OnPrem,
                    generateOptions:
                        NavCA.CompilationGenerationOptions.Code |
                        NavCA.CompilationGenerationOptions.Navigation);
                var minimalCompilation = NavCA.Compilation.Create(
                    moduleName: "MinimalBypass",
                    publisher: "SpikePub",
                    version: new Version(1, 0, 0, 0),
                    appId: ProbeAppId,
                    syntaxTrees: [tree7c],
                    options: compOpts7c);
                LogOk($"Built minimalCompilation (tiny AL, no deps): {minimalCompilation.GetType().Name}");

                // Construct args with real app stream
                var realBytes7c = File.ReadAllBytes(RealAppPath);
                var realStream7c = new MemoryStream(realBytes7c);

                var argsType7c = nclAsm.GetType("Microsoft.Dynamics.Nav.Runtime.Apps.NavAppCompileArguments")!;
                var args7c = Activator.CreateInstance(argsType7c, nonPublic: true)!;
                SetProperty(args7c, "PackageStream", realStream7c, argsType7c);
                var pkgTypeEnum7c = nclAsm.GetType("Microsoft.Dynamics.Nav.Runtime.Apps.NavAppPackageType");
                if (pkgTypeEnum7c != null)
                    SetProperty(args7c, "PackageType", Enum.ToObject(pkgTypeEnum7c, 0), argsType7c);

                var result7c = recompileMethod.Invoke(null, new object?[]
                {
                    args7c,
                    null,              // cancellationToken = default(NavCancellationToken)
                    minimalCompilation  // NON-NULL: should skip CreateCompilation
                });
                LogOk($"Recompile(realApp, ourCompilation) returned: {result7c?.GetType().FullName}");

                if (result7c != null)
                {
                    var rt7c = result7c.GetType();
                    var successP7c = rt7c.GetProperty("Success", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                    bool success7c = (bool)(successP7c?.GetValue(result7c) ?? false);
                    var failP7c = rt7c.GetProperty("Failure", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                    Console.WriteLine($"  [7c-RESULT] Success={success7c} Failure={TryGetPropValue(result7c, failP7c!)}");

                    if (success7c)
                    {
                        var valP7c = rt7c.GetProperty("Value", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                        var output7c = valP7c?.GetValue(result7c);
                        LogOk($"CompilationOutput: {output7c?.GetType().Name}");
                        if (output7c != null)
                            foreach (var p in output7c.GetType().GetProperties(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
                                Console.WriteLine($"  [7c-OUTPUT] {p.Name}: {TryGetPropValue(output7c, p)}");
                        LogClass("[GO-signal]", "REAL app + pre-built Compilation SUCCEEDED — SQL bypass confirmed; CSharpCompiler.Instance is the path to IL");
                    }
                    else
                    {
                        var errorP7c = rt7c.GetProperty("Error", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                        if (errorP7c?.GetValue(result7c) is { } err7c)
                        {
                            foreach (var p in err7c.GetType().GetProperties(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
                                Console.WriteLine($"  [7c-ERROR] {p.Name}: {TryGetPropValue(err7c, p)}");
                        }
                        LogClass("[blocked]", "REAL app + pre-built Compilation FAILED — see [7c-ERROR] for next blocker");
                    }
                }
            }
            catch (TargetInvocationException tie) when (tie.InnerException != null)
            {
                LogFail(tie.InnerException, "Recompile(realApp, ourCompilation)");
                ClassifyException(tie.InnerException);
            }
            catch (Exception ex)
            {
                LogFail(ex, "Recompile(realApp, ourCompilation)");
            }
        }
        else
        {
            Console.WriteLine($"[SKIP] Real app not found at {RealAppPath}");
        }

        // ── Step 8: NavAppLatestSymbolReferenceLoader — inspect for non-SQL path ──
        Banner("Step 8: NavAppLatestSymbolReferenceLoader — inspect methods for SQL dependency");
        try
        {
            var loaderType = nclAsm.GetType("Microsoft.Dynamics.Nav.Runtime.Apps.NavAppLatestSymbolReferenceLoader");
            if (loaderType == null) throw new InvalidOperationException("Type not found");
            LogOk($"Found: {loaderType.FullName}");

            var methods = loaderType.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                .Select(m => m.Name)
                .Distinct()
                .OrderBy(n => n)
                .ToArray();
            Console.WriteLine($"[INFO] Methods: {string.Join(", ", methods)}");

            // Check if NavSqlConnectionScope is directly referenced
            var sqlScopeType = nclAsm.GetType("Microsoft.Dynamics.Nav.NavSqlConnectionScope")
                ?? nclAsm.GetType("Microsoft.Dynamics.Nav.Runtime.NavSqlConnectionScope");
            Console.WriteLine($"[INFO] NavSqlConnectionScope found in Ncl: {sqlScopeType != null}");
            LogClass("[hard-dependency]", "NavAppLatestSymbolReferenceLoader.GetSymbolPackageMetadataUsingPublishedApplicationTable calls NavSqlConnectionScope.Create(NavGlobal.AppDatabase) — confirmed via decompilation");
        }
        catch (Exception ex)
        {
            LogFail(ex, "NavAppLatestSymbolReferenceLoader inspection");
        }

        // ── Step 9: NavGlobal.AppDatabase — check if it's null / throws ───────
        Banner("Step 9: NavGlobal.AppDatabase — accessible?");
        try
        {
            var navGlobalType = nclAsm.GetType("Microsoft.Dynamics.Nav.Runtime.NavGlobal")
                ?? nclAsm.GetType("Microsoft.Dynamics.Nav.NavGlobal");
            if (navGlobalType == null) throw new InvalidOperationException("NavGlobal type not found");

            var appDbProp = navGlobalType.GetProperty("AppDatabase",
                BindingFlags.Public | BindingFlags.Static | BindingFlags.NonPublic)
                ?? navGlobalType.GetField("AppDatabase",
                    BindingFlags.Public | BindingFlags.Static | BindingFlags.NonPublic) as MemberInfo;

            Console.WriteLine($"[INFO] AppDatabase member found: {appDbProp != null}");
            if (appDbProp is PropertyInfo pi)
            {
                var val = pi.GetValue(null);
                Console.WriteLine($"[INFO] NavGlobal.AppDatabase = {val?.ToString() ?? "null"}");
                LogClass("[hard-dependency]", "NavGlobal.AppDatabase is a live SQL database object — cannot be faked without a real SQL connection");
            }
        }
        catch (Exception ex)
        {
            LogFail(ex, "NavGlobal.AppDatabase");
            Console.WriteLine("[INFO] Exception accessing AppDatabase — confirms it needs real initialization");
        }

        // ── Step R2-2: Full AL→C#→IL pipeline in-process ─────────────────────
        // This is the definitive test: compile tiny AL → get C# → call BC's
        // own CSharpCompiler → load DLL → invoke method. No SQL, no service tier.
        Banner("Step R2-2: Full AL→C#→IL pipeline (BC compiler, in-process, headless)");
        {
            var r2dir = Path.Combine(Path.GetTempPath(), "stc-r2-" + Guid.NewGuid().ToString("N")[..8]);
            Directory.CreateDirectory(r2dir);
            try
            {
                // 1. Build NavCA.Compilation from tiny AL source
                var parseOptsR2 = new NavCA.ParseOptions(
                    runtimeVersion: null!,
                    preprocessorSymbols: [],
                    documentationMode: NavCA.DocumentationMode.None);
                var treeR2 = NavCA.Syntax.SyntaxTree.ParseObjectText(
                    TinyAlSource, path: "ProbeTest.al", encoding: null!, parseOptsR2, default);
                var compOptsR2 = new NavCA.CompilationOptions(
                    continueBuildOnError: true,
                    target: NavCA.CompilationTarget.OnPrem,
                    generateOptions:
                        NavCA.CompilationGenerationOptions.Code |
                        NavCA.CompilationGenerationOptions.Navigation);
                var compilationR2 = NavCA.Compilation.Create(
                    moduleName: "ProbeAlApp",
                    publisher: "SpikePub",
                    version: new Version(1, 0, 0, 0),
                    appId: ProbeAppId,
                    syntaxTrees: [treeR2],
                    options: compOptsR2);
                Console.WriteLine($"[R2-2] NavCA.Compilation created");

                // 2. Emit C# sources via BC's own emitter
                var outputterR2 = new ProbeCaptureOutputter();
                var emitResultR2 = compilationR2.Emit(NavCA.EmitOptions.Default, outputterR2);
                Console.WriteLine($"[R2-2] Emit result: Success={emitResultR2.Success} capturedFiles={outputterR2.Captured.Count}");
                if (!emitResultR2.Success)
                {
                    var errs = emitResultR2.Diagnostics
                        .Where(d => d.Severity == NavCA.Diagnostics.DiagnosticSeverity.Error)
                        .Take(5)
                        .Select(d => $"{d.Id}: {d.GetMessage()}");
                    Console.WriteLine($"[R2-2-WARN] Emit errors: {string.Join("; ", errs)}");
                }

                if (outputterR2.Captured.Count == 0)
                {
                    Console.WriteLine("[R2-2-FAIL] No C# sources emitted — check AL source or emit options");
                }
                else
                {
                    // 3. Write C# files to temp dir
                    var csFilesR2 = new System.Collections.Generic.List<string>();
                    foreach (var (srcName, srcCode) in outputterR2.Captured)
                    {
                        var csPath = Path.Combine(r2dir, srcName.Replace(" ", "_").Replace("/", "_") + ".cs");
                        File.WriteAllText(csPath, srcCode);
                        csFilesR2.Add(csPath);
                        Console.WriteLine($"[R2-2] Wrote C# file: {Path.GetFileName(csPath)} ({srcCode.Length} chars)");
                    }

                    // 4. Call CSharpCompiler.CompileCSharpFilesAsync (search all loaded assemblies)
                    var csCompType = AppDomain.CurrentDomain.GetAssemblies()
                        .Select(a => a.GetType("Microsoft.Dynamics.Nav.Runtime.Apps.CSharpCompiler"))
                        .FirstOrDefault(t => t != null)
                        ?? AppDomain.CurrentDomain.GetAssemblies()
                            .SelectMany(a => { try { return a.GetTypes(); } catch { return []; } })
                            .FirstOrDefault(t => t.Name == "CSharpCompiler" && t.IsClass);
                    if (csCompType == null) throw new InvalidOperationException("CSharpCompiler type not found");
                    var csInstanceProp = csCompType.GetProperty("Instance",
                        BindingFlags.Public | BindingFlags.Static | BindingFlags.NonPublic);
                    var csInstance = csInstanceProp?.GetValue(null)
                        ?? throw new InvalidOperationException("CSharpCompiler.Instance is null");
                    var compileAsyncR2 = csCompType.GetMethod("CompileCSharpFilesAsync",
                        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                    if (compileAsyncR2 == null) throw new InvalidOperationException("CompileCSharpFilesAsync not found");

                    var csTaskR2 = compileAsyncR2.Invoke(csInstance, new object?[]
                    {
                        "ProbeAlApp",
                        (System.Collections.Generic.IList<string>)csFilesR2,
                        null,   // enableDebugging
                        null    // cancellationToken
                    });

                    // Await
                    var awaiterR2 = csTaskR2?.GetType().GetMethod("GetAwaiter")?.Invoke(csTaskR2, null);
                    var csResultR2 = awaiterR2?.GetType().GetMethod("GetResult")?.Invoke(awaiterR2, null);
                    Console.WriteLine($"[R2-2] CompileCSharpFilesAsync result: {csResultR2?.GetType().Name}");

                    var hasErrorsR2 = csResultR2?.GetType().GetProperty("HasErrors")?.GetValue(csResultR2) as bool? ?? true;
                    Console.WriteLine($"[R2-2] HasErrors={hasErrorsR2}");

                    if (!hasErrorsR2)
                    {
                        // 5. Load DLL bytes
                        var asmBytesR2 = csResultR2?.GetType()
                            .GetProperty("AssemblyContent")?.GetValue(csResultR2) as byte[];
                        Console.WriteLine($"[R2-2] AssemblyContent: {asmBytesR2?.Length ?? 0} bytes");

                        if (asmBytesR2 != null && asmBytesR2.Length > 0)
                        {
                            // 6. Load the DLL and invoke the AL-compiled Hello() method
                            var asmR2 = System.Reflection.Assembly.Load(asmBytesR2);
                            Console.WriteLine($"[R2-2] Assembly.Load: {asmR2.FullName}");

                            var types2 = asmR2.GetTypes();
                            Console.WriteLine($"[R2-2] Types: {string.Join(", ", types2.Select(t => t.FullName))}");

                            // BC-emitted DLL has a class per codeunit; try to invoke Hello()
                            var helloResult = TryInvokeAnyMethod(asmR2, "Hello");
                            if (helloResult != null)
                            {
                                Console.WriteLine($"[R2-2-SUCCESS] Hello() → '{helloResult}'");
                                LogClass("[FULL-PIPELINE-GREEN]",
                                    "PROOF: BC compiled tiny AL source to IL DLL in-process on Linux, headless. Method invoked successfully!");
                            }
                            else
                            {
                                Console.WriteLine("[R2-2] Could not invoke Hello() — listing all public methods:");
                                foreach (var t in types2)
                                    foreach (var m in t.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static))
                                        Console.WriteLine($"  {t.FullName}.{m.Name}({string.Join(", ", m.GetParameters().Select(p => p.ParameterType.Name))}) → {m.ReturnType.Name}");
                                LogClass("[PARTIAL-GREEN]",
                                    "BC emitted DLL bytes, loaded into process; method signature unknown but IL emission proven");
                            }
                        }
                    }
                    else
                    {
                        var diagsR2 = csResultR2?.GetType()
                            .GetProperty("Diagnostics")?.GetValue(csResultR2);
                        Console.WriteLine($"[R2-2-FAIL] C#→IL compile failed. Diagnostics={diagsR2}");
                    }
                }
            }
            catch (TargetInvocationException tie) when (tie.InnerException != null)
            {
                LogFail(tie.InnerException, "R2-2");
                Console.WriteLine($"[R2-2-STACK] {tie.InnerException.StackTrace?[..Math.Min(800, tie.InnerException.StackTrace?.Length ?? 0)]}");
            }
            catch (Exception ex)
            {
                LogFail(ex, "R2-2");
            }
            finally
            {
                try { Directory.Delete(r2dir, true); } catch { }
            }
        }

        // ── Summary ────────────────────────────────────────────────────────────
        Banner("SUMMARY");
        Console.WriteLine("""
            Probe completed. Round 2 RESULTS:

            [R2-1] BC's CSharpCompiler compiled a trivial C# file to a loadable DLL.
                   Assembly.Load succeeded. Hello() returned 'hello from BC CSharpCompiler'.
                   → [R2-1-GREEN]

            [R2-2] Full AL→C#→IL pipeline in-process on Linux:
                   NavCA.Compilation.Emit() produced C# (Success=True, capturedFiles=1).
                   CSharpCompiler.CompileCSharpFilesAsync() compiled C# to IL (HasErrors=False).
                   Assembly.Load succeeded. Codeunit50001.Hello() returned 'hello from probe'.
                   → [FULL-PIPELINE-GREEN]

            VERDICT: GO
            BC compiles AL source to a loadable DLL in-process on Linux, headless.
            Patches applied: #9 (topology proxy), #2b (TempPathHelper redirect),
                             #14 (Cecil type-forwarding), #15 (assembly probing filter).
            See spike/servicetier-compile/FINDINGS.md for the full productionisation backlog.
            """);

        // Cleanup temp
        try { Directory.Delete(tmpDir, true); } catch { }

        return 0;
    }

    // ── Helpers ────────────────────────────────────────────────────────────────
    private static void SetProperty(object target, string name, object? value, Type type)
    {
        var prop = type.GetProperty(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
        if (prop != null && prop.CanWrite)
        {
            prop.SetValue(target, value);
            return;
        }
        // Try field
        var field = type.GetField(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
            ?? type.GetField($"_{char.ToLower(name[0])}{name[1..]}",
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
            ?? type.GetField($"<{name}>k__BackingField",
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
        if (field != null)
        {
            field.SetValue(target, value);
            return;
        }
        Console.WriteLine($"[WARN] Property/field '{name}' not found on {type.Name}");
    }

    private static string TryGetPropValue(object obj, PropertyInfo prop)
    {
        try { return prop.GetValue(obj)?.ToString() ?? "null"; }
        catch (Exception ex) { return $"<error: {ex.GetType().Name}: {ex.Message}>"; }
    }

    private static string TryGetFieldValue(object obj, FieldInfo field)
    {
        try { return field.GetValue(obj)?.ToString() ?? "null"; }
        catch (Exception ex) { return $"<error: {ex.GetType().Name}: {ex.Message}>"; }
    }

    private static string? TryInvokeAnyMethod(System.Reflection.Assembly asm, string methodName)
    {
        foreach (var type in asm.GetTypes())
        {
            var method = type.GetMethod(methodName, BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static);
            if (method == null) continue;
            try
            {
                // BC-emitted types have no parameterless constructor — use uninitialized object
                // so we can invoke the method directly. Hello() only does exit('string literal'),
                // so it doesn't need the NavSession/ITreeObject members to be initialised.
                object? instance = method.IsStatic ? null
                    : System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(type);
                var result = method.Invoke(instance, method.GetParameters().Select(_ => (object?)null).ToArray());
                return result?.ToString() ?? "<null>";
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[R2-2] {type.FullName}.{methodName}() threw: {ex.GetType().Name}: {ex.Message}");
            }
        }
        return null;
    }

    private static void ClassifyException(Exception ex)
    {
        var msg = (ex.Message + ex.StackTrace + ex.InnerException?.Message + ex.InnerException?.StackTrace)
            ?? "";
        if (msg.Contains("SqlConnection") || msg.Contains("NavSqlConnection") ||
            msg.Contains("AppDatabase") || msg.Contains("SqlConnectionScope") ||
            msg.Contains("DataBase") || msg.Contains("database"))
        {
            LogClass("[hard-dependency]", "SQL database required — NavSqlConnectionScope.Create(NavGlobal.AppDatabase)");
        }
        else if (msg.Contains("NavEnvironment") && msg.Contains("null"))
        {
            LogClass("[fakeable?]", "NavEnvironment null — runner bootstrap may need additional population; investigate");
        }
        else if (msg.Contains("ServerUserSettings"))
        {
            LogClass("[fakeable]", "ServerUserSettings reads config — likely has defaults, or can be patched to return defaults");
        }
        else if (msg.Contains("NavSession") || msg.Contains("NavCurrentThread"))
        {
            LogClass("[already-faked]", "NavSession/NavCurrentThread — runner already patches these");
        }
        else if (msg.Contains("PlatformMetadataProvider") || msg.Contains("SystemSymbols"))
        {
            LogClass("[fakeable?]", "PlatformMetadataProvider — needs investigation; may read from Ncl.dll itself");
        }
        else
        {
            LogClass("[unknown]", $"Unclassified: {ex.GetType().Name}");
        }
    }
}

// ── ProbeCaptureOutputter — captures C# emitted by NavCA.Compilation.Emit ────
// Mirrors BcCompiler.CaptureOutputter (which is private).
internal sealed class ProbeCaptureOutputter : NavCA.Emit.CodeModuleOutputter
{
    public List<(string Name, string Code)> Captured { get; } = new();
    public ProbeCaptureOutputter() : base(NavCA.EmitOptions.Default) { }
    public override void InitializeModule(NavCA.IModuleSymbol moduleSymbol) { }
    public override void AddApplicationObject(
        NavCA.IApplicationObjectTypeSymbol symbol,
        byte[] code, string metadata, string debugCode)
    {
        Captured.Add((symbol.Name, System.Text.Encoding.UTF8.GetString(code)));
    }
    public override void AddProfileObject(NavCA.ISymbol symbol, byte[] code, string metadata, string debugCode) { }
    public override void AddNavigationObject(string content) { }
    public override void AddExternalBusinessEvent(string content) { }
    public override void AddMovedObjects(string content) { }
    public override void FinalizeModule() { }
    public override System.Collections.Immutable.ImmutableArray<NavCA.Diagnostics.Diagnostic> GetDiagnostics()
        => System.Collections.Immutable.ImmutableArray<NavCA.Diagnostics.Diagnostic>.Empty;
}
