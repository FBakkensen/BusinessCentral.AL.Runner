// XmlPortPatches — replacements for NavXmlPortHandle.CreateTarget and NavXmlPort
// instance methods (Export, Import, Run, SetTableView).
//
// NavXmlPortHandle.CreateTarget normally calls
//   NavGlobal.NCLMetadata.GetMetaXmlPortById(id, true).CreateObjectInstance(this)
// which NREs because our skeleton NCLMetaXmlPort has no ApplicationObjectConstructor
// delegate. We bypass it by finding XmlPort{ID} in the loaded test assembly and
// constructing directly via reflection — same pattern as NavFormHandle/NavReportHandle.
//
// The NavXmlPort instance methods (Export, Import, Run, SetTableView) all internally
// call Session.BeginTransaction / ApplicationObjectRootScope which NRE on our skeleton.
// We replace them with stubs that return the "success" value without side effects.
using System.Collections.Concurrent;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace AlRunnerV2;

public static partial class BcRuntime
{
    private static readonly ConcurrentDictionary<int, Type?> _xmlPortTypeCache = new();

    // Lazily resolved reflection handles for NavXmlPortNode base-class private list fields.
    private static System.Reflection.FieldInfo? _fXmlPortNodeAttrChildren;
    private static System.Reflection.FieldInfo? _fXmlPortNodeElemChildren;

    // ──────────────────────────────────────────────────────────────────
    // NavXmlPortHandle.CreateTarget — bypass GetMetaXmlPortById +
    // CreateObjectInstance (which NREs on null delegate). Construct
    // XmlPort{ID} directly from the test assembly.
    // ──────────────────────────────────────────────────────────────────

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static object NavXmlPortHandle_CreateTarget(object self)
    {
        var objIdProp = self.GetType().GetProperty("ObjectId",
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
        var objId = objIdProp!.GetValue(self)!;
        var idProp = objId.GetType().GetProperty("ObjectNumber",
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
        int id = (int)idProp!.GetValue(objId)!;

        var xmlPortType = _xmlPortTypeCache.GetOrAdd(id, FindXmlPortType);
        if (xmlPortType == null)
            throw new InvalidOperationException(
                $"XmlPort{id} is not present in the test assembly or any loaded dependency.");

        // BC emits XmlPort ctors as either:
        //   (ITreeObject parent)                          — legacy
        //   (ITreeObject parent, NCLMetaXmlPort meta)    — modern (BC 27+)
        // Try 1-arg first; if missing try 2-arg with our skeleton meta from the cache.
        var ctors = xmlPortType.GetConstructors(
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
        var oneArg = ctors.FirstOrDefault(c => c.GetParameters().Length == 1 &&
            typeof(Microsoft.Dynamics.Nav.Runtime.ITreeObject)
                .IsAssignableFrom(c.GetParameters()[0].ParameterType));
        if (oneArg != null) return oneArg.Invoke(new object[] { self });

        var twoArg = ctors.FirstOrDefault(c => c.GetParameters().Length == 2 &&
            typeof(Microsoft.Dynamics.Nav.Runtime.ITreeObject)
                .IsAssignableFrom(c.GetParameters()[0].ParameterType));
        if (twoArg != null)
        {
            object? metaArg = LookupNclMetaForXmlPort(id);
            return twoArg.Invoke(new object?[] { self, metaArg });
        }
        throw new InvalidOperationException(
            $"XmlPort{id} has no (ITreeObject) or (ITreeObject, NCLMetaXmlPort) constructor");
    }

    private static object? LookupNclMetaForXmlPort(int id)
    {
        var nclMeta = BcRuntime.SkeletonNCLMetadata;
        if (nclMeta == null) return null;
        var navNcl = AppDomain.CurrentDomain.GetAssemblies()
            .FirstOrDefault(a => a.GetName().Name == "Microsoft.Dynamics.Nav.Ncl");
        var getMeta = nclMeta.GetType().GetMethod("GetMetaXmlPortById",
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance, null,
            new[] { typeof(int), typeof(bool) }, null)
            ?? nclMeta.GetType().GetMethod("GetMetaXmlPortById",
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance, null,
            new[] { typeof(int) }, null);
        try { return getMeta?.Invoke(nclMeta, getMeta.GetParameters().Length == 2
            ? new object[] { id, false }
            : new object[] { id }); }
        catch { return null; }
    }

    private static Type? FindXmlPortType(int id)
    {
        var navNcl = AppDomain.CurrentDomain.GetAssemblies()
            .FirstOrDefault(a => a.GetName().Name == "Microsoft.Dynamics.Nav.Ncl");
        Type? xmlPortBase = navNcl?.GetType("Microsoft.Dynamics.Nav.Runtime.NavXmlPort");
        var name = $"XmlPort{id}";
        if (_currentTestAssembly != null)
        {
            try
            {
                var t = Array.Find(_currentTestAssembly.GetTypes(),
                    x => x.Name == name && (xmlPortBase == null || xmlPortBase.IsAssignableFrom(x)));
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
                    x => x.Name == name && (xmlPortBase == null || xmlPortBase.IsAssignableFrom(x)));
                if (t != null) return t;
            }
            catch { }
        }
        return null;
    }

    // ──────────────────────────────────────────────────────────────────
    // NavXmlPort instance method stubs — all paths through Export/Import/
    // Run/SetTableView call Session.BeginTransaction or
    // ApplicationObjectRootScope which NRE on skeleton session.
    // Return the "success / no-op" value for each.
    // ──────────────────────────────────────────────────────────────────

    // ──────────────────────────────────────────────────────────────────
    // NavXmlPort static Export/Import — XMLPORT.EXPORT(id, stream) and
    // XMLPORT.IMPORT(id, stream) in AL compile to these static overloads.
    // They call NCLMetadata.GetMetaXmlPortById which throws on our skeleton.
    // Stub as no-op (return true = success) to match the instance stubs.
    // ──────────────────────────────────────────────────────────────────

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static bool NavXmlPort_StaticExport(int errorLevel, int xmlPortId, object outStream, object record) => true;

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static bool NavXmlPort_StaticImport(int errorLevel, int xmlPortId, object inStream, object record) => true;

    /// <summary>Export(DataError) → return true (no-op).</summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static bool NavXmlPort_Export(object self, int errorLevel) => true;

    /// <summary>Import(DataError) → return true (no-op).</summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static bool NavXmlPort_Import(object self, int errorLevel) => true;

    /// <summary>Run() → return (no-op).</summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static void NavXmlPort_Run(object self) { }

    /// <summary>RunXmlPort() (private) — the actual execution body that the BC-generated
    /// scope code calls directly for local XmlPort variables. Stubs as no-op alongside Run().</summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static void NavXmlPort_RunXmlPort(object self) { }

    /// <summary>SetTableView(NavRecord) → return (no-op). Iterating the empty
    /// nodes list would throw NavNCLXmlPortNodeNotFoundException on a fresh skeleton.</summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static void NavXmlPort_SetTableView(object self, object record) { }

    /// <summary>BeginInitialization() — called from the BC-generated XmlPort{ID} ctor.
    /// Dereferences Session.MetadataProvider (null on skeleton) → NRE. Stub as no-op;
    /// fields it would populate (metadata, fieldDelimiter, …) are not needed for our
    /// Export/Import/Run/SetTableView stubs to function.</summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static void NavXmlPort_BeginInitialization(object self)
    {
    }

    /// <summary>EndInitialization() — called from the BC-generated XmlPort{ID} ctor after
    /// the node-building code. Accesses metadata.UseRequestForm and requestOptionsPage
    /// (both null on skeleton after BeginInitialization is no-op'd) → NRE. Stub as no-op.</summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static void NavXmlPort_EndInitialization(object self)
    {
    }

    /// <summary>
    /// XmlPort{ID}.InitializeComponent() — the BC-generated override that calls
    /// BeginInitialization, constructs nodes, and calls EndInitialization. EndInitialization
    /// accesses metadata (null on skeleton) and may be JIT-inlined into the BC-generated
    /// InitializeComponent body, making the EndInitialization hook unreliable. We instead
    /// hook the concrete override directly (after the test assembly is loaded) so the JIT
    /// has not yet compiled the method and the hook is guaranteed to land.
    /// </summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static void NavXmlPort_InitializeComponent(object self)
    {
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static void NavXmlPort_AddTableNode(object self, object node) { }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static void NavXmlPort_AddFieldNode(object self, object node) { }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static void NavXmlPort_AddTextNode(object self, object node) { }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static void NavXmlPortTableNode_Ctor(object self, object record)
    {
        EnsureXmlPortNodeFields(self.GetType());
        if (_fXmlPortNodeAttrChildren != null)
        {
            _fXmlPortNodeAttrChildren.SetValue(self, Activator.CreateInstance(_xmlPortNodeListType!));
            _fXmlPortNodeElemChildren!.SetValue(self, Activator.CreateInstance(_xmlPortNodeListType!));
        }
    }

    private static System.Type? _xmlPortNodeListType;

    private static void EnsureXmlPortNodeFields(Type derivedType)
    {
        if (_fXmlPortNodeAttrChildren != null) return;
        var t = derivedType;
        while (t != null)
        {
            var attr = t.GetField("attributeChildren",
                BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly);
            var elem = t.GetField("elementChildren",
                BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly);
            if (attr != null && elem != null)
            {
                var listType = typeof(System.Collections.Generic.List<>).MakeGenericType(attr.FieldType.GetGenericArguments()[0]);
                System.Threading.Interlocked.CompareExchange(ref _xmlPortNodeListType, listType, null);
                System.Threading.Interlocked.CompareExchange(ref _fXmlPortNodeAttrChildren, attr, null);
                System.Threading.Interlocked.CompareExchange(ref _fXmlPortNodeElemChildren, elem, null);
                return;
            }
            t = t.BaseType;
        }
    }
}
