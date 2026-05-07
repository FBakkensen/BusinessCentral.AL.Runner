// RecordPatches.AlSourceParser — parses AL `table` declarations into ParsedTable
// records keyed by table ID. The output is consumed by NclMetaTableBuilder to
// produce real NCLMetaTable instances at runtime.
//
// The parser uses regex over raw .al text rather than a real AL syntax tree —
// good enough for the spike since we only need table layout (IDs, fields, PK).
using System.Text.RegularExpressions;

namespace AlRunnerV2.Patches;

public static partial class RecordPatches
{
    private static readonly Regex RxTable = new(
        @"\btable\s+(\d+)\s+(?:""([^""]+)""|([A-Za-z_]\w*))[^{]*?\{",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex RxField = new(
        @"\bfield\s*\(\s*(\d+)\s*;\s*(?:""([^""]+)""|([A-Za-z_]\w*))\s*;\s*([^)]+?)\s*\)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex RxKey = new(
        @"\bkey\s*\(\s*[^;]+;\s*([^)]+)\)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static void ParseAllSources()
    {
        foreach (var dir in _sourceDirs)
        {
            var files = Directory.GetFiles(dir, "*.al", SearchOption.AllDirectories);
            foreach (var file in files)
                TryParseTableFile(File.ReadAllText(file));
        }
    }

    private static void TryParseTableFile(string text)
    {
        var tableMatch = RxTable.Match(text);
        if (!tableMatch.Success) return;

        if (!int.TryParse(tableMatch.Groups[1].Value, out int tableId)) return;
        var tableName = tableMatch.Groups[2].Success ? tableMatch.Groups[2].Value : tableMatch.Groups[3].Value;

        var fields = new List<ParsedField>();
        foreach (Match fm in RxField.Matches(text))
        {
            if (!int.TryParse(fm.Groups[1].Value, out int fid)) continue;
            var fname = fm.Groups[2].Success ? fm.Groups[2].Value : fm.Groups[3].Value;
            var ftype = fm.Groups[4].Value.Trim();
            int length = 0;
            var lm = Regex.Match(ftype, @"\[(\d+)\]");
            if (lm.Success) int.TryParse(lm.Groups[1].Value, out length);
            fields.Add(new ParsedField(fid, fname, ftype, length));
        }

        // Parse first key as PK
        var pkFieldIds = new List<int>();
        var keyMatch = RxKey.Match(text);
        if (keyMatch.Success)
        {
            var keyFieldNames = keyMatch.Groups[1].Value
                .Split(',')
                .Select(s => s.Trim().Trim('"'))
                .ToList();
            foreach (var kn in keyFieldNames)
            {
                var f = fields.FirstOrDefault(x =>
                    string.Equals(x.FieldName, kn, StringComparison.OrdinalIgnoreCase));
                if (f != null) pkFieldIds.Add(f.FieldId);
            }
        }
        // Fallback: first field is PK
        if (pkFieldIds.Count == 0 && fields.Count > 0)
            pkFieldIds.Add(fields[0].FieldId);

        _parsedTables[tableId] = new ParsedTable(tableId, tableName, fields, pkFieldIds);
    }
}

// ─── Data holders ────────────────────────────────────────────────────────────

internal record ParsedField(int FieldId, string FieldName, string TypeName, int Length);
internal record ParsedTable(int TableId, string TableName,
    List<ParsedField> Fields, List<int> PkFieldIds);
