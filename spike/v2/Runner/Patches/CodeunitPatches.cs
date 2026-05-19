// CodeunitPatches — replacements for NavCodeunit.DoRunAsync and NavCodeunitHandle.CreateTarget.
//
// DoRunAsync normally goes through DiagnosticsResolver and the metadata layer; we bypass
// both and call the AL-emitted OnRun directly via reflection.
//
// CreateTarget normally looks up the codeunit class via NavGlobal.NCLMetadata; we replace
// it with an assembly-scan for `Codeunit{ID}` in the loaded test assembly.
using System.Collections.Concurrent;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace AlRunnerV2;

public static partial class BcRuntime
{
    // Cache: codeunit ID → generated codeunit Type (cleared per-test-assembly via SetTestAssembly).
    private static readonly ConcurrentDictionary<int, Type?> _codeunitTypeCache = new();

    // Fallback cache: report ID → skeleton NCLMetaReport built via CreateEmptyNCLMetaReport
    // when NavGlobal.NCLMetadata.GetMetaReportById returns null or throws.
    private static readonly ConcurrentDictionary<int, object> _metaReportFallbackCache = new();

    /// <summary>
    /// Replacement for NavCodeunit.DoRunAsync(DataError, NavRecord).
    /// Bypasses DiagnosticsResolver.GetMostSpecificInstance(Session) which NREs on the skeleton
    /// by calling the concrete subclass's OnRun(INavRecordHandle) directly via reflection.
    /// </summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static System.Threading.Tasks.ValueTask<bool> NavCodeunit_DoRunAsync(
        Microsoft.Dynamics.Nav.Runtime.NavCodeunit self,
        Microsoft.Dynamics.Nav.Types.DataError errorLevel,
        Microsoft.Dynamics.Nav.Runtime.NavRecord? record)
    {
        try
        {
            var onRun = self.GetType().GetMethod("OnRun",
                BindingFlags.NonPublic | BindingFlags.Instance, null,
                new[] { typeof(Microsoft.Dynamics.Nav.Runtime.INavRecordHandle) }, null);
            if (onRun != null)
                onRun.Invoke(self, new object?[] { record });
            else
            {
                var onRun0 = self.GetType().GetMethod("OnRun",
                    BindingFlags.NonPublic | BindingFlags.Instance, null, Type.EmptyTypes, null);
                onRun0?.Invoke(self, null);
            }
            return new System.Threading.Tasks.ValueTask<bool>(true);
        }
        catch (TargetInvocationException tie) when (tie.InnerException != null)
        {
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw(tie.InnerException);
            return default; // unreachable
        }
    }

    /// <summary>
    /// Replacement for NavCodeunitHandle.CreateTarget().
    /// Bypasses NavGlobal.NCLMetadata by looking up the compiled codeunit class directly
    /// from the loaded assembly and constructing it via the 1-arg ITreeObject ctor.
    /// </summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static Microsoft.Dynamics.Nav.Runtime.NavCodeunit NavCodeunitHandle_CreateTarget(
        Microsoft.Dynamics.Nav.Runtime.NavCodeunitHandle self)
    {
        int id = self.ObjectId.ObjectNumber;
        var codeunitType = _codeunitTypeCache.GetOrAdd(id, FindCodeunitType);
        if (codeunitType == null)
            throw new InvalidOperationException(BuildMissingCodeunitMessage(id));
        var ctor = codeunitType.GetConstructors()
            .FirstOrDefault(c => c.GetParameters().Length == 1 &&
                typeof(Microsoft.Dynamics.Nav.Runtime.ITreeObject)
                    .IsAssignableFrom(c.GetParameters()[0].ParameterType));
        if (ctor == null)
            throw new InvalidOperationException(
                $"Codeunit{id} has no single-arg ITreeObject constructor");
        return (Microsoft.Dynamics.Nav.Runtime.NavCodeunit)ctor.Invoke(new object[] { self });
    }

    // Cache: test-page ID → generated TestPage Type.
    private static readonly ConcurrentDictionary<int, Type?> _testPageTypeCache = new();

    /// <summary>
    /// Replacement for NavTestPageHandle.CreateTarget().
    /// Same shape as NavCodeunitHandle_CreateTarget — bypass NavGlobal.NCLMetadata
    /// by scanning the loaded test assembly for `TestPage{ID}` and constructing
    /// via the 1-arg ITreeObject ctor.
    /// </summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static object NavTestPageHandle_CreateTarget(object self)
    {
        // self is NavTestPageHandle. ObjectId.ObjectNumber via reflection so we don't
        // need a using import for the type (it lives in the same Runtime namespace).
        var objIdProp = self.GetType().GetProperty("ObjectId",
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
        var objId = objIdProp!.GetValue(self)!;
        var idProp = objId.GetType().GetProperty("ObjectNumber",
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
        int id = (int)idProp!.GetValue(objId)!;

        var pageType = _testPageTypeCache.GetOrAdd(id, FindTestPageType);
        if (pageType == null)
            throw new InvalidOperationException(
                $"TestPage handle target Page{id} is not present in the test assembly or any loaded dependency.");
        var ctor = pageType.GetConstructors()
            .FirstOrDefault(c => c.GetParameters().Length == 1 &&
                typeof(Microsoft.Dynamics.Nav.Runtime.ITreeObject)
                    .IsAssignableFrom(c.GetParameters()[0].ParameterType));
        if (ctor == null)
            throw new InvalidOperationException(
                $"Page{id} has no single-arg ITreeObject constructor (TestPage handle path)");
        return ctor.Invoke(new object[] { self });
    }

    // Cache: form ID → generated Form Type.
    private static readonly ConcurrentDictionary<int, Type?> _formTypeCache = new();

    // Cache: report ID → generated Report Type.
    private static readonly ConcurrentDictionary<int, Type?> _reportTypeCache = new();

    /// <summary>
    /// Replacement for NavFormHandle.CreateTarget().
    /// Same shape as NavCodeunitHandle/NavTestPageHandle — bypass NavGlobal.NCLMetadata
    /// by scanning the loaded test assembly for `Form{ID}` and constructing via the
    /// 1-arg ITreeObject ctor.
    /// </summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static object NavFormHandle_CreateTarget(object self)
    {
        var objIdProp = self.GetType().GetProperty("ObjectId",
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
        var objId = objIdProp!.GetValue(self)!;
        var idProp = objId.GetType().GetProperty("ObjectNumber",
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
        int id = (int)idProp!.GetValue(objId)!;

        var formType = _formTypeCache.GetOrAdd(id, FindFormType);
        if (formType == null)
            throw new InvalidOperationException(
                $"Page{id} is not present in the test assembly or any loaded dependency.");
        var ctor = formType.GetConstructors()
            .FirstOrDefault(c => c.GetParameters().Length == 1 &&
                typeof(Microsoft.Dynamics.Nav.Runtime.ITreeObject)
                    .IsAssignableFrom(c.GetParameters()[0].ParameterType));
        if (ctor == null)
            throw new InvalidOperationException(
                $"Page{id} has no single-arg ITreeObject constructor");
        return ctor.Invoke(new object[] { self });
    }

    // One-time hook: NavReport..ctor(ITreeObject, Int32, NCLStaticMetadata) → same replacement
    // as NavApplicationObjectBase..ctor. Registered lazily on first report creation because
    // NavReport is in Ncl.dll which is loaded during ApplyAllPatches, but the ctor may not
    // yet be JIT-compiled when we get here — JmpHook.Apply patches the native code regardless.
    private static int _navReportCtorHookApplied;

    private static void EnsureNavReportCtorHooked()
    {
        if (System.Threading.Interlocked.Exchange(ref _navReportCtorHookApplied, 1) != 0) return;
        try
        {
            var navNcl = AppDomain.CurrentDomain.GetAssemblies()
                .FirstOrDefault(a => a.GetName().Name == "Microsoft.Dynamics.Nav.Ncl");
            var navReportType = navNcl?.GetType("Microsoft.Dynamics.Nav.Runtime.NavReport");
            if (navReportType == null)
            {
                Console.Error.WriteLine("[BcRuntime] EnsureNavReportCtorHooked: NavReport type not found");
                return;
            }
            // Find NavReport..ctor(ITreeObject parent, Int32 objectId, NCLStaticMetadata staticMetadata).
            var navReportCtor = navReportType.GetConstructors(
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                .FirstOrDefault(c =>
                {
                    var ps = c.GetParameters();
                    return ps.Length == 3
                        && ps[0].ParameterType.Name == "ITreeObject"
                        && ps[1].ParameterType == typeof(int)
                        && ps[2].ParameterType.Name == "NCLStaticMetadata";
                });
            if (navReportCtor == null)
            {
                Console.Error.WriteLine("[BcRuntime] EnsureNavReportCtorHooked: NavReport..ctor(ITreeObject, Int32, NCLStaticMetadata) not found");
                return;
            }
            var replCtor = typeof(BcRuntime).GetMethod(nameof(NavApplicationObjectBaseCtorReplacement),
                BindingFlags.Public | BindingFlags.Static)
                ?? throw new InvalidOperationException("NavApplicationObjectBaseCtorReplacement not found");
            AlRunnerV2.Infrastructure.JmpHook.Apply(navReportCtor, replCtor, "NavReport..ctor(ITreeObject, Int32, NCLStaticMetadata)");
            Console.Error.WriteLine("[BcRuntime] NavReport..ctor(ITreeObject, Int32, NCLStaticMetadata) hooked → NavApplicationObjectBaseCtorReplacement");

            // Also hook NavReport.BeginInitialization / EndInitialization — same NRE cluster as
            // NavXmlPort.BeginInitialization (dereferences Session.MetadataProvider on skeleton).
            var replNoOp = typeof(BcRuntime).GetMethod(nameof(NavReport_BeginEndInitialization),
                BindingFlags.Public | BindingFlags.Static)
                ?? throw new InvalidOperationException("NavReport_BeginEndInitialization not found");
            var beginInit = navReportType.GetMethod("BeginInitialization",
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance, null, Type.EmptyTypes, null);
            if (beginInit != null)
            {
                AlRunnerV2.Infrastructure.JmpHook.Apply(beginInit, replNoOp, "NavReport.BeginInitialization()");
                Console.Error.WriteLine("[BcRuntime] NavReport.BeginInitialization() hooked → NoOp");
            }
            var endInit = navReportType.GetMethod("EndInitialization",
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance, null, Type.EmptyTypes, null);
            if (endInit != null)
            {
                AlRunnerV2.Infrastructure.JmpHook.Apply(endInit, replNoOp, "NavReport.EndInitialization()");
                Console.Error.WriteLine("[BcRuntime] NavReport.EndInitialization() hooked → NoOp");
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[BcRuntime] EnsureNavReportCtorHooked failed: {ex.Message}");
        }
    }

    // Cache: report types whose InitializeComponent has been no-op'd.
    private static readonly ConcurrentDictionary<Type, byte> _hookedReportInitComponents = new();

    private static void HookReportInitializeComponent(Type reportType)
    {
        if (!_hookedReportInitComponents.TryAdd(reportType, 0)) return;
        try
        {
            var m = reportType.GetMethod("InitializeComponent",
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance, null, Type.EmptyTypes, null);
            if (m == null || m.DeclaringType != reportType) return; // no override in this type
            var repl = typeof(BcRuntime).GetMethod(nameof(NavReport_BeginEndInitialization),
                BindingFlags.Public | BindingFlags.Static)!;
            AlRunnerV2.Infrastructure.JmpHook.Apply(m, repl, $"{reportType.Name}.InitializeComponent()");
            Console.Error.WriteLine($"[BcRuntime] {reportType.Name}.InitializeComponent() hooked → NoOp");
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[BcRuntime] HookReportInitializeComponent({reportType.Name}) failed: {ex.Message}");
        }
    }

    /// <summary>No-op replacement for NavReport.BeginInitialization, EndInitialization,
    /// and Report{N}.InitializeComponent — these dereference Session.MetadataProvider /
    /// NCLMetaReport fields that are null on the skeleton, causing NREs identical to the
    /// NavXmlPort.BeginInitialization cluster already patched in XmlPortPatches.cs.</summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static void NavReport_BeginEndInitialization(object self)
    {
    }

    /// <summary>
    /// Replacement for NavReportHandle.CreateTarget().
    /// Same shape as NavFormHandle: bypass NavGlobal.NCLMetadata.GetMetaReportById +
    /// NCLMetaReport.CreateObjectInstance (which NREs on a skeleton meta because
    /// ApplicationObjectConstructor returns null), and construct Report{ID} directly
    /// from the loaded test assembly via a 1-arg ITreeObject ctor.
    /// </summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static object NavReportHandle_CreateTarget(object self)
    {
        EnsureNavReportCtorHooked();
        var objIdProp = self.GetType().GetProperty("ObjectId",
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
        var objId = objIdProp!.GetValue(self)!;
        var idProp = objId.GetType().GetProperty("ObjectNumber",
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
        int id = (int)idProp!.GetValue(objId)!;

        var reportType = _reportTypeCache.GetOrAdd(id, FindReportType);
        if (reportType == null)
            throw new InvalidOperationException(
                $"Report{id} is not present in the test assembly or any loaded dependency.");
        // Hook this report type's InitializeComponent override to no-op before invoking the ctor.
        // Report{N}.InitializeComponent calls NavReport.BeginInitialization which dereferences
        // Session.MetadataProvider (null on skeleton). Same NRE cluster as XmlPort — hook the
        // concrete override before JIT compilation to guarantee the patch lands.
        HookReportInitializeComponent(reportType);
        // BC emits report ctors as either:
        //   (ITreeObject parent)
        //   (ITreeObject parent, NCLMetaReport metadata)
        // depending on AL features used. Try 1-arg first, then 2-arg with metadata
        // pulled from our populated SystemTenant cache (skeleton OK — NavReport.ctor
        // tolerates the cache-built skeleton entry).
        var ctors = reportType.GetConstructors();
        var oneArg = ctors.FirstOrDefault(c => c.GetParameters().Length == 1 &&
            typeof(Microsoft.Dynamics.Nav.Runtime.ITreeObject)
                .IsAssignableFrom(c.GetParameters()[0].ParameterType));
        if (oneArg != null) return oneArg.Invoke(new object[] { self });
        var twoArg = ctors.FirstOrDefault(c => c.GetParameters().Length == 2 &&
            typeof(Microsoft.Dynamics.Nav.Runtime.ITreeObject)
                .IsAssignableFrom(c.GetParameters()[0].ParameterType));
        if (twoArg != null)
        {
            var metaParamType = twoArg.GetParameters()[1].ParameterType;
            var metaArg = LookupNclMetaForReport(id, metaParamType);
            return twoArg.Invoke(new object?[] { self, metaArg });
        }
        throw new InvalidOperationException(
            $"Report{id} has no (ITreeObject) or (ITreeObject, NCLMetaReport) constructor");
    }

    private static object? LookupNclMetaForReport(int id, Type expectedMetaType)
    {
        // Primary: NavGlobal.NCLMetadata.GetMetaReportById (the real global metadata).
        // This succeeds only when BC has registered the report in its own metadata store,
        // which does not happen in runner mode — so we expect null or an exception here.
        var navNcl = AppDomain.CurrentDomain.GetAssemblies()
            .FirstOrDefault(a => a.GetName().Name == "Microsoft.Dynamics.Nav.Ncl");
        var navGlobal = navNcl?.GetType("Microsoft.Dynamics.Nav.Runtime.NavGlobal");
        var nclMeta = navGlobal?.GetProperty("NCLMetadata", BindingFlags.Public | BindingFlags.Static)?.GetValue(null);
        if (nclMeta != null)
        {
            var getMeta = nclMeta.GetType().GetMethod("GetMetaReportById",
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance, null,
                new[] { typeof(int) }, null);
            try
            {
                var primary = getMeta?.Invoke(nclMeta, new object[] { id });
                if (primary != null) return primary;
            }
            catch { /* NavNCLApplicationObjectNotFoundException expected — fall through */ }
        }

        // Fallback: build a skeleton NCLMetaReport via CreateEmptyNCLMetaReport (internal static
        // factory). This succeeds for any numeric report ID and gives the Report{N}..ctor a
        // non-null metadata argument, preventing the NRE observed in the Report*..ctor cluster.
        return _metaReportFallbackCache.GetOrAdd(id, static reportId =>
        {
            try
            {
                var nclAsm = AppDomain.CurrentDomain.GetAssemblies()
                    .FirstOrDefault(a => a.GetName().Name == "Microsoft.Dynamics.Nav.Ncl");
                var tMeta = nclAsm?.GetType("Microsoft.Dynamics.Nav.Runtime.NCLMetaReport");
                var factory = tMeta?.GetMethod("CreateEmptyNCLMetaReport",
                    BindingFlags.NonPublic | BindingFlags.Static);
                if (factory == null)
                    throw new InvalidOperationException("CreateEmptyNCLMetaReport not found on NCLMetaReport");

                var tAppGroup = nclAsm!.GetType("Microsoft.Dynamics.Nav.Runtime.Apps.NavAppGroup");
                var baseGroup = tAppGroup?.GetProperty("BaseGroup",
                        BindingFlags.Public | BindingFlags.Static)?.GetValue(null)
                    ?? tAppGroup?.GetField("BaseGroup",
                        BindingFlags.Public | BindingFlags.Static)?.GetValue(null);

                var meta = factory.Invoke(null, new object?[] { null, reportId, baseGroup, -1, string.Empty });
                if (meta == null)
                    throw new InvalidOperationException($"CreateEmptyNCLMetaReport returned null for Report{reportId}");

                // Mark metadataLoaded=true so the JMP-hooked Populate path is never entered.
                var tBase = nclAsm.GetType("Microsoft.Dynamics.Nav.Runtime.NCLMetaApplicationObject");
                var fLoaded = tBase?.GetField("metadataLoaded",
                    BindingFlags.NonPublic | BindingFlags.Instance);
                if (fLoaded != null)
                    AlRunnerV2.Infrastructure.FieldPoke.SetInstance(fLoaded, meta, true);

                Console.Error.WriteLine($"[BcRuntime] LookupNclMetaForReport({reportId}): built skeleton NCLMetaReport via fallback");
                return meta;
            }
            catch (Exception ex)
            {
                var inner = ex is TargetInvocationException tie ? tie.InnerException ?? ex : ex;
                throw new InvalidOperationException(
                    $"LookupNclMetaForReport fallback failed for Report{reportId}: {inner.GetType().Name}: {inner.Message}", inner);
            }
        });
    }

    private static Type? FindReportType(int id)
    {
        var navNcl = AppDomain.CurrentDomain.GetAssemblies()
            .FirstOrDefault(a => a.GetName().Name == "Microsoft.Dynamics.Nav.Ncl");
        Type? reportBase = navNcl?.GetType("Microsoft.Dynamics.Nav.Runtime.NavReport");
        var name = $"Report{id}";
        if (_currentTestAssembly != null)
        {
            try
            {
                var t = Array.Find(_currentTestAssembly.GetTypes(),
                    x => x.Name == name && (reportBase == null || reportBase.IsAssignableFrom(x)));
                if (t != null) return t;
            }
            catch { }
        }
        foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
        {
            if (asm == _currentTestAssembly) continue;
            try
            {
                var t = Array.Find(asm.GetTypes(),
                    x => x.Name == name && (reportBase == null || reportBase.IsAssignableFrom(x)));
                if (t != null) return t;
            }
            catch { }
        }
        return null;
    }

    private static Type? FindFormType(int id)
    {
        // BC's Compilation.Emit produces `class Page{id} : NavForm` for AL `page` objects
        // (the NavForm base class is what the runtime calls "form", but the C# class name
        // is `Page{id}`). The "Form{id}" name was a false guess in the §P session.
        var navNcl = AppDomain.CurrentDomain.GetAssemblies()
            .FirstOrDefault(a => a.GetName().Name == "Microsoft.Dynamics.Nav.Ncl");
        Type? formBase = navNcl?.GetType("Microsoft.Dynamics.Nav.Runtime.NavForm");
        var name = $"Page{id}";
        if (_currentTestAssembly != null)
        {
            try
            {
                var t = Array.Find(_currentTestAssembly.GetTypes(),
                    x => x.Name == name && (formBase == null || formBase.IsAssignableFrom(x)));
                if (t != null) return t;
            }
            catch { }
        }
        foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
        {
            if (asm == _currentTestAssembly) continue;
            try
            {
                var t = Array.Find(asm.GetTypes(),
                    x => x.Name == name && (formBase == null || formBase.IsAssignableFrom(x)));
                if (t != null) return t;
            }
            catch { }
        }
        return null;
    }

    private static Type? FindTestPageType(int id)
    {
        // BC's Compilation.Emit produces `class Page{id} : NavForm` even for AL pages
        // that test code references via `TestPage "Name"` — there is NO separate
        // TestPage{id} class. NavTestPageHandle wraps the same Page{id} behaviorally.
        // Match by NavForm base (NavTestPage doesn't exist as a real type).
        var navNcl = AppDomain.CurrentDomain.GetAssemblies()
            .FirstOrDefault(a => a.GetName().Name == "Microsoft.Dynamics.Nav.Ncl");
        Type? testPageBase = navNcl?.GetType("Microsoft.Dynamics.Nav.Runtime.NavForm");
        var name = $"Page{id}";
        if (_currentTestAssembly != null)
        {
            try
            {
                var t = Array.Find(_currentTestAssembly.GetTypes(),
                    x => x.Name == name && (testPageBase == null || testPageBase.IsAssignableFrom(x)));
                if (t != null) return t;
            }
            catch { }
        }
        foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
        {
            if (asm == _currentTestAssembly) continue;
            try
            {
                var t = Array.Find(asm.GetTypes(),
                    x => x.Name == name && (testPageBase == null || testPageBase.IsAssignableFrom(x)));
                if (t != null) return t;
            }
            catch { }
        }
        return null;
    }

    // Well-known codeunit IDs that ship inside Microsoft dependency apps. When the
    // test compile resolves a `Codeunit X` reference against a Microsoft .app at
    // symbol-resolution time but the runtime has no DLL for that .app loaded,
    // CreateTarget can't find the type. We surface the dependency name instead of
    // a bare numeric id so the user knows which dependency is missing.
    private static readonly Dictionary<int, (string Name, string Package)> _knownDependencyCodeunits = new()
    {
        [130000] = ("Assert",                  "Library Assert (test framework)"),
        [130002] = ("Library Assert",          "Library Assert (test framework)"),
        [130440] = ("Library Variable Storage","Library Variable Storage (test framework)"),
        [130500] = ("Test Runner",             "Test Runner (test framework)"),
        [131000] = ("Library - Test Initialize","Library - Test Initialize (test framework)"),
        [310]    = ("No. Series",              "Base Application"),
    };

    private static string BuildMissingCodeunitMessage(int id)
    {
        if (_knownDependencyCodeunits.TryGetValue(id, out var known))
        {
            return
                $"Codeunit {id} (\"{known.Name}\") is not present in the test " +
                $"assembly or any loaded dependency. It belongs to {known.Package}, " +
                $"a Microsoft dependency app. AL Runner v2 does not yet load " +
                $"Microsoft R2R packages at runtime — either provide an AL " +
                $"implementation/stub for this codeunit at the bucket level, or " +
                $"wait until runtime dependency loading lands.";
        }
        return
            $"Codeunit {id} is not present in the test assembly or any loaded " +
            $"dependency. It is most likely defined in a dependency .app " +
            $"(e.g. System Application, Base Application, a test-framework " +
            $"library, or a third-party app) whose runtime DLL is not loaded. " +
            $"AL Runner v2 does not yet load dependency-app DLLs at runtime — " +
            $"either provide an AL implementation/stub at the bucket level, " +
            $"or wait until runtime dependency loading lands.";
    }

    /// <summary>
    /// Public accessor used by other patches (e.g. SessionPatches.AlRunnerStartSession)
    /// that need to resolve a codeunit type by AL object id without duplicating the
    /// test-assembly-first / loaded-assembly-fallback scan logic.
    /// </summary>
    public static Type? FindCodeunitTypePublic(int id)
        => _codeunitTypeCache.GetOrAdd(id, FindCodeunitType);

    private static Type? FindCodeunitType(int id)
    {
        var baseCu = typeof(Microsoft.Dynamics.Nav.Runtime.NavCodeunit);
        var name = $"Codeunit{id}";
        // Search the current test assembly first (avoids cross-bucket ID collisions).
        if (_currentTestAssembly != null)
        {
            try
            {
                var t = Array.Find(_currentTestAssembly.GetTypes(),
                    x => x.Name == name && baseCu.IsAssignableFrom(x));
                if (t != null) return t;
            }
            catch { }
        }
        // Fall back to all loaded assemblies (e.g. stubs in other assemblies).
        foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
        {
            if (asm == _currentTestAssembly) continue;
            try
            {
                var t = Array.Find(asm.GetTypes(),
                    x => x.Name == name && baseCu.IsAssignableFrom(x));
                if (t != null) return t;
            }
            catch { /* skip dynamic/reflection-only assemblies */ }
        }
        return null;
    }
}
