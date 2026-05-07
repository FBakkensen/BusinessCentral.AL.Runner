// BcRuntime.Replacements — every static method that JMP-hooks redirect to.
//
// Each method's signature must be calling-convention-compatible with the BC instance
// method it replaces:
//   * Instance methods → static replacement with a leading `object self` parameter.
//   * Reference args → `object` (CLR pointer-sized) is fine; concrete types only
//     matter when the body needs to use the value.
//   * Async methods → return `ValueTask<T>` directly without an `async` modifier;
//     the JMP intercepts the kickoff stub before the state machine runs.
//
// Methods are grouped by the area they patch. The actual hook installation lives in
// BcRuntime.cs (ApplyAllPatches); this file only owns the bodies they call.
using System.Reflection;
using System.Runtime.CompilerServices;
using AlRunnerV2.Infrastructure;

namespace AlRunnerV2;

public static partial class BcRuntime
{
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
