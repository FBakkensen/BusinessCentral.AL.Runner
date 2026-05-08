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

    // NavStringValue.CompareTo(NavStringValue) — real impl reaches NavCurrentThread.Session.Culture
    // which is null on the skeleton. Fall back to ordinal comparison via the public Value property.
    private static PropertyInfo? _pNavStringValue_Value;
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static int NavStringValue_CompareTo(object self, object? other)
    {
        if (other == null) return 1;
        if (ReferenceEquals(other, self)) return 0;
        if (_pNavStringValue_Value == null)
            _pNavStringValue_Value = self.GetType().GetProperty("Value",
                BindingFlags.Public | BindingFlags.Instance);
        var sv = _pNavStringValue_Value!.GetValue(self) as string ?? "";
        var ov = _pNavStringValue_Value!.GetValue(other) as string ?? "";
        return string.Compare(sv, ov, StringComparison.Ordinal);
    }

    // ALSystemNumeric.ALRandomize / ALRandom — real impls hit NavCurrentThread.Session.Random
    // which is null on the skeleton. Back the statics with a process-static Random.
    private static System.Random _alRandom = new System.Random();
    private static readonly object _alRandomLock = new object();

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static void ALSystemNumeric_ALRandomize()
    {
        lock (_alRandomLock) _alRandom = new System.Random();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static void ALSystemNumeric_ALRandomize_Seed(int seed)
    {
        lock (_alRandomLock) _alRandom = new System.Random(seed);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static int ALSystemNumeric_ALRandom(int maxNumber)
    {
        if (maxNumber < 0) maxNumber = -maxNumber;
        if (maxNumber == 0) maxNumber = 1;
        lock (_alRandomLock) return _alRandom.Next(maxNumber) + 1;
    }

    // RecordImplementation.GetActiveCompany — touched by NavRecord.CloneRecord.
    // Real impl: Session.Database.CompanyTokens.Get(tableState.CompanyNameToken). Both
    // Database and tableState are null on the skeleton; return empty string. AL code
    // that compares company names will see "" == "" which is fine for most tests.
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static string RecordImplementation_GetActiveCompany(object self) => "";
}
