// HarmonySpike.cs — empirical validation of Harmony IL-patching on MS R2R/precompiled BC types.
//
// TWO TARGETS:
//   (a) NavRecord.ALFieldCaptionAsync(int)      — async / ValueTask<string>-returning method
//   (b) NavObjectDictionary`2.get_Target        — property getter on a generic type
//
// This file is a SPIKE ONLY. It adds no permanent functionality.
// Call HarmonySpike.Apply() from BcRuntime.EnsureApplied() after ForceLoadBcDlls().
//
// Findings are written to stderr with prefix [HarmonySpike].
using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;

namespace AlRunnerV2.Patches;

internal static class HarmonySpike
{
    private static bool _applied;
    // Harmony instance — id must be unique across the process
    private static readonly Harmony _harmony = new("al-runner.spike");

    // Timing counters (cheap volatile long, not Stopwatch, to minimise per-call overhead)
    private static long _fieldCaptionCallCount;
    private static long _dictTargetCallCount;
    private static long _fieldCaptionTotalTicks;
    private static long _dictTargetTotalTicks;

    public static void Apply()
    {
        if (_applied) return;
        _applied = true;

        var navNcl = AppDomain.CurrentDomain.GetAssemblies()
            .FirstOrDefault(a => a.GetName().Name == "Microsoft.Dynamics.Nav.Ncl");
        if (navNcl == null)
        {
            Console.Error.WriteLine("[HarmonySpike] FATAL: Microsoft.Dynamics.Nav.Ncl not loaded — spike aborted.");
            return;
        }

        // ── (a) NavRecord.ALFieldCaptionAsync(int) ──────────────────────────────────────────────
        PatchFieldCaptionAsync(navNcl);

        // ── (b) NavObjectDictionary`2.get_Target — find all closed instantiations ───────────────
        PatchObjectDictionaryTarget(navNcl);
    }

    // ────────────────────────────────────────────────────────────────────────────────────────────
    // (a) ALFieldCaptionAsync
    // ────────────────────────────────────────────────────────────────────────────────────────────

    private static void PatchFieldCaptionAsync(Assembly navNcl)
    {
        var navRecordType = navNcl.GetType("Microsoft.Dynamics.Nav.Runtime.NavRecord");
        if (navRecordType == null)
        {
            Console.Error.WriteLine("[HarmonySpike] (a) NavRecord type NOT FOUND — skipped.");
            return;
        }

        var target = navRecordType.GetMethod("ALFieldCaptionAsync",
            BindingFlags.Public | BindingFlags.Instance,
            null, new[] { typeof(int) }, null);
        if (target == null)
        {
            Console.Error.WriteLine("[HarmonySpike] (a) NavRecord.ALFieldCaptionAsync(int) NOT FOUND — skipped.");
            return;
        }

        Console.Error.WriteLine($"[HarmonySpike] (a) Found: {target.ReturnType.Name} NavRecord.{target.Name}(int)");

        var prefix = typeof(HarmonySpike).GetMethod(nameof(Prefix_ALFieldCaptionAsync),
            BindingFlags.NonPublic | BindingFlags.Static)!;
        try
        {
            _harmony.Patch(target, prefix: new HarmonyMethod(prefix));
            Console.Error.WriteLine("[HarmonySpike] (a) Harmony.Patch ALFieldCaptionAsync — OK");
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[HarmonySpike] (a) Harmony.Patch ALFieldCaptionAsync THREW: {ex.GetType().Name}: {ex.Message}");
        }
    }

    /// <summary>
    /// Harmony prefix for NavRecord.ALFieldCaptionAsync(int fieldNo).
    /// Returns false → Harmony replaces the original body.
    /// __result is set to a completed ValueTask{string} so the async caller gets "" without
    /// entering the C# state machine (which would try to reach Session.PushDynamicCaptionStack
    /// and NRE on the skeleton session).
    /// </summary>
    [HarmonyPrefix]
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static bool Prefix_ALFieldCaptionAsync(
        object __instance, int fieldNo, ref System.Threading.Tasks.ValueTask<string> __result)
    {
        var t0 = System.Diagnostics.Stopwatch.GetTimestamp();
        __result = new System.Threading.Tasks.ValueTask<string>(string.Empty);
        var dt = System.Diagnostics.Stopwatch.GetTimestamp() - t0;
        System.Threading.Interlocked.Increment(ref _fieldCaptionCallCount);
        System.Threading.Interlocked.Add(ref _fieldCaptionTotalTicks, dt);
        return false; // skip original
    }

    // ────────────────────────────────────────────────────────────────────────────────────────────
    // (b) NavObjectDictionary`2.get_Target
    // ────────────────────────────────────────────────────────────────────────────────────────────

    // Stores the concrete dictionary type(s) we successfully patched (for diagnostics).
    private static readonly List<string> _patchedDictInstantiations = new();

    private static void PatchObjectDictionaryTarget(Assembly navNcl)
    {
        var openDictType = navNcl.GetType("Microsoft.Dynamics.Nav.Runtime.NavObjectDictionary`2");
        if (openDictType == null)
        {
            Console.Error.WriteLine("[HarmonySpike] (b) NavObjectDictionary`2 type NOT FOUND — skipped.");
            return;
        }

        // The internal `get_Target` getter is on the open generic type definition.
        var openGetter = openDictType.GetProperty("Target",
            BindingFlags.NonPublic | BindingFlags.Instance)?.GetGetMethod(nonPublic: true);
        if (openGetter == null)
        {
            Console.Error.WriteLine("[HarmonySpike] (b) NavObjectDictionary`2.get_Target NOT FOUND — skipped.");
            return;
        }

        Console.Error.WriteLine($"[HarmonySpike] (b) Found open getter: {openGetter.Name} on {openDictType.Name}");

        // Strategy 1: patch the open generic method definition directly.
        // Harmony 2.x can sometimes handle open generics via MakeGenericMethod on specific
        // instantiation. Try the instantiation we know is used: NavObjectDictionary<Guid, NavCodeunitHandle>.
        var guidType = typeof(Guid);
        var codeunitHandleType = navNcl.GetType("Microsoft.Dynamics.Nav.Runtime.NavCodeunitHandle");
        if (codeunitHandleType == null)
        {
            Console.Error.WriteLine("[HarmonySpike] (b) NavCodeunitHandle NOT FOUND — trying open generic patch.");
        }

        // Collect closed instantiations already in the loaded types.
        var closedInstantiations = AppDomain.CurrentDomain.GetAssemblies()
            .SelectMany(a => { try { return a.GetTypes(); } catch { return Array.Empty<Type>(); } })
            .Where(t => t.IsConstructedGenericType
                        && t.GetGenericTypeDefinition() == openDictType)
            .Distinct()
            .ToList();

        Console.Error.WriteLine($"[HarmonySpike] (b) Closed instantiations found in loaded assemblies: {closedInstantiations.Count}");
        foreach (var inst in closedInstantiations.Take(5))
            Console.Error.WriteLine($"[HarmonySpike] (b)   {inst.FullName}");

        // Build extra instantiations to try.
        var toTry = new List<Type>(closedInstantiations);
        if (codeunitHandleType != null)
        {
            try { toTry.Add(openDictType.MakeGenericType(guidType, codeunitHandleType)); } catch { }
        }

        var prefix = typeof(HarmonySpike).GetMethod(nameof(Prefix_NavObjectDictionary_get_Target),
            BindingFlags.NonPublic | BindingFlags.Static)!;

        int patched = 0;
        foreach (var closed in toTry.Distinct())
        {
            var closedGetter = closed.GetProperty("Target",
                BindingFlags.NonPublic | BindingFlags.Instance)?.GetGetMethod(nonPublic: true);
            if (closedGetter == null) continue;

            try
            {
                // Force JIT so Harmony can get the function pointer.
                RuntimeHelpers.PrepareMethod(closedGetter.MethodHandle);
                _harmony.Patch(closedGetter, prefix: new HarmonyMethod(prefix));
                _patchedDictInstantiations.Add(closed.Name);
                patched++;
                Console.Error.WriteLine($"[HarmonySpike] (b) Patched: {closed.Name}.get_Target — OK");
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[HarmonySpike] (b) Patch {closed.Name}.get_Target THREW: {ex.GetType().Name}: {ex.Message}");
            }
        }

        if (patched == 0)
            Console.Error.WriteLine("[HarmonySpike] (b) No instantiations patched — spike is partial.");
        else
            Console.Error.WriteLine($"[HarmonySpike] (b) Total patched: {patched} instantiation(s).");
    }

    /// <summary>
    /// Harmony prefix for NavObjectDictionary{TKey,TValue}.get_Target (internal property).
    ///
    /// The real getter does:
    ///   SharedNavObjectDictionary target = (SharedNavObjectDictionary)base.Tree.GetReferenceTarget();
    ///   if (target == null) {
    ///     target = new SharedNavObjectDictionary(base.Tree.Session.Company.SharedObjects);  ← NRE here
    ///     base.Tree.SetReferenceTarget(target);
    ///   }
    ///   return target;
    ///
    /// The NRE is Session.Company (null on skeleton session).  The safe minimal fix is:
    ///   - if Tree.GetReferenceTarget() is non-null, let the original run (it just casts and returns);
    ///   - if it's null, return null ourselves so callers see a null-handle (ALCount=0, etc.).
    ///
    /// We can't return the correct return type from a generic method's prefix here without
    /// a compiled generic — so we set __result = null (object) and hope callers handle null.
    /// The actual signature check is done by Harmony via the MethodInfo we patched.
    /// </summary>
    [HarmonyPrefix]
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static bool Prefix_NavObjectDictionary_get_Target(
        object __instance, ref object? __result)
    {
        var t0 = System.Diagnostics.Stopwatch.GetTimestamp();

        // Try to call GetReferenceTarget via reflection to check if a target already exists.
        // If yes, let the original run (it just casts — safe path, no NRE).
        try
        {
            // __instance is the NavObjectDictionary<K,V>; Tree is via base.Tree (NavComplexValue.Tree).
            var treeField = __instance.GetType().BaseType?.GetProperty("Tree",
                BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance);
            // Walk up until we find the tree property
            Type? t = __instance.GetType();
            while (t != null && treeField == null)
            {
                treeField = t.GetProperty("Tree",
                    BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance);
                t = t.BaseType;
            }
            if (treeField != null)
            {
                var tree = treeField.GetValue(__instance);
                if (tree != null)
                {
                    var getReferenceTarget = tree.GetType().GetMethod("GetReferenceTarget",
                        BindingFlags.Public | BindingFlags.Instance, null, Type.EmptyTypes, null);
                    var existingTarget = getReferenceTarget?.Invoke(tree, null);
                    if (existingTarget != null)
                    {
                        // Target already set — let the original run (just a cast, no NRE).
                        var dt2 = System.Diagnostics.Stopwatch.GetTimestamp() - t0;
                        System.Threading.Interlocked.Increment(ref _dictTargetCallCount);
                        System.Threading.Interlocked.Add(ref _dictTargetTotalTicks, dt2);
                        return true; // run original — safe path
                    }
                }
            }
        }
        catch { /* if reflection fails, fall through to null return */ }

        // Target is null and creating it would NRE via Session.Company.SharedObjects.
        // Return null — callers like ALCount check TargetOrNull first, so null is tolerated.
        __result = null;
        var dt = System.Diagnostics.Stopwatch.GetTimestamp() - t0;
        System.Threading.Interlocked.Increment(ref _dictTargetCallCount);
        System.Threading.Interlocked.Add(ref _dictTargetTotalTicks, dt);
        return false; // skip original
    }

    // ────────────────────────────────────────────────────────────────────────────────────────────
    // Diagnostic summary — call after test run completes
    // ────────────────────────────────────────────────────────────────────────────────────────────

    public static void PrintSummary()
    {
        double freq = System.Diagnostics.Stopwatch.Frequency;
        double avgField = _fieldCaptionCallCount > 0
            ? (_fieldCaptionTotalTicks / (double)_fieldCaptionCallCount) / freq * 1e6 : 0;
        double avgDict = _dictTargetCallCount > 0
            ? (_dictTargetTotalTicks / (double)_dictTargetCallCount) / freq * 1e6 : 0;

        Console.Error.WriteLine("[HarmonySpike] === Summary ===");
        Console.Error.WriteLine($"[HarmonySpike] (a) ALFieldCaptionAsync calls intercepted: {_fieldCaptionCallCount}  avg overhead: {avgField:F2} µs/call");
        Console.Error.WriteLine($"[HarmonySpike] (b) NavObjectDictionary.Target intercepted: {_dictTargetCallCount}  avg overhead: {avgDict:F2} µs/call");
        Console.Error.WriteLine($"[HarmonySpike] (b) Patched instantiations: [{string.Join(", ", _patchedDictInstantiations)}]");
    }
}
