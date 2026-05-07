// MiscPatches — small replacements that don't fit a larger concern bucket.
//
// ALSession (session-lifecycle helpers) and NCLEnumMetadata (codeunit enum lookup)
// each have one tiny replacement; rather than spawn a file per area we keep them here.
using System.Runtime.CompilerServices;

namespace AlRunnerV2;

public static partial class BcRuntime
{
    /// <summary>
    /// Replacement for ALSession.GetALCurrentClientType(NavSession).
    /// The real body switches on session.ClientConnectionType which NREs on the skeleton session.
    /// Returns Background as a safe default matching headless/service-tier-less execution.
    /// </summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static Microsoft.Dynamics.Nav.Types.NavClientType ALSession_GetALCurrentClientType(
        object? session)
        => Microsoft.Dynamics.Nav.Types.NavClientType.Background;

    /// <summary>
    /// Replacement for all ALSession.ALStopSessionAsync overloads.
    /// The async body NREs via session.Diagnostics on the skeleton. Return false (not stopped).
    /// </summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static System.Threading.Tasks.ValueTask<bool> ALSession_StopSessionAsync(
        object? a, object? b, object? c, object? d)
    {
        return new System.Threading.Tasks.ValueTask<bool>(false);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static Microsoft.Dynamics.Nav.Runtime.NCLOptionMetadata NCLEnumMetadata_CreateById(int id)
        => Microsoft.Dynamics.Nav.Runtime.NCLOptionMetadata.Default;
}
