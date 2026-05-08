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
using JmpHook = AlRunnerV2.Infrastructure.JmpHook;

namespace AlRunnerV2;

public static partial class BcRuntime
{
    private static object? _skeletonNCLMetadata;
    private static object? _skeletonSystemTenant;

    /// <summary>Exposes the manufactured skeleton NCLMetadata so other patch files can
    /// FieldPoke into its caches (e.g. populate per-table NCLMetaTable entries).</summary>
    public static object? SkeletonNCLMetadata => _skeletonNCLMetadata;

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

        // GetEntryDictionary() walks `metadataCacheEntries[(int)objectType]`. With null arrays,
        // every call NREs at `dictionaries.Length`. Populate both with empty ConcurrentDictionary
        // entries sized to the ObjectType enum so callers get a defined "not in cache" path —
        // which translates to `NavNCLApplicationObjectNotFoundException` rather than NRE.
        var navTypesAsm0 = AppDomain.CurrentDomain.GetAssemblies()
            .First(a => a.GetName().Name == "Microsoft.Dynamics.Nav.Types");
        var objectTypeEnum = navTypesAsm0.GetType("Microsoft.Dynamics.Nav.Types.ObjectType");
        var enumSize = objectTypeEnum != null ? Enum.GetValues(objectTypeEnum).Length : 27;

        void PopulateCacheArray(string fieldName, Type entryValueType)
        {
            var f = nclMetadataType.GetField(fieldName, BindingFlags.NonPublic | BindingFlags.Instance);
            if (f == null) return;
            var dictType = typeof(System.Collections.Concurrent.ConcurrentDictionary<,>)
                .MakeGenericType(typeof(int), entryValueType);
            var arr = Array.CreateInstance(dictType, enumSize);
            for (int i = 0; i < enumSize; i++)
                arr.SetValue(Activator.CreateInstance(dictType), i);
            FieldPoke.SetInstance(f, _skeletonNCLMetadata, arr);
        }
        var entryT  = navNcl.GetType("Microsoft.Dynamics.Nav.Runtime.NCLMetadataCacheEntry");
        var extEntT = navNcl.GetType("Microsoft.Dynamics.Nav.Runtime.NCLMetadataExtensionCacheEntry");
        if (entryT  != null) PopulateCacheArray("metadataCacheEntries",          entryT);
        if (extEntT != null) PopulateCacheArray("metadataExtensionCacheEntries", extEntT);

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

        // 5. Populate cache hook target — NCLMetaApplicationObject.Populate is called
        //    from NCLMetadata.GetMetaApplicationObjectInternal when the cache entry's
        //    `metadataLoaded` flag is false. Our hand-built NCLMetaTable instances have
        //    no NCLObjectXmlMetadataLoader / MetaObjectCache backing, so the original
        //    Populate would NRE inside LoadTableMetadata. Replace it with a no-op:
        //    the cache populator already FieldPokes everything we need (fields, keys).
        //    Field-poking metadataLoaded=true alone is not enough — JIT inlines the
        //    MetadataLoaded getter and the runtime sometimes still drops into Populate
        //    along the lock-retry path; hooking the method body short-circuits that.
        var nclAppObjType = navNcl.GetType("Microsoft.Dynamics.Nav.Runtime.NCLMetaApplicationObject");
        if (nclAppObjType != null)
        {
            var populate = nclAppObjType.GetMethod("Populate",
                BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance,
                null, Type.EmptyTypes, null);
            if (populate != null)
            {
                JmpHook.Apply(populate,
                    typeof(BcRuntime).GetMethod(nameof(BcRuntime.NoOp_OneArg),
                        BindingFlags.Public | BindingFlags.Static)!,
                    "NCLMetaApplicationObject.Populate");
                Console.Error.WriteLine("[BcRuntime] NCLMetaApplicationObject.Populate hooked → NoOp");
            }

            // CompileAndLoadClrObject — same story as Populate. Original calls
            // `nclMetaObjectCLRTypeContainer.ApplicationObjectClrType = LoadClrType();`
            // which NREs (container is null on hand-built metas; LoadClrType walks
            // ObjectLoader which is null). The downstream property getter
            // ApplicationObjectClrType is already JMP-hooked
            // (NCLMetaApplicationObject_get_ApplicationObjectClrType) to look up
            // Record{ID} from the loaded test assembly directly, so making this a
            // no-op is safe.
            var compileLoad = nclAppObjType.GetMethod("CompileAndLoadClrObject",
                BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance,
                null, Type.EmptyTypes, null);
            if (compileLoad != null)
            {
                JmpHook.Apply(compileLoad,
                    typeof(BcRuntime).GetMethod(nameof(BcRuntime.NoOp_OneArg),
                        BindingFlags.Public | BindingFlags.Static)!,
                    "NCLMetaApplicationObject.CompileAndLoadClrObject");
                Console.Error.WriteLine("[BcRuntime] NCLMetaApplicationObject.CompileAndLoadClrObject hooked → NoOp");
            }
        }

        // 6. Inject skeleton SystemTenant into the real Tenants collection.
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
