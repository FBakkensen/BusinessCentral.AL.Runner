// BcRuntime — applies Linux-compatibility patches to BC service-tier DLLs at process start.
// Lifted directly from spike/bc-abi-identity/runner/LinuxBootstrap.cs (proven to work end-to-end).
// Pattern: bc-linux's JMP-hook via mprotect + RuntimeHelpers.PrepareMethod.
using System.Collections.Concurrent;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using AlRunnerV2.Infrastructure;

namespace AlRunnerV2;

public static partial class BcRuntime
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

    // NavApplicationObjectBase ctor replacement fields.
    private static FieldInfo? _fAoSession;             // NavApplicationObjectBase.session
    private static FieldInfo? _fAoObjectId;            // NavApplicationObjectBase.objectId  (if needed)
    private static FieldInfo? _fAoOrigGroupId;         // NavApplicationObjectBase.originalAppGroupId
    private static FieldInfo? _fAoRuntimeGroupId;      // NavApplicationObjectBase.runtimeAppGroupId
    private static FieldInfo? _fNavComplexValueTree;   // NavComplexValue.tree (distinct from TreeObject.tree)
    private static object? _skeletonCompany;            // cached skeleton NavCompany (CompanyNameToken=0)

    // NavRecord write-path replacement fields (cached for perf).
    private static object? _skeletonNavServerEventSource;
    private static FieldInfo? _fNavRecordRecordImplementation;     // NavRecord.recordImplementation
    private static MethodInfo? _mRecordImplementationInsertRecordAsync;  // RecordImplementation.InsertRecordAsync
    private static MethodInfo? _mRecordImplementationModifyRecordAsync;  // RecordImplementation.ModifyRecordAsync
    private static MethodInfo? _mRecordImplementationDeleteRecordAsync;  // RecordImplementation.DeleteRecordAsync
    private static MethodInfo? _mRecordImplementationRenameRecordAsync;  // RecordImplementation.RenameRecordAsync
    private static MethodInfo? _mNavRecordCloneRecord;                   // NavRecord.CloneRecord(ITreeObject,bool,bool)
    private static FieldInfo? _fRecordImplementationDataAccess;          // RecordImplementation.dataAccess
    private static FieldInfo? _fRecordImplementationMutableRecordBuffer; // RecordImplementation.mutableRecordBuffer
    private static MethodInfo? _mDataAccessTryGetByPrimaryKeyAsync;
    private static PropertyInfo? _pMrbResultResult;     // MutableRecordBufferResult<bool>.Result
    private static PropertyInfo? _pMrbResultRecordBuffer;

    // Set to the currently-loaded test assembly so CreateTarget looks up codeunit types there.
    private static Assembly? _currentTestAssembly;
    public static void SetTestAssembly(Assembly asm)
    {
        if (_currentTestAssembly == asm) return;
        _currentTestAssembly = asm;
        _codeunitTypeCache.Clear();
        // (B) Spike: enumerate closed NavObjectDictionary`2 instantiations now that
        //     the test assembly is loaded and its closed generic types are in the AppDomain.
        var navNcl = AppDomain.CurrentDomain.GetAssemblies()
            .FirstOrDefault(a => a.GetName().Name == "Microsoft.Dynamics.Nav.Ncl");
        if (navNcl != null)
            ApplyNavObjectDictionaryGetTargetHooks(navNcl);
        // Hook XmlPort{ID}.InitializeComponent() overrides in the test assembly.
        // The BC-generated InitializeComponent calls EndInitialization() which may be
        // inlined by the JIT into the caller — hooking EndInitialization() in NCL is
        // unreliable. Hooking the override directly (on the concrete XmlPort type in
        // the test assembly) is deterministic since the JIT hasn't seen this method yet.
        HookXmlPortInitializeComponents(asm);
    }

    private static void HookXmlPortInitializeComponents(Assembly asm)
    {
        var repl = typeof(BcRuntime).GetMethod(nameof(NavXmlPort_InitializeComponent),
            BindingFlags.Public | BindingFlags.Static)!;
        try
        {
            foreach (var t in asm.GetTypes())
            {
                if (!t.Name.StartsWith("XmlPort")) continue;
                var m = t.GetMethod("InitializeComponent",
                    BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly,
                    null, Type.EmptyTypes, null);
                if (m == null) continue;
                JmpHook.Apply(m, repl, $"{t.Name}.InitializeComponent");
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[BcRuntime] HookXmlPortInitializeComponents failed: {ex.Message}");
        }
    }

    // ── Spike 4: EventPipe JIT listener ──────────────────────────────────────────────────────
    public static EventPipeJitListener? JitListener { get; private set; }

    /// <summary>
    /// Starts the EventPipe JIT listener with registered targets.
    /// Must be called BEFORE ForceLoadBcDlls() so we catch methods JIT'd during BC load.
    /// Also called early from Program.cs if needed.
    /// </summary>
    public static void StartJitListener()
    {
        if (JitListener != null) return;
        JitListener = new EventPipeJitListener();

        // Register targets.
        // (A) NavRecord.ALFieldCaptionAsync — the primary async-method target.
        var repl = typeof(BcRuntime).GetMethod(nameof(NavRecord_ALFieldCaptionAsync),
            System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
        if (repl != null)
            JitListener.AddTarget(
                "Microsoft.Dynamics.Nav.Runtime.NavRecord",
                "ALFieldCaptionAsync",
                repl);
        else
            Console.Error.WriteLine("[Spike4] Warning: NavRecord_ALFieldCaptionAsync replacement method not found");

        JitListener.Enable();
        Console.Error.WriteLine("[Spike4] JIT listener started (targets registered, subscribed)");
    }

    public static void EnsureApplied()
    {
        if (_applied) return;
        _applied = true;
        Win32Stubs.Register();

        // DISABLED for now: StartJitListener();

        ForceLoadBcDlls();
        var navNcl = AppDomain.CurrentDomain.GetAssemblies()
            .First(a => a.GetName().Name == "Microsoft.Dynamics.Nav.Ncl");

        // Now that NavNcl is loaded, register the `original` MethodBase for each target
        // so InstallIndirect can use it.
        if (JitListener != null)
        {
            var navRecordType = navNcl.GetType("Microsoft.Dynamics.Nav.Runtime.NavRecord");
            var ep = navRecordType?.GetMethods(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
                .FirstOrDefault(m => m.Name == "ALFieldCaptionAsync");
            if (ep != null)
            {
                var repl = typeof(BcRuntime).GetMethod(nameof(NavRecord_ALFieldCaptionAsync),
                    System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
                if (repl != null)
                    JitListener.AddTarget("Microsoft.Dynamics.Nav.Runtime.NavRecord", "ALFieldCaptionAsync", repl, ep);
                Console.Error.WriteLine($"[Spike4] Registered original MethodBase for ALFieldCaptionAsync");
            }
            else
                Console.Error.WriteLine("[Spike4] Warning: ALFieldCaptionAsync not found in NavRecord after load");
        }

        RootTreeStub = new RootTreeObject();
        SuppressEventLogWriter();
        ApplyAllPatches(navNcl);
    }

    /// <summary>
    /// Sets `Microsoft.Dynamics.Nav.Types.EventLogWriter.CustomWriter` to a no-op so
    /// `Write(...)` short-circuits before enqueueing into the background thread that
    /// calls into `System.Diagnostics.EventLog.WriteEntry` — which P/Invokes
    /// `kernel32.dll!WaitForSingleObject` from `System.Diagnostics.EventLog.dll`
    /// (an assembly Win32Stubs' Nav-only resolver doesn't cover, so we avoid the
    /// path entirely instead of broadening the resolver scope).
    /// </summary>
    private static void SuppressEventLogWriter()
    {
        var typesAsm = AppDomain.CurrentDomain.GetAssemblies()
            .FirstOrDefault(a => a.GetName().Name == "Microsoft.Dynamics.Nav.Types");
        var elw = typesAsm?.GetType("Microsoft.Dynamics.Nav.Types.EventLogWriter");
        var prop = elw?.GetProperty("CustomWriter", BindingFlags.Public | BindingFlags.Static);
        if (prop?.SetMethod == null) return;
        // CustomWriter is Action<string, EventLogEntryType, string>. Build a
        // matching no-op via DynamicMethod so we don't have to import
        // System.Diagnostics.EventLog (which is what we're avoiding).
        var args = prop.PropertyType.GetGenericArguments(); // [string, EventLogEntryType, string]
        var dm = new System.Reflection.Emit.DynamicMethod(
            "EventLogNoOpDyn", typeof(void), args, typeof(BcRuntime).Module);
        var il = dm.GetILGenerator();
        il.Emit(System.Reflection.Emit.OpCodes.Ret);
        prop.SetValue(null, dm.CreateDelegate(prop.PropertyType));
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

        // No-op `new NavOpenTelemetryLogger(...)` — its ctor opens an OpenTelemetry pipeline that
        // tries to add the Geneva ETW exporter, which throws on Linux. The NavEnvironment ctor
        // assigns the result to NavDiagnostics.OpenTelemetryLogger and never reads members until
        // a trace is sent later (already suppressed via existing trace hooks).
        var navTypesAsm = AppDomain.CurrentDomain.GetAssemblies()
            .FirstOrDefault(a => a.GetName().Name == "Microsoft.Dynamics.Nav.Types");
        var navOtl = navTypesAsm?.GetType("Microsoft.Dynamics.Nav.Diagnostic.NavOpenTelemetryLogger");
        if (navOtl != null)
        {
            foreach (var c in navOtl.GetConstructors(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
            {
                var ps = c.GetParameters().Length;
                var noop = ps switch { 1 => nameof(NoOp_OneArg), 2 => nameof(NoOp2), 3 => nameof(NoOp3), 4 => nameof(NoOp4), _ => null };
                if (noop != null) Hook(c, noop, $"NavOpenTelemetryLogger..ctor/{ps}");
            }
        }

        // No-op `ALFunctionTimingExecutionListener.EnsureRegistered()` — the real env ctor
        // (line 1107) registers a process-global listener whose Start(NavMethodScope) reads
        // `methodScope.AppId.HasValue` and other metadata that NREs on AL test scopes that
        // run with our minimal NavMethodScopeCtorReplacement. With the skeleton init path
        // the listener was never registered. Easiest cleanup: don't register it.
        var alFnTimingT = navNcl.GetType("Microsoft.Dynamics.Nav.Runtime.ALFunctionTimingExecutionListener");
        var ensureReg = alFnTimingT?.GetMethod("EnsureRegistered", BindingFlags.Public | BindingFlags.Static);
        if (ensureReg != null)
            Hook(ensureReg, nameof(NoOp_0Args), "ALFunctionTimingExecutionListener.EnsureRegistered");
        // Even with EnsureRegistered no-op'd, `Start(NavMethodScope)` is reachable directly
        // via `ApplicationObjectRootScope.AddApplicationObjectRootScope -> NavForm.Update` —
        // a different listener-registration path (likely server-listener add wired up
        // earlier in the env init chain) installs the listener anyway. Start NREs because
        // `methodScope.Session.ExtensionMetrics` is null on AL test scopes built via our
        // minimal NavMethodScopeCtorReplacement. The method is purely a telemetry/diagnostic
        // side effect — no AL semantic impact — so no-op it. Sync, single-arg, JmpHook-safe
        // per HANDOFF §5.1 (callers in BusinessApplication.dll = external from NCL.dll).
        var startM = alFnTimingT?.GetMethod("Start", BindingFlags.Public | BindingFlags.Static);
        if (startM != null)
            Hook(startM, nameof(NoOp_OneArg), "ALFunctionTimingExecutionListener.Start");
        // Same reasoning for the symmetric Exit(NavMethodScope) — telemetry-only counter
        // updates + long-running tracing diagnostics, no AL semantic effect.
        var exitM = alFnTimingT?.GetMethod("Exit", BindingFlags.Public | BindingFlags.Static);
        if (exitM != null)
            Hook(exitM, nameof(NoOp_OneArg), "ALFunctionTimingExecutionListener.Exit");

        // Try the real factory first: NavEnvironment.InstantiateStandaloneNavEnvironment(true, false).
        // The cctor replacement above already wired the static `lockObject`/`instanceId`/
        // `serviceInstanceName` so the factory's MonitorLock(lockObject, ...) succeeds.
        // If the ctor throws (Linux-incompatible deps, missing settings file, KeyVault, DB...),
        // fall back to the skeleton so the runner still boots; per-throw JMP-hooks should be
        // added one-by-one until the real ctor runs to completion.
        var instField = envType.GetField("instance", BindingFlags.NonPublic | BindingFlags.Static);
        var factory = envType.GetMethod("InstantiateStandaloneNavEnvironment",
            BindingFlags.NonPublic | BindingFlags.Static);
        bool ctorOk = false;
        if (factory != null)
        {
            try
            {
                factory.Invoke(null, new object[] { true, false });
                ctorOk = instField?.GetValue(null) != null;
                if (ctorOk) Console.Error.WriteLine("[BcRuntime] NavEnvironment ctor: OK (full init)");

                // The NoOp4-hooked NavOpenTelemetryLogger ctor leaves the inner readonly fields
                // (openTelemetryLoggerInstanceForNstLog, ...SpanLoggerInstance, ...) null. The env
                // ctor assigned this half-initialised instance to NavDiagnostics.OpenTelemetryLogger;
                // every trace call routes through `OpenTelemetryLogger?.LogTelemetryEvent(...)` which
                // dispatches to LogTelemetryEventTrace and NREs on the null inner. Setting the static
                // back to null routes through the existing `?.` null-conditional and skips telemetry.
                var navDiagT = navTypesAsm?.GetType("Microsoft.Dynamics.Nav.Diagnostic.NavDiagnostics");
                var pOtl = navDiagT?.GetProperty("OpenTelemetryLogger", BindingFlags.Public | BindingFlags.Static);
                pOtl?.SetValue(null, null);
            }
            catch (Exception ex)
            {
                var inner = ex is TargetInvocationException tie ? tie.InnerException ?? ex : ex;
                Console.Error.WriteLine("[BcRuntime] NavEnvironment ctor THREW — falling back to skeleton:");
                Console.Error.WriteLine($"  {inner.GetType().FullName}: {inner.Message}");
                var st = new System.Diagnostics.StackTrace(inner, fNeedFileInfo: true);
                for (int fi = 0; fi < st.FrameCount; fi++)
                {
                    var frame = st.GetFrame(fi);
                    var m = frame?.GetMethod();
                    Console.Error.WriteLine($"    [{fi}] IL+0x{frame?.GetILOffset():X4} native+0x{frame?.GetNativeOffset():X4}  {m?.DeclaringType?.FullName}.{m?.Name}({string.Join(",", m?.GetParameters().Select(p=>p.ParameterType.Name) ?? Array.Empty<string>())})");
                }
            }
        }
        if (!ctorOk && instField != null)
        {
            var skel = RuntimeHelpers.GetUninitializedObject(envType);
            var instLock = envType.GetField("lockObject", BindingFlags.NonPublic | BindingFlags.Instance);
            if (instLock != null) instLock.SetValue(skel, new object());
            instField.SetValue(null, skel);
        }
        HookProperty(envType, "Instance", true, nameof(GetInstanceReplacement));

        // NavApplicationObjectBase.get_Session — return skeleton NavSession.
        // Also hook the NavApplicationObjectBase.ctor to inject _skeletonSession directly,
        // because the get_Session property is typically inlined by the JIT.
        var aoType = navNcl.GetType("Microsoft.Dynamics.Nav.Runtime.NavApplicationObjectBase");
        var sessType = navNcl.GetType("Microsoft.Dynamics.Nav.Runtime.NavSession");
        var msType = navNcl.GetType("Microsoft.Dynamics.Nav.Runtime.NavMethodScope");
        var treeObjType = navNcl.GetType("Microsoft.Dynamics.Nav.Runtime.TreeObject");
        var treeHandlerType = navNcl.GetType("Microsoft.Dynamics.Nav.Runtime.TreeHandler");
        if (aoType != null && sessType != null)
        {
            _skeletonSession = RuntimeHelpers.GetUninitializedObject(sessType);
            HookProperty(aoType, "Session", false, nameof(GetSessionReplacement));

            // Cache fields for the ctor replacement.
            _fAoSession       = aoType.GetField("session",             BindingFlags.NonPublic | BindingFlags.Instance);
            _fAoOrigGroupId   = aoType.GetField("originalAppGroupId",  BindingFlags.NonPublic | BindingFlags.Instance);
            _fAoRuntimeGroupId= aoType.GetField("runtimeAppGroupId",   BindingFlags.NonPublic | BindingFlags.Instance);

            // Hook NavApplicationObjectBase..ctor to bypass the tree-based session lookup.
            // The real ctor does `session = base.Tree.Session` which gives null (skeleton has no real tree chain).
            // get_Session is inlined at every call site so the property hook alone is insufficient.
            var aoCtor = aoType.GetConstructors(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                .FirstOrDefault(c => {
                    var ps = c.GetParameters();
                    return ps.Length >= 2
                        && ps[0].ParameterType.Name == "ITreeObject"
                        && ps[1].ParameterType.Name == "ApplicationObjectId";
                });
            if (aoCtor != null)
            {
                Console.Error.WriteLine($"[BcRuntime] Hooking NavApplicationObjectBase.ctor: {aoCtor}");
                Hook(aoCtor, nameof(NavApplicationObjectBaseCtorReplacement), "NavApplicationObjectBase..ctor");
            }
            else
                Console.Error.WriteLine("[BcRuntime] WARNING: NavApplicationObjectBase.ctor NOT FOUND");
        }
        if (sessType != null)
        {
            HookProperty(sessType, "CurrentMethodScope", false, nameof(GetCurrentMethodScopeReplacement));
            // NavAppGroup reads tenant.NavAppGroup which NREs on the skeleton (tenant null).
            // Return NavAppGroup.BaseGroup so NavForm..ctor and other consumers can complete.
            HookProperty(sessType, "NavAppGroup", false, nameof(NavSession_NavAppGroup));
            // LocalLanguageNoFallback reads globalLanguageStack which is null in skeleton session; return -1 (use default).
            HookProperty(sessType, "LocalLanguageNoFallback", false, nameof(NavSession_LocalLanguageNoFallback));
            // IsLocalLanguage reads globalLanguageStack.Count — same NRE source. Return false: the
            // skeleton session uses InvariantCulture for formatting in headless mode.
            var isLocalLang = sessType.GetProperty("IsLocalLanguage",
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)?.GetGetMethod(true);
            if (isLocalLang != null) Hook(isLocalLang, nameof(ReturnFalse_1Arg), "NavSession.get_IsLocalLanguage");
            // GetSecurityFilters reads Database.SecurityAndLicense which NREs on the skeleton DB.
            // Return null — RecordImplementation handles null filters as "no security filtering".
            var getSecFilters = sessType.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                .FirstOrDefault(m => m.Name == "GetSecurityFilters");
            if (getSecFilters != null)
                Hook(getSecFilters, nameof(NavSession_GetSecurityFilters), "NavSession.GetSecurityFilters");
            // PushDynamicCaptionStack — language-stack manipulation, NREs on skeleton.
            var pushDyn = sessType.GetMethod("PushDynamicCaptionStack",
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            // PushDynamicCaptionStack is `bool (this, int, int)`. NoOp_OneArg leaves RAX
            // undefined → callers occasionally see "true" and dive into await
            // GetDynamicCaptionAsync which NREs (no UIHelperTriggers on skeleton). Force
            // a deterministic `false` so the async wrapper falls through to the sync
            // FieldCaption path (which is also patched to return FieldName).
            if (pushDyn != null) Hook(pushDyn, nameof(ReturnFalse_3Args), "NavSession.PushDynamicCaptionStack");
            // SyncFormatSettings also accesses cultureSettings (null in skeleton); return new FormatSettings().
            var syncFmt = sessType.GetMethod("SyncFormatSettings", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            if (syncFmt != null) Hook(syncFmt, nameof(NavSession_SyncFormatSettings), "NavSession.SyncFormatSettings");

            // get_Culture / get_WindowsCulture — real getters call CultureInfo.GetCultureInfo(int)
            // with a 0 culture id on the skeleton session and throw ArgumentOutOfRangeException.
            // Return InvariantCulture so format/parse paths work headlessly.
            foreach (var propName in new[] { "Culture", "WindowsCulture" })
            {
                var getter = sessType.GetProperty(propName,
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)?.GetGetMethod(true);
                if (getter != null) Hook(getter, nameof(NavSession_get_Culture), $"NavSession.get_{propName}");
            }

            // NavSession.Company getter — NavRecord.GetCompanyNameToken reads Session.Company.CompanyNameToken.
            // Build skeleton NavCompany and inject into both the property and the backing field.
            var navCompanyType = navNcl.GetType("Microsoft.Dynamics.Nav.Runtime.NavCompany");
            if (navCompanyType != null)
            {
                var skelCompany = RuntimeHelpers.GetUninitializedObject(navCompanyType);
                var cnTokenField = navCompanyType.GetField("companyNameToken",
                    BindingFlags.NonPublic | BindingFlags.Instance);
                cnTokenField?.SetValue(skelCompany, 0);
                _skeletonCompany = skelCompany;
                var companyField = sessType.GetField("company", BindingFlags.NonPublic | BindingFlags.Instance);
                if (companyField != null)
                    FieldPoke.SetInstance(companyField, _skeletonSession!, skelCompany);
            }
            HookProperty(sessType, "Company", false, nameof(GetSkeletonCompanyReplacement));

            // EventBindings — initialized via field initializer
            // (`new List<NavCodeunit>(128)`) on the real ctor, but skeleton session was
            // built via GetUninitializedObject so the backing field is null. Without this
            // init, NavCodeunit.BindSubscription NREs on `Session.EventBindings.Add(this)`.
            var navCuType = navNcl.GetType("Microsoft.Dynamics.Nav.Runtime.NavCodeunit");
            var ebBackingField = sessType.GetField("<EventBindings>k__BackingField",
                BindingFlags.NonPublic | BindingFlags.Instance);
            if (ebBackingField != null && navCuType != null)
            {
                var listType = typeof(List<>).MakeGenericType(navCuType);
                var listInstance = Activator.CreateInstance(listType, 128);
                FieldPoke.SetInstance(ebBackingField, _skeletonSession!, listInstance);
            }

            // OverriddenAppGroup = NavAppGroup.BaseGroup so NavCurrentThread.TryResolveAppGroup
            // returns BaseGroup instead of dereferencing the uninitialized tenant.NavAppGroup.
            var navAppGroupType = navNcl.GetType("Microsoft.Dynamics.Nav.Runtime.Apps.NavAppGroup");
            if (navAppGroupType != null)
            {
                var baseGroupField = navAppGroupType.GetField("BaseGroup",
                    BindingFlags.Public | BindingFlags.Static);
                var baseGroup = baseGroupField?.GetValue(null);
                if (baseGroup != null)
                {
                    var overriddenField = sessType.GetField("<OverriddenAppGroup>k__BackingField",
                        BindingFlags.NonPublic | BindingFlags.Instance);
                    overriddenField?.SetValue(_skeletonSession, baseGroup);
                }
            }

            // cachedEnvironmentDefaultLcid — `private readonly LazyEx<int>` on NavSession
            // initialized via field initializer to `new LazyEx<int>(() => NavEnvironment.DefaultLanguage)`.
            // GetUninitializedObject skips field initializers → field is null on skeleton.
            // NCLCaptionStrings.GetValueOrDefault(int, NavSession) reads
            //   session.CachedEnvironmentDefaultLanguage  ⇒  cachedEnvironmentDefaultLcid.Value
            // and NREs. Construct a LazyEx<int> that returns NavEnvironment.DefaultLanguage
            // and plant it on the skeleton session so every caller (intra-NCL R2R included)
            // sees a non-null Lazy. Decompile pin: NCL @ 206393, 207228, 146556.
            var cachedLcidField = sessType.GetField("cachedEnvironmentDefaultLcid",
                BindingFlags.NonPublic | BindingFlags.Instance);
            if (cachedLcidField != null && cachedLcidField.GetValue(_skeletonSession) == null)
            {
                try
                {
                    var lazyExType = cachedLcidField.FieldType;          // LazyEx<int>
                    var funcOfInt = typeof(Func<>).MakeGenericType(typeof(int));
                    var lazyExCtor = lazyExType.GetConstructor(new[] { funcOfInt });
                    if (lazyExCtor != null)
                    {
                        // Resolve NavEnvironment.DefaultLanguage at call time.
                        var navEnvType = navNcl.GetType("Microsoft.Dynamics.Nav.Runtime.NavEnvironment");
                        var defaultLangProp = navEnvType?.GetProperty("DefaultLanguage",
                            BindingFlags.Public | BindingFlags.Static);
                        Func<int> producer = () =>
                        {
                            try
                            {
                                var v = defaultLangProp?.GetValue(null);
                                return v is int i && i > 0 ? i : 1033; // en-US fallback
                            }
                            catch { return 1033; }
                        };
                        // Convert Func<int> producer to the right Func type via Delegate.CreateDelegate?
                        // Simplest: invoke via a lambda using reflection-bound method.
                        var producerDel = Delegate.CreateDelegate(funcOfInt, producer.Target!,
                            producer.Method);
                        var lazyExInstance = lazyExCtor.Invoke(new object[] { producerDel });
                        FieldPoke.SetInstance(cachedLcidField, _skeletonSession!, lazyExInstance);
                    }
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"[BcRuntime] WARN: cachedEnvironmentDefaultLcid populate failed: {ex.GetType().Name}: {ex.Message}");
                }
            }

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
        // NavComplexValue (parent of NavApplicationObjectBase) has its OWN tree field distinct from TreeObject.tree.
        var navComplexValueType = navNcl.GetType("Microsoft.Dynamics.Nav.Runtime.NavComplexValue");
        if (navComplexValueType != null)
            _fNavComplexValueTree = navComplexValueType.GetField("tree", BindingFlags.NonPublic | BindingFlags.Instance);
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

            // Skeleton NavSession was built with GetUninitializedObject → its inherited
            // TreeObject.tree field is null. Several BC code paths construct TreeHandlers with
            // NavCurrentThread.Session as parent, e.g.:
            //   • NavSession.get_TestExecution → new NavTestExecution(this)
            //   • NavCodeunit.RunCodeunit / ContainsMethodWithAttribute → new NavCodeunitHandle(NavCurrentThread.Session, id)
            //   • ALCompiler.NavIndirectValueToNavValue → new NavScope/NavValue subclasses parented to the session
            // Each of these reaches `TreeHandler..ctor(parent, host)` which throws
            // InvalidOperationException("Parent.Tree cannot be null") when parent.Tree is null.
            // Plant a TreeRoot on the skeleton session so it has a valid Tree the same way the
            // RootMethodScope above does. CreateTreeRoot calls hostObject.Tree (must be null —
            // it is) and hostObject.SingleThreaded (DIM default = false on ITreeObject).
            if (_skeletonSession != null && _fTreeObjTree != null)
            {
                if (_fTreeObjTree.GetValue(_skeletonSession) == null)
                {
                    var sessRootTree = createRoot.Invoke(null, new object[] { _skeletonSession });
                    FieldPoke.SetInstance(_fTreeObjTree, _skeletonSession, sessRootTree);
                }
            }
        }

        // After the real NavEnvironment ctor + skeleton root scope are both ready,
        // inject a skeleton NavSystemTenant + NCLMetadata into the real Tenants collection.
        // No-op if env ctor fell back to skeleton (Tenants is null).
        InjectSkeletonSystemTenant(navNcl);

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
                    Hook(ctor3, nameof(NavMethodScopeCtorReplacement), "NavMethodScope..ctor(NavApplicationObjectBase,MethodScopeFlags,bool)");
                }
                else
                    Console.Error.WriteLine("[BcRuntime] WARNING: 3-arg NavMethodScope ctor NOT FOUND");
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

            // NavMethodScope.AssertError(Action body) — original calls session.Rollback() on the
            // catch path which NREs on the skeleton session. Replace with a body that just runs
            // the action and inverts pass/fail semantics for asserterror tests.
            var assertError = msType.GetMethod("AssertError",
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance,
                null, new[] { typeof(Action) }, null);
            if (assertError != null)
                Hook(assertError, nameof(NavMethodScope_AssertError), "NavMethodScope.AssertError");
        }

        // TreeHandler.get_Session — the tree's session field is null (root has no session propagated).
        // Return _skeletonSession so NavApplicationObjectBase.ctor and NavRecord.ctor can find a session.
        if (treeHandlerType != null)
        {
            var treeSessionProp = treeHandlerType.GetProperty("Session",
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            if (treeSessionProp?.GetGetMethod(true) != null)
                Hook(treeSessionProp.GetGetMethod(true)!, nameof(TreeHandler_get_Session), "TreeHandler.get_Session");
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
                Hook(createById, nameof(NCLEnumMetadata_CreateByIdAlAware), "NCLEnumMetadata.Create(int)");
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

        // NavCodeunit.get_MetaCodeunit — real getter chains through Session.NCLMetadata
        // .GetMetaCodeunitById(...) which NREs on the skeleton. The only sync caller in
        // the failing tests is BindSubscription → MetaCodeunit.IsEventManualBinding,
        // which only needs ApplicationObjectClrType (read off attributes on the
        // AL-emitted Codeunit{N} class). Replace the getter with a lazy-build that
        // pre-populates a skeleton NCLMetaCodeunit with the receiver's CLR type and
        // caches on the instance.metaCodeunit field (HANDOFF §5.2 Option C).
        var navCodeunitTypeForMeta = navNcl.GetType("Microsoft.Dynamics.Nav.Runtime.NavCodeunit");
        if (navCodeunitTypeForMeta != null)
        {
            var metaCuGetter = navCodeunitTypeForMeta.GetProperty("MetaCodeunit",
                BindingFlags.Public | BindingFlags.Instance)?.GetGetMethod(true);
            if (metaCuGetter != null)
                Hook(metaCuGetter, nameof(NavCodeunit_get_MetaCodeunit), "NavCodeunit.get_MetaCodeunit");
        }

        // NCLMetaCodeunit.get_IsEventManualBinding — bypass LoadOptionsFromAttributeOrInstance,
        // which dereferences base.ApplicationObjectClrType. That getter has a hooked
        // replacement (NCLMetaApplicationObject_get_ApplicationObjectClrType), but the call
        // site inside NCL.dll's own LoadOptionsFromAttributeOrInstance is R2R-precompiled
        // and bypasses the JmpHook. Hook IsEventManualBinding directly to read the
        // NavCodeunitOptionsAttribute from the stashed CLR type (companion to
        // NavCodeunit_get_MetaCodeunit above).
        var nclMetaCuType = navNcl.GetType("Microsoft.Dynamics.Nav.Runtime.NCLMetaCodeunit");
        if (nclMetaCuType != null)
        {
            var isManualGetter = nclMetaCuType.GetProperty("IsEventManualBinding",
                BindingFlags.Public | BindingFlags.Instance)?.GetGetMethod(true);
            if (isManualGetter != null)
                Hook(isManualGetter, nameof(NCLMetaCodeunit_get_IsEventManualBinding),
                    "NCLMetaCodeunit.get_IsEventManualBinding");
        }

        // NavDataTransfer.SetTables — uses NCLMetadata.GetMetaTableById to validate source/dest
        // tables before staging the transfer. Validation is meaningless in headless mode (the
        // actual data move happens via patched RecordImpl). No-op so AL DataTransfer.SetTables
        // calls succeed and downstream Add{Constant,Field,Source}Value can proceed against
        // skeleton-managed buffers.
        var navDataTransferType = navNcl.GetType("Microsoft.Dynamics.Nav.Runtime.NavDataTransfer");
        if (navDataTransferType != null)
        {
            var setTables = navDataTransferType.GetMethod("SetTables",
                BindingFlags.NonPublic | BindingFlags.Instance, null,
                new[] { typeof(int), typeof(int) }, null);
            if (setTables != null)
                Hook(setTables, nameof(NoOp3), "NavDataTransfer.SetTables");
        }

        // ALTaskScheduler.CheckCodeUnit — calls NCLMetadata.GetMetaCodeunitById to verify the
        // codeunit exists. We resolve codeunits via assembly-scan in CreateTarget; the metadata
        // verification is redundant. No-op so ALCreateTaskAsync proceeds.
        var alTaskSchedType = navNcl.GetType("Microsoft.Dynamics.Nav.Runtime.ALTaskScheduler");
        if (alTaskSchedType != null && sessType != null)
        {
            var checkCu = alTaskSchedType.GetMethod("CheckCodeUnit",
                BindingFlags.NonPublic | BindingFlags.Static, null,
                new[] { sessType, typeof(int) }, null);
            if (checkCu != null)
                Hook(checkCu, nameof(NoOp2), "ALTaskScheduler.CheckCodeUnit");
        }

        // ALMethodScope.AssignScopeId — chains through Session.NCLMetadata which is null;
        // no-op leaves scopeId = null which is tolerated by the ScopeId getter.
        var alMethodScopeType = navNcl.GetType("Microsoft.Dynamics.Nav.Runtime.ALMethodScope");
        if (alMethodScopeType != null)
        {
            var assignScopeId = alMethodScopeType.GetMethod("AssignScopeId",
                BindingFlags.NonPublic | BindingFlags.Instance, null, Type.EmptyTypes, null);
            if (assignScopeId != null)
                Hook(assignScopeId, nameof(ALMethodScope_AssignScopeId), "ALMethodScope.AssignScopeId");
        }

        // ALSystemErrorHandling.get_AL{GetLastErrorText,GetLastErrorCode,GetLastErrorCallStack}
        // and ALClearLastError — real getters chain through NavCurrentThread.Session which is
        // null on the skeleton thread. Hook to read/clear via the skeleton session directly.
        var alSysErrType = navNcl.GetType("Microsoft.Dynamics.Nav.Runtime.ALSystemErrorHandling");
        if (alSysErrType != null)
        {
            void HookAlErrProp(string propName, string replName, string desc)
            {
                var p = alSysErrType.GetProperty(propName,
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
                var g = p?.GetGetMethod(true);
                if (g != null) Hook(g, replName, desc);
            }
            HookAlErrProp("ALGetLastErrorText",     nameof(ALSystemErrorHandling_get_ALGetLastErrorText),     "ALSystemErrorHandling.get_ALGetLastErrorText");
            HookAlErrProp("ALGetLastErrorCode",     nameof(ALSystemErrorHandling_get_ALGetLastErrorCode),     "ALSystemErrorHandling.get_ALGetLastErrorCode");
            HookAlErrProp("ALGetLastErrorCallStack",nameof(ALSystemErrorHandling_get_ALGetLastErrorCallStack),"ALSystemErrorHandling.get_ALGetLastErrorCallStack");
            var clearMethod = alSysErrType.GetMethod("ALClearLastError",
                BindingFlags.Public | BindingFlags.Static, null, Type.EmptyTypes, null);
            if (clearMethod != null)
                Hook(clearMethod, nameof(ALSystemErrorHandling_ALClearLastError), "ALSystemErrorHandling.ALClearLastError");
        }

        // NavIntegerFormatter.FormatWithFormatNumber — value passed via NavValue[] varargs
        // is sometimes null on the skeleton runtime; real body NREs on value.ToInt32().
        var navIntFmtType = navNcl.GetType("Microsoft.Dynamics.Nav.Runtime.NavIntegerFormatter");
        if (navIntFmtType != null)
        {
            var fmtMethod = navIntFmtType.GetMethod("FormatWithFormatNumber",
                BindingFlags.NonPublic | BindingFlags.Instance);
            if (fmtMethod != null)
                Hook(fmtMethod, nameof(NavIntegerFormatter_FormatWithFormatNumber),
                    "NavIntegerFormatter.FormatWithFormatNumber");
        }

        // NavTestPageHandle.CreateTarget — same shape as NavCodeunitHandle: bypass the
        // NCLMetadata lookup and construct TestPage{ID} from the loaded test assembly.
        var testPageHandleType = navNcl.GetType("Microsoft.Dynamics.Nav.Runtime.NavTestPageHandle");
        if (testPageHandleType != null)
        {
            var createTarget = testPageHandleType.GetMethod("CreateTarget",
                BindingFlags.NonPublic | BindingFlags.Instance);
            if (createTarget != null)
                Hook(createTarget, nameof(NavTestPageHandle_CreateTarget), "NavTestPageHandle.CreateTarget");
        }

        // NavFormHandle.CreateTarget — same shape as NavCodeunitHandle: bypass the
        // NCLMetadata.GetMetaFormById lookup and construct Form{ID} from loaded assemblies.
        var formHandleType = navNcl.GetType("Microsoft.Dynamics.Nav.Runtime.NavFormHandle");
        if (formHandleType != null)
        {
            var createTarget = formHandleType.GetMethod("CreateTarget",
                BindingFlags.NonPublic | BindingFlags.Instance);
            if (createTarget != null)
                Hook(createTarget, nameof(NavFormHandle_CreateTarget), "NavFormHandle.CreateTarget");
        }

        // NavReportHandle.CreateTarget — §P, same shape as NavFormHandle. The default
        // implementation `NCLMetadata.GetMetaReportById(id, true).CreateObjectInstance(this)`
        // works after the §P cache populator (so the GetMetaReportById no longer NREs),
        // but CreateObjectInstance then dereferences a null ApplicationObjectConstructor
        // delegate (skeleton metas have no NCLMetaObjectCLRTypeContainer). Replace with
        // a direct construction of `Report{ID}` from the test assembly.
        var reportHandleType = navNcl.GetType("Microsoft.Dynamics.Nav.Runtime.NavReportHandle");
        if (reportHandleType != null)
        {
            var createTarget = reportHandleType.GetMethod("CreateTarget",
                BindingFlags.NonPublic | BindingFlags.Instance);
            if (createTarget != null)
                Hook(createTarget, nameof(NavReportHandle_CreateTarget), "NavReportHandle.CreateTarget");
        }

        // NavXmlPortHandle.CreateTarget — same pattern as NavFormHandle/NavReportHandle.
        // GetMetaXmlPortById throws ThrowMetaApplicationObjectNotFound for any XmlPort not
        // compiled by NCLCodeLoader; our cache has skeleton entries but CreateObjectInstance
        // NREs on the null delegate. Hook to construct XmlPort{ID} directly from the assembly.
        var xmlPortHandleType = navNcl.GetType("Microsoft.Dynamics.Nav.Runtime.NavXmlPortHandle");
        if (xmlPortHandleType != null)
        {
            var createTarget = xmlPortHandleType.GetMethod("CreateTarget",
                BindingFlags.NonPublic | BindingFlags.Instance);
            if (createTarget != null)
                Hook(createTarget, nameof(NavXmlPortHandle_CreateTarget), "NavXmlPortHandle.CreateTarget");
        }

        // NavXmlPort instance methods — Export/Import/Run all call Session.BeginTransaction
        // or ApplicationObjectRootScope which NRE on the skeleton; SetTableView iterates
        // empty nodes then throws NavNCLXmlPortNodeNotFoundException. Return no-op stubs.
        var navXmlPortType = navNcl.GetType("Microsoft.Dynamics.Nav.Runtime.NavXmlPort");
        if (navXmlPortType != null)
        {
            var tDataError = navNcl.GetType("Microsoft.Dynamics.Nav.Types.DataError")
                ?? AppDomain.CurrentDomain.GetAssemblies()
                    .SelectMany(a => { try { return a.GetTypes(); } catch { return Array.Empty<Type>(); } })
                    .FirstOrDefault(t => t.Name == "DataError");
            var xmlPortNavRecordType = navNcl.GetType("Microsoft.Dynamics.Nav.Runtime.NavRecord");

            if (tDataError != null)
            {
                var exportMethod = navXmlPortType.GetMethod("Export",
                    BindingFlags.Public | BindingFlags.Instance, null, new[] { tDataError }, null);
                if (exportMethod != null)
                    Hook(exportMethod, nameof(NavXmlPort_Export), "NavXmlPort.Export(DataError)");

                var importMethod = navXmlPortType.GetMethod("Import",
                    BindingFlags.Public | BindingFlags.Instance, null, new[] { tDataError }, null);
                if (importMethod != null)
                    Hook(importMethod, nameof(NavXmlPort_Import), "NavXmlPort.Import(DataError)");

                // Static XMLPORT.EXPORT(id, stream) / XMLPORT.IMPORT(id, stream) overloads.
                var tNavOutStream = navNcl.GetType("Microsoft.Dynamics.Nav.Runtime.NavOutStream");
                var tNavInStream  = navNcl.GetType("Microsoft.Dynamics.Nav.Runtime.NavInStream");
                var tNavRecord    = navNcl.GetType("Microsoft.Dynamics.Nav.Runtime.NavRecord");
                if (tNavOutStream != null && tNavRecord != null)
                {
                    var staticExport = navXmlPortType.GetMethod("Export",
                        BindingFlags.Public | BindingFlags.Static, null,
                        new[] { tDataError, typeof(int), tNavOutStream, tNavRecord }, null);
                    if (staticExport != null)
                        Hook(staticExport, nameof(NavXmlPort_StaticExport), "NavXmlPort.Export(DataError,int,NavOutStream,NavRecord)");
                }
                if (tNavInStream != null && tNavRecord != null)
                {
                    var staticImport = navXmlPortType.GetMethod("Import",
                        BindingFlags.Public | BindingFlags.Static, null,
                        new[] { tDataError, typeof(int), tNavInStream, tNavRecord }, null);
                    if (staticImport != null)
                        Hook(staticImport, nameof(NavXmlPort_StaticImport), "NavXmlPort.Import(DataError,int,NavInStream,NavRecord)");
                }
            }

            var runMethod = navXmlPortType.GetMethod("Run",
                BindingFlags.Public | BindingFlags.Instance, null, Type.EmptyTypes, null);
            if (runMethod != null)
                Hook(runMethod, nameof(NavXmlPort_Run), "NavXmlPort.Run()");

            // RunXmlPort() (private) is the actual execution body. The BC-generated code for
            // `XP.Run()` on a local XmlPort variable goes through ApplicationObjectRootScope
            // which calls RunXmlPort() directly, bypassing the public Run() hook above.
            var runXmlPortMethod = navXmlPortType.GetMethod("RunXmlPort",
                BindingFlags.NonPublic | BindingFlags.Instance, null, Type.EmptyTypes, null);
            if (runXmlPortMethod != null)
                Hook(runXmlPortMethod, nameof(NavXmlPort_RunXmlPort), "NavXmlPort.RunXmlPort()");

            if (xmlPortNavRecordType != null)
            {
                var setTableViewMethod = navXmlPortType.GetMethod("SetTableView",
                    BindingFlags.Public | BindingFlags.Instance, null, new[] { xmlPortNavRecordType }, null);
                if (setTableViewMethod != null)
                    Hook(setTableViewMethod, nameof(NavXmlPort_SetTableView), "NavXmlPort.SetTableView(NavRecord)");
            }

            // BeginInitialization/EndInitialization — called from the BC-generated XmlPort{ID}
            // constructor. BeginInitialization dereferences Session.MetadataProvider (null on
            // skeleton) → NRE. EndInitialization uses metadata/requestOptionsPage (null when
            // BeginInit is a no-op). Both must be stubbed to let the constructor complete safely.
            var beginInitMethod = navXmlPortType.GetMethod("BeginInitialization",
                BindingFlags.NonPublic | BindingFlags.Instance, null, Type.EmptyTypes, null);
            if (beginInitMethod != null)
                Hook(beginInitMethod, nameof(NavXmlPort_BeginInitialization), "NavXmlPort.BeginInitialization()");

            var endInitMethod = navXmlPortType.GetMethod("EndInitialization",
                BindingFlags.NonPublic | BindingFlags.Instance, null, Type.EmptyTypes, null);
            if (endInitMethod != null)
                Hook(endInitMethod, nameof(NavXmlPort_EndInitialization), "NavXmlPort.EndInitialization()");

            // Add(NavXmlPortTableNode/FieldNode/TextNode) — all three overloads access
            // metadata.Nodes[nodes.Count] which is null after BeginInitialization is no-op'd.
            // Hook to no-op; the node list is not needed for our Export/Import/Run stubs.
            var nclAssembly = navXmlPortType.Assembly;
            var tableNodeType = nclAssembly.GetType("Microsoft.Dynamics.Nav.Runtime.NavXmlPortTableNode");
            var fieldNodeType = nclAssembly.GetType("Microsoft.Dynamics.Nav.Runtime.NavXmlPortFieldNode");
            var textNodeType  = nclAssembly.GetType("Microsoft.Dynamics.Nav.Runtime.NavXmlPortTextNode");
            if (tableNodeType != null)
            {
                var addTable = navXmlPortType.GetMethod("Add",
                    BindingFlags.NonPublic | BindingFlags.Instance, null, new[] { tableNodeType }, null);
                if (addTable != null)
                    Hook(addTable, nameof(NavXmlPort_AddTableNode), "NavXmlPort.Add(NavXmlPortTableNode)");
            }
            if (fieldNodeType != null)
            {
                var addField = navXmlPortType.GetMethod("Add",
                    BindingFlags.NonPublic | BindingFlags.Instance, null, new[] { fieldNodeType }, null);
                if (addField != null)
                    Hook(addField, nameof(NavXmlPort_AddFieldNode), "NavXmlPort.Add(NavXmlPortFieldNode)");
            }
            if (textNodeType != null)
            {
                var addText = navXmlPortType.GetMethod("Add",
                    BindingFlags.NonPublic | BindingFlags.Instance, null, new[] { textNodeType }, null);
                if (addText != null)
                    Hook(addText, nameof(NavXmlPort_AddTextNode), "NavXmlPort.Add(NavXmlPortTextNode)");
            }

            // NavXmlPortTableNode(NavRecordHandle) constructor — called from the generated
            // XmlPort{ID}.InitializeComponent() for each tableelement. Calls record.Target which
            // triggers NavRecordHandle.CreateTarget → NCLMetaTable.CreateObjectInstance → the
            // generated Table{ID} ctor → record initialization that NREs before reaching Add().
            // Since Add is already a no-op and we never use the node list, stub ctor as no-op.
            if (tableNodeType != null)
            {
                var xmlPortHandleNavRecordType = nclAssembly.GetType("Microsoft.Dynamics.Nav.Runtime.NavRecordHandle");
                if (xmlPortHandleNavRecordType != null)
                {
                    var tableNodeCtor = tableNodeType.GetConstructor(
                        BindingFlags.Public | BindingFlags.Instance, null, new[] { xmlPortHandleNavRecordType }, null);
                    if (tableNodeCtor != null)
                        Hook(tableNodeCtor, nameof(NavXmlPortTableNode_Ctor), "NavXmlPortTableNode.ctor(NavRecordHandle)");
                }
            }
        }

        // NCLMetaField.get_FieldCaption — sync underbelly of NavRecord.ALFieldCaptionAsync.
        // Original chains through NavCurrentThread.ResolveAppGroup(Session) →
        // MetaField.GetMergedCaptionMultiLanguage → LanguageProvider/ServerUserSettings,
        // none of which the skeleton runtime initializes. Replace with FieldName, which is
        // what the original returns under FieldIsNotFromMetadata. Lights up the
        // Rec.TestField → ALFieldCaptionAsync error-formatting cascade without hooking the
        // async surface (HANDOFF §5.2 Option C).
        var nclMetaFieldType = navNcl.GetType("Microsoft.Dynamics.Nav.Runtime.NCLMetaField");
        if (nclMetaFieldType != null)
        {
            var fieldCaptionGetter = nclMetaFieldType.GetProperty("FieldCaption",
                BindingFlags.Public | BindingFlags.Instance)?.GetGetMethod(true);
            if (fieldCaptionGetter != null)
            {
                var repl = typeof(AlRunnerV2.Patches.RecordPatches).GetMethod(
                    nameof(AlRunnerV2.Patches.RecordPatches.NCLMetaField_get_FieldCaption),
                    BindingFlags.Public | BindingFlags.Static)!;
                Hook(fieldCaptionGetter, repl, "NCLMetaField.get_FieldCaption");
            }
        }

        // NavTextConstant.get_Value — every AL Label is emitted as a NavTextConstant. The
        // implicit NavStringValue→string conversion (used by `new NavText(constant)`) reads
        // Value, which dereferences NavCurrentThread.Session → NRE on skeleton thread. Replace
        // with a session-free lookup of the first ENU entry. Lights up Assert codeunit's
        // `LastErrorCode.Contains(testFieldValidationCodeTxt)` and friends (HANDOFF §5.2 Option C).
        var navTextConstantType = navNcl.GetType("Microsoft.Dynamics.Nav.Runtime.NavTextConstant");
        if (navTextConstantType != null)
        {
            var valueGetter = navTextConstantType.GetProperty("Value",
                BindingFlags.Public | BindingFlags.Instance)?.GetGetMethod(true);
            if (valueGetter != null)
            {
                var repl = typeof(AlRunnerV2.Patches.RecordPatches).GetMethod(
                    nameof(AlRunnerV2.Patches.RecordPatches.NavTextConstant_get_Value),
                    BindingFlags.Public | BindingFlags.Static)!;
                Hook(valueGetter, repl, "NavTextConstant.get_Value");
            }
        }
        // Also hook NavStringValue.op_Implicit(NavStringValue → string). The C# compiler
        // emits this for every `(string)stringValue` cast, including `new NavText(constant)`.
        // The original is `value?.Value` — a virtual call that JIT may devirtualize+inline,
        // bypassing the get_Value hook above. Patch the static op directly so the dispatch
        // is unconditional regardless of JIT inlining decisions.
        var navStringValueType_forOp = navNcl.GetType("Microsoft.Dynamics.Nav.Runtime.NavStringValue");
        if (navStringValueType_forOp != null)
        {
            var opImplicit = navStringValueType_forOp.GetMethod("op_Implicit",
                BindingFlags.Public | BindingFlags.Static, null,
                new[] { navStringValueType_forOp }, null);
            if (opImplicit != null)
            {
                var repl = typeof(AlRunnerV2.Patches.RecordPatches).GetMethod(
                    nameof(AlRunnerV2.Patches.RecordPatches.NavStringValue_op_Implicit),
                    BindingFlags.Public | BindingFlags.Static)!;
                Hook(opImplicit, repl, "NavStringValue.op_Implicit");
            }
        }

        // NavRecord.TestFieldNotBlank / TestFieldError — sync throw paths of Rec.TestField.
        // Real bodies dereference Session.WindowsCulture, Session.Diagnostics (via
        // TryAddTestFieldAction), Session.Permissions, NavGlobal.NCLMetadata — all null on
        // skeleton runtime. The throw path raises NRE which surfaces as "NullReference"
        // error code and breaks Assert.ExpectedTestFieldError's code-match check.
        // Replace with clean NavTestFieldException factory calls (HANDOFF §5.2 Option C).
        var navRecordType = navNcl.GetType("Microsoft.Dynamics.Nav.Runtime.NavRecord");
        if (navRecordType != null)
        {
            var nclMetaFieldT = navNcl.GetType("Microsoft.Dynamics.Nav.Runtime.NCLMetaField");
            var navAlErrorInfoT = navNcl.GetType("Microsoft.Dynamics.Nav.Runtime.NavALErrorInfo");
            if (nclMetaFieldT != null && navAlErrorInfoT != null)
            {
                var testFieldNotBlank = navRecordType.GetMethod("TestFieldNotBlank",
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance,
                    null, new[] { nclMetaFieldT, navAlErrorInfoT }, null);
                if (testFieldNotBlank != null)
                {
                    var repl = typeof(AlRunnerV2.Patches.RecordPatches).GetMethod(
                        nameof(AlRunnerV2.Patches.RecordPatches.NavRecord_TestFieldNotBlank),
                        BindingFlags.Public | BindingFlags.Static)!;
                    Hook(testFieldNotBlank, repl, "NavRecord.TestFieldNotBlank");
                }
                var testFieldError = navRecordType.GetMethod("TestFieldError",
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance,
                    null, new[] { nclMetaFieldT, typeof(string), navAlErrorInfoT }, null);
                if (testFieldError != null)
                {
                    var repl = typeof(AlRunnerV2.Patches.RecordPatches).GetMethod(
                        nameof(AlRunnerV2.Patches.RecordPatches.NavRecord_TestFieldError),
                        BindingFlags.Public | BindingFlags.Static)!;
                    Hook(testFieldError, repl, "NavRecord.TestFieldError");
                }
            }
        }

        // NavRecordRef.get_Target — bypass NRE on Session.Company.SharedObjects by
        // constructing a SharedRecordRef parented to a process-wide skeleton container.
        var navRecordRefType = navNcl.GetType("Microsoft.Dynamics.Nav.Runtime.NavRecordRef");
        if (navRecordRefType != null)
        {
            var targetGetter = navRecordRefType.GetProperty("Target",
                BindingFlags.NonPublic | BindingFlags.Instance)?.GetGetMethod(true);
            if (targetGetter != null)
                Hook(targetGetter, nameof(NavRecordRef_get_Target), "NavRecordRef.get_Target");
        }

        // NavStringValue.CompareTo(NavStringValue) — real impl uses NavCurrentThread.Session.Culture
        // (null on skeleton). Replace with ordinal Value comparison.
        var navStringValueType = navNcl.GetType("Microsoft.Dynamics.Nav.Runtime.NavStringValue");
        if (navStringValueType != null)
        {
            var compareTo = navStringValueType.GetMethod("CompareTo",
                BindingFlags.Public | BindingFlags.Instance, null, new[] { navStringValueType }, null);
            if (compareTo != null)
                Hook(compareTo, nameof(NavStringValue_CompareTo),
                    "NavStringValue.CompareTo(NavStringValue)");
        }

        // NavHttpRequestMessage.get_Target — same shape as NavRecordRef. Construct
        // SharedNavHttpRequestMessage parented to skeleton container.
        var navHttpReqType = navNcl.GetType("Microsoft.Dynamics.Nav.Runtime.NavHttpRequestMessage");
        if (navHttpReqType != null)
        {
            var targetGetter = navHttpReqType.GetProperty("Target",
                BindingFlags.NonPublic | BindingFlags.Instance)?.GetGetMethod(true);
            if (targetGetter != null)
                Hook(targetGetter, nameof(NavHttpRequestMessage_get_Target),
                    "NavHttpRequestMessage.get_Target");
        }

        // NavHttpResponseMessageBase.get_Target — same shape. Construct SharedNavHttpResponseMessage
        // parented to skeleton container. Ctor is safe (no HTTP infrastructure call).
        var navHttpRespType = navNcl.GetType("Microsoft.Dynamics.Nav.Runtime.NavHttpResponseMessageBase");
        if (navHttpRespType != null)
        {
            var targetGetter = navHttpRespType.GetProperty("Target",
                BindingFlags.NonPublic | BindingFlags.Instance)?.GetGetMethod(true);
            if (targetGetter != null)
                Hook(targetGetter, nameof(NavHttpResponseMessageBase_get_Target),
                    "NavHttpResponseMessageBase.get_Target");
        }

        // NavHttpClient.get_Target — same Option-C shape. SharedNavHttpClient(ITreeSharedObjectContainer)
        // is safe (no CreateClient/HTTP infrastructure in that ctor).
        var navHttpClientType = navNcl.GetType("Microsoft.Dynamics.Nav.Runtime.NavHttpClient");
        if (navHttpClientType != null)
        {
            var targetGetter = navHttpClientType.GetProperty("Target",
                BindingFlags.NonPublic | BindingFlags.Instance)?.GetGetMethod(true);
            if (targetGetter != null)
                Hook(targetGetter, nameof(NavHttpClient_get_Target), "NavHttpClient.get_Target");
        }

        // NavStream.get_Target — same shape as NavRecordRef. Construct SharedNavStream
        // parented to skeleton container. Fixes NRE in NavStream ctor and all call sites
        // that access Position, SharedStream, etc. on a freshly-created InStream/OutStream.
        var navStreamType = navNcl.GetType("Microsoft.Dynamics.Nav.Runtime.NavStream");
        if (navStreamType != null)
        {
            var targetGetter = navStreamType.GetProperty("Target",
                BindingFlags.NonPublic | BindingFlags.Instance)?.GetGetMethod(true);
            if (targetGetter != null)
                Hook(targetGetter, nameof(NavStream_get_Target), "NavStream.get_Target");
        }

        // NavSession.GetPermissionSet — skeleton has no Permissions object; NRE inside.
        // Return the NCL-internal VirtualDataProvider.PermissionSet singleton (HasPermissions=true,
        // VerifyPermissions=no-op). Hook both 3-arg overloads (single + IEnumerable).
        {
            var sessType2 = navNcl.GetType("Microsoft.Dynamics.Nav.Runtime.NavSession");
            var typesAsm2 = AppDomain.CurrentDomain.GetAssemblies()
                .FirstOrDefault(a => a.GetName().Name == "Microsoft.Dynamics.Nav.Types");
            var appObjIdType = typesAsm2?.GetType("Microsoft.Dynamics.Nav.Types.ApplicationObjectId");
            var navAppObjBaseT = navNcl.GetType("Microsoft.Dynamics.Nav.Runtime.NavApplicationObjectBase");
            if (sessType2 != null && appObjIdType != null && navAppObjBaseT != null)
            {
                var iEnumType = typeof(IEnumerable<>).MakeGenericType(appObjIdType);
                var mSingle = sessType2.GetMethod("GetPermissionSet",
                    BindingFlags.Public | BindingFlags.Instance, null,
                    new[] { navAppObjBaseT, typeof(int), appObjIdType }, null);
                if (mSingle != null)
                    Hook(mSingle,
                        typeof(BcRuntime).GetMethod(nameof(NavSession_GetPermissionSet_ByObjectId),
                            BindingFlags.Public | BindingFlags.Static)!,
                        "NavSession.GetPermissionSet(…,ApplicationObjectId)");

                var mMulti = sessType2.GetMethod("GetPermissionSet",
                    BindingFlags.Public | BindingFlags.Instance, null,
                    new[] { navAppObjBaseT, typeof(int), iEnumType }, null);
                if (mMulti != null)
                    Hook(mMulti,
                        typeof(BcRuntime).GetMethod(nameof(NavSession_GetPermissionSet_ByObjectIds),
                            BindingFlags.Public | BindingFlags.Static)!,
                        "NavSession.GetPermissionSet(…,IEnumerable<ApplicationObjectId>)");
            }
        }

        // ALSystemNumeric.ALRandomize/ALRandom — real impls reach NavCurrentThread.Session.Random
        // (null on skeleton). Back with a process-static Random.
        var alSysNumType = navNcl.GetType("Microsoft.Dynamics.Nav.Runtime.ALSystemNumeric");
        if (alSysNumType != null)
        {
            var randomizeNoArg = alSysNumType.GetMethod("ALRandomize",
                BindingFlags.Public | BindingFlags.Static, null, Type.EmptyTypes, null);
            if (randomizeNoArg != null)
                Hook(randomizeNoArg, nameof(ALSystemNumeric_ALRandomize), "ALSystemNumeric.ALRandomize()");
            var randomizeSeed = alSysNumType.GetMethod("ALRandomize",
                BindingFlags.Public | BindingFlags.Static, null, new[] { typeof(int) }, null);
            if (randomizeSeed != null)
                Hook(randomizeSeed, nameof(ALSystemNumeric_ALRandomize_Seed), "ALSystemNumeric.ALRandomize(int)");
            var alRandom = alSysNumType.GetMethod("ALRandom",
                BindingFlags.Public | BindingFlags.Static, null, new[] { typeof(int) }, null);
            if (alRandom != null)
                Hook(alRandom, nameof(ALSystemNumeric_ALRandom), "ALSystemNumeric.ALRandom(int)");
        }

        // NavDialog.ALOpen — UI dialog open NREs reaching Tree.Session on skeleton. No-op.
        var navDialogType2 = navNcl.GetType("Microsoft.Dynamics.Nav.Runtime.NavDialog");
        if (navDialogType2 != null)
        {
            foreach (var m in navDialogType2.GetMethods(BindingFlags.Public | BindingFlags.Instance)
                .Where(m => m.Name == "ALOpen" && m.GetParameters().Length == 3))
            {
                Hook(m, nameof(NavDialog_ALOpen), $"NavDialog.ALOpen/3");
            }
        }

        // ALSystemString.ALLowercase / ALUppercase — real impls reach Session.Culture (null
        // on skeleton). Fall back to InvariantCulture.
        var alSysStrType = navNcl.GetType("Microsoft.Dynamics.Nav.Runtime.ALSystemString");
        if (alSysStrType != null)
        {
            var lower = alSysStrType.GetMethod("ALLowercase",
                BindingFlags.Public | BindingFlags.Static, null, new[] { typeof(string) }, null);
            if (lower != null)
                Hook(lower, nameof(ALSystemString_ALLowercase), "ALSystemString.ALLowercase");
            var upper = alSysStrType.GetMethod("ALUppercase",
                BindingFlags.Public | BindingFlags.Static, null, new[] { typeof(string) }, null);
            if (upper != null)
                Hook(upper, nameof(ALSystemString_ALUppercase), "ALSystemString.ALUppercase");
        }

        // RecordImplementation.GetActiveCompany — downstream NRE exposed by the get_Target
        // patch above. Real impl reaches Session.Database.CompanyTokens which is null on
        // the skeleton; return empty string.
        var recImplType = navNcl.GetType("Microsoft.Dynamics.Nav.Runtime.RecordImplementation");
        if (recImplType != null)
        {
            var getActiveCompany = recImplType.GetMethod("GetActiveCompany",
                BindingFlags.NonPublic | BindingFlags.Instance, null, Type.EmptyTypes, null);
            if (getActiveCompany != null)
                Hook(getActiveCompany, nameof(RecordImplementation_GetActiveCompany),
                    "RecordImplementation.GetActiveCompany");
        }

        // ── (A) Spike: async entry-point hook DISABLED
        //ApplyALFieldCaptionAsyncHook(navNcl);

        // ── Record / data-access plumbing (~300 lines) lives in RecordWritePatches.cs ──
        ApplyRecordPatches(navNcl);

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
                // Only hook overloads that take NavALErrorInfo as the last param.
                if (ps.Length < 1 || ps[ps.Length - 1].ParameterType.Name != "NavALErrorInfo")
                    continue;
                // Guid (16 bytes) occupies 2 x64 register slots on Linux .NET 8.
                // 2-arg (Guid, NavALErrorInfo):          slots = Guid-lo, Guid-hi, errorInfo  → 3 params ✓
                // 3-arg (NavSession, Guid, NavALErrorInfo): slots = session, Guid-lo, Guid-hi, errorInfo → 4 params
                //   This 3-arg overload is only called from ALLogInternalError (Internal-type errors),
                //   which we already no-op; no-op the overload itself too as belt-and-suspenders.
                bool hasSession = ps.Length >= 2 && ps[0].ParameterType.Name == "NavSession";
                var replacementName = hasSession ? nameof(NoOp4) : nameof(NavDialogALError_NavALErrorInfo);
                Hook(m, replacementName, $"NavDialog.ALError/{ps.Length}");
            }
            // NavDialog.ALLogInternalError — calls ALError internally; no-op so Dialog.LogInternalError
            // behaves like a trace (matching existing AL Runner behavior). All static overloads.
            foreach (var m in navDialogType.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)
                .Where(m => m.Name == "ALLogInternalError"))
            {
                var np = m.GetParameters().Length;
                var noop = np switch { 3 => nameof(NoOp3), 4 => nameof(NoOp4), 5 => nameof(NoOp5), _ => null };
                if (noop != null) Hook(m, noop, $"NavDialog.ALLogInternalError/{np}");
            }
        }

        // NavALErrorInfo.LogAddActionFailure(string) — private static telemetry; no-op.
        var navALErrorInfoType = navNcl.GetType("Microsoft.Dynamics.Nav.Runtime.NavALErrorInfo");
        if (navALErrorInfoType != null)
        {
            var logFail = navALErrorInfoType.GetMethod("LogAddActionFailure",
                BindingFlags.NonPublic | BindingFlags.Static, null, new[] { typeof(string) }, null);
            if (logFail != null)
                Hook(logFail, nameof(NoOp_OneArg), "NavALErrorInfo.LogAddActionFailure(string)");
        }

        // ALSession.GetALCurrentClientType(NavSession) — switches on session.ClientConnectionType
        // which NREs on the skeleton session. Return Background as a safe default.
        // ALSession.ALStopSessionAsync — async stop-session; returns ValueTask<bool>(false).
        var alSessionType = navNcl.GetType("Microsoft.Dynamics.Nav.Runtime.ALSession");
        if (alSessionType != null && sessType != null)
        {
            var getClientType = alSessionType.GetMethod("GetALCurrentClientType",
                BindingFlags.Public | BindingFlags.Static, null, new[] { sessType }, null);
            if (getClientType != null)
                Hook(getClientType, nameof(ALSession_GetALCurrentClientType), "ALSession.GetALCurrentClientType");

            // Hook all ALStopSessionAsync overloads — they all NRE via session.Diagnostics on skeleton.
            // Also hook the sync ALStopSession wrappers as belt-and-suspenders (they call Async internally).
            foreach (var m in alSessionType.GetMethods(BindingFlags.Public | BindingFlags.Static)
                .Where(m => m.Name == "ALStopSessionAsync"))
            {
                Hook(m, nameof(ALSession_StopSessionAsync), $"ALSession.ALStopSessionAsync/{m.GetParameters().Length}");
            }
            foreach (var m in alSessionType.GetMethods(BindingFlags.Public | BindingFlags.Static)
                .Where(m => m.Name == "ALStopSession"))
            {
                var np = m.GetParameters().Length;
                var repl = np switch { 2 => nameof(ReturnFalse_2Args), 3 => nameof(ReturnFalse_3Args), _ => null };
                if (repl != null) Hook(m, repl, $"ALSession.ALStopSession/{np}");
            }
        }

        // NavCodeunit.DoRunAsync(DataError, NavRecord) — first line creates a timing scope via
        // DiagnosticsResolver.GetMostSpecificInstance(Session) which NREs on the skeleton.
        // Replacement calls OnRun(record) directly on the concrete subclass and returns true.
        var navCodeunitType2 = navNcl.GetType("Microsoft.Dynamics.Nav.Runtime.NavCodeunit");
        if (navCodeunitType2 != null)
        {
            var doRunAsync = navCodeunitType2.GetMethod("DoRunAsync",
                BindingFlags.NonPublic | BindingFlags.Instance);
            if (doRunAsync != null)
                Hook(doRunAsync, nameof(NavCodeunit_DoRunAsync), "NavCodeunit.DoRunAsync");
        }

        // NavMethodScope.ProcessException(Exception) — when an NRE occurs in OnRun(), the real
        // implementation tries to call session.Diagnostics.SendExceptionTag(...) which NREs again
        // on the skeleton session (Diagnostics is null), producing a secondary NRE that masks the
        // original.  Returning false immediately (= "not handled") lets the original exception
        // propagate cleanly through Run()'s outer catch clauses.
        if (msType != null)
        {
            var procExc = msType.GetMethod("ProcessException",
                BindingFlags.NonPublic | BindingFlags.Instance, null,
                new[] { typeof(Exception) }, null);
            if (procExc != null)
                Hook(procExc, nameof(NavMethodScope_ProcessException), "NavMethodScope.ProcessException(Exception)");
        }

        // ALDebugger — all methods are obsolete stubs; handled at source level via BcAssembler
        // polyfill redirects to avoid ABI issues with value-type parameters (DataError enum).

        // NavApplicationObjectBase.TryInvoke — needs session.CurrentMethodScope; skeleton session lacks it.
        var navAOB = navNcl.GetType("Microsoft.Dynamics.Nav.Runtime.NavApplicationObjectBase");
        if (navAOB != null)
        {
            var tryInvoke = navAOB.GetMethod("TryInvoke",
                BindingFlags.Public | BindingFlags.Static,
                new[] { navNcl.GetType("Microsoft.Dynamics.Nav.Runtime.NavSession")!,
                         typeof(Action) });
            if (tryInvoke != null)
                Hook(tryInvoke, nameof(NavApplicationObjectBase_TryInvoke), "NavApplicationObjectBase.TryInvoke(NavSession, Action)");
        }
        // ALSession.ALEnableVerboseTelemetry — telemetry enable/disable; no-op is safe.
        if (alSessionType != null)
        {
            foreach (var m in alSessionType.GetMethods(BindingFlags.Public | BindingFlags.Static)
                .Where(m => m.Name == "ALEnableVerboseTelemetry"))
            {
                var np = m.GetParameters().Length;
                var noop = np switch { 1 => nameof(NoOp_OneArg), 2 => nameof(NoOp2), 3 => nameof(NoOp3), 4 => nameof(NoOp4), _ => null };
                if (noop != null) Hook(m, noop, $"ALSession.ALEnableVerboseTelemetry/{np}");
            }
        }

        // ALNavApp.ALNavAppIsInstalling() — static, returns bool; no install in progress → false.
        var alNavAppType = navNcl.GetType("Microsoft.Dynamics.Nav.Runtime.ALNavApp");
        if (alNavAppType != null)
        {
            var isInstalling = alNavAppType.GetMethod("ALNavAppIsInstalling",
                BindingFlags.Public | BindingFlags.Static, null, Type.EmptyTypes, null);
            if (isInstalling != null)
                Hook(isInstalling, nameof(ReturnFalse_0Args), "ALNavApp.ALNavAppIsInstalling");
        }

        // NavSessionSettings.ALRequestSessionUpdate(bool) — no-op; no live session to update.
        if (sessSettingsType != null)
        {
            var reqUpdate = sessSettingsType.GetMethod("ALRequestSessionUpdate",
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            if (reqUpdate != null)
                Hook(reqUpdate, nameof(NoOp2), "NavSessionSettings.ALRequestSessionUpdate");
        }

        // CallStackElement.TryGetSourceInfo(out ObjectSourceInfo) — chains through NavGlobal.NCLMetadata
        // which NREs on the skeleton session. Return false (no source info available) and set the
        // out-param pointer to zero so callers see a null/default sourceInfo.
        var callStackElemType = navNcl.GetType("Microsoft.Dynamics.Nav.Runtime.CallStackElement");
        if (callStackElemType != null)
        {
            var tryGetSrc = callStackElemType.GetMethod("TryGetSourceInfo",
                BindingFlags.NonPublic | BindingFlags.Instance);
            if (tryGetSrc != null)
                Hook(tryGetSrc, nameof(CallStackElement_TryGetSourceInfo),
                    "CallStackElement.TryGetSourceInfo");
        }

        // NCLMetaApplicationObject.get_ApplicationObjectConstructor — real getter calls
        // CompileAndLoadClrObject under a lock on `nclMetaObjectCLRTypeContainer`, which
        // is null on a skeleton meta-object → NRE in Monitor.ReliableEnter. Returning null
        // is safe: callers like NCLMetaTable.CreateObjectInstance fall back to constructing
        // NavRecord directly via `new NavRecord(parent, TableId, this, ...)`.
        var metaAoType = navNcl.GetType("Microsoft.Dynamics.Nav.Runtime.NCLMetaApplicationObject");
        if (metaAoType != null)
        {
            var aoCtorGetter = metaAoType.GetProperty("ApplicationObjectConstructor",
                BindingFlags.NonPublic | BindingFlags.Instance)?.GetGetMethod(true);
            if (aoCtorGetter != null)
                Hook(aoCtorGetter, nameof(ReturnNull_OneArg),
                    "NCLMetaApplicationObject.get_ApplicationObjectConstructor");
        }

        // ALDatabase static-method NRE cluster — all 8 affected methods are hooked here.
        RegisterALDatabasePatches(navNcl);

        // ── Spike: validate JmpHook.InstallIndirect (cell-patch mechanism) ──────────────────
        // Step 3: sync re-hook smoke test — NavSession.get_IsLocalLanguage is already hooked
        //   above via JmpHook.Apply (WriteJmp 14-byte overwrite path). Re-hook the same method
        //   via InstallIndirect to confirm the cell-patch mechanism routes correctly.
        //   Expected: InstallIndirect returns true (FF 25 signature found).
        //   Test harness validates same result (false) as before, confirming identical behaviour.
        //
        // Step 4: async entry-point hook — NavRecord.ALFieldCaptionAsync(int).
        //   Previously crashed with 14-byte overwrite corrupting MOV R10.
        //   Cell-patch leaves MOV R10 intact → should not crash.
        ApplyInstallIndirectSpike(navNcl);
    }

    // ── InstallIndirect spike implementation ────────────────────────────────────────────────

    internal static void ApplyInstallIndirectSpike(Assembly navNcl)
    {
        Console.Error.WriteLine("[IndirectSpike] === BEGIN ApplyInstallIndirectSpike ===");
        try
        {
            // Step 3: sync re-hook — NavSession.get_IsLocalLanguage (already hooked by Apply above).
            // We call InstallIndirect on it a second time, pointing to the same replacement.
            // The cell is already pointing to our first hook, so this is a no-op functionally,
            // but it exercises the cell-locate / mprotect / write path on a known-safe method.
            var sessType = navNcl.GetType("Microsoft.Dynamics.Nav.Runtime.NavSession");
            if (sessType != null)
            {
                var isLocalLang = sessType.GetProperty("IsLocalLanguage",
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)?.GetGetMethod(true);
                if (isLocalLang != null)
                {
                    var repl = typeof(BcRuntime).GetMethod(nameof(ReturnFalse_1Arg),
                        BindingFlags.Public | BindingFlags.Static)
                        ?? throw new InvalidOperationException("ReturnFalse_1Arg not found");
                    bool ok = JmpHook.InstallIndirect(isLocalLang, repl,
                        "SPIKE-Step3: NavSession.get_IsLocalLanguage (sync re-hook)");
                    Console.Error.WriteLine($"[IndirectSpike] Step 3 (sync re-hook): {(ok ? "GREEN" : "RED — precode shape mismatch")}");
                }
                else
                {
                    Console.Error.WriteLine("[IndirectSpike] Step 3: IsLocalLanguage not found — skipped");
                }
            }
            else
            {
                Console.Error.WriteLine("[IndirectSpike] Step 3: NavSession type not found — skipped");
            }
        }
        catch (Exception ex3)
        {
            Console.Error.WriteLine($"[IndirectSpike] Step 3 THREW: {ex3.GetType().FullName}: {ex3.Message}");
        }

        try
        {
            // Step 4: async entry-point hook — NavRecord.ALFieldCaptionAsync(int).
            // Previous spike crashed (14-byte overwrite corrupted MOV R10 at bytes 6-12).
            // Cell-patch leaves bytes 6-12 intact — the MethodDesc stays readable for lazy JIT.
            //
            // Step 4: async entry-point hook — NavRecord.ALFieldCaptionAsync(int).
            // DISABLED — cell-patch installs without crash but SIGSEGV occurs during test
            // execution before the replacement is ever called. The crash is in JIT compilation
            // of a new caller that reads the patched cell. See spike report for full analysis.
            // The cell-patch mechanism itself is proven correct (Step 3 GREEN).
            // Async methods require a DIFFERENT dispatch strategy (see report).
            Console.Error.WriteLine("[IndirectSpike] Step 4: DISABLED — see spike report");
        }
        catch (Exception ex4)
        {
            Console.Error.WriteLine($"[IndirectSpike] Step 4 THREW: {ex4.GetType().FullName}: {ex4.Message}");
        }

        Console.Error.WriteLine("[IndirectSpike] === END ApplyInstallIndirectSpike ===");
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

    private static void Hook(MethodBase original, MethodInfo replacement, string description)
        => JmpHook.Apply(original, replacement, description);

}
