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

internal static class NavReportSync
{
    // Reflection handles cached after first use.
    private static FieldInfo? _dataItemsField;     // DataItemIterator.dataItems : List<DataItem>
    private static PropertyInfo? _onPreDataItem;   // DataItem.OnPreDataItem : NavTrigger
    private static PropertyInfo? _onPostDataItem;  // DataItem.OnPostDataItem : NavTrigger
    private static MethodInfo? _onPreReport;       // NavReport.OnPreReport()  (protected virtual)
    private static MethodInfo? _onPostReport;      // NavReport.OnPostReport() (protected virtual)
    private static MethodInfo? _onInitReport;      // NavReport.OnInitReport() (protected virtual)

    /// <summary>
    /// Replacement for NavReport.Run() / RunModal(). Invoked from Cecil-rewritten
    /// IL — the instance is the same NavReport the AL code constructed and holds.
    /// </summary>
    public static void SyncRun(object navReport)
    {
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
