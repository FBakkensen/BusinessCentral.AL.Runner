// Minimal subset of bc-linux StartupHook adapted for AL Runner spike.
// Goal: instantiate a BC NavCodeunit on Linux without WindowsIdentity throwing.
// Source: github.com/StefanMaron/bc-linux (MIT). Reused per the user's project.

using System;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

internal static class LinuxBootstrap
{
    private static Type? _navEnvironmentType;

    public static void Apply(Assembly navNcl)
    {
        var envType = navNcl.GetType("Microsoft.Dynamics.Nav.Runtime.NavEnvironment")
            ?? throw new InvalidOperationException("NavEnvironment type not found");
        _navEnvironmentType = envType;

        // Hook 1: the .cctor — replace with one that doesn't call WindowsIdentity.GetCurrent()
        var cctor = envType.TypeInitializer
            ?? throw new InvalidOperationException("NavEnvironment.cctor not found");
        var cctorRepl = typeof(LinuxBootstrap).GetMethod(nameof(NavEnvironmentCctorReplacement),
            BindingFlags.Public | BindingFlags.Static)!;
        ApplyJmpHook(cctor, cctorRepl, "NavEnvironment..cctor");

        // Hook 2: ServiceAccount getter — original would dereference a null serviceAccount
        var saGet = envType.GetProperty("ServiceAccount", BindingFlags.Public | BindingFlags.Static)?.GetMethod;
        if (saGet != null)
        {
            var repl = typeof(LinuxBootstrap).GetMethod(nameof(GetServiceAccountReplacement),
                BindingFlags.Public | BindingFlags.Static)!;
            ApplyJmpHook(saGet, repl, "NavEnvironment.get_ServiceAccount");
        }

        // Hook 3: ServiceAccountName getter — same reason
        var sanGet = envType.GetProperty("ServiceAccountName", BindingFlags.Public | BindingFlags.Static)?.GetMethod;
        if (sanGet != null)
        {
            var repl = typeof(LinuxBootstrap).GetMethod(nameof(GetServiceAccountNameReplacement),
                BindingFlags.Public | BindingFlags.Static)!;
            ApplyJmpHook(sanGet, repl, "NavEnvironment.get_ServiceAccountName");
        }

        // Hook: get_Instance — original calls `new NavEnvironment(flags)` whose body deref's a null field.
        // We pre-create an uninitialized instance and store it in the singleton field; getter returns it.
        var instanceField = envType.GetField("instance", BindingFlags.NonPublic | BindingFlags.Static);
        if (instanceField != null)
        {
            var skeletonInstance = RuntimeHelpers.GetUninitializedObject(envType);
            // Force any required instance fields. lockObject is required by Dispose.
            var instLock = envType.GetField("lockObject", BindingFlags.NonPublic | BindingFlags.Instance);
            if (instLock != null) instLock.SetValue(skeletonInstance, new object());
            instanceField.SetValue(null, skeletonInstance);
            Console.WriteLine($"[Linux] Pre-populated NavEnvironment.instance with skeleton object");
        }
        var getInstance = envType.GetProperty("Instance", BindingFlags.Public | BindingFlags.Static)?.GetMethod;
        if (getInstance != null)
        {
            var repl = typeof(LinuxBootstrap).GetMethod(nameof(GetInstanceReplacement),
                BindingFlags.Public | BindingFlags.Static)!;
            ApplyJmpHook(getInstance, repl, "NavEnvironment.get_Instance");
        }

        // Hook 4: EmitServerStartupTraceEvents touches System.Drawing fonts → no-op
        var emit = envType.GetMethod("EmitServerStartupTraceEvents",
            BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance);
        if (emit != null)
        {
            var repl = typeof(LinuxBootstrap).GetMethod(
                emit.IsStatic ? nameof(NoOp2) : nameof(NoOp3),
                BindingFlags.Public | BindingFlags.Static)!;
            ApplyJmpHook(emit, repl, "NavEnvironment.EmitServerStartupTraceEvents");
        }
    }

    // === Replacements ===
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static void NavEnvironmentCctorReplacement()
    {
        Console.WriteLine("[Linux] NavEnvironment..cctor replacement running");
        var t = _navEnvironmentType!;
        SetStaticField(t, "lockObject", new object());
        SetStaticField(t, "instanceId", Guid.NewGuid());
        SetStaticField(t, "serviceInstanceName", string.Empty);
        // Deliberately do NOT touch serviceAccount — getters are JMP-hooked above.
        // Try to init optional fields; ignore failures.
        TryInitDefault(t, "compactLohGate");
        TryInitDefault(t, "TerminatedSessionsMetric");
        TryInitDefault(t, "defaultAwaitedShutdownConnectionTypesList");
        TryInitDefault(t, "defaultRestartNotificationConnectionTypesList");
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static object? GetInstanceReplacement()
    {
        var f = _navEnvironmentType!.GetField("instance", BindingFlags.NonPublic | BindingFlags.Static);
        return f?.GetValue(null);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static object? GetServiceAccountReplacement() =>
        new System.Security.Principal.SecurityIdentifier("S-1-5-18");

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static string GetServiceAccountNameReplacement() => "SYSTEM";

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static void NoOp2(object? a, object? b) { }
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static void NoOp3(object? self, object? a, object? b) { }

    // === JMP-hook plumbing (verbatim from bc-linux) ===
    [DllImport("libc", SetLastError = true)]
    private static extern int mprotect(IntPtr addr, nuint len, int prot);
    private const int PROT_READ = 1, PROT_WRITE = 2, PROT_EXEC = 4;

    private static void ApplyJmpHook(MethodBase original, MethodInfo replacement, string name)
    {
        RuntimeHelpers.PrepareMethod(original.MethodHandle);
        RuntimeHelpers.PrepareMethod(replacement.MethodHandle);
        var origFp = original.MethodHandle.GetFunctionPointer();
        var replFp = replacement.MethodHandle.GetFunctionPointer();

        IntPtr compiledCode = IntPtr.Zero;
        try
        {
            byte[] precode = new byte[24];
            Marshal.Copy(origFp, precode, 0, 24);
            // .NET 8 x64 FixupPrecode: MOV r10,MD ; JMP [rip+disp32]
            if (precode[10] == 0xFF && precode[11] == 0x25)
            {
                int disp32 = BitConverter.ToInt32(precode, 12);
                compiledCode = Marshal.ReadIntPtr(origFp + 16 + disp32);
            }
            // StubPrecode: JMP [rip+disp32]
            if (compiledCode == IntPtr.Zero && precode[0] == 0xFF && precode[1] == 0x25)
            {
                int disp32 = BitConverter.ToInt32(precode, 2);
                compiledCode = Marshal.ReadIntPtr(origFp + 6 + disp32);
            }
            // E9 relative
            if (compiledCode == IntPtr.Zero && precode[0] == 0xE9)
            {
                int disp32 = BitConverter.ToInt32(precode, 1);
                compiledCode = origFp + 5 + disp32;
            }
        }
        catch { }

        WriteJmp(origFp, replFp, name);
        if (compiledCode != IntPtr.Zero && compiledCode != origFp && compiledCode != replFp)
        {
            try { WriteJmp(compiledCode, replFp, name + " (compiled)"); }
            catch (Exception ex) { Console.WriteLine($"[Linux]   compiled patch failed: {ex.Message}"); }
        }
    }

    private static void WriteJmp(IntPtr target, IntPtr destination, string name)
    {
        // x86-64 absolute indirect: FF 25 00 00 00 00 [imm64]
        byte[] jmp = new byte[14];
        jmp[0] = 0xFF; jmp[1] = 0x25;
        BitConverter.GetBytes(destination.ToInt64()).CopyTo(jmp, 6);
        long pageSize = 4096;
        long addr = target.ToInt64();
        long pageStart = addr & ~(pageSize - 1);
        var regionSize = (nuint)((addr - pageStart) + jmp.Length + pageSize);
        if (mprotect(new IntPtr(pageStart), regionSize, PROT_READ | PROT_WRITE | PROT_EXEC) != 0)
        {
            Console.WriteLine($"[Linux] mprotect failed for {name}: errno={Marshal.GetLastWin32Error()}");
            return;
        }
        Marshal.Copy(jmp, 0, target, jmp.Length);
        Console.WriteLine($"[Linux] Patched {name} at 0x{target:X} -> 0x{destination:X}");
    }

    // === Static field setters ===
    private static void SetStaticField(Type type, string name, object? value)
    {
        var f = type.GetField(name, BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
        if (f == null) { Console.WriteLine($"[Linux]   field {name} not found"); return; }
        try { f.SetValue(null, value); }
        catch (FieldAccessException) { SetReadonlyStaticField(f, value); }
    }

    private static void SetReadonlyStaticField(FieldInfo field, object? value)
    {
        var dm = new DynamicMethod($"set_{field.Name}", typeof(void), new[] { typeof(object) },
            field.DeclaringType!.Module, skipVisibility: true);
        var il = dm.GetILGenerator();
        if (value == null) il.Emit(OpCodes.Ldnull);
        else
        {
            il.Emit(OpCodes.Ldarg_0);
            if (field.FieldType.IsValueType) il.Emit(OpCodes.Unbox_Any, field.FieldType);
        }
        il.Emit(OpCodes.Stsfld, field);
        il.Emit(OpCodes.Ret);
        ((Action<object?>)dm.CreateDelegate(typeof(Action<object?>)))(value);
    }

    private static void TryInitDefault(Type type, string fieldName)
    {
        var f = type.GetField(fieldName, BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
        if (f == null) return;
        try { SetStaticField(type, fieldName, Activator.CreateInstance(f.FieldType)); }
        catch (Exception ex) { Console.WriteLine($"[Linux]   {fieldName} ({f.FieldType.Name}): {ex.GetType().Name}"); }
    }
}
