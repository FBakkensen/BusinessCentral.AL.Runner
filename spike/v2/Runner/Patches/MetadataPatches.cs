// MetadataPatches — manufactures skeleton NavSystemTenant + NCLMetadata, injects them
// into the real NavTenantCollection so NavGlobal.NCLMetadata, NavGlobal.SystemTenant,
// and the chain of NavGlobal.* getters return non-null objects rather than NRE'ing on
// `Tenants.SystemTenant == null`. Field-poke based: no method bodies are rewritten.
//
// Field NREs *inside* NCLMetadata are then patched iteratively: when a corpus call site
// dereferences a null field on the skeleton, FieldPoke a sane default in the static init
// below.
using System.Reflection;
using System.Runtime.CompilerServices;
using AlRunnerV2.Infrastructure;

namespace AlRunnerV2;

public static partial class BcRuntime
{
    private static object? _skeletonNCLMetadata;
    private static object? _skeletonSystemTenant;

    /// <summary>
    /// Called from ApplyAllPatches *after* the real NavEnvironment ctor has run successfully
    /// (`InstantiateStandaloneNavEnvironment(true,false)`). At that point
    /// <c>NavEnvironment.Instance.Tenants</c> is a real, non-null <c>NavTenantCollection</c> —
    /// but its <c>systemTenant</c> field is null because <c>AddSystemTenant</c> requires a real
    /// SQL connection. We manufacture a skeleton via <c>GetUninitializedObject</c> and write it
    /// into the field directly.
    /// </summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static void InjectSkeletonSystemTenant(Assembly navNcl)
    {
        var nclMetadataType    = navNcl.GetType("Microsoft.Dynamics.Nav.Runtime.NCLMetadata");
        var systemTenantType   = navNcl.GetType("Microsoft.Dynamics.Nav.Runtime.NavSystemTenant");
        var navTenantType      = navNcl.GetType("Microsoft.Dynamics.Nav.Runtime.NavTenant");
        var envType            = _navEnvironmentType!;
        if (nclMetadataType == null || systemTenantType == null || navTenantType == null)
        {
            Console.Error.WriteLine("[BcRuntime] InjectSkeletonSystemTenant: type lookup failed");
            return;
        }

        // 1. Build skeleton NCLMetadata (no ctor call — its ctor needs NavDatabase).
        _skeletonNCLMetadata = RuntimeHelpers.GetUninitializedObject(nclMetadataType);

        // 2. Build skeleton NavSystemTenant.
        _skeletonSystemTenant = RuntimeHelpers.GetUninitializedObject(systemTenantType);

        // 3. Make NavTenant.IsDisposed return false on the skeleton: requires `disposed=false`
        //    (default) AND `Tree` non-null AND Tree.IsDisposed=false. Reuse the root tree we
        //    already built around _skeletonRootScope.
        var disposedField = navTenantType.GetField("disposed", BindingFlags.NonPublic | BindingFlags.Instance);
        if (disposedField != null) FieldPoke.SetInstance(disposedField, _skeletonSystemTenant, false);
        var treeBackingField = navTenantType.GetField("<Tree>k__BackingField",
            BindingFlags.NonPublic | BindingFlags.Instance);
        if (treeBackingField != null && _skeletonRootScope != null)
        {
            // _skeletonRootScope.Tree is a TreeHandler with hostObject != null → IsDisposed==false.
            var rootScopeTree = _skeletonRootScope.GetType()
                .GetProperty("Tree", BindingFlags.Public | BindingFlags.Instance)?
                .GetValue(_skeletonRootScope);
            if (rootScopeTree != null)
                FieldPoke.SetInstance(treeBackingField, _skeletonSystemTenant, rootScopeTree);
        }

        // 4. Wire skeleton NCLMetadata into the skeleton SystemTenant's `nclMetadata` field.
        var stNclField = systemTenantType.GetField("nclMetadata", BindingFlags.NonPublic | BindingFlags.Instance);
        if (stNclField != null)
            FieldPoke.SetInstance(stNclField, _skeletonSystemTenant, _skeletonNCLMetadata);

        // 5. Inject skeleton SystemTenant into the real Tenants collection.
        var tenantsProp = envType.GetProperty("Tenants",
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
        var instance = envType.GetField("instance", BindingFlags.NonPublic | BindingFlags.Static)?.GetValue(null);
        var tenants = (instance != null && tenantsProp != null) ? tenantsProp.GetValue(instance) : null;
        if (tenants != null)
        {
            var tenantsType = tenants.GetType();
            var stField = tenantsType.GetField("systemTenant", BindingFlags.NonPublic | BindingFlags.Instance);
            if (stField != null)
            {
                FieldPoke.SetInstance(stField, tenants, _skeletonSystemTenant);
                Console.Error.WriteLine("[BcRuntime] InjectSkeletonSystemTenant: skeleton wired into Tenants.systemTenant");
            }
            else
                Console.Error.WriteLine("[BcRuntime] InjectSkeletonSystemTenant: systemTenant field NOT FOUND on " + tenantsType.FullName);
        }
        else
        {
            Console.Error.WriteLine("[BcRuntime] InjectSkeletonSystemTenant: Tenants is null — env ctor likely fell back to skeleton");
        }
    }
}
