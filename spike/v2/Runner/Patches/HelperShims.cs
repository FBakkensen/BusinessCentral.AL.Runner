// BcRuntime.Helpers — generic NoOp / ReturnX shims that JMP-hooks redirect to.
//
// Each helper matches the receiver+args slot count of the call sites it replaces:
//   * For an instance method, the static replacement takes one extra leading object
//     parameter for the receiver (`this`). e.g. an instance void method with two
//     reference args needs a NoOp3.
//   * For value-returning helpers (Return*), the return type's CLR slot must match
//     the original — bool→bool, ValueTask→ValueTask, etc.
//
// All helpers are `[MethodImpl(NoInlining)]` because the JIT must produce a real
// callable function pointer for JmpHook to patch.
using System.Runtime.CompilerServices;

namespace AlRunnerV2;

public static partial class BcRuntime
{
    [MethodImpl(MethodImplOptions.NoInlining)] public static void NoOp_0Args() { }
    [MethodImpl(MethodImplOptions.NoInlining)] public static void NoOp_OneArg(object? a) { }
    [MethodImpl(MethodImplOptions.NoInlining)] public static void NoOp2(object? a, object? b) { }
    [MethodImpl(MethodImplOptions.NoInlining)] public static void NoOp3(object? a, object? b, object? c) { }
    [MethodImpl(MethodImplOptions.NoInlining)] public static void NoOp4(object? a, object? b, object? c, object? d) { }
    [MethodImpl(MethodImplOptions.NoInlining)] public static void NoOp5(object? a, object? b, object? c, object? d, object? e) { }

    [MethodImpl(MethodImplOptions.NoInlining)] public static bool ReturnFalse_0Args() => false;
    [MethodImpl(MethodImplOptions.NoInlining)] public static bool ReturnFalse_1Arg(object? a) => false;
    [MethodImpl(MethodImplOptions.NoInlining)] public static bool ReturnFalse_2Args(object? a, object? b) => false;

    [MethodImpl(MethodImplOptions.NoInlining)] public static System.Threading.Tasks.ValueTask ReturnValueTask2(object? a, object? b) => default;
    [MethodImpl(MethodImplOptions.NoInlining)] public static System.Threading.Tasks.ValueTask ReturnValueTask3(object? a, object? b, object? c) => default;
    [MethodImpl(MethodImplOptions.NoInlining)] public static System.Threading.Tasks.ValueTask ReturnValueTask4(object? a, object? b, object? c, object? d) => default;
    [MethodImpl(MethodImplOptions.NoInlining)] public static System.Threading.Tasks.ValueTask ReturnValueTask5(object? a, object? b, object? c, object? d, object? e) => default;

    [MethodImpl(MethodImplOptions.NoInlining)] public static object? ReturnNull_OneArg(object a) => null;
    [MethodImpl(MethodImplOptions.NoInlining)] public static object? GetSkeletonCompanyReplacement(object self) => _skeletonCompany;

    [MethodImpl(MethodImplOptions.NoInlining)] public static bool ReturnFalse_3Args(object? a, object? b, object? c) => false;
    [MethodImpl(MethodImplOptions.NoInlining)] public static bool ReturnFalse2(object? a, object? b) => false;

    /// <summary>
    /// Diagnostic helper used for the RecordImplementation.IsOpen hook — logs the call
    /// site so we can trace which patched receiver is being asked. Always returns true
    /// (record is open) because by the time the test harness asks, we want the read path
    /// to proceed against TempTableDataProvider rather than throw NotOpened.
    /// </summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static bool ReturnTrue(object? a)
    {
        Console.Error.WriteLine($"[ReturnTrue] IsOpen hook fired for {a?.GetType().Name}");
        return true;
    }
}
