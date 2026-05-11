// MethodScopePatches — replacements for NavMethodScope ctor + AssertError + ProcessException.
//
// NavMethodScope is the per-AL-frame execution unit. The real ctor body dereferences
// many session/scope properties that NRE on a skeleton; we replace it with a minimal
// version that only sets the fields the test harness actually depends on.
//
// AssertError mediates `asserterror` blocks. The real implementation rolls back the
// session transaction, which NREs on the skeleton; we replicate the pass/fail semantics
// without touching the (non-existent) transaction layer.
//
// Recursion guard: a [ThreadStatic] depth counter is incremented in the ctor and
// decremented in the Dispose(bool) hook. When depth exceeds MaxRecursionDepth,
// NavNCLDialogException is thrown so AL `asserterror` can trap it.
using System.Reflection;
using System.Runtime.CompilerServices;
using AlRunnerV2.Infrastructure;

namespace AlRunnerV2;

public static partial class BcRuntime
{
    [ThreadStatic] private static int _navMethodScopeDepth;
    private const int MaxRecursionDepth = 500;
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
    ///   NavMethodScope.parentScope   — the actual current scope at ctor entry (NOT always root),
    ///                                  so NavMethodScope_Dispose can restore CurrentMethodScope correctly
    ///   NavMethodScope.flags         — GetMethodScopeFlags() on the concrete subtype
    ///   NavMethodScope.StackDepth    — 2 (root=1, one level deeper)
    ///   NavMethodScope.TopLevelApplicationObject — applicationObject
    ///   NavSession.CurrentMethodScope (backing field) — self
    ///
    /// Recursion guard: increments _navMethodScopeDepth and throws NavNCLDialogException if
    /// MaxRecursionDepth is exceeded, so AL `asserterror` can trap recursive-trigger loops.
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
        // Capture the actual current scope (our parent) BEFORE we update CurrentMethodScope.
        object? actualParent = _fSessCurrentScope != null && _skeletonSession != null
            ? _fSessCurrentScope.GetValue(_skeletonSession)
            : null;
        actualParent ??= _skeletonRootScope;

        // Recursion guard: increment depth and throw if the limit is exceeded.
        // Decrement before throwing to keep the counter balanced.
        _navMethodScopeDepth++;
        if (_navMethodScopeDepth > MaxRecursionDepth)
        {
            _navMethodScopeDepth--;
            var msg = $"Maximum recursion depth ({MaxRecursionDepth}) exceeded";
            throw _navNCLDialogExceptionType != null
                ? (Exception)Activator.CreateInstance(_navNCLDialogExceptionType, msg)!
                : new InvalidOperationException(msg);
        }

        try
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
            // 3. NavMethodScope.parentScope = actual parent scope at entry (enables correct
            //    CurrentMethodScope restoration in NavMethodScope_Dispose).
            if (_fMsParentScope != null) FieldPoke.SetInstance(_fMsParentScope, self, actualParent);
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
        catch
        {
            // Unexpected failure during initialization: keep counter balanced.
            _navMethodScopeDepth--;
            throw;
        }
    }

    /// <summary>
    /// Replacement for NavMethodScope.Dispose(bool disposing).
    ///
    /// JmpHook on the virtual override intercepts the virtual dispatch from TreeObject.Dispose()
    /// (which calls `this.Dispose(true)` via callvirt). Our replacement:
    ///   1. Decrements the ThreadStatic recursion depth counter.
    ///   2. Restores session.CurrentMethodScope to parentScope (the actual parent captured at
    ///      ctor entry), mirroring what the original Dispose(bool) body does.
    ///
    /// The original 89-byte body (resource cleanup, base.Dispose call) is not run; for the
    /// headless test harness this is acceptable — no real transactions, cancellation, or tree
    /// deregistration is needed for test correctness.
    /// </summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static void NavMethodScope_Dispose(object? self, bool disposing)
    {
        if (!disposing) return;

        _navMethodScopeDepth = Math.Max(0, _navMethodScopeDepth - 1);

        // Restore CurrentMethodScope to the scope's parent (captured at ctor entry in parentScope).
        if (_fSessCurrentScope != null && _skeletonSession != null && _fMsParentScope != null)
        {
            var msScope = self as Microsoft.Dynamics.Nav.Runtime.NavMethodScope;
            var parent = msScope != null ? _fMsParentScope.GetValue(msScope) : _skeletonRootScope;
            FieldPoke.SetInstance(_fSessCurrentScope, _skeletonSession, parent ?? _skeletonRootScope);
        }
    }

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
        catch (Exception ex)
        {
            // Store the caught exception in skeleton session.lastException so that
            // ALSystemErrorHandling.get_ALGetLastErrorText (and the patched override
            // in MiscPatches) can return its message — Assert.ExpectedError / Library
            // Assert depend on this round-trip.
            StoreLastExceptionOnSkeletonSession(ex);
            return; /* asserterror passed: body threw something */
        }
        throw new Microsoft.Dynamics.Nav.Types.Exceptions.NavNCLAssertErrorException();
    }

    private static System.Reflection.FieldInfo? _fSessLastException;
    private static void StoreLastExceptionOnSkeletonSession(Exception ex)
    {
        if (_skeletonSession == null) return;
        if (_fSessLastException == null)
            _fSessLastException = _skeletonSession.GetType().GetField("lastException",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        _fSessLastException?.SetValue(_skeletonSession, ex);
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
    /// Replacement for ALMethodScope.AssignScopeId(). Real body chains through
    /// `Session.NCLMetadata.CodeEnvironment.AssignScopeId(this)` — NCLMetadata is null
    /// on the skeleton session and NREs. No-op: scopeId stays null and ALMethodScope's
    /// `ScopeId` getter tolerates that (`value.HasValue ? value.Value : 0`).
    /// </summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static void ALMethodScope_AssignScopeId(object? self) { }
}
