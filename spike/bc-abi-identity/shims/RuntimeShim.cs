// Spike: polyfill for AL-compiler-emitted helpers absent from BC 27.5 service-tier DLLs.
// In production, this would either ship as a side-loaded DLL or live in a renamed
// drop-in `Microsoft.Dynamics.Nav.Ncl.dll` we ship.
namespace SpikeShims;

public static class RuntimeShim
{
    public static void ThrowIfWrongArgumentCount(int expected, object[] args, string memberName)
    {
        if (args is null || args.Length != expected)
            throw new System.ArgumentException(
                $"Expected {expected} argument(s) for '{memberName}', got {(args?.Length ?? 0)}");
    }
}
