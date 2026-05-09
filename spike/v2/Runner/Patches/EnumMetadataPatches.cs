// EnumMetadataPatches — populate NCLOptionMetadata replacements for AL enums.
//
// Rationale:
//   The compiled AL emits `NCLEnumMetadata.Create(<enumId>).GetOrdinals()` /
//   `.GetNames()` for AL `Enum::"X".Ordinals()` / `.Names()` calls. The real
//   `NCLEnumMetadata.Create(int)` chains through NavGlobal.MetadataProvider
//   → SystemTenant which is null on the skeleton runtime, so MiscPatches
//   already hooks it to return `NCLOptionMetadata.Default`. However, that base
//   instance has virtual `GetNames()` / `GetOrdinals()` methods that throw
//   `NavNCLNotSupportedOperationException` — only the `NCLEnumMetadata`
//   subclass populates them.
//
//   Per HANDOFF §2.4 (reuse service-tier code before patching) we'd ideally
//   construct a real `NCLEnumMetadata`, but its protected ctor wires up
//   ServerUserSettings-backed LRU caches and per-value `NavOption.CreateBypassCache`
//   calls that aren't necessary just for `GetNames()`/`GetOrdinals()`. Instead
//   we ship a minimal `NCLOptionMetadata` subclass (`AlEnumOptionMetadata`)
//   that overrides exactly those two virtuals (and `OrdinalValues`/`Name`/`Id`
//   for completeness), constructed from the `(name, id, options[], indexes[])`
//   tuple captured by `BcCompiler.CaptureOutputter` at AL emit time.
//
// Decompile:
//   NCLOptionMetadata: Microsoft.Dynamics.Nav.Ncl.decompiled.cs:158163
//     - Base GetNames/GetOrdinals at 158334/158339 throw NotSupported.
//   NCLEnumMetadata override: 158980 / 158985 returns namesList / ordinalsList.
//
using System.Collections.Concurrent;
using System.Reflection;
using System.Runtime.CompilerServices;
using NCLOptionMetadata = Microsoft.Dynamics.Nav.Runtime.NCLOptionMetadata;
using NavList = Microsoft.Dynamics.Nav.Runtime.NavList<Microsoft.Dynamics.Nav.Runtime.NavText>;
using NavListInt = Microsoft.Dynamics.Nav.Runtime.NavList<int>;
using NavText = Microsoft.Dynamics.Nav.Runtime.NavText;

namespace AlRunnerV2;

/// <summary>
/// Captures (id, name, options[], indexes[]) for every AL enum compiled by
/// <see cref="BcCompiler"/>. Populated at emit time by <c>CaptureOutputter</c>;
/// consumed at runtime by <see cref="BcRuntime.NCLEnumMetadata_CreateById"/>.
/// </summary>
public static class AlEnumMetadataRegistry
{
    public sealed record Entry(int Id, string Name, string[] Options, int[] Indexes);

    private static readonly ConcurrentDictionary<int, Entry> _byId = new();

    /// <summary>Last-writer-wins; bundle-wide enum-id collisions are quarantined upstream.</summary>
    public static void Register(int id, string name, string[] options, int[] indexes)
    {
        if (options == null || indexes == null) return;
        if (options.Length != indexes.Length) return;
        _byId[id] = new Entry(id, name ?? string.Empty, options, indexes);
    }

    public static bool TryGet(int id, out Entry entry) => _byId.TryGetValue(id, out entry!);

    public static void Clear() => _byId.Clear();

    public static int Count => _byId.Count;

    /// <summary>Snapshot of all currently registered entries. Used by the
    /// AL-output cache sidecar writer (Program.cs). Order is stable
    /// (sorted by Id) so the sidecar is byte-deterministic across runs.</summary>
    public static IReadOnlyList<Entry> Snapshot()
    {
        return _byId.Values.OrderBy(e => e.Id).ToList();
    }
}

/// <summary>
/// Minimal <see cref="NCLOptionMetadata"/> subclass that satisfies
/// <c>GetNames()</c>/<c>GetOrdinals()</c> for AL enums by carrying the
/// captured names + ordinal indexes alongside the base <c>options</c> array.
/// </summary>
internal sealed class AlEnumOptionMetadata : NCLOptionMetadata
{
    private readonly NavList _names;
    private readonly NavListInt _ordinals;
    private readonly int[] _ordinalValues;
    private readonly string _name;
    private readonly int _id;

    public AlEnumOptionMetadata(string name, int id, string[] options, int[] indexes)
        : base(JoinOptions(options))
    {
        _name = name;
        _id = id;
        _ordinalValues = indexes;
        _names = (NavList)NavListCtorOfNavText.Invoke(
            new object[] { options.Select(o => NavText.Create(o ?? string.Empty)).ToList(), /*asReadOnly*/ true });
        _ordinals = (NavListInt)NavListCtorOfInt.Invoke(
            new object[] { indexes.ToList(), /*asReadOnly*/ true });
    }

    public override Microsoft.Dynamics.Nav.Runtime.NavList<NavText> GetNames() => _names;
    public override Microsoft.Dynamics.Nav.Runtime.NavList<int> GetOrdinals() => _ordinals;

    // OrdinalValues is internal-virtual on the base; the base body of
    // GetOrdinalFromIndex(int) uses it when non-null. Setting it lets
    // sparse AL enums (e.g. value(5; ...)) resolve correctly.
    // We can't `override internal` from another assembly, so we rely on the
    // base default (`null`) — AL Ordinals() goes through GetOrdinals() above
    // which returns _ordinals directly, so OrdinalValues isn't on the hot path.

    // -- reflection cache for NavList<T> internal ctor --
    private static readonly ConstructorInfo NavListCtorOfNavText = ResolveNavListCtor<NavText>();
    private static readonly ConstructorInfo NavListCtorOfInt     = ResolveNavListCtor<int>();

    private static ConstructorInfo ResolveNavListCtor<T>()
    {
        var t = typeof(Microsoft.Dynamics.Nav.Runtime.NavList<T>);
        var ctor = t.GetConstructor(
            BindingFlags.NonPublic | BindingFlags.Instance,
            binder: null,
            new[] { typeof(System.Collections.Generic.List<T>), typeof(bool) },
            modifiers: null);
        if (ctor == null)
            throw new InvalidOperationException(
                $"NavList<{typeof(T).Name}>(List<{typeof(T).Name}>, bool) ctor not found");
        return ctor;
    }

    /// <summary>
    /// Build the comma-joined option string the base ctor expects. AL enum
    /// names are unique within an enum (BC compile-time enforced), so the
    /// duplicate check inside <c>NCLOptionMetadata(string)</c> won't fire.
    /// Empty / null members are normalized to empty string — matches BC's
    /// convention for the special " " (space-named) value.
    /// </summary>
    private static string JoinOptions(string[] options)
    {
        return string.Join(",", options.Select(o => o ?? string.Empty));
    }
}

public static partial class BcRuntime
{
    private static readonly ConcurrentDictionary<int, NCLOptionMetadata> _alEnumCache = new();

    /// <summary>
    /// Replacement for NCLEnumMetadata.Create(int).
    /// Look up the AL enum metadata captured at emit time; fall back to
    /// <c>NCLOptionMetadata.Default</c> for system / dependency enums whose
    /// metadata isn't in the registry (existing behavior — preserves ordinal
    /// arithmetic via NavOption.Value passthrough).
    /// </summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static NCLOptionMetadata NCLEnumMetadata_CreateByIdAlAware(int id)
    {
        if (_alEnumCache.TryGetValue(id, out var cached))
            return cached;
        if (AlEnumMetadataRegistry.TryGet(id, out var e))
        {
            try
            {
                var meta = new AlEnumOptionMetadata(e.Name, e.Id, e.Options, e.Indexes);
                return _alEnumCache.GetOrAdd(id, meta);
            }
            catch
            {
                // Fall through to Default on any construction issue (e.g.
                // duplicate option string the base ctor refuses) — preserves
                // pre-patch behavior for that one enum.
            }
        }
        return NCLOptionMetadata.Default;
    }
}
