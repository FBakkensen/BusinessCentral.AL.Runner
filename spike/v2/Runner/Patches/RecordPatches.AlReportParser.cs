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

    private static void TryParseReportFile(string text)
    {
        foreach (Match m in RxReport.Matches(text))
        {
            if (!int.TryParse(m.Groups[1].Value, out int id)) continue;
            var name = m.Groups[2].Success ? m.Groups[2].Value : m.Groups[3].Value;
            _parsedReports[id] = new ParsedReport(id, name, IsExtension: false);
        }

        foreach (Match m in RxReportExtension.Matches(text))
        {
            if (!int.TryParse(m.Groups[1].Value, out int id)) continue;
            var name = m.Groups[2].Success ? m.Groups[2].Value : m.Groups[3].Value;
            _parsedReports[id] = new ParsedReport(id, name, IsExtension: true);
        }
    }
}

internal record ParsedReport(int Id, string Name, bool IsExtension);
