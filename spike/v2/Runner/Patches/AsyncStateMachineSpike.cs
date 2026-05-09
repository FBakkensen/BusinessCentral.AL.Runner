// AsyncStateMachineSpike — entry-point hook for ALFieldCaptionAsync
// and closed-instantiation enumerator for NavObjectDictionary`2.get_Target.
//
// (A) Async entry-point hook (NOT MoveNext):
//   The earlier agent (§K) tried to hook the async method directly and reported
//   "hangs the test process". That agent was attempting to hook the state-machine
//   *MoveNext* method (private, struct, non-trivial ABI). This spike takes a
//   different approach: hook the *entry point* — the outer sync wrapper that
//   BC's compiler generates to create the state-machine struct and kick off the
//   first MoveNext call. The entry point:
//     ValueTask<string> ALFieldCaptionAsync(int fieldNo)
//   is a regular non-generic instance method whose FunctionPointer is directly
//   accessible. Our replacement returns ValueTask<string>.FromResult("") which
//   satisfies the awaiting callers without any state-machine creation.
//
//   Investigation confirmed (§T):
//   - smType.IsValueType = true (struct)
//   - MoveNext is private via explicit interface impl (complex to hook as struct)
//   - Entry point ALFieldCaptionAsync: FunctionPointer OK, ContainsGenericParameters=false
//   - return type == System.Threading.Tasks.ValueTask<string> (same BCL type we reference)
//
// (B) Generic via closed-instantiation enumeration:
//   NavObjectDictionary`2.get_Target has ContainsGenericParameters=true on the
//   open generic type — not directly hookable. After the test assembly is loaded,
//   we scan all loaded assemblies for closed instantiations of NavObjectDictionary`2
//   and hook each one's get_Target individually. Each closed instantiation is a
//   non-generic type that JmpHook can patch normally.
//
// Both strategies use only JmpHook/mprotect — no Harmony, no MonoMod.
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using AlRunnerV2.Infrastructure;

namespace AlRunnerV2;

public static partial class BcRuntime
{
    // ── (A) ALFieldCaptionAsync entry-point hook ────────────────────────────

    /// <summary>
    /// Hooks NavRecord.ALFieldCaptionAsync(int) to return an already-completed
    /// ValueTask&lt;string&gt; with an empty string, bypassing the full field-caption
    /// metadata lookup that NREs on the skeleton session.
    ///
    /// Replacement signature: ValueTask&lt;string&gt;(object self, int fieldNo)
    /// — matches the instance-method ABI: first arg = receiver (NavRecord as object),
    ///   second arg = the int parameter.
    /// </summary>
    /// <summary>
    /// Replacement for NavRecord.ALFieldCaptionAsync(int).
    /// Returns an already-completed ValueTask&lt;string&gt; with an empty string,
    /// bypassing the full field-caption metadata lookup that NREs on the skeleton.
    /// </summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static ValueTask<string> NavRecord_ALFieldCaptionAsync(object self, int fieldNo)
        => ValueTask.FromResult(string.Empty);

    internal static void ApplyALFieldCaptionAsyncHook(Assembly navNcl)
    {
        var navRecordType = navNcl.GetType("Microsoft.Dynamics.Nav.Runtime.NavRecord");
        if (navRecordType == null)
        {
            Console.Error.WriteLine("[AsyncSM] NavRecord not found — skipping");
            return;
        }

        var entryPoint = navRecordType
            .GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
            .FirstOrDefault(m => m.Name == "ALFieldCaptionAsync");

        if (entryPoint == null)
        {
            Console.Error.WriteLine("[AsyncSM] ALFieldCaptionAsync entry point not found");
            return;
        }

        if (entryPoint.ContainsGenericParameters)
        {
            Console.Error.WriteLine("[AsyncSM] ALFieldCaptionAsync has open generic params — unexpected, skipping");
            return;
        }

        if (entryPoint.ReturnType != typeof(ValueTask<string>))
        {
            Console.Error.WriteLine($"[AsyncSM] Return type mismatch: expected ValueTask<string>, got {entryPoint.ReturnType.FullName}");
            return;
        }

        Console.Error.WriteLine($"[AsyncSM] Hooking entry point: {entryPoint}");
        var repl = typeof(BcRuntime).GetMethod(nameof(NavRecord_ALFieldCaptionAsync),
            BindingFlags.Public | BindingFlags.Static)!;
        JmpHook.Apply(entryPoint, repl, "NavRecord.ALFieldCaptionAsync(int)");
        Console.Error.WriteLine("[AsyncSM] ALFieldCaptionAsync hook ACTIVE (slot-only, compiledCode patching disabled due to crash-on-write)");
    }

    // ── (B) NavObjectDictionary`2 closed-instantiation get_Target hooks ─────

    private static Type? _navObjDictOpenGeneric;  // cached open generic typedef

    /// <summary>
    /// Enumerates all closed instantiations of NavObjectDictionary`2 that are
    /// currently loaded and hooks each one's get_Target to return null. Must be
    /// called after the test assembly is loaded so the closed types the test DLL
    /// uses are present in the AppDomain.
    /// </summary>
    internal static void ApplyNavObjectDictionaryGetTargetHooks(Assembly navNcl)
    {
        if (_navObjDictOpenGeneric == null)
        {
            _navObjDictOpenGeneric = navNcl.GetTypes()
                .FirstOrDefault(t => t.Name.StartsWith("NavObjectDictionary") && t.IsGenericTypeDefinition);
            if (_navObjDictOpenGeneric == null)
            {
                Console.Error.WriteLine("[ObjDict] NavObjectDictionary`2 open generic not found");
                return;
            }
        }

        var openGenericFqn = _navObjDictOpenGeneric.FullName!;
        int hookCount = 0;
        int skipCount = 0;
        int errCount  = 0;

        foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
        {
            Type[] types;
            try { types = asm.GetTypes(); }
            catch { continue; }

            foreach (var t in types)
            {
                if (!t.IsGenericType || t.IsGenericTypeDefinition) continue;

                string? defName = null;
                try { defName = t.GetGenericTypeDefinition().FullName; }
                catch { continue; }
                if (defName != openGenericFqn) continue;

                // Closed NavObjectDictionary`2<K,V>
                var getTarget = t.GetProperty("Target",
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                    ?.GetGetMethod(true);

                if (getTarget == null) { skipCount++; continue; }
                if (getTarget.ContainsGenericParameters) { skipCount++; continue; }

                // Replacement: a static method that returns null.
                // The calling convention is: first arg = receiver (the NavObjectDictionary<K,V>
                // instance as an object reference). Return type must be compatible with
                // SharedNavObjectDictionary`2<K,V> (which is a reference type) — we return null.
                try
                {
                    var repl = typeof(BcRuntime).GetMethod(nameof(NavObjectDictionary_get_Target),
                        BindingFlags.Public | BindingFlags.Static)!;
                    JmpHook.Apply(getTarget, repl,
                        $"NavObjectDictionary`2<{string.Join(",", t.GetGenericArguments().Select(a => a.Name))}>.get_Target");
                    hookCount++;
                    Console.Error.WriteLine($"[ObjDict] Hooked: {t.FullName}.get_Target");
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"[ObjDict] Hook failed for {t.Name}: {ex.Message}");
                    errCount++;
                }
            }
        }

        Console.Error.WriteLine(
            $"[ObjDict] Scan complete: {hookCount} hooked, {skipCount} skipped, {errCount} errors");
    }

    /// <summary>
    /// Replacement for NavObjectDictionary`2&lt;K,V&gt;.get_Target.
    /// Returns null — callers that null-check will handle gracefully;
    /// callers that don't will throw NullReferenceException (same class as before,
    /// but now with a different stack rather than an InvalidOperationException
    /// deep in NCLMetadata).
    /// </summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static object? NavObjectDictionary_get_Target(object self) => null;
}
