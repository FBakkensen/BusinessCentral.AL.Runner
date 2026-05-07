// BcRuntime — applies Linux-compatibility patches to BC service-tier DLLs at process start.
// Lifted directly from spike/bc-abi-identity/runner/LinuxBootstrap.cs (proven to work end-to-end).
// Pattern: bc-linux's JMP-hook via mprotect + RuntimeHelpers.PrepareMethod.
using System.Collections.Concurrent;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace AlRunnerV2;

public static class BcRuntime
{
    private static bool _applied;
    private static Type? _navEnvironmentType;
    private static object? _skeletonSession;
    private static Microsoft.Dynamics.Nav.Runtime.NavMethodScope? _skeletonRootScope;
    public static Microsoft.Dynamics.Nav.Runtime.ITreeObject? RootTreeStub;

    // Reflected fields used by the NavMethodScope ctor replacement.
    // Populated in ApplyAllPatches; used in NavMethodScopeCtorReplacement.
    private static FieldInfo? _fTreeObjTree;           // TreeObject.tree
    private static FieldInfo? _fMsSession;             // NavMethodScope.session
    private static FieldInfo? _fMsParentScope;         // NavMethodScope.parentScope
    private static FieldInfo? _fMsFlags;               // NavMethodScope.flags
    private static FieldInfo? _fMsStackDepth;          // NavMethodScope.<StackDepth>k__BackingField
    private static FieldInfo? _fMsTopLevelAppObj;      // NavMethodScope.<TopLevelApplicationObject>k__BackingField
    private static FieldInfo? _fSessCurrentScope;      // NavSession.<CurrentMethodScope>k__BackingField
    private static MethodInfo? _mCreateTreeHandler;    // TreeHandler.CreateTreeHandler
    private static Type? _navNCLDialogExceptionType;   // NavNCLDialogException (for NavDialog.ALError replacement)

    // Set to the currently-loaded test assembly so CreateTarget looks up codeunit types there.
    private static Assembly? _currentTestAssembly;
    public static void SetTestAssembly(Assembly asm)
    {
        if (_currentTestAssembly == asm) return;
        _currentTestAssembly = asm;
        _codeunitTypeCache.Clear();
    }

    public static void EnsureApplied()
    {
        if (_applied) return;
        _applied = true;
        Win32Stubs.Register();
        ForceLoadBcDlls();
        var navNcl = AppDomain.CurrentDomain.GetAssemblies()
            .First(a => a.GetName().Name == "Microsoft.Dynamics.Nav.Ncl");
        RootTreeStub = new RootTreeObject();
        ApplyAllPatches(navNcl);
    }

    private static void ForceLoadBcDlls()
    {
        var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".local/share/al-runner/artifacts/27.5.46862.48827");
        foreach (var n in new[] { "Microsoft.Dynamics.Nav.Common", "Microsoft.Dynamics.Nav.Types",
                                  "Microsoft.Dynamics.Nav.Language", "Microsoft.Dynamics.Nav.Ncl" })
            Assembly.LoadFrom(Path.Combine(dir, n + ".dll"));
    }

    private static void ApplyAllPatches(Assembly navNcl)
    {
        var envType = navNcl.GetType("Microsoft.Dynamics.Nav.Runtime.NavEnvironment")
            ?? throw new InvalidOperationException("NavEnvironment not found");
        _navEnvironmentType = envType;

        // NavEnvironment.cctor — replace WindowsIdentity-touching init
        Hook(envType.TypeInitializer!, nameof(NavEnvironmentCctorReplacement), "NavEnvironment..cctor");
        HookProperty(envType, "ServiceAccount", true, nameof(GetServiceAccountReplacement));
        HookProperty(envType, "ServiceAccountName", true, nameof(GetServiceAccountNameReplacement));
        HookMethodIfExists(envType, "EmitServerStartupTraceEvents",
            BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance,
            (m) => m.IsStatic ? nameof(NoOp2) : nameof(NoOp3));

        // Pre-populate NavEnvironment.instance to a skeleton; hook Instance getter.
        var instField = envType.GetField("instance", BindingFlags.NonPublic | BindingFlags.Static);
        if (instField != null)
        {
            var skel = RuntimeHelpers.GetUninitializedObject(envType);
            var instLock = envType.GetField("lockObject", BindingFlags.NonPublic | BindingFlags.Instance);
            if (instLock != null) instLock.SetValue(skel, new object());
            instField.SetValue(null, skel);
        }
        HookProperty(envType, "Instance", true, nameof(GetInstanceReplacement));

        // NavApplicationObjectBase.get_Session — return skeleton NavSession
        var aoType = navNcl.GetType("Microsoft.Dynamics.Nav.Runtime.NavApplicationObjectBase");
        var sessType = navNcl.GetType("Microsoft.Dynamics.Nav.Runtime.NavSession");
        var msType = navNcl.GetType("Microsoft.Dynamics.Nav.Runtime.NavMethodScope");
        var treeObjType = navNcl.GetType("Microsoft.Dynamics.Nav.Runtime.TreeObject");
        var treeHandlerType = navNcl.GetType("Microsoft.Dynamics.Nav.Runtime.TreeHandler");
        if (aoType != null && sessType != null)
        {
            _skeletonSession = RuntimeHelpers.GetUninitializedObject(sessType);
            HookProperty(aoType, "Session", false, nameof(GetSessionReplacement));
        }
        if (sessType != null)
        {
            HookProperty(sessType, "CurrentMethodScope", false, nameof(GetCurrentMethodScopeReplacement));
            // VerifyExecutePermission overloads → no-op
            foreach (var m in sessType.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                .Where(m => m.Name == "VerifyExecutePermission" && m.ReturnType == typeof(void)))
            {
                var p = m.GetParameters().Length;
                var noop = p switch { 1 => nameof(NoOp2), 2 => nameof(NoOp3), _ => null };
                if (noop != null) Hook(m, noop, $"NavSession.VerifyExecutePermission/{p}");
            }
        }

        // Reflect and cache the fields we need for the ctor replacement below.
        if (treeObjType != null)
            _fTreeObjTree = treeObjType.GetField("tree", BindingFlags.NonPublic | BindingFlags.Instance);
        if (msType != null)
        {
            _fMsSession    = msType.GetField("session",      BindingFlags.NonPublic | BindingFlags.Instance);
            _fMsParentScope= msType.GetField("parentScope",  BindingFlags.NonPublic | BindingFlags.Instance);
            _fMsFlags      = msType.GetField("flags",        BindingFlags.NonPublic | BindingFlags.Instance);
            _fMsStackDepth = msType.GetField("<StackDepth>k__BackingField", BindingFlags.NonPublic | BindingFlags.Instance);
            _fMsTopLevelAppObj = msType.GetField("<TopLevelApplicationObject>k__BackingField", BindingFlags.NonPublic | BindingFlags.Instance);
        }
        if (sessType != null)
            _fSessCurrentScope = sessType.GetField("<CurrentMethodScope>k__BackingField", BindingFlags.NonPublic | BindingFlags.Instance);
        if (treeHandlerType != null)
            _mCreateTreeHandler = treeHandlerType.GetMethod("CreateTreeHandler",
                BindingFlags.Public | BindingFlags.Static);

        // Build a proper NavMethodScope+RootMethodScope skeleton so the NavMethodScope ctor
        // (which calls base(parent)) can create child TreeHandlers from it safely.
        // Returning RootTreeObject (an ITreeObject, not NavMethodScope) caused out-of-bounds
        // field reads on a corpus run where heap was fragmented.
        if (msType != null && sessType != null && treeObjType != null && treeHandlerType != null)
        {
            var rootMSType = msType.GetNestedType("RootMethodScope", BindingFlags.NonPublic);
            var createRoot = treeHandlerType.GetMethod("CreateTreeRoot",
                BindingFlags.Public | BindingFlags.Static);
            if (rootMSType != null && createRoot != null)
            {
                var skel = RuntimeHelpers.GetUninitializedObject(rootMSType);
                // CreateTreeRoot(skel) sets parentHandler=null, hostObject=skel.
                // Requires skel.Tree == null (it is — uninitialized) and calls skel.SingleThreaded.
                var rootTree = createRoot.Invoke(null, new object[] { skel });
                // Populate fields so IsDisposed, StackDepth, IsRootScope, etc. work correctly.
                var treeField = treeObjType.GetField("tree", BindingFlags.NonPublic | BindingFlags.Instance);
                var sessionField = msType.GetField("session", BindingFlags.NonPublic | BindingFlags.Instance);
                var flagsField = msType.GetField("flags", BindingFlags.NonPublic | BindingFlags.Instance);
                var depthField = msType.GetField("<StackDepth>k__BackingField",
                    BindingFlags.NonPublic | BindingFlags.Instance);
                if (treeField != null) FieldPoke.SetInstance(treeField, skel, rootTree);
                if (sessionField != null) FieldPoke.SetInstance(sessionField, skel, _skeletonSession);
                if (flagsField != null) FieldPoke.SetInstance(flagsField, skel, Enum.ToObject(flagsField.FieldType, 1)); // RootScope=1
                if (depthField != null) FieldPoke.SetInstance(depthField, skel, 1);
                _skeletonRootScope = (Microsoft.Dynamics.Nav.Runtime.NavMethodScope)skel;
            }
        }

        // Hook the 3-arg NavMethodScope ctor that all generated test-scope nested classes call.
        // The BC ctor body dereferences properties on the skeleton session/root-scope that NRE
        // once earlier-bucket test scopes have mutated shared state (e.g. session.CurrentMethodScope
        // setter writes back, some paths touch Diagnostics, etc.).
        // Replace the whole ctor body with a minimal safe implementation that sets only the
        // fields actually needed for Pass/Fail/Error classification at this layer of the pipeline.
        if (msType != null)
        {
            var aoType2 = navNcl.GetType("Microsoft.Dynamics.Nav.Runtime.NavApplicationObjectBase");
            var msFlagsType = _fMsFlags?.FieldType;
            if (aoType2 != null && msFlagsType != null)
            {
                var ctor3 = msType.GetConstructors(
                    BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Public)
                    .FirstOrDefault(c => {
                        var ps = c.GetParameters();
                        return ps.Length == 3
                            && ps[0].ParameterType == aoType2
                            && ps[2].ParameterType == typeof(bool);
                    });
                if (ctor3 != null)
                {
                    Console.Error.WriteLine($"[BcRuntime] Hooking 3-arg NavMethodScope ctor: {ctor3}");
                    Hook(ctor3, nameof(NavMethodScopeCtorReplacement), "NavMethodScope..ctor(NavApplicationObjectBase,MethodScopeFlags,bool)");
                }
                else
                    Console.Error.WriteLine($"[BcRuntime] WARNING: 3-arg NavMethodScope ctor NOT FOUND");
            }
            else
                Console.Error.WriteLine($"[BcRuntime] WARNING: msFlagsType={msFlagsType}, aoType2={aoType2}");
        }

        // NavMethodScope.ThrowStackOverflow — stack-depth check uses non-NavMethodScope, false-positive
        if (msType != null)
        {
            var tso = msType.GetMethod("ThrowStackOverflow",
                BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Static | BindingFlags.Instance);
            if (tso != null)
            {
                var p = tso.GetParameters().Length + (tso.IsStatic ? 0 : 1);
                var noop = p switch { 1 => nameof(NoOp_OneArg), 2 => nameof(NoOp2), _ => null };
                if (noop != null) Hook(tso, noop, "NavMethodScope.ThrowStackOverflow");
            }
        }

        // ALTelemetryHelper.LogALErrorTelemetry — called before creating NavNCLDialogException;
        // NREs through SessionContextHelper.GetALScope → NavGlobal.get_NCLMetadata on skeleton.
        // No-op is safe because the throw still happens immediately after.
        // The type lives in Microsoft.Dynamics.Nav.Runtime.AL namespace (not Runtime directly).
        foreach (var telTypeName in new[] {
            "Microsoft.Dynamics.Nav.Runtime.ALTelemetryHelper",       // older builds
            "Microsoft.Dynamics.Nav.Runtime.AL.ALTelemetryHelper" })  // 27.x+
        {
            var telType = navNcl.GetType(telTypeName);
            if (telType == null) continue;
            foreach (var m in telType.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)
                .Where(m => m.Name == "LogALErrorTelemetry"))
            {
                var p = m.GetParameters().Length;
                var noop = p switch { 2 => nameof(NoOp2), 3 => nameof(NoOp3), 4 => nameof(NoOp4), _ => null };
                if (noop != null) Hook(m, noop, $"ALTelemetryHelper.LogALErrorTelemetry/{p}");
            }
        }

        // SessionTransactionExtensions.Rollback — called by AssertError after catching an AL error;
        // NREs through skeleton session.DataAccessSource (null).
        var stExtType = navNcl.GetType("Microsoft.Dynamics.Nav.Runtime.SessionTransactionExtensions");
        var rollback = stExtType?.GetMethod("Rollback",
            BindingFlags.Public | BindingFlags.Static, null, new[] { sessType! }, null);
        if (rollback != null)
            Hook(rollback, nameof(NoOp_OneArg), "SessionTransactionExtensions.Rollback");

        // NCLEnumMetadata.Create(int) — called at field-initializer time for every enum variable;
        // chains through NavGlobal.MetadataProvider → SystemTenant → NavEnvironment.Tenants → NRE.
        // Returning NCLOptionMetadata.Default preserves ordinal arithmetic (NavOption.Value = passed int).
        var nclEnumMeta = navNcl.GetType("Microsoft.Dynamics.Nav.Runtime.NCLEnumMetadata");
        if (nclEnumMeta != null)
        {
            var createById = nclEnumMeta.GetMethod("Create",
                BindingFlags.Public | BindingFlags.Static, null, new[] { typeof(int) }, null);
            if (createById != null)
                Hook(createById, nameof(NCLEnumMetadata_CreateById), "NCLEnumMetadata.Create(int)");
        }

        // NavCodeunitHandle.CreateTarget — bypass NavGlobal.NCLMetadata by constructing
        // the codeunit directly from the loaded compiled assembly via reflection.
        var codeunitHandleType = navNcl.GetType("Microsoft.Dynamics.Nav.Runtime.NavCodeunitHandle");
        if (codeunitHandleType != null)
        {
            var createTarget = codeunitHandleType.GetMethod("CreateTarget",
                BindingFlags.NonPublic | BindingFlags.Instance);
            if (createTarget != null)
                Hook(createTarget, nameof(NavCodeunitHandle_CreateTarget), "NavCodeunitHandle.CreateTarget");
        }

        // NavCancellationToken throws — uninitialized cancellation tokens trip the check.
        var typesAsm = AppDomain.CurrentDomain.GetAssemblies()
            .FirstOrDefault(a => a.GetName().Name == "Microsoft.Dynamics.Nav.Types");
        var ctType = typesAsm?.GetType("Microsoft.Dynamics.Nav.Types.NavCancellationToken");
        if (ctType != null)
        {
            foreach (var name in new[] { "ThrowOperationCanceledException", "ThrowIfCancellationRequested" })
            foreach (var m in ctType.GetMethods(BindingFlags.Public | BindingFlags.NonPublic
                                                | BindingFlags.Instance | BindingFlags.Static)
                                    .Where(mm => mm.Name == name))
            {
                var p = m.GetParameters().Length + (m.IsStatic ? 0 : 1);
                var noop = p switch { 1 => nameof(NoOp_OneArg), 2 => nameof(NoOp2), _ => null };
                if (noop != null) Hook(m, noop, $"NavCancellationToken.{name}/{m.GetParameters().Length}");
            }
        }

        // NavSessionSettings.ALInit — called when AL SessionSettings variable is initialised;
        // NREs through NavGlobal / session infrastructure. No-op leaves settings at defaults.
        var sessSettingsType = navNcl.GetType("Microsoft.Dynamics.Nav.Runtime.NavSessionSettings");
        if (sessSettingsType != null)
        {
            var alInit = sessSettingsType.GetMethod("ALInit",
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            if (alInit != null)
                Hook(alInit, nameof(NoOp_OneArg), "NavSessionSettings.ALInit");
        }

        // NavCodeunit.ContainsMethod(int, string, object[]) — chains through NCLMetadata; return false.
        var navCodeunitType = navNcl.GetType("Microsoft.Dynamics.Nav.Runtime.NavCodeunit");
        if (navCodeunitType != null)
        {
            var containsMethod = navCodeunitType.GetMethod("ContainsMethod",
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
            if (containsMethod != null)
                Hook(containsMethod, nameof(ReturnFalse_3Args), "NavCodeunit.ContainsMethod");
        }

        // NavDialog.ALError(NavSession, Guid, NavALErrorInfo) — NREs when accessing diagnostics on
        // the skeleton session. Throw NavNCLDialogException so asserterror traps it correctly.
        var navDialogType = navNcl.GetType("Microsoft.Dynamics.Nav.Runtime.NavDialog");
        if (navDialogType != null && typesAsm != null)
        {
            _navNCLDialogExceptionType = typesAsm.GetType(
                "Microsoft.Dynamics.Nav.Types.Exceptions.NavNCLDialogException");
            foreach (var m in navDialogType.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)
                .Where(m => m.Name == "ALError"))
            {
                var ps = m.GetParameters();
                // Only hook the overloads that NRE; simpler string overloads already work fine.
                if (ps.Length >= 1 && ps[ps.Length - 1].ParameterType.Name == "NavALErrorInfo")
                    Hook(m, nameof(NavDialogALError_NavALErrorInfo), $"NavDialog.ALError/{ps.Length}");
            }
        }

    }

    private static void HookProperty(Type t, string propName, bool isStatic, string replacementName)
    {
        var flags = BindingFlags.Public | BindingFlags.NonPublic |
                    (isStatic ? BindingFlags.Static : BindingFlags.Instance);
        var p = t.GetProperty(propName, flags);
        if (p?.GetMethod != null) Hook(p.GetMethod, replacementName, $"{t.Name}.get_{propName}");
    }

    private static void HookMethodIfExists(Type t, string methodName, BindingFlags flags,
                                           Func<MethodInfo, string?> picker)
    {
        var m = t.GetMethod(methodName, flags);
        if (m == null) return;
        var name = picker(m);
        if (name != null) Hook(m, name, $"{t.Name}.{methodName}");
    }

    private static void Hook(MethodBase original, string replacementName, string description)
    {
        var repl = typeof(BcRuntime).GetMethod(replacementName,
            BindingFlags.Public | BindingFlags.Static)
            ?? throw new InvalidOperationException($"Replacement {replacementName} not found");
        JmpHook.Apply(original, repl, description);
    }

    // === Replacement methods ===
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static void NavEnvironmentCctorReplacement()
    {
        var t = _navEnvironmentType!;
        FieldPoke.SetStatic(t, "lockObject", new object());
        FieldPoke.SetStatic(t, "instanceId", Guid.NewGuid());
        FieldPoke.SetStatic(t, "serviceInstanceName", string.Empty);
        FieldPoke.TryInitDefault(t, "compactLohGate");
        FieldPoke.TryInitDefault(t, "TerminatedSessionsMetric");
        FieldPoke.TryInitDefault(t, "defaultAwaitedShutdownConnectionTypesList");
        FieldPoke.TryInitDefault(t, "defaultRestartNotificationConnectionTypesList");
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static object? GetServiceAccountReplacement() =>
        new System.Security.Principal.SecurityIdentifier("S-1-5-18");

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static string GetServiceAccountNameReplacement() => "SYSTEM";

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static object? GetInstanceReplacement()
    {
        var f = _navEnvironmentType!.GetField("instance", BindingFlags.NonPublic | BindingFlags.Static);
        return f?.GetValue(null);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static object? GetSessionReplacement(object self) => _skeletonSession;

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static object? GetCurrentMethodScopeReplacement(object self)
    {
        if (_skeletonRootScope != null && _skeletonRootScope.IsDisposed)
            Console.Error.WriteLine("[BcRuntime] WARNING: _skeletonRootScope is disposed!");
        return _skeletonRootScope;
    }

    /// <summary>
    /// Replacement for NavMethodScope..ctor(NavApplicationObjectBase, MethodScopeFlags, bool).
    ///
    /// The real ctor body dereferences many properties on the skeleton session and root scope that
    /// become unreliable once earlier test-bucket scopes have mutated shared state (e.g. the
    /// session's CurrentMethodScope backing field, Diagnostics, ExecutionUnit, etc.).  Instead of
    /// patching each individually we replace the entire body with a minimal implementation that
    /// only sets the fields callers actually depend on in our thin test harness.
    ///
    /// Fields set (all via FieldPoke to bypass readonly/private access restrictions):
    ///   TreeObject.tree              — new child TreeHandler under _skeletonRootScope
    ///   NavMethodScope.session       — _skeletonSession
    ///   NavMethodScope.parentScope   — _skeletonRootScope
    ///   NavMethodScope.flags         — GetMethodScopeFlags() (virtual, resolved on concrete subtype)
    ///   NavMethodScope.StackDepth    — 2 (root=1, one level deeper)
    ///   NavMethodScope.TopLevelApplicationObject — applicationObject
    ///   NavSession.CurrentMethodScope (backing field) — self
    /// </summary>
    /// <summary>
    /// Replacement for the BODY of NavMethodScope..ctor(NavApplicationObjectBase, MethodScopeFlags, bool).
    ///
    /// By the time this is called, the base-chain ctors (TreeObject..ctor → NavScope..ctor) have
    /// already run, so the TreeObject.tree field is already initialised.  This replacement only
    /// needs to initialise the fields that are declared in NavMethodScope itself:
    ///   session, parentScope, flags, cancellationToken (left at default 0), StackDepth,
    ///   TopLevelApplicationObject, and session.CurrentMethodScope.
    ///
    /// This avoids every property dereference in the real ctor body that can NRE on a skeleton
    /// session/root-scope, especially when shared mutable state (CurrentMethodScope backing field,
    /// Diagnostics, ServiceConnection, etc.) is dirty from a previous test bucket.
    /// </summary>
    /// <summary>
    /// Full replacement for NavMethodScope..ctor(NavApplicationObjectBase, MethodScopeFlags, bool).
    ///
    /// When a JMP-hook replaces a constructor, the ENTIRE ctor is replaced — including the
    /// base-chain call (: base(...)). That means TreeObject..ctor and NavScope..ctor do NOT run,
    /// so we must set up every field that any of those base ctors would have initialised.
    ///
    /// Fields initialised (all via FieldPoke to bypass readonly/private restrictions):
    ///
    ///   TreeObject.tree              — new child TreeHandler under _skeletonRootScope; sets up
    ///                                  the parent-child link in the tree so Dispose bookkeeping works
    ///   NavMethodScope.session       — _skeletonSession
    ///   NavMethodScope.parentScope   — _skeletonRootScope
    ///   NavMethodScope.flags         — GetMethodScopeFlags() on the concrete subtype
    ///   NavMethodScope.StackDepth    — 2 (root=1, one level deeper)
    ///   NavMethodScope.TopLevelApplicationObject — applicationObject
    ///   NavSession.CurrentMethodScope (backing field) — self
    ///
    /// TreeHandler.isDisposing is left false (default); all other TreeObject/NavMethodScope
    /// fields default to null/0/false which is safe for the thin test-harness usage.
    /// </summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static void NavMethodScopeCtorReplacement(
        Microsoft.Dynamics.Nav.Runtime.NavMethodScope self,
        Microsoft.Dynamics.Nav.Runtime.NavApplicationObjectBase applicationObject,
        object flags,   // MethodScopeFlags — superseded by GetMethodScopeFlags()
        bool eventSource)
    {
        // 1. TreeObject.tree — CreateTreeHandler links self as a child of _skeletonRootScope.
        //    This is the equivalent of base(applicationObject.Session.CurrentMethodScope)
        //    → TreeObject..ctor(_skeletonRootScope) → tree = CreateTreeHandler(_skeletonRootScope, self).
        if (_mCreateTreeHandler != null && _fTreeObjTree != null && _skeletonRootScope != null)
        {
            var handler = _mCreateTreeHandler.Invoke(null, new object[] { _skeletonRootScope, self });
            FieldPoke.SetInstance(_fTreeObjTree, self, handler);
        }
        // 2. NavMethodScope.session
        if (_fMsSession != null)     FieldPoke.SetInstance(_fMsSession,     self, _skeletonSession);
        // 3. NavMethodScope.parentScope = _skeletonRootScope
        if (_fMsParentScope != null) FieldPoke.SetInstance(_fMsParentScope, self, _skeletonRootScope);
        // 4. NavMethodScope.flags — resolve via virtual GetMethodScopeFlags() on the concrete subtype.
        //    NavMethodScope<T> → IsStackFrame; TryMethodScope → IsInTryScope; etc.
        if (_fMsFlags != null)
        {
            try
            {
                var getFlags = self.GetType().GetMethod("GetMethodScopeFlags",
                    BindingFlags.NonPublic | BindingFlags.Instance);
                var scopeFlags = getFlags != null ? getFlags.Invoke(self, null) : null;
                FieldPoke.SetInstance(_fMsFlags, self, scopeFlags ?? Enum.ToObject(_fMsFlags.FieldType, 0));
            }
            catch { /* leave flags at default 0 on reflection error */ }
        }
        // 5. NavMethodScope.StackDepth = 2 (_skeletonRootScope.StackDepth=1)
        if (_fMsStackDepth != null)  FieldPoke.SetInstance(_fMsStackDepth,  self, 2);
        // 6. NavMethodScope.TopLevelApplicationObject = applicationObject
        if (_fMsTopLevelAppObj != null) FieldPoke.SetInstance(_fMsTopLevelAppObj, self, applicationObject);
        // 7. NavSession.CurrentMethodScope backing field = self  (mirrors real ctor's session.CurrentMethodScope = this)
        if (_fSessCurrentScope != null && _skeletonSession != null)
            FieldPoke.SetInstance(_fSessCurrentScope, _skeletonSession, self);
        // cancellationToken, sqlStatisticsAvailable, globalSql*AtStart all left at default
        // (zero-value structs / false) — safe for the test harness since no SQL paths run.
    }

    [MethodImpl(MethodImplOptions.NoInlining)] public static void NoOp_OneArg(object? a) { }
    [MethodImpl(MethodImplOptions.NoInlining)] public static void NoOp2(object? a, object? b) { }
    [MethodImpl(MethodImplOptions.NoInlining)] public static void NoOp3(object? a, object? b, object? c) { }
    [MethodImpl(MethodImplOptions.NoInlining)] public static void NoOp4(object? a, object? b, object? c, object? d) { }

    /// <summary>
    /// Replacement for NavCodeunit.ContainsMethod(int, string, object[]).
    /// Returns false — callers use this to check whether a codeunit event handler exists.
    /// The real implementation chains through NCLMetadata which NREs on a skeleton session.
    /// </summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static bool ReturnFalse_3Args(object? a, object? b, object? c) => false;

    /// <summary>
    /// Replacement for NavDialog.ALError(NavSession, Guid, NavALErrorInfo) and similar overloads
    /// that take a NavALErrorInfo as the last parameter.  The real body NREs through diagnostics
    /// infrastructure on the skeleton session.  We construct NavNCLDialogException directly from
    /// the error message in NavALErrorInfo so asserterror traps it correctly.
    /// </summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static void NavDialogALError_NavALErrorInfo(object? a, object? b, object? errorInfo)
    {
        string msg = string.Empty;
        if (errorInfo != null)
        {
            try
            {
                var msgProp = errorInfo.GetType().GetProperty("ALMessage",
                    BindingFlags.Public | BindingFlags.Instance);
                msg = msgProp?.GetValue(errorInfo) as string ?? string.Empty;
            }
            catch { }
        }
        if (_navNCLDialogExceptionType != null)
        {
            var exc = Activator.CreateInstance(_navNCLDialogExceptionType, msg) as Exception
                ?? new Exception(msg);
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw(exc);
        }
        throw new Exception(msg.Length > 0 ? msg : "AL error");
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static Microsoft.Dynamics.Nav.Runtime.NCLOptionMetadata NCLEnumMetadata_CreateById(int id)
        => Microsoft.Dynamics.Nav.Runtime.NCLOptionMetadata.Default;

    // Cache: codeunit ID → generated codeunit Type (keyed per loaded assembly bytes).
    private static readonly ConcurrentDictionary<int, Type?> _codeunitTypeCache = new();

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
            throw new InvalidOperationException(
                $"NavCodeunitHandle.CreateTarget: no loaded type Codeunit{id} found");
        var ctor = codeunitType.GetConstructors()
            .FirstOrDefault(c => c.GetParameters().Length == 1 &&
                typeof(Microsoft.Dynamics.Nav.Runtime.ITreeObject)
                    .IsAssignableFrom(c.GetParameters()[0].ParameterType));
        if (ctor == null)
            throw new InvalidOperationException(
                $"Codeunit{id} has no single-arg ITreeObject constructor");
        return (Microsoft.Dynamics.Nav.Runtime.NavCodeunit)ctor.Invoke(new object[] { self });
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

// --- supporting helpers ---

internal sealed class RootTreeObject : Microsoft.Dynamics.Nav.Runtime.ITreeObject
{
    private readonly RootHandler _h;
    public RootTreeObject() { _h = new RootHandler(this); }
    Microsoft.Dynamics.Nav.Runtime.TreeHandler Microsoft.Dynamics.Nav.Runtime.ITreeObject.Tree => _h;
    Microsoft.Dynamics.Nav.Runtime.TreeObjectType Microsoft.Dynamics.Nav.Runtime.ITreeObject.Type => default;
    bool Microsoft.Dynamics.Nav.Runtime.ITreeObject.SingleThreaded => false;
}

internal sealed class RootHandler : Microsoft.Dynamics.Nav.Runtime.TreeHandler
{
    private static readonly FieldInfo _fHost =
        typeof(Microsoft.Dynamics.Nav.Runtime.TreeHandler)
            .GetField("hostObject", BindingFlags.NonPublic | BindingFlags.Instance)!;
    public RootHandler(Microsoft.Dynamics.Nav.Runtime.ITreeObject host) : base()
    {
        // IsDisposed = (hostObject == null) — flip it.
        _fHost.SetValue(this, host);
    }
}

internal static class FieldPoke
{
    public static void SetStatic(Type t, string name, object? value)
    {
        var f = t.GetField(name, BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
        if (f == null) return;
        try { f.SetValue(null, value); }
        catch (FieldAccessException) { SetStaticReadonly(f, value); }
    }
    public static void TryInitDefault(Type t, string fieldName)
    {
        var f = t.GetField(fieldName, BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
        if (f == null) return;
        try { SetStatic(t, fieldName, Activator.CreateInstance(f.FieldType)); }
        catch { /* optional */ }
    }
    public static void SetInstance(FieldInfo f, object obj, object? value)
    {
        try { f.SetValue(obj, value); }
        catch (FieldAccessException) { SetInstanceReadonly(f, obj, value); }
    }
    private static void SetInstanceReadonly(FieldInfo field, object obj, object? value)
    {
        var dm = new DynamicMethod($"setinst_{field.Name}", typeof(void),
            new[] { typeof(object), typeof(object) },
            field.DeclaringType!.Module, skipVisibility: true);
        var il = dm.GetILGenerator();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_1);
        if (field.FieldType.IsValueType) il.Emit(OpCodes.Unbox_Any, field.FieldType);
        il.Emit(OpCodes.Stfld, field);
        il.Emit(OpCodes.Ret);
        ((Action<object?, object?>)dm.CreateDelegate(typeof(Action<object?, object?>)))(obj, value);
    }
    private static void SetStaticReadonly(FieldInfo field, object? value)
    {
        var dm = new DynamicMethod($"set_{field.Name}", typeof(void), new[] { typeof(object) },
            field.DeclaringType!.Module, skipVisibility: true);
        var il = dm.GetILGenerator();
        if (value == null) il.Emit(OpCodes.Ldnull);
        else
        {
            il.Emit(OpCodes.Ldarg_0);
            if (field.FieldType.IsValueType) il.Emit(OpCodes.Unbox_Any, field.FieldType);
        }
        il.Emit(OpCodes.Stsfld, field);
        il.Emit(OpCodes.Ret);
        ((Action<object?>)dm.CreateDelegate(typeof(Action<object?>)))(value);
    }
}

internal static class JmpHook
{
    [DllImport("libc", SetLastError = true)]
    private static extern int mprotect(IntPtr addr, nuint len, int prot);
    private const int PROT_READ = 1, PROT_WRITE = 2, PROT_EXEC = 4;

    public static void Apply(MethodBase original, MethodInfo replacement, string name)
    {
        RuntimeHelpers.PrepareMethod(original.MethodHandle);
        RuntimeHelpers.PrepareMethod(replacement.MethodHandle);
        var origFp = original.MethodHandle.GetFunctionPointer();
        var replFp = replacement.MethodHandle.GetFunctionPointer();

        IntPtr compiledCode = IntPtr.Zero;
        try
        {
            byte[] precode = new byte[24];
            Marshal.Copy(origFp, precode, 0, 24);
            // .NET 8 x64 FixupPrecode: MOV r10,MD ; JMP [rip+disp32]
            if (precode[10] == 0xFF && precode[11] == 0x25)
                compiledCode = Marshal.ReadIntPtr(origFp + 16 + BitConverter.ToInt32(precode, 12));
            // StubPrecode
            if (compiledCode == IntPtr.Zero && precode[0] == 0xFF && precode[1] == 0x25)
                compiledCode = Marshal.ReadIntPtr(origFp + 6 + BitConverter.ToInt32(precode, 2));
            // E9 relative
            if (compiledCode == IntPtr.Zero && precode[0] == 0xE9)
                compiledCode = origFp + 5 + BitConverter.ToInt32(precode, 1);
        }
        catch { }

        WriteJmp(origFp, replFp);
        if (compiledCode != IntPtr.Zero && compiledCode != origFp && compiledCode != replFp)
            try { WriteJmp(compiledCode, replFp); } catch { }
    }

    private static void WriteJmp(IntPtr target, IntPtr destination)
    {
        // x86-64 absolute indirect: FF 25 00 00 00 00 [imm64]
        byte[] jmp = new byte[14];
        jmp[0] = 0xFF; jmp[1] = 0x25;
        BitConverter.GetBytes(destination.ToInt64()).CopyTo(jmp, 6);
        long pageSize = 4096;
        long addr = target.ToInt64();
        long pageStart = addr & ~(pageSize - 1);
        var regionSize = (nuint)((addr - pageStart) + jmp.Length + pageSize);
        if (mprotect(new IntPtr(pageStart), regionSize, PROT_READ | PROT_WRITE | PROT_EXEC) != 0) return;
        Marshal.Copy(jmp, 0, target, jmp.Length);
    }
}
