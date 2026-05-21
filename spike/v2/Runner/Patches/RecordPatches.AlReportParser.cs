// RecordPatches.AlReportParser — parses AL `report` / `reportextension`
// declarations into ParsedReport records keyed by report ID. Mirror of the
// page parser; same minimal shape (id + name).
using System.Text.RegularExpressions;

namespace AlRunnerV2.Patches;

public static partial class RecordPatches
{
    private static readonly Regex RxReport = new(
        @"\breport\s+(\d+)\s+(?:""([^""]+)""|([A-Za-z_]\w*))[^{]*?\{",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex RxReportExtension = new(
        @"\breportextension\s+(\d+)\s+(?:""([^""]+)""|([A-Za-z_]\w*))\s+extends\s+(?:""([^""]+)""|([A-Za-z_]\w*))[^{]*?\{",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static void ParseAllReportSources()
    {
        foreach (var dir in _sourceDirs)
        {
            var files = Directory.GetFiles(dir, "*.al", SearchOption.AllDirectories);
            foreach (var file in files)
                TryParseReportFile(File.ReadAllText(file));
        }
    }

    // Property-level scanner: finds `ProcessingOnly = true|false` inside a
    // report body. Scoped within the object body via balanced-brace extraction
    // so we don't pick up a `ProcessingOnly` declaration from a neighbouring
    // object in the same .al file.
    private static readonly Regex RxProcessingOnly = new(
        @"\bProcessingOnly\s*=\s*(true|false)\s*;",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static void TryParseReportFile(string text)
    {
        foreach (Match m in RxReport.Matches(text))
        {
            if (!int.TryParse(m.Groups[1].Value, out int id)) continue;
            var name = m.Groups[2].Success ? m.Groups[2].Value : m.Groups[3].Value;
            var body = ExtractObjectBody(text, m.Index + m.Length - 1);
            bool processingOnly = TryReadProcessingOnly(body);
            _parsedReports[id] = new ParsedReport(id, name, IsExtension: false, ProcessingOnly: processingOnly);
        }

        foreach (Match m in RxReportExtension.Matches(text))
        {
            if (!int.TryParse(m.Groups[1].Value, out int id)) continue;
            var name = m.Groups[2].Success ? m.Groups[2].Value : m.Groups[3].Value;
            // reportextension cannot redeclare ProcessingOnly; default false.
            _parsedReports[id] = new ParsedReport(id, name, IsExtension: true, ProcessingOnly: false);
        }
    }

    // Returns the substring spanning the balanced-brace object body starting at
    // the position of the opening '{'. Falls back to the full remainder if no
    // closing brace is found (defensive).
    private static string ExtractObjectBody(string text, int openBraceIndex)
    {
        if (openBraceIndex < 0 || openBraceIndex >= text.Length || text[openBraceIndex] != '{')
            return string.Empty;
        int depth = 0;
        for (int i = openBraceIndex; i < text.Length; i++)
        {
            char c = text[i];
            if (c == '{') depth++;
            else if (c == '}')
            {
                depth--;
                if (depth == 0) return text.Substring(openBraceIndex, i - openBraceIndex + 1);
            }
        }
        return text.Substring(openBraceIndex);
    }

    // AL default for ProcessingOnly on `report` is false. Returns true only if
    // the body declares `ProcessingOnly = true;` (case-insensitive).
    private static bool TryReadProcessingOnly(string body)
    {
        var m = RxProcessingOnly.Match(body);
        if (!m.Success) return false;
        return string.Equals(m.Groups[1].Value, "true", System.StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// AL-source-derived ProcessingOnly for the given report ID. Returns
    /// <c>false</c> when the report is unknown — matches the AL default and
    /// causes the runner to throw an out-of-scope error at Run time (no
    /// rendering pipeline available without a service tier).
    /// </summary>
    public static bool IsReportProcessingOnly(int reportId) =>
        _parsedReports.TryGetValue(reportId, out var p) && p.ProcessingOnly;
}

internal record ParsedReport(int Id, string Name, bool IsExtension, bool ProcessingOnly = false);
