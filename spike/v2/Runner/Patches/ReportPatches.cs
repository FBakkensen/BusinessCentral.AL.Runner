// ReportPatches — static no-op replacements for NavReport.Run and NavReport.RunModal.
//
// REPORT.RUN(id [, reqPage [, sysPrinter [, record]]]) in AL compiles to static
// NavReport.Run(int, ...) overloads, and REPORT.RUNMODAL(...) to NavReport.RunModal(...).
// Without hooks these call NCLMetadata.GetMetaReportById → ThrowMetaApplicationObjectNotFound
// for every test-assembly report.  All Run/RunModal overloads are void; OnRun trigger
// execution and rendering are both OOS in standalone mode.  Silent no-op is honest.
//
// Mirror of XmlPortPatches.cs static-Run block (commit 473be259).
using System.Runtime.CompilerServices;

namespace AlRunnerV2;

public static partial class BcRuntime
{
    // ──────────────────────────────────────────────────────────────────
    // NavReport.Run static overloads — no-ops
    // ──────────────────────────────────────────────────────────────────

    /// <summary>NavReport.Run(int reportId) — no-op.</summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static void NavReport_StaticRun1(int reportId)
    {
        Console.Error.WriteLine($"[BcRuntime] NavReport.Run({reportId}) → no-op (static Run hook)");
    }

    /// <summary>NavReport.Run(int reportId, bool requestWindow) — no-op.</summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static void NavReport_StaticRun2(int reportId, bool requestWindow)
    {
        Console.Error.WriteLine($"[BcRuntime] NavReport.Run({reportId}, {requestWindow}) → no-op (static Run hook)");
    }

    /// <summary>NavReport.Run(ReportRunOptions) — no-op.</summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static void NavReport_StaticRunOpts(object reportRunOptions)
    {
        Console.Error.WriteLine("[BcRuntime] NavReport.Run(ReportRunOptions) → no-op (static Run hook)");
    }

    /// <summary>NavReport.Run(int reportId, bool requestWindow, bool systemPrinter) — no-op.</summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static void NavReport_StaticRun3(int reportId, bool requestWindow, bool systemPrinter)
    {
        Console.Error.WriteLine($"[BcRuntime] NavReport.Run({reportId}, {requestWindow}, {systemPrinter}) → no-op (static Run hook)");
    }

    /// <summary>NavReport.Run(int reportId, bool requestWindow, bool systemPrinter, NavRecord record) — no-op.</summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static void NavReport_StaticRun4(int reportId, bool requestWindow, bool systemPrinter, object record)
    {
        Console.Error.WriteLine($"[BcRuntime] NavReport.Run({reportId}, {requestWindow}, {systemPrinter}, record) → no-op (static Run hook)");
    }

    // ──────────────────────────────────────────────────────────────────
    // NavReport.RunModal static overloads — no-ops
    // ──────────────────────────────────────────────────────────────────

    /// <summary>NavReport.RunModal(int reportId) — no-op.</summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static void NavReport_StaticRunModal1(int reportId)
    {
        Console.Error.WriteLine($"[BcRuntime] NavReport.RunModal({reportId}) → no-op (static RunModal hook)");
    }

    /// <summary>NavReport.RunModal(int reportId, bool requestWindow) — no-op.</summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static void NavReport_StaticRunModal2(int reportId, bool requestWindow)
    {
        Console.Error.WriteLine($"[BcRuntime] NavReport.RunModal({reportId}, {requestWindow}) → no-op (static RunModal hook)");
    }

    /// <summary>NavReport.RunModal(int reportId, bool requestWindow, bool systemPrinter) — no-op.</summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static void NavReport_StaticRunModal3(int reportId, bool requestWindow, bool systemPrinter)
    {
        Console.Error.WriteLine($"[BcRuntime] NavReport.RunModal({reportId}, {requestWindow}, {systemPrinter}) → no-op (static RunModal hook)");
    }

    /// <summary>NavReport.RunModal(int reportId, bool requestWindow, bool systemPrinter, NavRecord record) — no-op.</summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static void NavReport_StaticRunModal4(int reportId, bool requestWindow, bool systemPrinter, object record)
    {
        Console.Error.WriteLine($"[BcRuntime] NavReport.RunModal({reportId}, {requestWindow}, {systemPrinter}, record) → no-op (static RunModal hook)");
    }
}
