// NavReportSync — in-process synchronous report execution for the v2 runner.
//
// Why this exists:
//   NavReport.Run() / RunModal() in Ncl.dll are sync-over-async wrappers around
//   NavReport.RunReportAsync (ValueTask). The async path NREs deep inside
//   RunReportInternalAsync on a null `parent`/Session.MetadataProvider — the
//   runner has no service tier to satisfy those preconditions. Rewriting the
//   async ValueTask state machine bodies is forbidden (CoreCLR R2R segfault
//   risk — see checkpoint 002).
//
// Approach:
//   Cecil rewrites NavReport.Run / RunModal to call the static method below
//   instead of entering RunReportAsync. The method invokes the report's
//   lifecycle triggers (OnPreReport, per-DataItem Pre/Post, OnPostReport)
//   reflectively against the same NavReport instance the AL code holds. No AL
//   semantics are silently dropped: trigger code authored in AL still runs.
//
// Runner policy (documented in docs/scope.md):
//   The runner has no service tier and cannot render report layouts. All
//   reports execute as if `ProcessingOnly = true`. Layout-rendering APIs that
//   would produce a rendered artifact (SaveAsPdf / SaveAsHtml / SaveAsWord /
//   SaveAsExcel / SaveAsDocx / RunRequestPage) throw an AL-observable
//   NavNCLDialogException with the "out-of-scope:" prefix — tests rewrite
//   those calls as `asserterror`.
//
// Limitations of v0:
//   - DataItem row iteration (FindSet + OnAfterGetRecord per row) is not yet
//     wired through. OnPreDataItem / OnPostDataItem triggers still fire once.
//     Reports whose only data-item logic is row-iteration triggers will not
//     execute that logic. Tracked as future work.

using System;
using System.Collections;
using System.Linq;
using System.Reflection;

namespace AlRunnerV2;

public static class NavReportSync
{
    /// <summary>Diagnostic marker; gated by AL_RUNNER_DIAG_IC=1.</summary>
    public static void Diag(string msg)
    {
        if (Environment.GetEnvironmentVariable("AL_RUNNER_DIAG_IC") == "1")
            Console.Error.WriteLine($"[DiagIC] {msg}");
    }

    // Reflection handles cached after first use.
    private static FieldInfo? _dataItemsField;     // DataItemIterator.dataItems : List<DataItem>
    private static PropertyInfo? _onPreDataItem;   // DataItem.OnPreDataItem : NavTrigger
    private static PropertyInfo? _onPostDataItem;  // DataItem.OnPostDataItem : NavTrigger
    private static MethodInfo? _onPreReport;       // NavReport.OnPreReport()  (protected virtual)
    private static MethodInfo? _onPostReport;      // NavReport.OnPostReport() (protected virtual)
    private static MethodInfo? _onInitReport;      // NavReport.OnInitReport() (protected virtual)
    private static PropertyInfo? _objectIdProp;    // NavApplicationObjectBase.ObjectId : ApplicationObjectId
    private static PropertyInfo? _objectNumberProp;// ApplicationObjectId.ObjectNumber : int
    private static MethodInfo? _rdlcLayoutMethod;  // NavReport.RDLCLayout(DataError, int, NavInStream)
    private static Type? _dataErrorType;           // Microsoft.Dynamics.Nav.Types.DataError

    // Stub-Metadata path (called from Cecil-rewritten NavReport.BeginInitialization).
    private static Type? _metaReportType;          // Microsoft.Dynamics.Nav.Types.MetaReport
    private static Type? _masterPageType;          // Microsoft.Dynamics.Nav.Types.MasterPage
    private static ConstructorInfo? _masterPageCtor;
    private static FieldInfo? _metaReportMasterPageField; // MetaReport.masterPage : MasterPage
    private static FieldInfo? _processingOnlyBackingField; // MetaReport.<ProcessingOnly>k__BackingField : bool
    private static PropertyInfo? _metadataSetter;  // DataItemIterator.Metadata : MetaReport (protected set)

    /// <summary>
    /// Replacement body for NavReport.BeginInitialization (sync wrapper).
    /// The real implementation calls VerifyExecutePermission + reads
    /// Tree.Session.MetadataProvider.GetReportMetadata(NclMetaReport) — both
    /// of which NRE on the runner's skeleton Session. We instead populate
    /// `base.Metadata` with an uninitialized MetaReport whose `masterPage`
    /// field points at an empty MasterPage. That makes the BC-emitted IC's
    /// tail line `RequestOptionsPage = new RequestPage(this, Metadata.RequestFormMetadata)`
    /// null-safe: `RequestFormMetadata` calls `EnsureMasterPageLoaded()` →
    /// `CreateMasterPage()` which early-returns when `masterPage != null`.
    /// </summary>
    public static void StubInitializeMetadata(object navReport)
    {
        Diag($"IC step: BeginInitialization (StubInit) on {navReport?.GetType().Name}");
        if (navReport == null) { Console.Error.WriteLine("[NavReportSync] StubInit: navReport=null"); return; }

        if (_metaReportType == null)
        {
            var typesAsm = AppDomain.CurrentDomain.GetAssemblies()
                .FirstOrDefault(a => a.GetName().Name == "Microsoft.Dynamics.Nav.Types");
            if (typesAsm == null) { Console.Error.WriteLine("[NavReportSync] StubInit: Types asm not loaded"); return; }
            _metaReportType = typesAsm.GetType("Microsoft.Dynamics.Nav.Types.Metadata.MetaReport");
            _masterPageType = typesAsm.GetType("Microsoft.Dynamics.Nav.Types.Metadata.MasterPage");
            if (_metaReportType == null || _masterPageType == null) { Console.Error.WriteLine($"[NavReportSync] StubInit: MetaReport={_metaReportType}, MasterPage={_masterPageType}"); return; }
            _masterPageCtor = _masterPageType.GetConstructor(Type.EmptyTypes);
            _metaReportMasterPageField = _metaReportType.GetField("masterPage",
                BindingFlags.Instance | BindingFlags.NonPublic);
            _processingOnlyBackingField = _metaReportType.GetField("<ProcessingOnly>k__BackingField",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Console.Error.WriteLine($"[NavReportSync] StubInit: cached MetaReport={_metaReportType.FullName}, MasterPageCtor={_masterPageCtor != null}, masterPageField={_metaReportMasterPageField != null}, ProcessingOnlyBacking={_processingOnlyBackingField != null}");
        }
        if (_masterPageCtor == null || _metaReportMasterPageField == null) { Console.Error.WriteLine("[NavReportSync] StubInit: missing ctor/field"); return; }

        if (_metadataSetter == null)
        {
            var t = navReport.GetType();
            while (t != null && _metadataSetter == null)
            {
                _metadataSetter = t.GetProperty("Metadata",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                t = t.BaseType;
            }
            Console.Error.WriteLine($"[NavReportSync] StubInit: Metadata prop found={_metadataSetter != null} canWrite={_metadataSetter?.CanWrite}");
        }
        if (_metadataSetter == null) return;

        if (_metadataSetter.GetValue(navReport) != null) return;

        var masterPage = _masterPageCtor.Invoke(null);
        var metaReport = System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(_metaReportType!);
        _metaReportMasterPageField.SetValue(metaReport, masterPage);
        _processingOnlyBackingField?.SetValue(metaReport, true);
        _metadataSetter.SetValue(navReport, metaReport);
        Console.Error.WriteLine($"[NavReportSync] StubInit: installed stub on {navReport.GetType().Name}");
    }

    /// <summary>
    /// Replacement for NavReport.Run() / RunModal(). Invoked from Cecil-rewritten
    /// IL — the instance is the same NavReport the AL code constructed and holds.
    /// </summary>
    public static void SyncRun(object navReport)
    {
        if (Environment.GetEnvironmentVariable("AL_RUNNER_DIAG_IC") == "1")
            Console.Error.WriteLine($"[NavReportSync] SyncRun entry: type={navReport?.GetType().FullName}");
        if (navReport == null) return;

        var t = navReport.GetType();
        // Walk down to NavReport base type so we can find protected virtuals.
        Type? navReportBase = t;
        while (navReportBase != null && navReportBase.Name != "NavReport")
            navReportBase = navReportBase.BaseType;
        if (navReportBase == null) return;

        // DataItemIterator (NavReport's base) owns the dataItems list.
        Type? dataItemIteratorBase = navReportBase.BaseType;
        if (dataItemIteratorBase != null && _dataItemsField == null)
        {
            _dataItemsField = dataItemIteratorBase.GetField("dataItems",
                BindingFlags.Instance | BindingFlags.NonPublic);
        }

        if (_onInitReport == null)
            _onInitReport = navReportBase.GetMethod("OnInitReport",
                BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public,
                null, Type.EmptyTypes, null);
        if (_onPreReport == null)
            _onPreReport = navReportBase.GetMethod("OnPreReport",
                BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public,
                null, Type.EmptyTypes, null);
        if (_onPostReport == null)
            _onPostReport = navReportBase.GetMethod("OnPostReport",
                BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public,
                null, Type.EmptyTypes, null);

        InvokeVirtual(_onInitReport, navReport);
        InvokeVirtual(_onPreReport, navReport);
        InvokeDataItems(navReport);
        InvokeVirtual(_onPostReport, navReport);

        // Strict AL semantics: when the AL source declares `ProcessingOnly =
        // false` (the AL default), Run() must attempt rendering after the
        // lifecycle triggers. The runner has no service tier and cannot
        // render layouts, so the rendering attempt must surface as an
        // AL-observable error. We trigger that via NavReport.RDLCLayout —
        // a public static method that forwards to GetLayoutCore (Cecil-
        // rewritten to throw an OOS InvalidOperationException on
        // ThrowError). The error therefore originates from the actual
        // layout-resolution code path, not from a guard at the top of Run.
        if (!IsProcessingOnly(navReport, navReportBase))
            InvokeLayoutForReport(navReport, navReportBase);
    }

    // Looks up ProcessingOnly from the parsed AL source (RecordPatches).
    // Falls back to true when the report ID cannot be resolved — defensive
    // so unknown reports do not trip the rendering guard.
    private static bool IsProcessingOnly(object navReport, Type navReportBase)
    {
        int reportId = TryGetObjectId(navReport, navReportBase);
        bool diag = Environment.GetEnvironmentVariable("AL_RUNNER_DIAG_IC") == "1";
        if (reportId <= 0)
        {
            if (diag) Console.Error.WriteLine($"[NavReportSync] SyncRun: reportId=0 (could not resolve), defaulting ProcessingOnly=true");
            return true;
        }
        bool po = AlRunnerV2.Patches.RecordPatches.IsReportProcessingOnly(reportId);
        if (diag) Console.Error.WriteLine($"[NavReportSync] SyncRun: report {reportId} ProcessingOnly={po}");
        return po;
    }

    private static int TryGetObjectId(object navReport, Type navReportBase)
    {
        // Primary path: AL-emitted report types are named "Report<N>" (e.g.
        // "Report50600"). This avoids the ApplicationObjectId.ObjectNumber=0
        // mystery we hit going through the inherited ObjectId property —
        // the field IS set by base ctor IL but boxed-struct reflection
        // returns 0 in some scenarios on the Cecil-rewritten ctor chain.
        // Type-name parse is robust against that and trivially correct
        // for AL-emitted reports.
        var name = navReport.GetType().Name;
        if (name.Length > 6 && name.StartsWith("Report", StringComparison.Ordinal)
            && int.TryParse(name.AsSpan(6), out int idFromName))
        {
            return idFromName;
        }

        // Fallback: reflective ObjectId.ObjectNumber.
        if (_objectIdProp == null)
        {
            Type? t = navReportBase;
            while (t != null && _objectIdProp == null)
            {
                _objectIdProp = t.GetProperty("ObjectId",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                t = t.BaseType;
            }
        }
        if (_objectIdProp == null) return 0;
        var appObjId = _objectIdProp.GetValue(navReport);
        if (appObjId == null) return 0;
        if (_objectNumberProp == null)
            _objectNumberProp = appObjId.GetType().GetProperty("ObjectNumber",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        if (_objectNumberProp == null) return 0;
        var n = _objectNumberProp.GetValue(appObjId);
        return n is int i ? i : 0;
    }

    private static void InvokeLayoutForReport(object navReport, Type navReportBase)
    {
        if (_rdlcLayoutMethod == null)
        {
            // Look up NavReport.RDLCLayout(DataError, int, NavInStream).
            _rdlcLayoutMethod = navReportBase.GetMethods(
                BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
                .FirstOrDefault(m => m.Name == "RDLCLayout" && m.GetParameters().Length == 3);
        }
        if (_dataErrorType == null)
        {
            var typesAsm = AppDomain.CurrentDomain.GetAssemblies()
                .FirstOrDefault(a => a.GetName().Name == "Microsoft.Dynamics.Nav.Types");
            _dataErrorType = typesAsm?.GetType("Microsoft.Dynamics.Nav.Types.DataError");
        }
        int reportId = TryGetObjectId(navReport, navReportBase);
        if (_rdlcLayoutMethod != null && _dataErrorType != null)
        {
            // DataError.ThrowError = 1 → GetLayoutCore (Cecil-rewritten) throws OOS.
            var throwError = Enum.ToObject(_dataErrorType, 1);
            try
            {
                _rdlcLayoutMethod.Invoke(null, new object?[] { throwError, reportId, null });
            }
            catch (TargetInvocationException tie) when (tie.InnerException != null)
            {
                throw tie.InnerException;
            }
            // RDLCLayout returning normally would mean we somehow found a
            // layout — defensive throw in case Cecil rewrite didn't apply.
            throw new InvalidOperationException(
                "out-of-scope: NavReport.Run on layout-rendering report (ProcessingOnly = false) " +
                "— rendering requires a service tier — see docs/scope.md#report-rendering");
        }

        // Fallback if reflection failed: still throw AL-observable error.
        throw new InvalidOperationException(
            "out-of-scope: NavReport.Run on layout-rendering report (ProcessingOnly = false) " +
            "— rendering requires a service tier — see docs/scope.md#report-rendering");
    }

    private static void InvokeVirtual(MethodInfo? m, object instance)
    {
        if (m == null) return;
        try { m.Invoke(instance, null); }
        catch (TargetInvocationException tie) when (tie.InnerException != null)
        {
            // Surface the AL trigger's exception (e.g. Assert.AreEqual failure)
            // as the original, not wrapped in TargetInvocationException.
            throw tie.InnerException;
        }
    }

    private static void InvokeDataItems(object navReport)
    {
        if (_dataItemsField == null) return;
        if (_dataItemsField.GetValue(navReport) is not IEnumerable items) return;

        foreach (var di in items)
        {
            if (di == null) continue;
            if (_onPreDataItem == null)
                _onPreDataItem = di.GetType().GetProperty("OnPreDataItem",
                    BindingFlags.Instance | BindingFlags.Public);
            if (_onPostDataItem == null)
                _onPostDataItem = di.GetType().GetProperty("OnPostDataItem",
                    BindingFlags.Instance | BindingFlags.Public);

            InvokeTrigger(_onPreDataItem, di);
            // TODO: iterate source table and fire OnAfterGetRecord per row.
            // For test reports without row triggers this is a no-op anyway.
            InvokeTrigger(_onPostDataItem, di);
        }
    }

    private static void InvokeTrigger(PropertyInfo? prop, object dataItem)
    {
        if (prop == null) return;
        var trigger = prop.GetValue(dataItem) as Delegate;
        if (trigger == null) return;
        try { trigger.DynamicInvoke(); }
        catch (TargetInvocationException tie) when (tie.InnerException != null)
        {
            throw tie.InnerException;
        }
    }
}
