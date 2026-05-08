// NavRecordRefPatches — replacements for NavRecordRef.get_Target and small siblings.
//
// NavRecordRef.get_Target tries to construct a SharedRecordRef using
//     base.Tree.Session.Company.SharedObjects
// which NREs on the skeleton because Session.Company.SharedObjects is null.
// Replacement constructs a SharedRecordRef using a process-wide skeleton
// TreeSharedObjectContainer parented to RootTreeStub, and stashes it via
// Tree.SetReferenceTarget so subsequent gets see the cached value.
using System.Reflection;
using System.Runtime.CompilerServices;

namespace AlRunnerV2;

public static partial class BcRuntime
{
    private static object? _skeletonSharedObjectContainer;
    private static ConstructorInfo? _ctorSharedRecordRef;
    private static MethodInfo? _mTreeGetReferenceTarget;
    private static MethodInfo? _mTreeSetReferenceTarget;
    private static PropertyInfo? _pNavRecordRefTree;

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static object NavRecordRef_get_Target(object self)
    {
        // Reflection paths are cached after first call.
        if (_pNavRecordRefTree == null)
            _pNavRecordRefTree = self.GetType().GetProperty("Tree",
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
        var tree = _pNavRecordRefTree!.GetValue(self)!;

        if (_mTreeGetReferenceTarget == null)
            _mTreeGetReferenceTarget = tree.GetType().GetMethod("GetReferenceTarget",
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance,
                null, Type.EmptyTypes, null);
        if (_mTreeSetReferenceTarget == null)
            _mTreeSetReferenceTarget = tree.GetType().GetMethod("SetReferenceTarget",
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);

        var existing = _mTreeGetReferenceTarget?.Invoke(tree, null);
        if (existing != null) return existing;

        // Construct SharedRecordRef using a skeleton TreeSharedObjectContainer.
        var navNcl = AppDomain.CurrentDomain.GetAssemblies()
            .First(a => a.GetName().Name == "Microsoft.Dynamics.Nav.Ncl");
        if (_skeletonSharedObjectContainer == null)
        {
            var tContainer = navNcl.GetType("Microsoft.Dynamics.Nav.Runtime.TreeSharedObjectContainer")!;
            var tITree = navNcl.GetType("Microsoft.Dynamics.Nav.Runtime.ITreeObject")!;
            var ctor = tContainer.GetConstructor(new[] { tITree });
            _skeletonSharedObjectContainer = ctor!.Invoke(new object?[] { RootTreeStub });
        }
        if (_ctorSharedRecordRef == null)
        {
            var tShared = navNcl.GetType("Microsoft.Dynamics.Nav.Runtime.SharedRecordRef")!;
            var tIContainer = navNcl.GetType("Microsoft.Dynamics.Nav.Runtime.ITreeSharedObjectContainer")!;
            _ctorSharedRecordRef = tShared.GetConstructor(
                BindingFlags.NonPublic | BindingFlags.Instance, null, new[] { tIContainer }, null);
        }
        var srr = _ctorSharedRecordRef!.Invoke(new object?[] { _skeletonSharedObjectContainer });
        _mTreeSetReferenceTarget?.Invoke(tree, new object?[] { srr });
        return srr;
    }
}
