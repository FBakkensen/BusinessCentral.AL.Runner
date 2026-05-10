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

    private static readonly Regex RxFieldClass = new(
        @"\bFieldClass\s*=\s*FlowField\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex RxCalcFormula = new(
        @"\bCalcFormula\s*=\s*([^;]+)\s*;",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // Captures: (type) table ["."field] [where(filters)]
    private static readonly Regex RxCalcFormulaParts = new(
        @"^\s*(count|sum|lookup|exist|average|min|max)\s*\(\s*""([^""]+)""(?:\.""([^""]+)"")?\s*(?:where\s*\((.+)\))?\s*\)\s*$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.Singleline);

    // Captures field-reference filter: "SourceField" = field("ParentField")
    private static readonly Regex RxCalcFilter = new(
        @"""([^""]+)""\s*=\s*field\s*\(\s*""([^""]+)""\s*\)",
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
        // Multiple `table N "Name" { ... }` declarations may live in one .al file.
        // Slice the text between consecutive RxTable matches so each table only sees
        // its own fields/keys.
        var tableMatches = RxTable.Matches(text);
        if (tableMatches.Count == 0) return;

        for (int i = 0; i < tableMatches.Count; i++)
        {
            var tableMatch = tableMatches[i];
            int sliceStart = tableMatch.Index;
            int sliceEnd = (i + 1 < tableMatches.Count) ? tableMatches[i + 1].Index : text.Length;
            var slice = text.Substring(sliceStart, sliceEnd - sliceStart);

            if (!int.TryParse(tableMatch.Groups[1].Value, out int tableId)) continue;
            var tableName = tableMatch.Groups[2].Success ? tableMatch.Groups[2].Value : tableMatch.Groups[3].Value;

            var fields = new List<ParsedField>();
            foreach (Match fm in RxField.Matches(slice))
            {
                if (!int.TryParse(fm.Groups[1].Value, out int fid)) continue;
                var fname = fm.Groups[2].Success ? fm.Groups[2].Value : fm.Groups[3].Value;
                var ftype = fm.Groups[4].Value.Trim();
                int length = 0;
                var lm = Regex.Match(ftype, @"\[(\d+)\]");
                if (lm.Success) int.TryParse(lm.Groups[1].Value, out length);

                // Extract the field body block (e.g. { FieldClass = FlowField; CalcFormula = ...; })
                var fieldBody = ExtractFieldBody(slice, fm.Index + fm.Length);
                bool isFlowField = fieldBody != null && RxFieldClass.IsMatch(fieldBody);
                ParsedCalcFormula? calcFormula = null;
                if (isFlowField && fieldBody != null)
                    calcFormula = TryParseCalcFormula(fieldBody);

                fields.Add(new ParsedField(fid, fname, ftype, length, isFlowField, calcFormula));
            }

            // Parse first key as PK
            var pkFieldIds = new List<int>();
            var keyMatch = RxKey.Match(slice);
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

    /// <summary>Extracts the brace-balanced body of a field block starting near <paramref name="pos"/> in <paramref name="slice"/>.</summary>
    private static string? ExtractFieldBody(string slice, int pos)
    {
        while (pos < slice.Length && char.IsWhiteSpace(slice[pos])) pos++;
        if (pos >= slice.Length || slice[pos] != '{') return null;
        int depth = 0, start = pos;
        while (pos < slice.Length)
        {
            if (slice[pos] == '{') depth++;
            else if (slice[pos] == '}') { depth--; if (depth == 0) return slice.Substring(start + 1, pos - start - 1); }
            pos++;
        }
        return null;
    }

    private static ParsedCalcFormula? TryParseCalcFormula(string fieldBody)
    {
        var m = RxCalcFormula.Match(fieldBody);
        if (!m.Success) return null;
        var formulaText = m.Groups[1].Value.Trim();
        var pm = RxCalcFormulaParts.Match(formulaText);
        if (!pm.Success) return null;

        var formulaType = pm.Groups[1].Value;
        var sourceTableName = pm.Groups[2].Value;
        var sourceFieldName = pm.Groups[3].Success && pm.Groups[3].Length > 0 ? pm.Groups[3].Value : null;
        var whereText = pm.Groups[4].Success ? pm.Groups[4].Value : "";

        var filters = new List<ParsedCalcFilter>();
        foreach (Match fm in RxCalcFilter.Matches(whereText))
            filters.Add(new ParsedCalcFilter(fm.Groups[1].Value, fm.Groups[2].Value));

        return new ParsedCalcFormula(formulaType, sourceTableName, sourceFieldName, filters);
    }
}

// ─── Data holders ────────────────────────────────────────────────────────────

internal record ParsedCalcFilter(string SourceFieldName, string ParentFieldName);
internal record ParsedCalcFormula(string FormulaType, string SourceTableName, string? SourceFieldName, List<ParsedCalcFilter> Filters);
internal record ParsedField(int FieldId, string FieldName, string TypeName, int Length, bool IsFlowField = false, ParsedCalcFormula? CalcFormula = null);
internal record ParsedTable(int TableId, string TableName,
    List<ParsedField> Fields, List<int> PkFieldIds);
