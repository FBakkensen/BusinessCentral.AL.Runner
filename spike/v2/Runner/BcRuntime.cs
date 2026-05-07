// BcRuntime — applies Linux-compatibility patches to BC service-tier DLLs at process start.
// Lifted directly from spike/bc-abi-identity/runner/LinuxBootstrap.cs (proven to work end-to-end).
// Pattern: bc-linux's JMP-hook via mprotect + RuntimeHelpers.PrepareMethod.
using System.Collections.Concurrent;
using System.Reflection;
using System.Runtime.CompilerServices;
using AlRunnerV2.Infrastructure;

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

        // ── RECORD PATCHES (Approach A spike) ────────────────────────────────────────
        // NavRecordHandle.CreateTarget — bypass NCLMetadata by constructing Record{ID}
        // directly using an NCLMetaTable built from parsed AL source, backed by BC's own
        // TempTableDataProvider (in-memory AVL-tree store).
        AlRunnerV2.Patches.RecordPatches.Register();

        // Pre-populate skeleton session's DataAccessSource field directly.
        // NavSession.DataAccessSource getter is inlined by JIT (trivial field return),
        // so the JMP hook on it never fires — we must inject DAS via field reflection.
        Console.Error.WriteLine($"[BcRuntime] _skeletonSession null? {_skeletonSession == null}");
        if (_skeletonSession != null)
            AlRunnerV2.Patches.RecordPatches.InitializeSkeletonSession(_skeletonSession);
        else
            Console.Error.WriteLine("[BcRuntime] WARN: _skeletonSession is null — DAS not injected");

        var recordHandleType = navNcl.GetType("Microsoft.Dynamics.Nav.Runtime.NavRecordHandle");
        if (recordHandleType != null)
        {
            var createTargetRec = recordHandleType.GetMethod("CreateTarget",
                BindingFlags.NonPublic | BindingFlags.Instance,
                null, Type.EmptyTypes, null);
            if (createTargetRec != null)
                Hook(createTargetRec,
                    typeof(AlRunnerV2.Patches.RecordPatches).GetMethod("NavRecordHandle_CreateTarget",
                        BindingFlags.Public | BindingFlags.Static)!,
                    "NavRecordHandle.CreateTarget");
        }

        // NavSession.DataAccessSource getter — return skeleton DataAccessSource backed by in-memory store.
        // NavSession.Database getter — return skeleton NavDatabase (real Database => Tenant.Database NREs).
        var sessType2 = navNcl.GetType("Microsoft.Dynamics.Nav.Runtime.NavSession");
        if (sessType2 != null)
        {
            var dasProp = sessType2.GetProperty("DataAccessSource",
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            if (dasProp?.GetGetMethod(true) != null)
                Hook(dasProp.GetGetMethod(true)!,
                    typeof(AlRunnerV2.Patches.RecordPatches).GetMethod("NavSession_get_DataAccessSource",
                        BindingFlags.Public | BindingFlags.Static)!,
                    "NavSession.get_DataAccessSource");

            var dbProp = sessType2.GetProperty("Database",
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            if (dbProp?.GetGetMethod(true) != null)
                Hook(dbProp.GetGetMethod(true)!,
                    typeof(AlRunnerV2.Patches.RecordPatches).GetMethod("NavSession_get_Database",
                        BindingFlags.Public | BindingFlags.Static)!,
                    "NavSession.get_Database");
        }

        // DataAccessSource.GetDataAccessForTable — always route to CreateTempDataAccess (in-memory).
        var dasType2 = navNcl.GetType("Microsoft.Dynamics.Nav.Runtime.DataAccessSource");
        if (dasType2 != null)
        {
            var gdaft = dasType2.GetMethod("GetDataAccessForTable",
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            if (gdaft != null)
                Hook(gdaft,
                    typeof(AlRunnerV2.Patches.RecordPatches).GetMethod("NavDataAccessSource_GetDataAccessForTable",
                        BindingFlags.Public | BindingFlags.Static)!,
                    "DataAccessSource.GetDataAccessForTable");
        }

        // TempTableDataProvider.ctor — navSession.Database.CollationAwareStringComparer NREs on skeleton session.
        // Replace with manual field injection to bypass the Database access.
        var ttdpType = navNcl.GetType("Microsoft.Dynamics.Nav.Runtime.TempTableDataProvider");
        if (ttdpType != null)
        {
            var sessT = navNcl.GetType("Microsoft.Dynamics.Nav.Runtime.NavSession");
            var nclMetaT = navNcl.GetType("Microsoft.Dynamics.Nav.Runtime.NCLMetaTable");
            var ttdpCtor = ttdpType.GetConstructor(
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance,
                null, new[] { sessT!, nclMetaT! }, null);
            if (ttdpCtor != null)
                Hook(ttdpCtor,
                    typeof(AlRunnerV2.Patches.RecordPatches).GetMethod("TempTableDataProviderCtorReplacement",
                        BindingFlags.Public | BindingFlags.Static)!,
                    "TempTableDataProvider.ctor");
        }

        // NavDatabase.CollationAwareStringComparer getter — return OrdinalIgnoreCase comparer.
        var navDbType = navNcl.GetType("Microsoft.Dynamics.Nav.Runtime.NavDatabase");
        if (navDbType != null)
        {
            var collProp = navDbType.GetProperty("CollationAwareStringComparer",
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            if (collProp?.GetGetMethod(true) != null)
                Hook(collProp.GetGetMethod(true)!,
                    typeof(AlRunnerV2.Patches.RecordPatches).GetMethod("NavDatabase_get_CollationAwareStringComparer",
                        BindingFlags.Public | BindingFlags.Static)!,
                    "NavDatabase.get_CollationAwareStringComparer");
        }
        // NavRecord.Dispose(bool) — NREs when RequiredSessionId != NavCurrentThread.Session.Id
        // (skeleton session has no real Id, DiagnosticsResolver.GetMostSpecificInstance(null) NREs).
        // Safe to no-op since our in-memory DataAccess/TempTableDataProvider needs no cleanup.
        var navRecordType = navNcl.GetType("Microsoft.Dynamics.Nav.Runtime.NavRecord");
        if (navRecordType != null)
        {
            var disposeMethod = navRecordType.GetMethod("Dispose",
                BindingFlags.NonPublic | BindingFlags.Instance, null,
                new[] { typeof(bool) }, null);
            if (disposeMethod != null)
                Hook(disposeMethod, nameof(NoOp2), "NavRecord.Dispose(bool)");

            // IsGlobalTriggerImplemented — checks Session.SystemCodeunitFactory.GlobalTriggers which
            // NREs on skeleton session. Return false: no global triggers in headless mode.
            var isGlobalTrigger = navRecordType.GetMethod("IsGlobalTriggerImplemented",
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            Console.Error.WriteLine($"[BcRuntime] IsGlobalTriggerImplemented found: {isGlobalTrigger != null} {isGlobalTrigger}");
            if (isGlobalTrigger != null)
                Hook(isGlobalTrigger, nameof(ReturnFalse2), "NavRecord.IsGlobalTriggerImplemented");

            // NavRecord.InsertAsync(DataError, bool, bool, bool) — full body NREs through
            // NavCurrentThread.ResolveAppGroup / metaTable.IsEventSubscribed / DataModificationListener
            // before reaching the storage layer. Replace the whole body with a minimal call that
            // delegates straight to recordImplementation.InsertRecordAsync — which goes through our
            // already-hooked TempTableDataProvider DataAccessSource. Skips trigger/event dispatch
            // (W-8 will reintroduce that on top of the temp store).
            _fNavRecordRecordImplementation = navRecordType.GetField("recordImplementation",
                BindingFlags.NonPublic | BindingFlags.Instance);
            var recImplTypeForWrites = navNcl.GetType("Microsoft.Dynamics.Nav.Runtime.RecordImplementation");
            if (recImplTypeForWrites != null)
            {
                _mRecordImplementationInsertRecordAsync = recImplTypeForWrites.GetMethod(
                    "InsertRecordAsync",
                    BindingFlags.NonPublic | BindingFlags.Instance);
                _mRecordImplementationModifyRecordAsync = recImplTypeForWrites.GetMethod(
                    "ModifyRecordAsync",
                    BindingFlags.NonPublic | BindingFlags.Instance);
                _mRecordImplementationDeleteRecordAsync = recImplTypeForWrites.GetMethod(
                    "DeleteRecordAsync",
                    BindingFlags.NonPublic | BindingFlags.Instance);
            }
            var insertAsync4 = navRecordType.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                .FirstOrDefault(m => m.Name == "InsertAsync" && m.GetParameters().Length == 4);
            if (insertAsync4 != null && _mRecordImplementationInsertRecordAsync != null)
                Hook(insertAsync4, nameof(NavRecord_InsertAsync), "NavRecord.InsertAsync(DataError,bool,bool,bool)");
        }
        // NCLMetaApplicationObject.CheckApplicationObjectIsValid — validates app-group ID matches.
        // Fails on skeleton session because tenant is null. No-op is safe: headless mode doesn't
        // do hot-reload or app-group switching, so stale-object detection has no value here.
        var nclMetaAppObjType = navNcl.GetType("Microsoft.Dynamics.Nav.Runtime.NCLMetaApplicationObject");
        if (nclMetaAppObjType != null)
        {
            var checkValid = nclMetaAppObjType.GetMethod("CheckApplicationObjectIsValid",
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            if (checkValid != null)
                Hook(checkValid, nameof(NoOp2), "NCLMetaApplicationObject.CheckApplicationObjectIsValid");

            // get_ApplicationObjectClrType — does lock(nclMetaObjectCLRTypeContainer) which NREs
            // when the container is null (our CreateFromMetaTable-built tables don't have it set).
            // Replace with a dynamic lookup in loaded assemblies: Record{ID} in BusinessApplication namespace.
            var getClrType = nclMetaAppObjType.GetProperty("ApplicationObjectClrType",
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)?.GetGetMethod(true);
            if (getClrType != null)
                Hook(getClrType,
                    typeof(AlRunnerV2.Patches.RecordPatches).GetMethod("NCLMetaApplicationObject_get_ApplicationObjectClrType",
                        BindingFlags.Public | BindingFlags.Static)!,
                    "NCLMetaApplicationObject.get_ApplicationObjectClrType");
        }

        // RecordImplementation.VerifyPermissions — calls NavSession.GetPermissionSet → Permissions field
        // NREs on skeleton session (no real security infrastructure). No-op is safe in headless mode.
        var recImplType = navNcl.GetType("Microsoft.Dynamics.Nav.Runtime.RecordImplementation");
        if (recImplType != null)
        {
            // Find the 2-arg private instance VerifyPermissions(PermissionMask, bool).
            var verifyPerms = recImplType.GetMethods(BindingFlags.NonPublic | BindingFlags.Instance)
                .FirstOrDefault(m => m.Name == "VerifyPermissions" && m.GetParameters().Length == 2);
            if (verifyPerms != null)
                Hook(verifyPerms, nameof(NoOp3), "RecordImplementation.VerifyPermissions");

            // InternalFindRecordWithoutCheckingValuesAsync — replace with a thin call that hits
            // dataAccess.TryGetByPrimaryKeyAsync and bypasses the NRE-prone fallback branch
            // (Session.CurrentMethodScope.ApplicationObject is null on the skeleton root scope).
            _fRecordImplementationDataAccess = recImplType.GetField("dataAccess",
                BindingFlags.NonPublic | BindingFlags.Instance);
            _fRecordImplementationMutableRecordBuffer = recImplType.GetField("mutableRecordBuffer",
                BindingFlags.NonPublic | BindingFlags.Instance);
            var dataAccessType = navNcl.GetType("Microsoft.Dynamics.Nav.Runtime.DataAccess");
            if (dataAccessType != null)
            {
                _mDataAccessTryGetByPrimaryKeyAsync = dataAccessType.GetMethod("TryGetByPrimaryKeyAsync",
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            }
            var mrbResultType = navNcl.GetType("Microsoft.Dynamics.Nav.Runtime.MutableRecordBufferResult`1")
                ?.MakeGenericType(typeof(bool));
            if (mrbResultType != null)
            {
                _pMrbResultResult = mrbResultType.GetProperty("Result");
                _pMrbResultRecordBuffer = mrbResultType.GetProperty("RecordBuffer");
            }
            var internalFind = recImplType.GetMethods(BindingFlags.NonPublic | BindingFlags.Instance)
                .FirstOrDefault(m => m.Name == "InternalFindRecordWithoutCheckingValuesAsync"
                                     && m.GetParameters().Length == 4);
            if (internalFind != null && _fRecordImplementationDataAccess != null
                && _mDataAccessTryGetByPrimaryKeyAsync != null && _pMrbResultResult != null)
                Hook(internalFind, nameof(RecordImpl_InternalFindRecordWithoutCheckingValuesAsync),
                    "RecordImplementation.InternalFindRecordWithoutCheckingValuesAsync");

            // VerifySecurityFiltersOnRecordAsync(IRecordBuffer, FilterFieldDictionary, bool, bool)
            // — called from InternalFindRecordWithoutCheckingValuesAsync; NREs through Session
            // permission infrastructure. No-op (returns completed ValueTask).
            foreach (var m in recImplType.GetMethods(BindingFlags.NonPublic | BindingFlags.Instance)
                .Where(m => m.Name == "VerifySecurityFiltersOnRecordAsync"))
            {
                var p = m.GetParameters().Length;
                Hook(m, p switch {
                    2 => nameof(ReturnValueTask3),  // self + 2 args
                    3 => nameof(ReturnValueTask4),
                    4 => nameof(ReturnValueTask5),
                    _ => nameof(ReturnValueTask3)
                }, $"RecordImplementation.VerifySecurityFiltersOnRecordAsync/{p}");
            }
            // VerifySecurityFiltersAsync(MutableRecordBuffer, SecurityFilterType) — write-path equivalent.
            var verifySecAsync = recImplType.GetMethods(BindingFlags.NonPublic | BindingFlags.Instance)
                .FirstOrDefault(m => m.Name == "VerifySecurityFiltersAsync" && m.GetParameters().Length == 2);
            if (verifySecAsync != null)
                Hook(verifySecAsync, nameof(ReturnValueTask3), "RecordImplementation.VerifySecurityFiltersAsync");

            // RecordImplementation.get_IsOpen — diagnose null tree.
            // IsOpen = !base.IsDisposed && initialized. base.IsDisposed = tree.IsDisposed.
            // If tree is null, NRE here (inlined into ThrowIfRecordStaleOrNotOpen).
            var getIsOpen = recImplType.GetProperty("IsOpen",
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)?.GetGetMethod(true);
            if (getIsOpen != null)
            {
                Console.Error.WriteLine($"[BcRuntime] RecordImplementation.IsOpen found: {getIsOpen}");
                Hook(getIsOpen, nameof(ReturnTrue), "RecordImplementation.get_IsOpen");
            }
        }

        // NavServerEventSource.WritePermissionUncheckedEvent — telemetry event called from
        // RecordImplementation.InternalFindRecordWithoutCheckingValuesAsync; the property
        // get_NavServerTracingEvents NREs because the singleton EventSource is uninitialized in
        // headless mode. No-op the public method, AND ensure NavServerEventSource.Log returns a
        // non-null instance so the call-site doesn't NRE on virtual dispatch.
        var navServerEventSourceType = navNcl.GetType("Microsoft.Dynamics.Nav.Runtime.NavServerEventSource");
        if (navServerEventSourceType != null)
        {
            // Pre-build an uninitialised NavServerEventSource singleton (cached in static field).
            _skeletonNavServerEventSource = RuntimeHelpers.GetUninitializedObject(navServerEventSourceType);

            var getLog = navServerEventSourceType.GetProperty("Log",
                BindingFlags.Public | BindingFlags.Static)?.GetGetMethod(true);
            if (getLog != null)
                Hook(getLog, nameof(NavServerEventSource_get_Log), "NavServerEventSource.get_Log");

            // No-op every event-write method on the type — they all dereference the (uninit)
            // EventSource internals which would NRE. Cheap belt-and-braces compared to chasing
            // each one individually as new tests hit them.
            // 10-arg specific (WritePermissionUncheckedEvent already has its own typed no-op).
            var writePerm = navServerEventSourceType.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                .FirstOrDefault(m => m.Name == "WritePermissionUncheckedEvent" && m.GetParameters().Length == 10);
            if (writePerm != null)
                Hook(writePerm, nameof(NavServerEventSource_WritePermissionUncheckedEvent),
                    "NavServerEventSource.WritePermissionUncheckedEvent");
        }

        // NavSession.get_SortingProperties — Database.SqlSortingProperties path. Belt-and-suspenders
        // hook in case the skeleton Database's pre-poked sqlSortingProperties field is bypassed
        // by JIT inlining of the chained property accesses.
        if (sessType != null)
        {
            var getSortingProps = sessType.GetProperty("SortingProperties",
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)?.GetGetMethod(true);
            if (getSortingProps != null)
                Hook(getSortingProps,
                    typeof(AlRunnerV2.Patches.RecordPatches).GetMethod(
                        nameof(AlRunnerV2.Patches.RecordPatches.NavSession_get_SortingProperties),
                        BindingFlags.Public | BindingFlags.Static)!,
                    "NavSession.get_SortingProperties");
        }

        // SequentialUuidCreator.NativeMethods.NewSequentialId — P/Invokes rpcrt4.dll (Windows only).
        // Replace with Guid.NewGuid() on all platforms.
        var seqUuidCreator = navNcl.GetType("Microsoft.Dynamics.Nav.Runtime.Data.SequentialUuidCreator+NativeMethods");
        if (seqUuidCreator != null)
        {
            var newSeqId = seqUuidCreator.GetMethod("NewSequentialId",
                BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Static);
            var newSeqIdRepl = typeof(AlRunnerV2.Patches.RecordPatches).GetMethod(
                nameof(AlRunnerV2.Patches.RecordPatches.NewSequentialId_Replacement),
                BindingFlags.Public | BindingFlags.Static);
            if (newSeqId != null && newSeqIdRepl != null)
                Hook(newSeqId, newSeqIdRepl, "SequentialUuidCreator.NewSequentialId");
        }

        // TempTableStatistics.ReportIncrementChange — tries to call NavEnvironment.PerformanceCounterSetter
        // which is null on our skeleton. No-op: temp table statistics are not needed in headless mode.
        var tTempStats = navNcl.GetType("Microsoft.Dynamics.Nav.Runtime.TempTableStatistics");
        if (tTempStats != null)
        {
            var reportChange = tTempStats.GetMethod("ReportIncrementChange",
                BindingFlags.NonPublic | BindingFlags.Instance,
                null, new[] { typeof(int), typeof(int), typeof(int) }, null);
            if (reportChange != null)
                Hook(reportChange, nameof(NoOp4), "TempTableStatistics.ReportIncrementChange");
        }
        // ── END RECORD PATCHES ────────────────────────────────────────────────────────

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

    [MethodImpl(MethodImplOptions.NoInlining)] public static void NoOp_0Args() { }
    [MethodImpl(MethodImplOptions.NoInlining)] public static void NoOp_OneArg(object? a) { }
    [MethodImpl(MethodImplOptions.NoInlining)] public static void NoOp2(object? a, object? b) { }
    [MethodImpl(MethodImplOptions.NoInlining)] public static void NoOp3(object? a, object? b, object? c) { }
    [MethodImpl(MethodImplOptions.NoInlining)] public static void NoOp4(object? a, object? b, object? c, object? d) { }
    [MethodImpl(MethodImplOptions.NoInlining)] public static void NoOp5(object? a, object? b, object? c, object? d, object? e) { }
    [MethodImpl(MethodImplOptions.NoInlining)] public static bool ReturnFalse_0Args() => false;
    [MethodImpl(MethodImplOptions.NoInlining)] public static bool ReturnFalse_1Arg(object? a) => false;
    [MethodImpl(MethodImplOptions.NoInlining)] public static bool ReturnFalse_2Args(object? a, object? b) => false;
    [MethodImpl(MethodImplOptions.NoInlining)] public static System.Threading.Tasks.ValueTask ReturnValueTask3(object? a, object? b, object? c) => default;
    [MethodImpl(MethodImplOptions.NoInlining)] public static System.Threading.Tasks.ValueTask ReturnValueTask4(object? a, object? b, object? c, object? d) => default;
    [MethodImpl(MethodImplOptions.NoInlining)] public static System.Threading.Tasks.ValueTask ReturnValueTask5(object? a, object? b, object? c, object? d, object? e) => default;
    [MethodImpl(MethodImplOptions.NoInlining)] public static object? ReturnNull_OneArg(object a) => null;
    [MethodImpl(MethodImplOptions.NoInlining)] public static object? GetSkeletonCompanyReplacement(object self) => _skeletonCompany;

    /// <summary>
    /// Replacement for NavApplicationObjectBase(ITreeObject parent, ApplicationObjectId objectId, NCLStaticMetadata staticMetadata).
    /// The real ctor body does three problematic things:
    ///   1. `session = base.Tree.Session` — returns null because our skeleton tree has no session chain.
    ///   2. `NavCurrentThread.ResolveAppGroup(session)` — NREs through NCLMetadata on null session.
    ///   3. `base(parent)` chain call — this IS included in the method body and we must replicate it,
    ///      otherwise TreeObject.ctor (which sets `this.tree`) is never called.
    /// Our replacement: call CreateTreeHandler to set the tree, inject _skeletonSession, skip ResolveAppGroup.
    /// </summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static void NavApplicationObjectBaseCtorReplacement(object self, object parent, object objectId, object? staticMetadata)
    {
        Console.Error.WriteLine($"[AoCtor] called for {self?.GetType().Name ?? "null"}");
        // 1. Replicate TreeObject.ctor: create the TreeHandler from parent and assign to this.tree.
        //    This is what the `base(parent)` chain normally does for NavApplicationObjectBase.
        if (_mCreateTreeHandler != null && _fNavComplexValueTree != null)
        {
            // Use parent if it has a valid tree; otherwise fall back to _skeletonRootScope.
            // This ensures every NavRecord/NavApplicationObjectBase always gets a non-null tree field,
            // which is required for TreeObject.IsDisposed (called from RecordImplementation.IsOpen).
            var parentAsTreeObject = parent as Microsoft.Dynamics.Nav.Runtime.ITreeObject;
            var effectiveParent = (parentAsTreeObject?.Tree != null)
                ? parentAsTreeObject
                : (Microsoft.Dynamics.Nav.Runtime.ITreeObject?)_skeletonRootScope;
            if (effectiveParent != null)
            {
                try
                {
                    var handler = _mCreateTreeHandler.Invoke(null, new object[] { effectiveParent, self });
                    FieldPoke.SetInstance(_fNavComplexValueTree, self, handler);
                    var treeCheck = _fNavComplexValueTree.GetValue(self);
                    Console.Error.WriteLine($"[AoCtor] tree set for {self?.GetType().Name}: {treeCheck != null}");
                }
                catch (Exception ex) { Console.Error.WriteLine($"[AoCtor] tree creation failed for {self?.GetType().Name}: {ex.Message}"); }
            }
        }
        // 2. Inject skeleton session instead of `session = base.Tree.Session` (which gives null).
        if (_fAoSession != null)
        {
            FieldPoke.SetInstance(_fAoSession, self, _skeletonSession);
            // Verify: read back the session field immediately to confirm write succeeded.
            var check = _fAoSession.GetValue(self);
            if (check == null)
                Console.Error.WriteLine($"[BcRuntime] WARN: session field write failed on {self.GetType().Name}");
        }
        else
        {
            Console.Error.WriteLine("[BcRuntime] WARN: _fAoSession is null — cannot inject session");
        }
        // 3. Skip NavCurrentThread.ResolveAppGroup — use BaseGroupId=0.
        if (_fAoOrigGroupId != null)    FieldPoke.SetInstance(_fAoOrigGroupId,    self, 0);
        if (_fAoRuntimeGroupId != null) FieldPoke.SetInstance(_fAoRuntimeGroupId, self, 0);
    }

    /// <summary>
    /// Replacement for TreeHandler.get_Session.
    /// The tree hierarchy is built from skeleton objects whose session fields are null.
    /// Always return the skeleton session so NavRecord.ctor and NavApplicationObjectBase.ctor
    /// can access a non-null session without needing a real BC tree.
    /// </summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static object? TreeHandler_get_Session(object self) => _skeletonSession;

    /// <summary>
    /// Replacement for NavCodeunit.ContainsMethod(int, string, object[]).
    /// Returns false — callers use this to check whether a codeunit event handler exists.
    /// The real implementation chains through NCLMetadata which NREs on a skeleton session.
    /// </summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static bool ReturnFalse_3Args(object? a, object? b, object? c) => false;

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static bool ReturnFalse2(object? a, object? b) => false;

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static bool ReturnTrue(object? a)
    {
        Console.Error.WriteLine($"[ReturnTrue] IsOpen hook fired for {a?.GetType().Name}");
        return true;
    }

    /// <summary>
    /// Replacement for NavMethodScope.ProcessException(Exception).
    /// The real body calls session.Diagnostics.SendExceptionTag(...) when the exception is an NRE,
    /// but session.Diagnostics is null on the skeleton session → secondary NRE that masks the original.
    /// Returning false immediately means "exception not handled here" so the original exception
    /// propagates cleanly through Run()'s outer catch clauses.
    /// </summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static bool NavMethodScope_ProcessException(object? self, Exception? exception) => false;

    /// <summary>
    /// Replacement for CallStackElement.TryGetSourceInfo(out ObjectSourceInfo sourceInfo).
    /// The real implementation chains through NavGlobal.NCLMetadata which NREs on a skeleton session.
    /// Returns false (no source info) and zeros the out-param so callers see a null/default sourceInfo.
    /// The out-param is passed as an IntPtr to the raw managed pointer location.
    /// </summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static unsafe bool CallStackElement_TryGetSourceInfo(object? self, IntPtr sourceInfoOutPtr)
    {
        if (sourceInfoOutPtr != IntPtr.Zero)
            *(IntPtr*)sourceInfoOutPtr.ToPointer() = IntPtr.Zero;
        return false;
    }

    /// <summary>
    /// Replacement for ALSession.GetALCurrentClientType(NavSession).
    /// The real body switches on session.ClientConnectionType which NREs on the skeleton session.
    /// Returns Background as a safe default matching headless/service-tier-less execution.
    /// </summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static Microsoft.Dynamics.Nav.Types.NavClientType ALSession_GetALCurrentClientType(
        object? session)
        => Microsoft.Dynamics.Nav.Types.NavClientType.Background;

    /// <summary>
    /// Replacement for all ALSession.ALStopSessionAsync overloads.
    /// The async body NREs via session.Diagnostics on the skeleton. Return false (not stopped).
    /// </summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static System.Threading.Tasks.ValueTask<bool> ALSession_StopSessionAsync(
        object? a, object? b, object? c, object? d)
    {
        return new System.Threading.Tasks.ValueTask<bool>(false);
    }

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

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static object? NavServerEventSource_get_Log() => _skeletonNavServerEventSource;

    /// <summary>
    /// No-op for NavServerEventSource.WritePermissionUncheckedEvent — instance method with 10 args
    /// (4 strings + 6 ints). The replacement is static so first arg is the receiver.
    /// </summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static void NavServerEventSource_WritePermissionUncheckedEvent(
        object self,
        string serverInstanceName, string navTenantId, string environmentName, string environmentType,
        int sessionId, int objectType, int objectId, int permissions, int callingObjectType, int callingObjectId)
    { }

    /// <summary>
    /// Replacement for RecordImplementation.InternalFindRecordWithoutCheckingValuesAsync —
    /// thin passthrough that hits dataAccess.TryGetByPrimaryKeyAsync and bypasses the original
    /// body's permission-event/diagnostic args evaluation, which NREs through
    /// Session.CurrentMethodScope.ApplicationObject (null on the skeleton root scope) when the
    /// requested record is not found.
    /// </summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static System.Threading.Tasks.ValueTask<bool> RecordImpl_InternalFindRecordWithoutCheckingValuesAsync(
        object self,
        Microsoft.Dynamics.Nav.Types.DataError errorLevel,
        object request,
        bool useRecord,
        bool calcAutoCalcFields)
    {
        try
        {
            var dataAccess = _fRecordImplementationDataAccess?.GetValue(self);
            if (dataAccess == null || _mDataAccessTryGetByPrimaryKeyAsync == null)
                return new System.Threading.Tasks.ValueTask<bool>(false);
            var taskObj = _mDataAccessTryGetByPrimaryKeyAsync.Invoke(dataAccess, new[] { request });
            if (taskObj == null) return new System.Threading.Tasks.ValueTask<bool>(false);

            // taskObj is ValueTask<MutableRecordBufferResult<bool>> — block via .AsTask().Result.
            var asTaskMi = taskObj.GetType().GetMethod("AsTask");
            var asTask = asTaskMi?.Invoke(taskObj, null) as System.Threading.Tasks.Task;
            asTask?.Wait();
            var resultObj = asTask?.GetType().GetProperty("Result")?.GetValue(asTask);
            if (resultObj == null) return new System.Threading.Tasks.ValueTask<bool>(false);

            bool found = (bool)(_pMrbResultResult!.GetValue(resultObj) ?? false);
            if (found && useRecord)
            {
                var recBuffer = _pMrbResultRecordBuffer?.GetValue(resultObj);
                _fRecordImplementationMutableRecordBuffer?.SetValue(self, recBuffer);
            }
            if (found) return new System.Threading.Tasks.ValueTask<bool>(true);
            // Not-found path: TrapError → false; ThrowError → throw.
            if ((int)errorLevel == 1) return new System.Threading.Tasks.ValueTask<bool>(false);
            throw new InvalidOperationException("Record not found (skeleton find).");
        }
        catch (TargetInvocationException tie) when (tie.InnerException != null)
        {
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw(tie.InnerException);
            return default;
        }
    }

    /// <summary>
    /// Replacement for NavRecord.InsertAsync(DataError, bool, bool, bool).
    /// Bypasses all the trigger/event/extension dispatch that NREs on a skeleton session
    /// (NavCurrentThread.ResolveAppGroup, DataModificationListener, etc.) and delegates straight
    /// to recordImplementation.InsertRecordAsync, which goes through our hooked
    /// TempTableDataProvider DataAccessSource. W-8 will layer trigger dispatch back on top of
    /// this once permanent-table semantics are wired up.
    /// </summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static System.Threading.Tasks.ValueTask<bool> NavRecord_InsertAsync(
        Microsoft.Dynamics.Nav.Runtime.NavRecord self,
        Microsoft.Dynamics.Nav.Types.DataError errorLevel,
        bool runApplicationTrigger,
        bool runGlobalTrigger,
        bool insertWithSystemId)
    {
        try
        {
            var recImpl = _fNavRecordRecordImplementation?.GetValue(self);
            if (recImpl == null || _mRecordImplementationInsertRecordAsync == null)
                return new System.Threading.Tasks.ValueTask<bool>(false);
            var result = _mRecordImplementationInsertRecordAsync.Invoke(recImpl, new object?[] { errorLevel });
            if (result is System.Threading.Tasks.ValueTask<bool> vt) return vt;
            return new System.Threading.Tasks.ValueTask<bool>(true);
        }
        catch (TargetInvocationException tie) when (tie.InnerException != null)
        {
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw(tie.InnerException);
            return default; // unreachable
        }
    }

    /// <summary>
    /// Replacement for NavDialog.ALError(Guid automationId, NavALErrorInfo errorInfo).
    /// On Linux x86-64, Guid (16 bytes) occupies two register slots, so the actual
    /// parameters received are: a = Guid-lo64, b = Guid-hi64, errorInfo = NavALErrorInfo.
    /// The real body NREs through diagnostics infrastructure; we construct NavNCLDialogException
    /// directly from the error message so asserterror traps it correctly.
    /// Note: the 3-arg overload ALError(NavSession, Guid, NavALErrorInfo) is hooked to NoOp4
    /// (session + two Guid halves + errorInfo) since it is only called from ALLogInternalError
    /// which we already suppress.
    /// </summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static void NavDialogALError_NavALErrorInfo(object? a, object? b, object? errorInfo)
    {
        string msg = string.Empty;
        if (errorInfo != null)
        {
            try
            {
                var ei = (Microsoft.Dynamics.Nav.Runtime.NavALErrorInfo)errorInfo;
                if (ei.ALErrorType == Microsoft.Dynamics.Nav.Types.ALErrorType.Internal)
                    return;
                msg = ei.ALMessage ?? string.Empty;
            }
            catch
            {
                try
                {
                    var msgProp = errorInfo.GetType().GetProperty("ALMessage", BindingFlags.Public | BindingFlags.Instance);
                    msg = msgProp?.GetValue(errorInfo) as string ?? string.Empty;
                }
                catch { }
            }
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

    /// <summary>
    /// Replacement for NavSession.get_LocalLanguageNoFallback.
    /// The real getter reads globalLanguageStack which is null in our skeleton session.
    /// Return -1 = "no override, use default language" (same as empty stack result).
    /// </summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static int NavSession_LocalLanguageNoFallback(object? self) => -1;

    /// <summary>
    /// Replacement for NavMethodScope.AssertError(Action body). The real method calls
    /// session.Rollback() in its catch path, which NREs on the skeleton session. We invert the
    /// pass/fail semantics in headless mode: if the body throws, the asserterror succeeded;
    /// if the body completes normally, throw NavNCLAssertErrorException so the test driver
    /// sees an asserterror failure.
    /// </summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static void NavMethodScope_AssertError(object self, Action body)
    {
        try { body(); }
        catch { return; /* asserterror passed: body threw something */ }
        throw new Microsoft.Dynamics.Nav.Types.Exceptions.NavNCLAssertErrorException();
    }

    /// <summary>
    /// Replacement for NavSession.GetSecurityFilters — bypasses Database.SecurityAndLicense which
    /// NREs on the skeleton database. Return null; RecordImplementation treats null as "no security
    /// filters" (matches the IsPermissionSystemEnabled=false code path in the original method).
    /// </summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static object? NavSession_GetSecurityFilters(object self,
        int companyNameToken, int tableId, object securityFilterType,
        object? callingObject, object? securableObject) => null;

    /// <summary>
    /// Replacement for NavSession.SyncFormatSettings().
    /// Accesses cultureSettings (null in skeleton) → NRE.  Return a default FormatSettings.
    /// </summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static Microsoft.Dynamics.Nav.Runtime.FormatSettings NavSession_SyncFormatSettings(object? self)
        => new Microsoft.Dynamics.Nav.Runtime.FormatSettings();

    /// <summary>
    /// Replacement for NavApplicationObjectBase.TryInvoke(NavSession session, Action method).
    /// The real body calls session.CurrentMethodScope.GetTryMethodScope() which NREs on the
    /// skeleton session.  We run the method directly, catching trappable AL exceptions.
    /// </summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static bool NavApplicationObjectBase_TryInvoke(object? session, Action? method)
    {
        if (method == null) return false;
        try
        {
            method();
            return true;
        }
        catch (Exception ex)
        {
            // Rethrow untrappable errors; swallow trappable NavBaseExceptions.
            if (ex is Microsoft.Dynamics.Nav.Types.Exceptions.NavBaseException nbe && !nbe.UntrappableError)
                return false;
            throw;
        }
    }
}
