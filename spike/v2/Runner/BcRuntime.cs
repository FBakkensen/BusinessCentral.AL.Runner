// BcRuntime — applies Linux-compatibility patches to BC service-tier DLLs at process start.
// Lifted directly from spike/bc-abi-identity/runner/LinuxBootstrap.cs (proven to work end-to-end).
// Pattern: bc-linux's JMP-hook via mprotect + RuntimeHelpers.PrepareMethod.
using System.Collections.Concurrent;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace AlRunnerV2;

public static class BcRuntime
{
    private static bool _applied;
    private static Type? _navEnvironmentType;
    private static object? _skeletonSession;
    public static Microsoft.Dynamics.Nav.Runtime.ITreeObject? RootTreeStub;

    // Set to the currently-loaded test assembly so CreateTarget looks up codeunit types there.
    private static Assembly? _currentTestAssembly;
    public static void SetTestAssembly(Assembly asm)
    {
        if (_currentTestAssembly == asm) return;
        _currentTestAssembly = asm;
        _codeunitTypeCache.Clear();
    }

    public static void EnsureApplied()
    {
        if (_applied) return;
        _applied = true;
        Win32Stubs.Register();
        ForceLoadBcDlls();
        var navNcl = AppDomain.CurrentDomain.GetAssemblies()
            .First(a => a.GetName().Name == "Microsoft.Dynamics.Nav.Ncl");
        RootTreeStub = new RootTreeObject();
        ApplyAllPatches(navNcl);
    }

    private static void ForceLoadBcDlls()
    {
        var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".local/share/al-runner/artifacts/27.5.46862.48827");
        foreach (var n in new[] { "Microsoft.Dynamics.Nav.Common", "Microsoft.Dynamics.Nav.Types",
                                  "Microsoft.Dynamics.Nav.Language", "Microsoft.Dynamics.Nav.Ncl" })
            Assembly.LoadFrom(Path.Combine(dir, n + ".dll"));
    }

    private static void ApplyAllPatches(Assembly navNcl)
    {
        var envType = navNcl.GetType("Microsoft.Dynamics.Nav.Runtime.NavEnvironment")
            ?? throw new InvalidOperationException("NavEnvironment not found");
        _navEnvironmentType = envType;

        // NavEnvironment.cctor — replace WindowsIdentity-touching init
        Hook(envType.TypeInitializer!, nameof(NavEnvironmentCctorReplacement), "NavEnvironment..cctor");
        HookProperty(envType, "ServiceAccount", true, nameof(GetServiceAccountReplacement));
        HookProperty(envType, "ServiceAccountName", true, nameof(GetServiceAccountNameReplacement));
        HookMethodIfExists(envType, "EmitServerStartupTraceEvents",
            BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance,
            (m) => m.IsStatic ? nameof(NoOp2) : nameof(NoOp3));

        // Pre-populate NavEnvironment.instance to a skeleton; hook Instance getter.
        var instField = envType.GetField("instance", BindingFlags.NonPublic | BindingFlags.Static);
        if (instField != null)
        {
            var skel = RuntimeHelpers.GetUninitializedObject(envType);
            var instLock = envType.GetField("lockObject", BindingFlags.NonPublic | BindingFlags.Instance);
            if (instLock != null) instLock.SetValue(skel, new object());
            instField.SetValue(null, skel);
        }
        HookProperty(envType, "Instance", true, nameof(GetInstanceReplacement));

        // NavApplicationObjectBase.get_Session — return skeleton NavSession
        var aoType = navNcl.GetType("Microsoft.Dynamics.Nav.Runtime.NavApplicationObjectBase");
        var sessType = navNcl.GetType("Microsoft.Dynamics.Nav.Runtime.NavSession");
        if (aoType != null && sessType != null)
        {
            _skeletonSession = RuntimeHelpers.GetUninitializedObject(sessType);
            HookProperty(aoType, "Session", false, nameof(GetSessionReplacement));
        }
        if (sessType != null)
        {
            HookProperty(sessType, "CurrentMethodScope", false, nameof(GetCurrentMethodScopeReplacement));
            // VerifyExecutePermission overloads → no-op
            foreach (var m in sessType.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                .Where(m => m.Name == "VerifyExecutePermission" && m.ReturnType == typeof(void)))
            {
                var p = m.GetParameters().Length;
                var noop = p switch { 1 => nameof(NoOp2), 2 => nameof(NoOp3), _ => null };
                if (noop != null) Hook(m, noop, $"NavSession.VerifyExecutePermission/{p}");
            }
        }

        // NavMethodScope.ThrowStackOverflow — stack-depth check uses non-NavMethodScope, false-positive
        var msType = navNcl.GetType("Microsoft.Dynamics.Nav.Runtime.NavMethodScope");
        if (msType != null)
        {
            var tso = msType.GetMethod("ThrowStackOverflow",
                BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Static | BindingFlags.Instance);
            if (tso != null)
            {
                var p = tso.GetParameters().Length + (tso.IsStatic ? 0 : 1);
                var noop = p switch { 1 => nameof(NoOp_OneArg), 2 => nameof(NoOp2), _ => null };
                if (noop != null) Hook(tso, noop, "NavMethodScope.ThrowStackOverflow");
            }
        }

        // NavCodeunitHandle.CreateTarget — bypass NavGlobal.NCLMetadata by constructing
        // the codeunit directly from the loaded compiled assembly via reflection.
        var codeunitHandleType = navNcl.GetType("Microsoft.Dynamics.Nav.Runtime.NavCodeunitHandle");
        if (codeunitHandleType != null)
        {
            var createTarget = codeunitHandleType.GetMethod("CreateTarget",
                BindingFlags.NonPublic | BindingFlags.Instance);
            if (createTarget != null)
                Hook(createTarget, nameof(NavCodeunitHandle_CreateTarget), "NavCodeunitHandle.CreateTarget");
        }

        // NavCancellationToken throws — uninitialized cancellation tokens trip the check.
        var typesAsm = AppDomain.CurrentDomain.GetAssemblies()
            .FirstOrDefault(a => a.GetName().Name == "Microsoft.Dynamics.Nav.Types");
        var ctType = typesAsm?.GetType("Microsoft.Dynamics.Nav.Types.NavCancellationToken");
        if (ctType != null)
        {
            foreach (var name in new[] { "ThrowOperationCanceledException", "ThrowIfCancellationRequested" })
            foreach (var m in ctType.GetMethods(BindingFlags.Public | BindingFlags.NonPublic
                                                | BindingFlags.Instance | BindingFlags.Static)
                                    .Where(mm => mm.Name == name))
            {
                var p = m.GetParameters().Length + (m.IsStatic ? 0 : 1);
                var noop = p switch { 1 => nameof(NoOp_OneArg), 2 => nameof(NoOp2), _ => null };
                if (noop != null) Hook(m, noop, $"NavCancellationToken.{name}/{m.GetParameters().Length}");
            }
        }
    }

    private static void HookProperty(Type t, string propName, bool isStatic, string replacementName)
    {
        var flags = BindingFlags.Public | BindingFlags.NonPublic |
                    (isStatic ? BindingFlags.Static : BindingFlags.Instance);
        var p = t.GetProperty(propName, flags);
        if (p?.GetMethod != null) Hook(p.GetMethod, replacementName, $"{t.Name}.get_{propName}");
    }

    private static void HookMethodIfExists(Type t, string methodName, BindingFlags flags,
                                           Func<MethodInfo, string?> picker)
    {
        var m = t.GetMethod(methodName, flags);
        if (m == null) return;
        var name = picker(m);
        if (name != null) Hook(m, name, $"{t.Name}.{methodName}");
    }

    private static void Hook(MethodBase original, string replacementName, string description)
    {
        var repl = typeof(BcRuntime).GetMethod(replacementName,
            BindingFlags.Public | BindingFlags.Static)
            ?? throw new InvalidOperationException($"Replacement {replacementName} not found");
        JmpHook.Apply(original, repl, description);
    }

    // === Replacement methods ===
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static void NavEnvironmentCctorReplacement()
    {
        var t = _navEnvironmentType!;
        FieldPoke.SetStatic(t, "lockObject", new object());
        FieldPoke.SetStatic(t, "instanceId", Guid.NewGuid());
        FieldPoke.SetStatic(t, "serviceInstanceName", string.Empty);
        FieldPoke.TryInitDefault(t, "compactLohGate");
        FieldPoke.TryInitDefault(t, "TerminatedSessionsMetric");
        FieldPoke.TryInitDefault(t, "defaultAwaitedShutdownConnectionTypesList");
        FieldPoke.TryInitDefault(t, "defaultRestartNotificationConnectionTypesList");
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static object? GetServiceAccountReplacement() =>
        new System.Security.Principal.SecurityIdentifier("S-1-5-18");

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static string GetServiceAccountNameReplacement() => "SYSTEM";

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static object? GetInstanceReplacement()
    {
        var f = _navEnvironmentType!.GetField("instance", BindingFlags.NonPublic | BindingFlags.Static);
        return f?.GetValue(null);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static object? GetSessionReplacement(object self) => _skeletonSession;

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static object? GetCurrentMethodScopeReplacement(object self) => RootTreeStub;

    [MethodImpl(MethodImplOptions.NoInlining)] public static void NoOp_OneArg(object? a) { }
    [MethodImpl(MethodImplOptions.NoInlining)] public static void NoOp2(object? a, object? b) { }
    [MethodImpl(MethodImplOptions.NoInlining)] public static void NoOp3(object? a, object? b, object? c) { }

    // Cache: codeunit ID → generated codeunit Type (keyed per loaded assembly bytes).
    private static readonly ConcurrentDictionary<int, Type?> _codeunitTypeCache = new();

    /// <summary>
    /// Replacement for NavCodeunitHandle.CreateTarget().
    /// Bypasses NavGlobal.NCLMetadata by looking up the compiled codeunit class directly
    /// from the loaded assembly and constructing it via the 1-arg ITreeObject ctor.
    /// </summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static Microsoft.Dynamics.Nav.Runtime.NavCodeunit NavCodeunitHandle_CreateTarget(
        Microsoft.Dynamics.Nav.Runtime.NavCodeunitHandle self)
    {
        int id = self.ObjectId.ObjectNumber;
        var codeunitType = _codeunitTypeCache.GetOrAdd(id, FindCodeunitType);
        if (codeunitType == null)
            throw new InvalidOperationException(
                $"NavCodeunitHandle.CreateTarget: no loaded type Codeunit{id} found");
        var ctor = codeunitType.GetConstructors()
            .FirstOrDefault(c => c.GetParameters().Length == 1 &&
                typeof(Microsoft.Dynamics.Nav.Runtime.ITreeObject)
                    .IsAssignableFrom(c.GetParameters()[0].ParameterType));
        if (ctor == null)
            throw new InvalidOperationException(
                $"Codeunit{id} has no single-arg ITreeObject constructor");
        return (Microsoft.Dynamics.Nav.Runtime.NavCodeunit)ctor.Invoke(new object[] { self });
    }

    private static Type? FindCodeunitType(int id)
    {
        var baseCu = typeof(Microsoft.Dynamics.Nav.Runtime.NavCodeunit);
        var name = $"Codeunit{id}";
        // Search the current test assembly first (avoids cross-bucket ID collisions).
        if (_currentTestAssembly != null)
        {
            try
            {
                var t = Array.Find(_currentTestAssembly.GetTypes(),
                    x => x.Name == name && baseCu.IsAssignableFrom(x));
                if (t != null) return t;
            }
            catch { }
        }
        // Fall back to all loaded assemblies (e.g. stubs in other assemblies).
        foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
        {
            if (asm == _currentTestAssembly) continue;
            try
            {
                var t = Array.Find(asm.GetTypes(),
                    x => x.Name == name && baseCu.IsAssignableFrom(x));
                if (t != null) return t;
            }
            catch { /* skip dynamic/reflection-only assemblies */ }
        }
        return null;
    }
}

// --- supporting helpers ---

internal sealed class RootTreeObject : Microsoft.Dynamics.Nav.Runtime.ITreeObject
{
    private readonly RootHandler _h;
    public RootTreeObject() { _h = new RootHandler(this); }
    Microsoft.Dynamics.Nav.Runtime.TreeHandler Microsoft.Dynamics.Nav.Runtime.ITreeObject.Tree => _h;
    Microsoft.Dynamics.Nav.Runtime.TreeObjectType Microsoft.Dynamics.Nav.Runtime.ITreeObject.Type => default;
    bool Microsoft.Dynamics.Nav.Runtime.ITreeObject.SingleThreaded => false;
}

internal sealed class RootHandler : Microsoft.Dynamics.Nav.Runtime.TreeHandler
{
    private static readonly FieldInfo _fHost =
        typeof(Microsoft.Dynamics.Nav.Runtime.TreeHandler)
            .GetField("hostObject", BindingFlags.NonPublic | BindingFlags.Instance)!;
    public RootHandler(Microsoft.Dynamics.Nav.Runtime.ITreeObject host) : base()
    {
        // IsDisposed = (hostObject == null) — flip it.
        _fHost.SetValue(this, host);
    }
}

internal static class FieldPoke
{
    public static void SetStatic(Type t, string name, object? value)
    {
        var f = t.GetField(name, BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
        if (f == null) return;
        try { f.SetValue(null, value); }
        catch (FieldAccessException) { SetStaticReadonly(f, value); }
    }
    public static void TryInitDefault(Type t, string fieldName)
    {
        var f = t.GetField(fieldName, BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
        if (f == null) return;
        try { SetStatic(t, fieldName, Activator.CreateInstance(f.FieldType)); }
        catch { /* optional */ }
    }
    private static void SetStaticReadonly(FieldInfo field, object? value)
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
}

internal static class JmpHook
{
    [DllImport("libc", SetLastError = true)]
    private static extern int mprotect(IntPtr addr, nuint len, int prot);
    private const int PROT_READ = 1, PROT_WRITE = 2, PROT_EXEC = 4;

    public static void Apply(MethodBase original, MethodInfo replacement, string name)
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
                compiledCode = Marshal.ReadIntPtr(origFp + 16 + BitConverter.ToInt32(precode, 12));
            // StubPrecode
            if (compiledCode == IntPtr.Zero && precode[0] == 0xFF && precode[1] == 0x25)
                compiledCode = Marshal.ReadIntPtr(origFp + 6 + BitConverter.ToInt32(precode, 2));
            // E9 relative
            if (compiledCode == IntPtr.Zero && precode[0] == 0xE9)
                compiledCode = origFp + 5 + BitConverter.ToInt32(precode, 1);
        }
        catch { }

        WriteJmp(origFp, replFp);
        if (compiledCode != IntPtr.Zero && compiledCode != origFp && compiledCode != replFp)
            try { WriteJmp(compiledCode, replFp); } catch { }
    }

    private static void WriteJmp(IntPtr target, IntPtr destination)
    {
        // x86-64 absolute indirect: FF 25 00 00 00 00 [imm64]
        byte[] jmp = new byte[14];
        jmp[0] = 0xFF; jmp[1] = 0x25;
        BitConverter.GetBytes(destination.ToInt64()).CopyTo(jmp, 6);
        long pageSize = 4096;
        long addr = target.ToInt64();
        long pageStart = addr & ~(pageSize - 1);
        var regionSize = (nuint)((addr - pageStart) + jmp.Length + pageSize);
        if (mprotect(new IntPtr(pageStart), regionSize, PROT_READ | PROT_WRITE | PROT_EXEC) != 0) return;
        Marshal.Copy(jmp, 0, target, jmp.Length);
    }
}
