// NavRecordIdPatches — replacement for NavRecordId.get_CollationAwareStringComparer.
//
// Rationale: BC's real getter walks Session.Database.CollationAwareStringComparer.
// The skeleton runtime has a NavSession but its Tenant.database (LazyEx<NavDatabase>)
// is null, so the chain NREs deep inside NavRecordId.GetHashCode / Equals which is
// invoked from TempTableDataProvider.Modify on every record write.
//
// Faithful semantics: the getter caches a CollationAwareStringComparer on the
// NavRecordId instance once computed. We return a single process-wide cached
// comparer built from a default SqlSortingProperties (InvariantCulture, no
// case/accent flags, "default" collation). Two NavRecordIds with the same
// primary-key values will compare and hash identically — which is exactly what
// TempTableDataProvider.Modify needs to locate the row being modified.
//
// Per .claude/rules/precompiled-dll-respect.md: NavRecordId lives in
// Microsoft.Dynamics.Nav.Ncl.dll (engine, not AL business logic), so JmpHook on
// the getter is allowed. Same pattern as ALDatabasePatches (Inv-1).
using System.Globalization;
using System.Reflection;
using System.Runtime.CompilerServices;
using AlRunnerV2.Infrastructure;

namespace AlRunnerV2.Patches;

public static class NavRecordIdPatches
{
    private static object? _cachedComparer;

    public static void Register(Assembly navNcl)
    {
        var tNri = navNcl.GetType("Microsoft.Dynamics.Nav.Runtime.NavRecordId");
        if (tNri == null)
        {
            Console.Error.WriteLine("[NavRecordIdPatches] NavRecordId type not found; skipping");
            return;
        }

        var getter = tNri.GetProperty("CollationAwareStringComparer",
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)?.GetGetMethod(true);
        if (getter == null)
        {
            Console.Error.WriteLine("[NavRecordIdPatches] get_CollationAwareStringComparer not found; skipping");
            return;
        }

        try
        {
            _cachedComparer = BuildComparer(navNcl);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[NavRecordIdPatches] failed to build CollationAwareStringComparer: {ex.GetType().Name}: {ex.Message}");
            return;
        }

        var repl = typeof(NavRecordIdPatches).GetMethod(nameof(NavRecordId_get_CollationAwareStringComparer),
            BindingFlags.Public | BindingFlags.Static);
        if (repl == null) return;

        try
        {
            JmpHook.Apply(getter, repl, "NavRecordId.get_CollationAwareStringComparer");
            Console.Error.WriteLine("[NavRecordIdPatches] hooked NavRecordId.get_CollationAwareStringComparer");
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[NavRecordIdPatches] hook failed: {ex.Message}");
        }
    }

    private static object BuildComparer(Assembly navNcl)
    {
        var tSsp = navNcl.GetType("Microsoft.Dynamics.Nav.Runtime.SqlSortingProperties")
                   ?? throw new InvalidOperationException("SqlSortingProperties type not found");
        var tCasc = navNcl.GetType("Microsoft.Dynamics.Nav.Runtime.CollationAwareStringComparer")
                    ?? throw new InvalidOperationException("CollationAwareStringComparer type not found");

        // SqlSortingProperties(CultureInfo, CompareOptions, String) — public ctor.
        // Use IgnoreCase + IgnoreNonSpace to match BC default Windows-collation behaviour
        // (the canonical Latin1_General_100_CI_AS comparison style).
        var sspCtor = tSsp.GetConstructor(new[] { typeof(CultureInfo), typeof(CompareOptions), typeof(string) })
                      ?? throw new InvalidOperationException("SqlSortingProperties(CultureInfo,CompareOptions,String) ctor not found");
        var ssp = sspCtor.Invoke(new object[] {
            CultureInfo.InvariantCulture,
            CompareOptions.IgnoreCase | CompareOptions.IgnoreNonSpace,
            "default"
        });

        // CollationAwareStringComparer(SqlSortingProperties) — internal ctor.
        var cascCtor = tCasc.GetConstructor(BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance,
            binder: null, types: new[] { tSsp }, modifiers: null)
            ?? throw new InvalidOperationException("CollationAwareStringComparer(SqlSortingProperties) ctor not found");
        return cascCtor.Invoke(new object?[] { ssp });
    }

    /// <summary>Replacement for NavRecordId.get_CollationAwareStringComparer (instance).
    /// Returns a process-wide cached comparer; sidesteps the Session→Tenant→Database
    /// chain that NREs on the skeleton runtime. Faithful for hash/equals semantics
    /// used by TempTableDataProvider.Modify and friends.</summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static object? NavRecordId_get_CollationAwareStringComparer(object self) => _cachedComparer;
}
