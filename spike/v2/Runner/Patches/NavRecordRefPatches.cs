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

    // NavStream.get_Target — same shape as NavRecordRef. Construct SharedNavStream parented
    // to the skeleton container. NavStream wraps AL InStream / OutStream variables; fixing
    // get_Target lets the NavStream ctor succeed and subsequent SharedStream assignment work.
    private static ConstructorInfo? _ctorSharedNavStream;
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static object NavStream_get_Target(object self)
    {
        var treeProp = self.GetType().GetProperty("Tree",
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.FlattenHierarchy);
        var tree = treeProp!.GetValue(self)!;
        if (_mTreeGetReferenceTarget == null)
            _mTreeGetReferenceTarget = tree.GetType().GetMethod("GetReferenceTarget",
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance,
                null, Type.EmptyTypes, null);
        if (_mTreeSetReferenceTarget == null)
            _mTreeSetReferenceTarget = tree.GetType().GetMethod("SetReferenceTarget",
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
        var existing = _mTreeGetReferenceTarget?.Invoke(tree, null);
        if (existing != null) return existing;

        var navNcl = AppDomain.CurrentDomain.GetAssemblies()
            .First(a => a.GetName().Name == "Microsoft.Dynamics.Nav.Ncl");
        if (_skeletonSharedObjectContainer == null)
        {
            var tContainer = navNcl.GetType("Microsoft.Dynamics.Nav.Runtime.TreeSharedObjectContainer")!;
            var tITree = navNcl.GetType("Microsoft.Dynamics.Nav.Runtime.ITreeObject")!;
            _skeletonSharedObjectContainer = tContainer.GetConstructor(new[] { tITree })!
                .Invoke(new object?[] { RootTreeStub });
        }
        if (_ctorSharedNavStream == null)
        {
            var tShared = navNcl.GetType("Microsoft.Dynamics.Nav.Runtime.SharedNavStream")!;
            var tIContainer = navNcl.GetType("Microsoft.Dynamics.Nav.Runtime.ITreeSharedObjectContainer")!;
            _ctorSharedNavStream = tShared.GetConstructor(
                BindingFlags.NonPublic | BindingFlags.Instance, null, new[] { tIContainer }, null)!;
        }
        var shared = _ctorSharedNavStream.Invoke(new object?[] { _skeletonSharedObjectContainer });
        _mTreeSetReferenceTarget?.Invoke(tree, new object?[] { shared });
        return shared;
    }

    // NavHttpRequestMessage.get_Target — same shape as NavRecordRef.Target. Construct
    // SharedNavHttpRequestMessage parented to the skeleton container.
    private static ConstructorInfo? _ctorSharedHttpReq;
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static object NavHttpRequestMessage_get_Target(object self)
    {
        if (_pNavRecordRefTree == null) // reuse Tree-property lookup logic per type below
            _pNavRecordRefTree = self.GetType().GetProperty("Tree",
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
        // Tree property is on TreeObject base — look up on the actual self type:
        var treeProp = self.GetType().GetProperty("Tree",
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.FlattenHierarchy);
        var tree = treeProp!.GetValue(self)!;
        if (_mTreeGetReferenceTarget == null)
            _mTreeGetReferenceTarget = tree.GetType().GetMethod("GetReferenceTarget",
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance,
                null, Type.EmptyTypes, null);
        if (_mTreeSetReferenceTarget == null)
            _mTreeSetReferenceTarget = tree.GetType().GetMethod("SetReferenceTarget",
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
        var existing = _mTreeGetReferenceTarget?.Invoke(tree, null);
        if (existing != null) return existing;

        var navNcl = AppDomain.CurrentDomain.GetAssemblies()
            .First(a => a.GetName().Name == "Microsoft.Dynamics.Nav.Ncl");
        if (_skeletonSharedObjectContainer == null)
        {
            var tContainer = navNcl.GetType("Microsoft.Dynamics.Nav.Runtime.TreeSharedObjectContainer")!;
            var tITree = navNcl.GetType("Microsoft.Dynamics.Nav.Runtime.ITreeObject")!;
            _skeletonSharedObjectContainer = tContainer.GetConstructor(new[] { tITree })!
                .Invoke(new object?[] { RootTreeStub });
        }
        if (_ctorSharedHttpReq == null)
        {
            var tShared = navNcl.GetType("Microsoft.Dynamics.Nav.Runtime.SharedNavHttpRequestMessage")!;
            var tIContainer = navNcl.GetType("Microsoft.Dynamics.Nav.Runtime.ITreeSharedObjectContainer")!;
            _ctorSharedHttpReq = tShared.GetConstructor(
                BindingFlags.NonPublic | BindingFlags.Instance, null, new[] { tIContainer }, null);
        }
        var shared = _ctorSharedHttpReq!.Invoke(new object?[] { _skeletonSharedObjectContainer });
        _mTreeSetReferenceTarget?.Invoke(tree, new object?[] { shared });
        return shared;
    }

    // NavHttpResponseMessageBase.get_Target — same shape. Construct SharedNavHttpResponseMessage
    // parented to skeleton container. SharedNavHttpResponseMessage(ITreeSharedObjectContainer) ctor
    // is safe — unlike HttpClient, it does NOT call InitializeToDefault/CreateClient.
    private static ConstructorInfo? _ctorSharedHttpResponseMsg;
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static object NavHttpResponseMessageBase_get_Target(object self)
    {
        var treeProp = self.GetType().GetProperty("Tree",
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.FlattenHierarchy);
        var tree = treeProp!.GetValue(self)!;
        if (_mTreeGetReferenceTarget == null)
            _mTreeGetReferenceTarget = tree.GetType().GetMethod("GetReferenceTarget",
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance,
                null, Type.EmptyTypes, null);
        if (_mTreeSetReferenceTarget == null)
            _mTreeSetReferenceTarget = tree.GetType().GetMethod("SetReferenceTarget",
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
        var existing = _mTreeGetReferenceTarget?.Invoke(tree, null);
        if (existing != null) return existing;

        var navNcl = AppDomain.CurrentDomain.GetAssemblies()
            .First(a => a.GetName().Name == "Microsoft.Dynamics.Nav.Ncl");
        if (_skeletonSharedObjectContainer == null)
        {
            var tContainer = navNcl.GetType("Microsoft.Dynamics.Nav.Runtime.TreeSharedObjectContainer")!;
            var tITree = navNcl.GetType("Microsoft.Dynamics.Nav.Runtime.ITreeObject")!;
            _skeletonSharedObjectContainer = tContainer.GetConstructor(new[] { tITree })!
                .Invoke(new object?[] { RootTreeStub });
        }
        if (_ctorSharedHttpResponseMsg == null)
        {
            var tShared = navNcl.GetType("Microsoft.Dynamics.Nav.Runtime.SharedNavHttpResponseMessage")!;
            var tIContainer = navNcl.GetType("Microsoft.Dynamics.Nav.Runtime.ITreeSharedObjectContainer")!;
            _ctorSharedHttpResponseMsg = tShared.GetConstructor(
                BindingFlags.NonPublic | BindingFlags.Instance, null, new[] { tIContainer }, null);
        }
        var shared = _ctorSharedHttpResponseMsg!.Invoke(new object?[] { _skeletonSharedObjectContainer });
        _mTreeSetReferenceTarget?.Invoke(tree, new object?[] { shared });
        return shared;
    }

    // NavHttpClient.get_Target — same Option-C shape. SharedNavHttpClient(ITreeSharedObjectContainer)
    // is safe: just calls base(sharedObjectContainer), no CreateClient or HTTP infrastructure.
    private static ConstructorInfo? _ctorSharedHttpClient;
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static object NavHttpClient_get_Target(object self)
    {
        var treeProp = self.GetType().GetProperty("Tree",
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.FlattenHierarchy);
        var tree = treeProp!.GetValue(self)!;
        if (_mTreeGetReferenceTarget == null)
            _mTreeGetReferenceTarget = tree.GetType().GetMethod("GetReferenceTarget",
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance,
                null, Type.EmptyTypes, null);
        if (_mTreeSetReferenceTarget == null)
            _mTreeSetReferenceTarget = tree.GetType().GetMethod("SetReferenceTarget",
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
        var existing = _mTreeGetReferenceTarget?.Invoke(tree, null);
        if (existing != null) return existing;

        var navNcl = AppDomain.CurrentDomain.GetAssemblies()
            .First(a => a.GetName().Name == "Microsoft.Dynamics.Nav.Ncl");
        if (_skeletonSharedObjectContainer == null)
        {
            var tContainer = navNcl.GetType("Microsoft.Dynamics.Nav.Runtime.TreeSharedObjectContainer")!;
            var tITree = navNcl.GetType("Microsoft.Dynamics.Nav.Runtime.ITreeObject")!;
            _skeletonSharedObjectContainer = tContainer.GetConstructor(new[] { tITree })!
                .Invoke(new object?[] { RootTreeStub });
        }
        if (_ctorSharedHttpClient == null)
        {
            var tShared = navNcl.GetType("Microsoft.Dynamics.Nav.Runtime.SharedNavHttpClient")!;
            var tIContainer = navNcl.GetType("Microsoft.Dynamics.Nav.Runtime.ITreeSharedObjectContainer")!;
            _ctorSharedHttpClient = tShared.GetConstructor(
                BindingFlags.NonPublic | BindingFlags.Instance, null, new[] { tIContainer }, null);
        }
        var shared = _ctorSharedHttpClient!.Invoke(new object?[] { _skeletonSharedObjectContainer });
        _mTreeSetReferenceTarget?.Invoke(tree, new object?[] { shared });
        return shared;
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

    // NavDialog.ALOpen — UI dialog open. Real impl reaches Tree.Session which is null.
    // No-op for skeleton tests; AL test code just needs the call to not throw.
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static void NavDialog_ALOpen(object self, Guid automationId, string message, object[] getters) { }

    // ALSystemString.ALLowercase / ALUppercase — real impls reach NavCurrentThread.Session.Culture
    // which is null on the skeleton. Fall back to InvariantCulture.
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static string ALSystemString_ALLowercase(string value)
        => string.IsNullOrEmpty(value) ? string.Empty : value.ToLower(System.Globalization.CultureInfo.InvariantCulture);

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static string ALSystemString_ALUppercase(string value)
        => string.IsNullOrEmpty(value) ? string.Empty : value.ToUpper(System.Globalization.CultureInfo.InvariantCulture);

    // RecordImplementation.GetActiveCompany — touched by NavRecord.CloneRecord.
    // Real impl: Session.Database.CompanyTokens.Get(tableState.CompanyNameToken). Both
    // Database and tableState are null on the skeleton; return empty string. AL code
    // that compares company names will see "" == "" which is fine for most tests.
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static string RecordImplementation_GetActiveCompany(object self) => "";

    // NavSession.GetPermissionSet — skeleton has no Permissions object, causing NREs on
    // permission checks during CalcFields, HasReadPermission, HasWritePermission, etc.
    // NCL already ships VirtualDataProvider.PermissionSet (a private singleton of
    // VirtualTablePermissionSet) whose HasPermissions returns true and VerifyPermissions
    // is a no-op. We return it for all GetPermissionSet calls on the skeleton.
    private static object? _allGrantedPermSet;

    private static object GetAllGrantedPermSet()
    {
        if (_allGrantedPermSet != null) return _allGrantedPermSet;
        var navNcl = AppDomain.CurrentDomain.GetAssemblies()
            .First(a => a.GetName().Name == "Microsoft.Dynamics.Nav.Ncl");
        var tVdp = navNcl.GetType("Microsoft.Dynamics.Nav.Runtime.VirtualDataProvider")!;
        var fPermSet = tVdp.GetField("PermissionSet",
            BindingFlags.NonPublic | BindingFlags.Static)!;
        _allGrantedPermSet = fPermSet.GetValue(null)!;
        return _allGrantedPermSet;
    }

    // Overload: GetPermissionSet(NavApplicationObjectBase, int, ApplicationObjectId)
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static object NavSession_GetPermissionSet_ByObjectId(
        object self, object callingObject, int companyNameToken,
        Microsoft.Dynamics.Nav.Types.ApplicationObjectId applicationObjectId)
        => GetAllGrantedPermSet();

    // Overload: GetPermissionSet(NavApplicationObjectBase, int, IEnumerable<ApplicationObjectId>)
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static object NavSession_GetPermissionSet_ByObjectIds(
        object self, object callingObject, int companyNameToken, object applicationObjects)
        => GetAllGrantedPermSet();
}
