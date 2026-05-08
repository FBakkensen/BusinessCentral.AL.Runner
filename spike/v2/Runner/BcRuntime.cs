// BcRuntime — applies Linux-compatibility patches to BC service-tier DLLs at process start.
// Lifted directly from spike/bc-abi-identity/runner/LinuxBootstrap.cs (proven to work end-to-end).
// Pattern: bc-linux's JMP-hook via mprotect + RuntimeHelpers.PrepareMethod.
using System.Collections.Concurrent;
using System.Reflection;
using System.Runtime.CompilerServices;
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
            if (pushDyn != null) Hook(pushDyn, nameof(NoOp_OneArg), "NavSession.PushDynamicCaptionStack");
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
