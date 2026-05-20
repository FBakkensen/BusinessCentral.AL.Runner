// ReportPatches — static OOS-throw replacements for NavReport.Run and NavReport.RunModal.
//
// REPORT.RUN(id [, reqPage [, sysPrinter [, record]]]) in AL compiles to static
// NavReport.Run(int, ...) overloads, and REPORT.RUNMODAL(...) to NavReport.RunModal(...).
// Without hooks these call NCLMetadata.GetMetaReportById → ThrowMetaApplicationObjectNotFound
// for every test-assembly report.  Report execution and rendering are OOS in standalone
// mode.  Per the no-silent-no-op directive, every overload throws a loud, asserterror-able
// InvalidOperationException so test authors can verify the surface was actually hit.
//
// Mirror of the SaveAsAsync OOS pattern (commit 2d0e22ba).
using System.Runtime.CompilerServices;

namespace AlRunnerV2;

public static partial class BcRuntime
{
    // ──────────────────────────────────────────────────────────────────
    // NavReport.Run static overloads — throw OOS
    // ──────────────────────────────────────────────────────────────────

    /// <summary>NavReport.Run(int reportId) — throws OOS.</summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static void NavReport_StaticRun1(int reportId)
    {
        Console.Error.WriteLine($"[NavReport.Run/1] hooked → throwing OOS");
        throw new System.InvalidOperationException(
            "out-of-scope: NavReport.Run — report-execution — see docs/scope.md#report-rendering");
    }

    /// <summary>NavReport.Run(int reportId, bool requestWindow) — throws OOS.</summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static void NavReport_StaticRun2(int reportId, bool requestWindow)
    {
        Console.Error.WriteLine($"[NavReport.Run/2] hooked → throwing OOS");
        throw new System.InvalidOperationException(
            "out-of-scope: NavReport.Run — report-execution — see docs/scope.md#report-rendering");
    }

    /// <summary>NavReport.Run(ReportRunOptions) — throws OOS.</summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static void NavReport_StaticRunOpts(object reportRunOptions)
    {
        Console.Error.WriteLine("[NavReport.Run/opts] hooked → throwing OOS");
        throw new System.InvalidOperationException(
            "out-of-scope: NavReport.Run — report-execution — see docs/scope.md#report-rendering");
    }

    /// <summary>NavReport.Run(int reportId, bool requestWindow, bool systemPrinter) — throws OOS.</summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static void NavReport_StaticRun3(int reportId, bool requestWindow, bool systemPrinter)
    {
        Console.Error.WriteLine($"[NavReport.Run/3] hooked → throwing OOS");
        throw new System.InvalidOperationException(
            "out-of-scope: NavReport.Run — report-execution — see docs/scope.md#report-rendering");
    }

    /// <summary>NavReport.Run(int reportId, bool requestWindow, bool systemPrinter, NavRecord record) — throws OOS.</summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static void NavReport_StaticRun4(int reportId, bool requestWindow, bool systemPrinter, object record)
    {
        Console.Error.WriteLine($"[NavReport.Run/4] hooked → throwing OOS");
        throw new System.InvalidOperationException(
            "out-of-scope: NavReport.Run — report-execution — see docs/scope.md#report-rendering");
    }

    // ──────────────────────────────────────────────────────────────────
    // NavReport.RunModal static overloads — throw OOS
    // ──────────────────────────────────────────────────────────────────

    /// <summary>NavReport.RunModal(int reportId) — throws OOS.</summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static void NavReport_StaticRunModal1(int reportId)
    {
        Console.Error.WriteLine($"[NavReport.RunModal/1] hooked → throwing OOS");
        throw new System.InvalidOperationException(
            "out-of-scope: NavReport.RunModal — report-execution — see docs/scope.md#report-rendering");
    }

    /// <summary>NavReport.RunModal(int reportId, bool requestWindow) — throws OOS.</summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static void NavReport_StaticRunModal2(int reportId, bool requestWindow)
    {
        Console.Error.WriteLine($"[NavReport.RunModal/2] hooked → throwing OOS");
        throw new System.InvalidOperationException(
            "out-of-scope: NavReport.RunModal — report-execution — see docs/scope.md#report-rendering");
    }

    /// <summary>NavReport.RunModal(int reportId, bool requestWindow, bool systemPrinter) — throws OOS.</summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static void NavReport_StaticRunModal3(int reportId, bool requestWindow, bool systemPrinter)
    {
        Console.Error.WriteLine($"[NavReport.RunModal/3] hooked → throwing OOS");
        throw new System.InvalidOperationException(
            "out-of-scope: NavReport.RunModal — report-execution — see docs/scope.md#report-rendering");
    }

    /// <summary>NavReport.RunModal(int reportId, bool requestWindow, bool systemPrinter, NavRecord record) — throws OOS.</summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static void NavReport_StaticRunModal4(int reportId, bool requestWindow, bool systemPrinter, object record)
    {
        Console.Error.WriteLine($"[NavReport.RunModal/4] hooked → throwing OOS");
        throw new System.InvalidOperationException(
            "out-of-scope: NavReport.RunModal — report-execution — see docs/scope.md#report-rendering");
    }
}
