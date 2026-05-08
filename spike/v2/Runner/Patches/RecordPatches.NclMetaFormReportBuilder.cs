// RecordPatches.NclMetaFormReportBuilder — turns ParsedPage / ParsedReport
// into skeleton NCLMetaForm / NCLMetaReport instances, suitable for inserting
// into NCLMetadata.metadataCacheEntries[Page] / [Report].
//
// Strategy: NCLMetaForm and NCLMetaReport both expose internal static factory
// methods `CreateEmptyNCLMetaForm` / `CreateEmptyNCLMetaReport` that take only
// (loader, id, NavAppGroup appGroup, depOrder, alNamespace). The result has
// `objectId` / `metadataAppGroup` populated but no MetaPageDefinition /
// MetaReportDefinition — which is fine for our needs:
//
//   • The cache slot just has to be non-null so
//     `NCLMetadata.GetMetaApplicationObjectInternal` finds an entry instead of
//     throwing `NavNCLApplicationObjectNotFoundException`.
//   • Every property getter on NCLMetaForm that touches
//     `metadataAppGroupPageDefinition.Item` is gated by Populate() / runtime
//     code paths we already JMP-hook to no-op (§O).
//   • `ApplicationObjectClrType` is JMP-hooked elsewhere and looks up
//     `Form{N}` / `Report{N}` from the loaded test assembly (extended below).
//
// The loader can be passed as null because the §O Populate / CompileAndLoadClrObject
// JMP no-ops mean the loader is never dereferenced after construction.
using System.Reflection;
using System.Runtime.CompilerServices;

namespace AlRunnerV2.Patches;

public static partial class RecordPatches
{
    // Parsed page/report tables, mirror of _parsedTables.
    private static readonly Dictionary<int, ParsedPage> _parsedPages = new();
    private static readonly Dictionary<int, ParsedReport> _parsedReports = new();

    // Cache: pageId/reportId → NCLMetaForm / NCLMetaReport instance.
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<int, object?> _metaFormCache = new();
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<int, object?> _metaReportCache = new();

    // Type/method handles resolved lazily.
    private static Type? _tNCLMetaForm;
    private static Type? _tNCLMetaReport;
    private static MethodInfo? _mCreateEmptyNCLMetaForm;
    private static MethodInfo? _mCreateEmptyNCLMetaReport;
    private static object? _baseAppGroup;

    private static void EnsureFormReportReflection()
    {
        if (_tNCLMetaForm != null && _tNCLMetaReport != null) return;
        var nclAsm = AppDomain.CurrentDomain.GetAssemblies()
            .FirstOrDefault(a => a.GetName().Name == "Microsoft.Dynamics.Nav.Ncl");
        if (nclAsm == null) return;

        _tNCLMetaForm = nclAsm.GetType("Microsoft.Dynamics.Nav.Runtime.NCLMetaForm");
        _tNCLMetaReport = nclAsm.GetType("Microsoft.Dynamics.Nav.Runtime.NCLMetaReport");

        // Both factories are `internal static`.
        _mCreateEmptyNCLMetaForm = _tNCLMetaForm?.GetMethod("CreateEmptyNCLMetaForm",
            BindingFlags.NonPublic | BindingFlags.Static);
        _mCreateEmptyNCLMetaReport = _tNCLMetaReport?.GetMethod("CreateEmptyNCLMetaReport",
            BindingFlags.NonPublic | BindingFlags.Static);

        // NavAppGroup.BaseGroup
        var tAppGroup = nclAsm.GetType("Microsoft.Dynamics.Nav.Runtime.Apps.NavAppGroup");
        _baseAppGroup = tAppGroup?.GetProperty("BaseGroup",
                BindingFlags.Public | BindingFlags.Static)?.GetValue(null)
            ?? tAppGroup?.GetField("BaseGroup",
                BindingFlags.Public | BindingFlags.Static)?.GetValue(null);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static object? BuildNCLMetaForm(int pageId)
    {
        if (!_parsedPages.TryGetValue(pageId, out var parsed)) return null;
        EnsureFormReportReflection();
        if (_mCreateEmptyNCLMetaForm == null) return null;

        try
        {
            // (loader, id, appGroup, depOrder=-1, alNamespace="")
            var meta = _mCreateEmptyNCLMetaForm.Invoke(null,
                new object?[] { null, pageId, _baseAppGroup, -1, string.Empty });

            // Mark metadataLoaded=true on the freshly-built skeleton so the
            // shared NCLMetaApplicationObject.Populate path is skipped (in
            // addition to the JMP-hook NoOp installed in §O).
            EnsureCachePopulatorReflection();
            if (meta != null && _fNCLMetaAppObjMetadataLoaded != null)
                AlRunnerV2.Infrastructure.FieldPoke.SetInstance(_fNCLMetaAppObjMetadataLoaded, meta, true);

            return meta;
        }
        catch (Exception ex)
        {
            var inner = ex is TargetInvocationException tie ? tie.InnerException ?? ex : ex;
            Console.Error.WriteLine($"[RecordPatches] BuildNCLMetaForm({pageId}) failed: {inner.GetType().Name}: {inner.Message}");
            return null;
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static object? BuildNCLMetaReport(int reportId)
    {
        if (!_parsedReports.TryGetValue(reportId, out var parsed)) return null;
        EnsureFormReportReflection();
        if (_mCreateEmptyNCLMetaReport == null) return null;

        try
        {
            var meta = _mCreateEmptyNCLMetaReport.Invoke(null,
                new object?[] { null, reportId, _baseAppGroup, -1, string.Empty });

            EnsureCachePopulatorReflection();
            if (meta != null && _fNCLMetaAppObjMetadataLoaded != null)
                AlRunnerV2.Infrastructure.FieldPoke.SetInstance(_fNCLMetaAppObjMetadataLoaded, meta, true);

            return meta;
        }
        catch (Exception ex)
        {
            var inner = ex is TargetInvocationException tie ? tie.InnerException ?? ex : ex;
            Console.Error.WriteLine($"[RecordPatches] BuildNCLMetaReport({reportId}) failed: {inner.GetType().Name}: {inner.Message}");
            return null;
        }
    }
}
