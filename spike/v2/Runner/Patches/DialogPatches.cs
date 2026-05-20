// DialogPatches — JmpHook replacements for NavDialog.ALStrMenu and NavDialog.ALConfirm.
//
// Both methods invoke BC's callback mechanism (NavNCLCallbackNotAllowedException) in
// standalone mode. We intercept them before they reach the callback infrastructure:
//
//   ALStrMenu(options)                      → 0   (no selection / cancel)
//   ALStrMenu(options, defaultNo, ...)      → defaultNo  (return the default)
//   ALConfirm(...)                          → false  (no interactive UI; no handler stack)
//
// There is no handler-stack system in this commit — see PAGE-REPORT-CLUSTERS §5.
// If the runner gains a [ConfirmHandler] dispatch layer later, route through it here.
//
// Hook installation: BcRuntime.cs ApplyNavDialogPatches block.
// Cecil NoInlining marks: NclCecilRewrite.cs (NavDialog.ALStrMenu* / ALConfirm* section).
using System.Runtime.CompilerServices;

namespace AlRunnerV2.Patches;

public static class DialogPatches
{
    // ── ALStrMenu overloads ──────────────────────────────────────────────────────────────

    // ALStrMenu(String option, Guid automationId)
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static int NavDialog_ALStrMenu_2(string option, System.Guid automationId)
    {
        Console.Error.WriteLine("[NavDialog.ALStrMenu/2] hooked → 0 (no selection)");
        return 0;
    }

    // ALStrMenu(NavSession session, String option, Guid automationId)
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static int NavDialog_ALStrMenu_S2(object session, string option, System.Guid automationId)
    {
        Console.Error.WriteLine("[NavDialog.ALStrMenu/S2] hooked → 0 (no selection)");
        return 0;
    }

    // ALStrMenu(String option, Int32 defaultNumber, Guid automationId)
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static int NavDialog_ALStrMenu_3(string option, int defaultNumber, System.Guid automationId)
    {
        Console.Error.WriteLine($"[NavDialog.ALStrMenu/3] hooked → {defaultNumber} (defaultNo)");
        return defaultNumber;
    }

    // ALStrMenu(NavSession session, String option, Int32 defaultNumber, Guid automationId)
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static int NavDialog_ALStrMenu_S3(object session, string option, int defaultNumber, System.Guid automationId)
    {
        Console.Error.WriteLine($"[NavDialog.ALStrMenu/S3] hooked → {defaultNumber} (defaultNo)");
        return defaultNumber;
    }

    // ALStrMenu(String option, Int32 defaultNumber, String instruction, Guid automationId)
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static int NavDialog_ALStrMenu_4(string option, int defaultNumber, string instruction, System.Guid automationId)
    {
        Console.Error.WriteLine($"[NavDialog.ALStrMenu/4] hooked → {defaultNumber} (defaultNo)");
        return defaultNumber;
    }

    // ALStrMenu(NavSession session, String option, Int32 defaultNumber, String instruction, Guid automationId)
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static int NavDialog_ALStrMenu_S4(object session, string option, int defaultNumber, string instruction, System.Guid automationId)
    {
        Console.Error.WriteLine($"[NavDialog.ALStrMenu/S4] hooked → {defaultNumber} (defaultNo)");
        return defaultNumber;
    }

    // ── ALConfirm overloads ──────────────────────────────────────────────────────────────

    // ALConfirm(Guid automationId, String message)
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static bool NavDialog_ALConfirm_2(System.Guid automationId, string message)
    {
        Console.Error.WriteLine("[NavDialog.ALConfirm/2] hooked → false");
        return false;
    }

    // ALConfirm(NavSession session, Guid automationId, String message)
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static bool NavDialog_ALConfirm_S2(object session, System.Guid automationId, string message)
    {
        Console.Error.WriteLine("[NavDialog.ALConfirm/S2] hooked → false");
        return false;
    }

    // ALConfirm(Guid automationId, String message, Boolean defaultButton, NavValue[] values)
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static bool NavDialog_ALConfirm_4(System.Guid automationId, string message, bool defaultButton, object[] values)
    {
        Console.Error.WriteLine("[NavDialog.ALConfirm/4] hooked → false");
        return false;
    }

    // ALConfirm(NavSession session, Guid automationId, String message, Boolean defaultButton, NavValue[] values)
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static bool NavDialog_ALConfirm_S4(object session, System.Guid automationId, string message, bool defaultButton, object[] values)
    {
        Console.Error.WriteLine("[NavDialog.ALConfirm/S4] hooked → false");
        return false;
    }
}
