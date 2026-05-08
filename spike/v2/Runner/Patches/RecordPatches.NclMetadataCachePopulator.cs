// RecordPatches.NclMetadataCachePopulator — lazy-populates the skeleton
// NCLMetadata.metadataCacheEntries[Table] dictionary from parsed AL sources, so
// that NavGlobal.NCLMetadata.GetMetaTableById / GetMetaApplicationObject
// callers find a real NCLMetaTable instead of throwing
// NavNCLApplicationObjectNotFoundException.
//
// Sequence:
//   1. BcRuntime.InjectSkeletonSystemTenant builds a skeleton NCLMetadata and
//      pre-allocates empty ConcurrentDictionary entries per ObjectType.
//   2. RecordPatches.Register() parses every .al source dir registered so far
//      via TryParseTableFile → _parsedTables.
//   3. After ParseAllSources, this populator iterates _parsedTables, calls the
//      existing BuildNCLMetaTable(int) factory (which uses NCLMetaTable's
//      internal CreateFromMetaTable), wraps each result in
//      NCLMetadataCacheEntry.CreateWithBase, and inserts it into the skeleton
//      cache dictionary at metadataCacheEntries[(int)ObjectType.Table].
//   4. Subsequent AddSourceDir calls (which parse on-demand when _registered)
//      also feed the cache so per-suite tests see their own tables.
//
// This is the §O follow-up to §N — §N populated empty cache arrays so the
// failure mode shifted from NRE → NavNCLApplicationObjectNotFoundException;
// §O fills those arrays with real entries built from AL source.
using System.Collections.Concurrent;
using System.Reflection;
using AlRunnerV2.Infrastructure;

namespace AlRunnerV2.Patches;

public static partial class RecordPatches
{
    private static Type? _tNCLMetadataCacheEntry;
    private static MethodInfo? _mCreateWithBase;
    private static FieldInfo? _fNCLMetadataCacheEntries;   // NCLMetadata.metadataCacheEntries
    private static FieldInfo? _fNCLMetaAppObjMetadataLoaded; // NCLMetaApplicationObject.metadataLoaded

    /// <summary>
    /// Populate the skeleton NCLMetadata's cache with one entry per parsed AL table.
    /// Idempotent — duplicates are skipped via TryAdd.
    /// </summary>
    internal static void PopulateNclMetadataCache()
    {
        var skeleton = BcRuntime.SkeletonNCLMetadata;
        if (skeleton == null)
        {
            // Skeleton NCLMetadata wasn't built (env-ctor fallback path). No cache to fill.
            return;
        }

        EnsureCachePopulatorReflection();
        if (_fNCLMetadataCacheEntries == null || _mCreateWithBase == null) return;

        // metadataCacheEntries is a ConcurrentDictionary<int, NCLMetadataCacheEntry>[]
        // — index 1 == ObjectType.Table.
        var arr = _fNCLMetadataCacheEntries.GetValue(skeleton) as Array;
        if (arr == null) return;
        const int objectTypeTable = 1;
        if (arr.Length <= objectTypeTable) return;
        var tableDict = arr.GetValue(objectTypeTable);
        if (tableDict == null) return;

        // Use the IDictionary view to avoid generics gymnastics across versions.
        var dict = (System.Collections.IDictionary)tableDict;

        int added = 0, failed = 0;
        foreach (var kv in _parsedTables)
        {
            int tableId = kv.Key;
            if (dict.Contains(tableId)) continue;
            object? meta;
            try
            {
                meta = _metaTableCache.GetOrAdd(tableId, BuildNCLMetaTable);
            }
            catch
            {
                meta = null;
            }
            if (meta == null) { failed++; continue; }

            // Mark metadataLoaded=true so NCLMetadata.GetMetaApplicationObjectInternal
            // doesn't fall into the Populate()/LoadMetadata() path. (Belt-and-braces:
            // NCLMetaApplicationObject.Populate is also JMP-hooked to NoOp by
            // MetadataPatches.InjectSkeletonSystemTenant, in case the field-poke is
            // raced by an inlined property read.)
            if (_fNCLMetaAppObjMetadataLoaded != null)
                FieldPoke.SetInstance(_fNCLMetaAppObjMetadataLoaded, meta, true);

            object? entry;
            try
            {
                entry = _mCreateWithBase.Invoke(null, new object?[] { meta });
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[RecordPatches] CacheEntry.CreateWithBase({tableId}) failed: " +
                    ((ex is TargetInvocationException tie ? tie.InnerException ?? ex : ex).Message));
                failed++;
                continue;
            }
            if (entry == null) { failed++; continue; }

            try
            {
                dict[tableId] = entry;
                added++;
            }
            catch { failed++; }
        }

        if (added > 0 || failed > 0)
            Console.Error.WriteLine($"[RecordPatches] PopulateNclMetadataCache: added={added}, failed={failed}, total={_parsedTables.Count}");
    }

    private static void EnsureCachePopulatorReflection()
    {
        if (_fNCLMetadataCacheEntries != null) return;

        var nclAsm = AppDomain.CurrentDomain.GetAssemblies()
            .FirstOrDefault(a => a.GetName().Name == "Microsoft.Dynamics.Nav.Ncl");
        if (nclAsm == null) return;

        var tNclMetadata = nclAsm.GetType("Microsoft.Dynamics.Nav.Runtime.NCLMetadata");
        _fNCLMetadataCacheEntries = tNclMetadata?.GetField("metadataCacheEntries",
            BindingFlags.NonPublic | BindingFlags.Instance);

        _tNCLMetadataCacheEntry = nclAsm.GetType("Microsoft.Dynamics.Nav.Runtime.NCLMetadataCacheEntry");
        _mCreateWithBase = _tNCLMetadataCacheEntry?.GetMethod("CreateWithBase",
            BindingFlags.Public | BindingFlags.Static);

        var tNclMetaAppObj = nclAsm.GetType("Microsoft.Dynamics.Nav.Runtime.NCLMetaApplicationObject");
        _fNCLMetaAppObjMetadataLoaded = tNclMetaAppObj?.GetField("metadataLoaded",
            BindingFlags.NonPublic | BindingFlags.Instance);
    }
}
