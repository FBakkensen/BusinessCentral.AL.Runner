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
                $"TestPage{id} is not present in the test assembly or any loaded dependency.");
        var ctor = pageType.GetConstructors()
            .FirstOrDefault(c => c.GetParameters().Length == 1 &&
                typeof(Microsoft.Dynamics.Nav.Runtime.ITreeObject)
                    .IsAssignableFrom(c.GetParameters()[0].ParameterType));
        if (ctor == null)
            throw new InvalidOperationException(
                $"TestPage{id} has no single-arg ITreeObject constructor");
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
        var ctor = reportType.GetConstructors()
            .FirstOrDefault(c => c.GetParameters().Length == 1 &&
                typeof(Microsoft.Dynamics.Nav.Runtime.ITreeObject)
                    .IsAssignableFrom(c.GetParameters()[0].ParameterType));
        if (ctor == null)
            throw new InvalidOperationException(
                $"Report{id} has no single-arg ITreeObject constructor");
        return ctor.Invoke(new object[] { self });
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
        // TestPage classes derive from a TestPage base in Microsoft.Dynamics.Nav.Runtime;
        // we find the base by name to avoid hard-coding the type ref.
        var navNcl = AppDomain.CurrentDomain.GetAssemblies()
            .FirstOrDefault(a => a.GetName().Name == "Microsoft.Dynamics.Nav.Ncl");
        Type? testPageBase = null;
        if (navNcl != null)
            testPageBase = navNcl.GetType("Microsoft.Dynamics.Nav.Runtime.NavTestPage")
                ?? navNcl.GetType("Microsoft.Dynamics.Nav.Runtime.TestPage");
        var name = $"TestPage{id}";
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
