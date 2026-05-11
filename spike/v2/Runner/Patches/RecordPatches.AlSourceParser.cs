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

    private static readonly Regex RxTableExtension = new(
        @"\btableextension\s+(\d+)\s+(?:""([^""]+)""|([A-Za-z_]\w*))\s+extends\s+(?:""([^""]+)""|([A-Za-z_]\w*))[^{]*?\{",
        RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.Singleline);

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
            {
                var text = File.ReadAllText(file);
                TryParseTableFile(text);
                TryParseTableExtensionFile(text);
            }
        }
    }

    private static void TryParseTableFile(string text)
    {
        // Multiple `table N "Name" { ... }` declarations may live in one .al file.
        // Slice the text between consecutive RxTable matches so each table only sees
        // its own fields/keys.
        var tableMatches = RxTable.Matches(text);
        if (tableMatches.Count == 0) return;

        // Collect all tableextension start positions so we can use them as slice boundaries.
        var extPositions = RxTableExtension.Matches(text).Cast<Match>().Select(m => m.Index).ToArray();

        for (int i = 0; i < tableMatches.Count; i++)
        {
            var tableMatch = tableMatches[i];
            int sliceStart = tableMatch.Index;
            int nextTableIdx = (i + 1 < tableMatches.Count) ? tableMatches[i + 1].Index : text.Length;
            // Also stop at any tableextension that follows this table block.
            int nextExtIdx = extPositions.Where(p => p > sliceStart).Append(text.Length).Min();
            int sliceEnd = Math.Min(nextTableIdx, nextExtIdx);
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

    private static void TryParseTableExtensionFile(string text)
    {
        var extMatches = RxTableExtension.Matches(text);
        if (extMatches.Count == 0) return;

        for (int i = 0; i < extMatches.Count; i++)
        {
            var m = extMatches[i];
            if (!int.TryParse(m.Groups[1].Value, out int extId)) continue;
            var extName = m.Groups[2].Success ? m.Groups[2].Value : m.Groups[3].Value;
            var baseName = m.Groups[4].Success ? m.Groups[4].Value : m.Groups[5].Value;

            int sliceStart = m.Index;
            int sliceEnd = (i + 1 < extMatches.Count) ? extMatches[i + 1].Index : text.Length;
            var slice = text.Substring(sliceStart, sliceEnd - sliceStart);

            var fields = new List<ParsedField>();
            foreach (Match fm in RxField.Matches(slice))
            {
                if (!int.TryParse(fm.Groups[1].Value, out int fid)) continue;
                var fname = fm.Groups[2].Success ? fm.Groups[2].Value : fm.Groups[3].Value;
                var ftype = fm.Groups[4].Value.Trim();
                int length = 0;
                var lm = Regex.Match(ftype, @"\[(\d+)\]");
                if (lm.Success) int.TryParse(lm.Groups[1].Value, out length);

                var fieldBody = ExtractFieldBody(slice, fm.Index + fm.Length);
                bool isFlowField = fieldBody != null && RxFieldClass.IsMatch(fieldBody);
                ParsedCalcFormula? calcFormula = null;
                if (isFlowField && fieldBody != null)
                    calcFormula = TryParseCalcFormula(fieldBody);

                fields.Add(new ParsedField(fid, fname, ftype, length, isFlowField, calcFormula));
            }

            Console.Error.WriteLine($"[TableExt] parsed extension {extId} '{extName}' extends '{baseName}' with {fields.Count} fields");

            var key = baseName.ToLowerInvariant();
            if (!_parsedExtensionFields.TryGetValue(key, out var existing))
                _parsedExtensionFields[key] = fields;
            else
                existing.AddRange(fields);
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
