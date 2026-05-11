// RecordWritePatches — registration AND replacements for the NavRecord write/find path
// plus the underlying TempTableDataProvider data-access plumbing.
//
// Registration: ApplyRecordPatches(navNcl) wires up every JMP-hook required for AL records
// to behave correctly in headless mode — NavRecordHandle.CreateTarget, the
// NavSession DataAccessSource/Database getters, the TempTableDataProvider ctor, the
// CollationAwareStringComparer, NavRecord.Dispose, RecordImplementation permission/security
// no-ops, the SystemId UUID hook, and the NavRecord.InsertAsync /
// InternalFindRecordWithoutCheckingValuesAsync replacements.
//
// Replacements: NavRecord.InsertAsync and InternalFindRecordWithoutCheckingValuesAsync —
// the original bodies dispatch through trigger/event/extension and permission-event
// telemetry that NREs on the skeleton session. We bypass those and call the underlying
// dataAccess directly. The TempTableDataProvider hooked up by RecordPatches.cs handles
// actual storage.
using System.Reflection;
using System.Runtime.CompilerServices;

namespace AlRunnerV2;

public static partial class BcRuntime
{
    /// <summary>
    /// All record / data-access JMP-hooks. Called from ApplyAllPatches once during
    /// runtime bootstrap, after NavSession / NavMethodScope / NavApplicationObjectBase
    /// have been wired up (the record path needs a usable skeleton session).
    /// </summary>
    private static void ApplyRecordPatches(Assembly navNcl)
    {
        // Locals frequently referenced below — resolved once, used many times.
        var sessType = navNcl.GetType("Microsoft.Dynamics.Nav.Runtime.NavSession");

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

            // CalcNumeric — the real override throws NotSupportedException; our replacement
            // iterates in-memory rows via the private Filter() method to compute count/sum/avg.
            var ttdpCalcNumeric = ttdpType.GetMethod("CalcNumeric",
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            if (ttdpCalcNumeric != null)
                Hook(ttdpCalcNumeric,
                    typeof(AlRunnerV2.Patches.RecordPatches).GetMethod("TempTableDataProvider_CalcNumeric",
                        BindingFlags.Public | BindingFlags.Static)!,
                    "TempTableDataProvider.CalcNumeric");
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
                _mRecordImplementationRenameRecordAsync = recImplTypeForWrites.GetMethod(
                    "RenameRecordAsync",
                    BindingFlags.NonPublic | BindingFlags.Instance);
            }
            _mNavRecordCloneRecord = navRecordType.GetMethod("CloneRecord",
                BindingFlags.Public | BindingFlags.Instance);
            // W-8a PR1: bypass-drain for InsertAsync. The real Ncl body
            // (NavRecord-275.cs:2832-2906) now runs end-to-end:
            //   - DataModificationListener.Instance: null-checked → no-op when null
            //   - WriteEventRaised: gated on Session.IsEventSessionRecorderEnabled (false)
            //   - NavCurrentThread.ResolveAppGroup: falls back to NavAppGroup.BaseGroup
            //   - NavGlobalTriggers.InsertAsync: short-circuits via IsGlobalTriggerImplemented hook
            //   - metaTable.IsInsertTriggerDefined: reflects on Record{id}.OnInsert override
            //     (works after RecordPatches.NCLMetaApplicationObject_get_ApplicationObjectClrType
            //     hierarchy-walk fix to discover inherited `objectId` non-public field)
            //   - metaTable.IsEventSubscribed: false until subscribers registered (PR2 work)
            //   - ParentCompany.TrackChanges: null-tolerant, real body handles
            //
            // The InsertAsync hook is intentionally NOT installed below so the real
            // Ncl trigger-dispatch path runs. NavRecord_InsertAsync replacement body
            // is left in place for reference / quick re-enable if needed.
            //
            // Modify/Delete/Rename remain bypassed for PR1 scope; W-8 follow-on PR
            // will drain those in lockstep once the Insert path is validated.

            // W-8a PR2: ModifyAsync drain DEFERRED — blocked on a bounded-depth recursion
            // guard. Draining the bypass exposes
            // Codeunit108002.Modify_WithRecursiveTrigger_DoesNotStackOverflow whose OnModify
            // calls Rec.Modify(true). Without a session-level depth-counter guard the process
            // stack-overflows. Real BC raises a runtime error after a few hundred frames; we
            // need a faithful equivalent in MethodScopePatches.cs before this drain can land.
            // Once the guard lands, the block below should mirror the Delete/Rename drain shape.
            var modifyAsync4 = navRecordType.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                .FirstOrDefault(m => m.Name == "ModifyAsync" && m.GetParameters().Length == 4);
            if (modifyAsync4 != null && _mRecordImplementationModifyRecordAsync != null)
                Hook(modifyAsync4, nameof(NavRecord_ModifyAsync), "NavRecord.ModifyAsync(DataError,bool,bool,bool)");

            // W-8a PR2: bypass-drain for DeleteAsync. Same rationale as ModifyAsync above:
            //   - IsDeleteTriggerDefined powered by inherited-objectId field-walk fix.
            //   - TrackChanges no-ops cleanly.
            //
            // The DeleteAsync hook is intentionally NOT installed. NavRecord_DeleteAsync
            // replacement body is left in place for reference.
            var deleteAsync4 = navRecordType.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                .FirstOrDefault(m => m.Name == "DeleteAsync" && m.GetParameters().Length == 4);

            // W-8a PR2: bypass-drain for RenameAsync. Signature differs from the others:
            //   NavRecord.RenameAsync(DataError, bool runApplicationTrigger, bool runGlobalTrigger, NavValue[])
            //   - IsRenameTriggerDefined powered by inherited-objectId field-walk fix.
            //   - TrackChanges no-ops cleanly.
            //
            // The RenameAsync hook is intentionally NOT installed. NavRecord_RenameAsync
            // replacement body is left in place for reference.
            var renameAsync4 = navRecordType.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                .FirstOrDefault(m => m.Name == "RenameAsync" && m.GetParameters().Length == 4);
        }
        // RecordLink.MoveLinksAsync(NavRecord, NavRecord) — called by NavRecord.RenameAsync after
        // the rename commit to move record-link rows from old PK to new PK. Calls
        // RecordLink.IsRecordLinkTableLocked(ITreeObject) which NREs on the skeleton session
        // (no real lock-tracking infrastructure). No-op is safe in headless mode: there are no
        // record links to move, and the rename itself has already committed before this call.
        var recordLinkType = navNcl.GetType("Microsoft.Dynamics.Nav.Runtime.RecordLink");
        if (recordLinkType != null)
        {
            var moveLinks = recordLinkType.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)
                .FirstOrDefault(m => m.Name == "MoveLinksAsync" && m.GetParameters().Length == 2);
            if (moveLinks != null)
                Hook(moveLinks, nameof(ReturnValueTask2), "RecordLink.MoveLinksAsync(NavRecord,NavRecord)");
        }
        // NavRecord.UpdateReferencesOnRenameAsync(List<...>, NavRecord) — called by RenameAsync to
        // cascade PK changes to related tables. Calls NCLMetaTable.GetReferencingRelations() →
        // ComputeReferencingRelations(NavAppGroup,...) which NREs because the skeleton session has
        // no real AppGroup-aware metadata catalog. No-op is safe in headless mode: skeleton tables
        // have no foreign-key references to cascade.
        if (navRecordType != null)
        {
            var updateRefs = navRecordType.GetMethods(BindingFlags.NonPublic | BindingFlags.Instance)
                .FirstOrDefault(m => m.Name == "UpdateReferencesOnRenameAsync" && m.GetParameters().Length == 2);
            if (updateRefs != null)
                Hook(updateRefs, nameof(ReturnValueTask3), "NavRecord.UpdateReferencesOnRenameAsync");
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
    }

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
    /// Replacement for NavRecord.ModifyAsync(DataError, bool, bool, bool).
    /// Same bypass pattern as InsertAsync — skips trigger/event dispatch that NREs on skeleton
    /// session and delegates to RecordImplementation.ModifyRecordAsync directly.
    /// </summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static System.Threading.Tasks.ValueTask<bool> NavRecord_ModifyAsync(
        Microsoft.Dynamics.Nav.Runtime.NavRecord self,
        Microsoft.Dynamics.Nav.Types.DataError errorLevel,
        bool runApplicationTrigger,
        bool runGlobalTrigger,
        bool isBulkModify)
    {
        try
        {
            var recImpl = _fNavRecordRecordImplementation?.GetValue(self);
            if (recImpl == null || _mRecordImplementationModifyRecordAsync == null)
                return new System.Threading.Tasks.ValueTask<bool>(false);
            var result = _mRecordImplementationModifyRecordAsync.Invoke(recImpl, new object?[] { errorLevel });
            if (result is System.Threading.Tasks.ValueTask<bool> vt) return vt;
            return new System.Threading.Tasks.ValueTask<bool>(true);
        }
        catch (TargetInvocationException tie) when (tie.InnerException != null)
        {
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw(tie.InnerException);
            return default;
        }
    }

    /// <summary>
    /// Replacement for NavRecord.DeleteAsync(DataError, bool, bool, bool).
    /// Same bypass pattern as InsertAsync — skips trigger/event dispatch that NREs on skeleton
    /// session and delegates to RecordImplementation.DeleteRecordAsync directly.
    /// </summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static System.Threading.Tasks.ValueTask<bool> NavRecord_DeleteAsync(
        Microsoft.Dynamics.Nav.Runtime.NavRecord self,
        Microsoft.Dynamics.Nav.Types.DataError errorLevel,
        bool runApplicationTrigger,
        bool isCalledFromUI,
        bool isBulkDelete)
    {
        try
        {
            var recImpl = _fNavRecordRecordImplementation?.GetValue(self);
            if (recImpl == null || _mRecordImplementationDeleteRecordAsync == null)
                return new System.Threading.Tasks.ValueTask<bool>(false);
            var result = _mRecordImplementationDeleteRecordAsync.Invoke(recImpl, new object?[] { errorLevel });
            if (result is System.Threading.Tasks.ValueTask<bool> vt) return vt;
            return new System.Threading.Tasks.ValueTask<bool>(true);
        }
        catch (TargetInvocationException tie) when (tie.InnerException != null)
        {
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw(tie.InnerException);
            return default;
        }
    }

    /// <summary>
    /// Replacement for NavRecord.RenameAsync(DataError, bool, bool, NavValue[]).
    /// Bypasses trigger/event dispatch. Clones self, sets new PK field values on the clone
    /// using NCLMetaField directly (avoids GetFieldByNo), then calls
    /// RecordImplementation.RenameRecordAsync(errorLevel, renamedRecord).
    /// </summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static System.Threading.Tasks.ValueTask<bool> NavRecord_RenameAsync(
        Microsoft.Dynamics.Nav.Runtime.NavRecord self,
        Microsoft.Dynamics.Nav.Types.DataError errorLevel,
        bool runApplicationTrigger,
        bool runGlobalTrigger,
        Microsoft.Dynamics.Nav.Runtime.NavValue[] values)
    {
        try
        {
            var recImpl = _fNavRecordRecordImplementation?.GetValue(self);
            if (recImpl == null || _mRecordImplementationRenameRecordAsync == null || _mNavRecordCloneRecord == null)
                return new System.Threading.Tasks.ValueTask<bool>(false);

            values ??= Array.Empty<Microsoft.Dynamics.Nav.Runtime.NavValue>();
            var key = self.MetaTable.GetKeyByIndex(0);
            if (values.Length < key.KeyFieldCount)
                return new System.Threading.Tasks.ValueTask<bool>(false);

            var newRecord = (Microsoft.Dynamics.Nav.Runtime.NavRecord)_mNavRecordCloneRecord.Invoke(
                self, new object[] { self, false, true })!;
            for (int i = 0; i < key.KeyFieldCount; i++)
            {
                var field = key.GetKeyFieldByIndex(i);
                newRecord.SetFieldValue(field, values[i]);
            }
            var result = _mRecordImplementationRenameRecordAsync.Invoke(recImpl, new object[] { errorLevel, newRecord });
            if (result is System.Threading.Tasks.ValueTask<bool> vt) return vt;
            return new System.Threading.Tasks.ValueTask<bool>(true);
        }
        catch (TargetInvocationException tie) when (tie.InnerException != null)
        {
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw(tie.InnerException);
            return default;
        }
    }
}
