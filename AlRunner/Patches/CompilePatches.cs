// SPIKE — bc-linux compile-subset patches lifted for in-process headless AL compile.
// Source: StefanMaron/MsDyn365Bc.On.Linux src/StartupHook/StartupHook.cs
//
// These patches make BC's server-side AL→IL compile pipeline run on Linux without
// a service tier. Each patch is mapped to its bc-linux origin and faithfulness justification.
//
// FAITHFULNESS: none of these patches alter emitted IL — they only remove Windows-only
// infra calls (ACL setup, assembly probing paths, Cecil type-forwarding recursion) that
// are not part of the AL→C#→IL translation. bc-linux's _disabledPatches set documents
// which Cecil patches cause emission drift; none of the three patches below are in that set.
using System.Reflection;
using System.Runtime.CompilerServices;
using AlRunnerV2.Infrastructure;

namespace AlRunnerV2;

public static partial class BcRuntime
{
    private static bool _compilePatched;
    private static object? _originalTopology;

    /// <summary>
    /// Installs the compile-subset patches needed to run BC's server-side AL→IL compiler
    /// in-process on Linux (no service tier, no SQL, no Windows ACL APIs).
    ///
    /// Patches lifted from bc-linux StartupHook.cs:
    ///   Patch #9  — Topology.IsServiceRunningInLocalEnvironment=false (ACL bypass)
    ///   Patch #14 — CecilDotNetTypeLoader.IsTypeForwardingCircular → false
    ///   Patch #15 — NavAppCompilationAssemblyLocator.GetLocationOfAssembliesLoadedInServerAppDomain → filter runtime dirs
    /// </summary>
    public static void EnsureCompilePatches()
    {
        if (_compilePatched) return;
        _compilePatched = true;

        var navNcl = AppDomain.CurrentDomain.GetAssemblies()
            .FirstOrDefault(a => a.GetName().Name == "Microsoft.Dynamics.Nav.Ncl");
        if (navNcl == null)
        {
            Console.Error.WriteLine("[CompilePatches] Ncl not loaded — skipping compile patches");
            return;
        }

        ApplyTopologyProxy(navNcl);
        ApplyTempPathHelperPatch(navNcl);
        ApplyCecilTypeForwardingPatch();
        ApplyAssemblyProbingPatch(navNcl);
    }

    // =========================================================================
    // Patch #9: Topology proxy → IsServiceRunningInLocalEnvironment = false
    // bc-linux source: StartupHook.cs:572 (PatchNavEnvironment → Patch #9 block)
    // Faithfulness: controls Windows file-ACL setup, not AL emit logic.
    // =========================================================================

    private static void ApplyTopologyProxy(Assembly navNcl)
    {
        try
        {
            var envType = navNcl.GetType("Microsoft.Dynamics.Nav.Runtime.NavEnvironment");
            if (envType == null) { Console.Error.WriteLine("[CompilePatches] NavEnvironment not found"); return; }

            // Try IServiceTopology-based DispatchProxy first (mirrors bc-linux exactly)
            var topoIfaceType = navNcl.GetType("Microsoft.Dynamics.Nav.Runtime.IServiceTopology");
            if (topoIfaceType != null)
            {
                var createProxy = typeof(System.Reflection.DispatchProxy)
                    .GetMethod("Create", 2, Type.EmptyTypes)
                    ?.MakeGenericMethod(topoIfaceType, typeof(LinuxTopologyProxyImpl));
                if (createProxy != null)
                {
                    var proxy = createProxy.Invoke(null, null);
                    var topoProp = envType.GetProperty("Topology",
                        BindingFlags.Public | BindingFlags.Static);
                    if (topoProp != null)
                    {
                        _originalTopology = topoProp.GetValue(null);
                        topoProp.SetValue(null, proxy);
                        Console.Error.WriteLine("[CompilePatches] Patch #9: Topology replaced with Linux proxy (IsServiceRunningInLocalEnvironment=false)");
                        return;
                    }
                }
            }

            // Fallback: hook the getter on the concrete topology object
            var topoObj = envType.GetProperty("Topology",
                BindingFlags.Public | BindingFlags.Static)?.GetValue(null);
            if (topoObj != null)
            {
                var getter = topoObj.GetType().GetMethod(
                    "get_IsServiceRunningInLocalEnvironment",
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (getter != null)
                {
                    var repl = typeof(BcRuntime).GetMethod(
                        nameof(ReturnFalse_NoArgs),
                        BindingFlags.Public | BindingFlags.Static);
                    if (repl != null)
                    {
                        JmpHook.Apply(getter, repl, "IsServiceRunningInLocalEnvironment→false");
                        Console.Error.WriteLine("[CompilePatches] Patch #9 fallback: hooked IsServiceRunningInLocalEnvironment→false");
                        return;
                    }
                }
            }

            // Final fallback: directly hook NavDirectorySecurity.CreateSecurityForDomainDirectory
            // to return null, which is exactly what happens when IsServiceRunningInLocalEnvironment=false.
            var navDirSecType = navNcl.GetType("Microsoft.Dynamics.Nav.Runtime.NavDirectorySecurity");
            if (navDirSecType != null)
            {
                var m = navDirSecType.GetMethod(
                    "CreateSecurityForDomainDirectory",
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance);
                if (m != null)
                {
                    var repl = typeof(BcRuntime).GetMethod(
                        nameof(ReturnNull_NoArgs),
                        BindingFlags.Public | BindingFlags.Static);
                    if (repl != null)
                    {
                        JmpHook.Apply(m, repl, "NavDirectorySecurity.CreateSecurityForDomainDirectory→null");
                        Console.Error.WriteLine("[CompilePatches] Patch #9 final-fallback: NavDirectorySecurity.CreateSecurityForDomainDirectory→null");
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[CompilePatches] Patch #9 failed: {ex.GetType().Name}: {ex.Message}");
        }
    }

    // =========================================================================
    // Patch #2b: TempPathHelper redirect → /tmp/bc-alrunner/
    // bc-linux creates /usr/share/Microsoft/... as root. We can't sudo, so we
    // redirect InitializeFolders() to write to /tmp/ instead.
    // Faithfulness: temp directory location doesn't affect emitted IL.
    // =========================================================================

    private static void ApplyTempPathHelperPatch(Assembly navNcl)
    {
        try
        {
            var t = navNcl.GetType("Microsoft.Dynamics.Nav.Runtime.TempPathHelper");
            if (t == null)
            {
                Console.Error.WriteLine("[CompilePatches] TempPathHelper not found — skipping Patch #2b");
                return;
            }

            // Redirect InitializeFolders() to use /tmp/bc-alrunner/ instead of /usr/share/...
            var initFolders = t.GetMethod("InitializeFolders",
                BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            if (initFolders != null)
            {
                var repl = typeof(BcRuntime).GetMethod(
                    nameof(TempPathHelper_InitializeFolders),
                    BindingFlags.Public | BindingFlags.Static);
                if (repl != null)
                {
                    JmpHook.Apply(initFolders, repl, "TempPathHelper.InitializeFolders→/tmp/bc-alrunner/");
                    Console.Error.WriteLine("[CompilePatches] Patch #2b: TempPathHelper.InitializeFolders hooked → /tmp/bc-alrunner/");
                }
                return;
            }

            // Fallback: also no-op Dispose(bool) to prevent finalizer crash
            var dispose = t.GetMethod("Dispose",
                BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public,
                new[] { typeof(bool) });
            if (dispose == null)
            {
                Console.Error.WriteLine("[CompilePatches] TempPathHelper.Dispose(bool)+InitializeFolders not found — skipping Patch #2b");
                return;
            }

            var replDispose = typeof(BcRuntime).GetMethod(
                nameof(TempPathHelper_Dispose_NoOp),
                BindingFlags.Public | BindingFlags.Static);
            if (replDispose == null) return;

            JmpHook.Apply(dispose, replDispose, "TempPathHelper.Dispose(bool)→no-op (finalizer crash fix)");
            Console.Error.WriteLine("[CompilePatches] Patch #2b fallback: TempPathHelper.Dispose hooked → no-op");
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[CompilePatches] Patch #2b failed: {ex.GetType().Name}: {ex.Message}");
        }
    }

    /// <summary>
    /// Replacement for TempPathHelper.InitializeFolders() — redirects to /tmp/bc-alrunner/
    /// so the BC compile pipeline works on systems where /usr/share/Microsoft/ is not writable.
    /// Sets instanceBasePath and configurableBasePath fields via reflection.
    /// </summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static void TempPathHelper_InitializeFolders(object @this)
    {
        var t = @this.GetType();
        var baseDir = Path.Combine(Path.GetTempPath(), "bc-alrunner", Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(baseDir);

        t.GetField("instanceBasePath", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)?.SetValue(@this, baseDir);
        t.GetField("configurableBasePath", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)?.SetValue(@this, baseDir);
        // driveName is used on Windows only for drive letter; set empty on Linux
        t.GetField("driveName", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)?.SetValue(@this, string.Empty);

        Console.Error.WriteLine($"[CompilePatches] TempPathHelper.InitializeFolders → redirected to {baseDir}");
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static void TempPathHelper_Dispose_NoOp(object @this, bool disposing) { /* temp cleanup not needed for compile-only probe */ }



    // bc-linux source: StartupHook.cs:610 (PatchCecilTypeForwarding)
    // Faithfulness: prevents NullReferenceException when Cecil follows netstandard
    // type-forwarding chains. No effect on emitted IL.
    // =========================================================================

    private static void ApplyCecilTypeForwardingPatch()
    {
        try
        {
            var codeAnalysisAsm = AppDomain.CurrentDomain.GetAssemblies()
                .FirstOrDefault(a => a.GetName().Name == "Microsoft.Dynamics.Nav.CodeAnalysis");
            if (codeAnalysisAsm == null)
            {
                Console.Error.WriteLine("[CompilePatches] CodeAnalysis assembly not loaded — skipping Patch #14");
                return;
            }

            var loaderType = codeAnalysisAsm.GetType(
                "Microsoft.Dynamics.Nav.CodeAnalysis.DotNet.Cecil.CecilDotNetTypeLoader");
            if (loaderType == null)
            {
                Console.Error.WriteLine("[CompilePatches] CecilDotNetTypeLoader not found — skipping Patch #14");
                return;
            }

            var isCircular = loaderType.GetMethod("IsTypeForwardingCircular",
                BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            if (isCircular == null)
            {
                Console.Error.WriteLine("[CompilePatches] IsTypeForwardingCircular not found — skipping Patch #14");
                return;
            }

            var repl = typeof(BcRuntime).GetMethod(
                nameof(ReturnFalse_NoArgs),
                BindingFlags.Public | BindingFlags.Static);
            if (repl == null) return;

            JmpHook.Apply(isCircular, repl, "CecilDotNetTypeLoader.IsTypeForwardingCircular→false");
            Console.Error.WriteLine("[CompilePatches] Patch #14: CecilDotNetTypeLoader.IsTypeForwardingCircular hooked → false");
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[CompilePatches] Patch #14 failed: {ex.GetType().Name}: {ex.Message}");
        }
    }

    // =========================================================================
    // Patch #15: Filter runtime DLLs from NavAppCompilationAssemblyLocator
    // bc-linux source: StartupHook.cs:679 (PatchAssemblyProbing)
    // Faithfulness: removes R2R/forwarding runtime DLLs from Cecil's probing set so
    // type definitions are found in Add-Ins reference assemblies. No effect on emit.
    // =========================================================================

    private static void ApplyAssemblyProbingPatch(Assembly navNcl)
    {
        try
        {
            var locatorType = navNcl.GetType(
                "Microsoft.Dynamics.Nav.Runtime.Apps.NavAppCompilationAssemblyLocator");
            if (locatorType == null)
            {
                Console.Error.WriteLine("[CompilePatches] NavAppCompilationAssemblyLocator not found — skipping Patch #15");
                return;
            }

            var getLocations = locatorType.GetMethod(
                "GetLocationOfAssembliesLoadedInServerAppDomain",
                BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
            if (getLocations == null)
            {
                Console.Error.WriteLine("[CompilePatches] GetLocationOfAssembliesLoadedInServerAppDomain not found — skipping Patch #15");
                return;
            }

            var repl = typeof(BcRuntime).GetMethod(
                nameof(FilteredAssemblyLocations),
                BindingFlags.Public | BindingFlags.Static);
            if (repl == null) return;

            JmpHook.Apply(getLocations,
                repl,
                "NavAppCompilationAssemblyLocator.GetLocationOfAssembliesLoadedInServerAppDomain→filtered");
            Console.Error.WriteLine("[CompilePatches] Patch #15: assembly probing filter hooked");
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[CompilePatches] Patch #15 failed: {ex.GetType().Name}: {ex.Message}");
        }
    }

    // =========================================================================
    // Replacement methods
    // =========================================================================

    /// <summary>
    /// bc-linux Patch #9 replacement: returns false for IsServiceRunningInLocalEnvironment
    /// (and as a fallback for any bool-returning method that needs to return false).
    /// </summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static bool ReturnFalse_NoArgs() => false;

    /// <summary>
    /// bc-linux Patch #9 final-fallback: return null for CreateSecurityForDomainDirectory.
    /// Equivalent to the IsServiceRunningInLocalEnvironment=false case in NavDirectorySecurity.
    /// </summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static object? ReturnNull_NoArgs() => null;

    /// <summary>
    /// bc-linux Patch #15: Returns loaded assemblies with .NET runtime DLLs filtered out.
    /// Prevents Cecil from finding R2R DLLs (unreadable by Cecil) or type-forwarder-only DLLs
    /// that forward into System.Private.CoreLib (also R2R), forcing Cecil to use Add-Ins
    /// reference assemblies that have actual type definitions.
    /// Source: StartupHook.cs:745 (FilteredAssemblyLocations)
    /// </summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static Dictionary<string, string> FilteredAssemblyLocations()
    {
        var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        int skipped = 0;
        foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
        {
            try
            {
                if (asm.IsDynamic) continue;
                var loc = asm.Location;
                if (string.IsNullOrEmpty(loc)) continue;
                // Skip .NET runtime assemblies — R2R or type-forwarder-only
                if (loc.Contains("/dotnet/shared/Microsoft.NETCore.App/") ||
                    loc.Contains("/dotnet/shared/Microsoft.AspNetCore.App/") ||
                    loc.Contains("\\dotnet\\shared\\Microsoft.NETCore.App\\") ||
                    loc.Contains("\\dotnet\\shared\\Microsoft.AspNetCore.App\\"))
                {
                    skipped++;
                    continue;
                }
                var name = asm.GetName().Name;
                if (name != null && !dict.ContainsKey(name))
                    dict[name] = loc;
            }
            catch { }
        }
        Console.Error.WriteLine($"[CompilePatches] Patch #15: FilteredAssemblyLocations: {dict.Count} kept, {skipped} runtime excluded");
        return dict;
    }
}

/// <summary>
/// bc-linux Patch #9: DispatchProxy that returns false for IsServiceRunningInLocalEnvironment
/// and AllowToRegisterServicePrincipalName, delegates all other calls to the original topology.
/// Source: StartupHook.cs:1675 (LinuxTopologyProxy class)
/// </summary>
public class LinuxTopologyProxyImpl : System.Reflection.DispatchProxy
{
    protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
    {
        if (targetMethod?.Name == "get_IsServiceRunningInLocalEnvironment") return false;
        if (targetMethod?.Name == "get_AllowToRegisterServicePrincipalName") return false;

        // Delegate to original topology stored in BcRuntime
        var original = (object?)typeof(BcRuntime)
            .GetField("_originalTopology", BindingFlags.NonPublic | BindingFlags.Static)
            ?.GetValue(null);
        if (original != null && targetMethod != null)
        {
            try { return targetMethod.Invoke(original, args); }
            catch (TargetInvocationException ex) { throw ex.InnerException ?? ex; }
        }
        // Safe defaults for any method not delegated
        if (targetMethod?.ReturnType == typeof(bool)) return false;
        if (targetMethod?.ReturnType == typeof(string)) return string.Empty;
        if (targetMethod?.ReturnType == typeof(int)) return 0;
        return null;
    }
}
