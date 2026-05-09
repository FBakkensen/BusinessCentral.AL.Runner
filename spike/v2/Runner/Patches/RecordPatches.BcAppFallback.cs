// RecordPatches.BcAppFallback — populate _parsedTables on demand from BC .app
// dependency packages when AL test source doesn't define the requested table.
//
// Why: tests under tests/spike-a-baseapp (and any integration test that touches
// a Base App / System App table such as Currency = table 4) fail with
//   "no NCLMetaTable for table N (AL source not parsed)"
// because BuildNCLMetaTable only consults _parsedTables, populated from the
// test suite's own src/ directory. The compiled Record{N} : NavRecord type IS
// loaded (Tier 2 R2R), but it doesn't carry table-shape attributes — field
// metadata in BC compiled apps lives as AL source inside the .app NAVX zip.
//
// Per .claude/rules/precompiled-dll-respect.md the fix is upstream from the
// AL business logic: when a table id is missing from _parsedTables, walk the
// list of dependency .app files (registered by Program.cs after dep load),
// extract the matching `*.Table.al` source via AppLoader.ExtractAl, run it
// through the existing TryParseTableFile, and the rest of BuildNCLMetaTable
// proceeds unchanged.
//
// Performance: index built lazily on first miss by scanning each .app's
// AL sources for `table <id>` declarations. The result (tableId → appPath)
// is cached so subsequent misses are O(1). Negative misses are also cached
// so a non-existent table doesn't re-scan every .app on every Init().

using System.Text.RegularExpressions;

namespace AlRunnerV2.Patches;

public static partial class RecordPatches
{
    // .app file paths registered by Program.cs after DependencyLoader.LoadAll.
    private static readonly List<string> _bcAppPaths = new();

    // Lazy index: tableId → (appPath, alSource). Built on first miss.
    private static Dictionary<int, (string AppPath, string Source)>? _bcTableIndex;
    private static readonly object _bcTableIndexLock = new();

    // Negative cache: tableIds we've already tried and not found.
    private static readonly HashSet<int> _bcMissCache = new();

    /// <summary>
    /// Register a BC dependency .app path so its AL table sources can be used
    /// as a fallback when a test's own src/ doesn't define a referenced table.
    /// Called from Program.cs after DependencyLoader.LoadAll.
    /// </summary>
    public static void AddBcAppPath(string appPath)
    {
        if (string.IsNullOrEmpty(appPath) || !File.Exists(appPath)) return;
        lock (_bcTableIndexLock)
        {
            if (!_bcAppPaths.Contains(appPath, StringComparer.OrdinalIgnoreCase))
            {
                _bcAppPaths.Add(appPath);
                // Invalidate the index so newly-added .app gets picked up on next miss.
                _bcTableIndex = null;
            }
        }
    }

    /// <summary>
    /// On _parsedTables miss for tableId, scan registered BC .app dependencies,
    /// find the matching `table <id>` declaration, and feed it through
    /// TryParseTableFile so _parsedTables gets populated. Returns true iff a
    /// matching table source was found and parsed.
    /// </summary>
    private static bool TryPopulateParsedTableFromBcApps(int tableId)
    {
        lock (_bcTableIndexLock)
        {
            if (_bcMissCache.Contains(tableId)) return false;
            EnsureBcTableIndex();
            if (_bcTableIndex == null || !_bcTableIndex.TryGetValue(tableId, out var entry))
            {
                _bcMissCache.Add(tableId);
                return false;
            }
            // Parse the source slice that contains this table id.
            TryParseTableFile(entry.Source);
            if (_parsedTables.ContainsKey(tableId))
            {
                Console.Error.WriteLine($"[RecordPatches] BcAppFallback: parsed table {tableId} from {Path.GetFileName(entry.AppPath)}");
                return true;
            }
            // Source had a `table N` regex match but TryParseTableFile didn't materialise
            // it — likely a non-table object reusing the keyword. Treat as miss.
            _bcMissCache.Add(tableId);
            return false;
        }
    }

    private static readonly Regex _rxAnyTableId = new(
        @"\btable\s+(\d+)\s+(?:""[^""]+""|[A-Za-z_]\w*)[^{]*?\{",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static void EnsureBcTableIndex()
    {
        if (_bcTableIndex != null) return;
        var idx = new Dictionary<int, (string, string)>();
        foreach (var appPath in _bcAppPaths)
        {
            IReadOnlyList<(string Name, string Source)> sources;
            try { sources = AlRunnerV2.AppLoader.ExtractAl(appPath); }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[RecordPatches] BcAppFallback: ExtractAl failed for {Path.GetFileName(appPath)}: {ex.Message}");
                continue;
            }
            foreach (var (name, source) in sources)
            {
                // Cheap pre-filter — skip files that don't contain the keyword `table`.
                if (source.IndexOf("table", StringComparison.OrdinalIgnoreCase) < 0) continue;
                foreach (Match m in _rxAnyTableId.Matches(source))
                {
                    if (!int.TryParse(m.Groups[1].Value, out int id)) continue;
                    // First definition wins — Base App should always trump System App
                    // ordering by virtue of being scanned in dep-resolution order.
                    if (!idx.ContainsKey(id))
                        idx[id] = (appPath, source);
                }
            }
        }
        _bcTableIndex = idx;
        Console.Error.WriteLine($"[RecordPatches] BcAppFallback: indexed {idx.Count} table id(s) across {_bcAppPaths.Count} BC .app file(s)");
    }
}
