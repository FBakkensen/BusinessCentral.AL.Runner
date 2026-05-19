// FormPatches — OOS throw sites for non-modal page run (§3.11 ui).
//
// NavFormHandle.Run implements the page-variable .Run() path. The static
// Page.Run call sites are redirected at source level via BcAssembler._polyfillRedirects
// (NavForm.Run → NavRuntimeHelpersShim.NavForm_Run) which is more reliable than
// JmpHook.Apply on .NET 8 R2R code — see BcAssembler.cs comment for details.
//
// NavFormHandle.Run:  3 instance overloads (0/1/2 extra params beyond self).
// Page variable .Run() in AL typically goes through MockFormHandle already, so
// these hooks fire mainly during BC SA init (with OosHooksActive=false → no-op).
//
// Hook installation: NavFormHandle.Run via JmpHook.Apply in BcRuntime.cs.
using System.Runtime.CompilerServices;
using AlRunnerV2.Infrastructure;

namespace AlRunnerV2;

public static class FormPatches
{
    // ──────────────────────────────────────────────────────────────────
    // NavFormHandle.Run — Page variable .Run() (§3.11 OOS)
    // ──────────────────────────────────────────────────────────────────

    /// <summary>NavFormHandle.Run() — 0 extra params.</summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static void NavFormHandle_Run_0(object self)
    {
        if (BcRuntime.OosHooksActive)
            RunnerScope.ThrowOutOfScope("NavForm.RunAsync", "non-modal-ui", "ui");
    }

    /// <summary>NavFormHandle.Run(arg1) — 1 extra param.</summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static void NavFormHandle_Run_1(object self, object arg1)
    {
        if (BcRuntime.OosHooksActive)
            RunnerScope.ThrowOutOfScope("NavForm.RunAsync", "non-modal-ui", "ui");
    }

    /// <summary>NavFormHandle.Run(arg1, arg2) — 2 extra params.</summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static void NavFormHandle_Run_2(object self, object arg1, object arg2)
    {
        if (BcRuntime.OosHooksActive)
            RunnerScope.ThrowOutOfScope("NavForm.RunAsync", "non-modal-ui", "ui");
    }
}
