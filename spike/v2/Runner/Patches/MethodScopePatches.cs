// MethodScopePatches — replacements for NavMethodScope ctor + AssertError + ProcessException.
//
// NavMethodScope is the per-AL-frame execution unit. The real ctor body dereferences
// many session/scope properties that NRE on a skeleton; we replace it with a minimal
// version that only sets the fields the test harness actually depends on.
//
// AssertError mediates `asserterror` blocks. The real implementation rolls back the
// session transaction, which NREs on the skeleton; we replicate the pass/fail semantics
// without touching the (non-existent) transaction layer.
using System.Reflection;
using System.Runtime.CompilerServices;
using AlRunnerV2.Infrastructure;

namespace AlRunnerV2;

public static partial class BcRuntime
{
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
    /// Replacement for NavMethodScope.ProcessException(Exception).
    /// The real body calls session.Diagnostics.SendExceptionTag(...) when the exception is an NRE,
    /// but session.Diagnostics is null on the skeleton session → secondary NRE that masks the original.
    /// Returning false immediately means "exception not handled here" so the original exception
    /// propagates cleanly through Run()'s outer catch clauses.
    /// </summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static bool NavMethodScope_ProcessException(object? self, Exception? exception) => false;
}
