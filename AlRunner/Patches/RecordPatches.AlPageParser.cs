// RecordPatches.AlPageParser — parses AL `page` / `pageextension` declarations
// into ParsedPage records keyed by page ID. Mirror of AlSourceParser for tables.
//
// We only need the (id, name, base-id-for-extensions) tuple — the cache slot
// just has to be non-null so NCLMetadata.GetMetaApplicationObjectInternal
// finds an entry. Field/action/group layout is irrelevant: every page-level
// property getter on NCLMetaForm reads `metadataAppGroupPageDefinition.Item`
// which is a default struct on a hand-built skeleton; those getters aren't
// reached by the metadata lookup path itself.
using System.Text.RegularExpressions;

namespace AlRunnerV2.Patches;

public static partial class RecordPatches
{
    private static readonly Regex RxPage = new(
        @"\bpage\s+(\d+)\s+(?:""([^""]+)""|([A-Za-z_]\w*))[^{]*?\{",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex RxPageExtension = new(
        @"\bpageextension\s+(\d+)\s+(?:""([^""]+)""|([A-Za-z_]\w*))\s+extends\s+(?:""([^""]+)""|([A-Za-z_]\w*))[^{]*?\{",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static void ParseAllPageSources()
    {
        foreach (var dir in _sourceDirs)
        {
            var files = Directory.GetFiles(dir, "*.al", SearchOption.AllDirectories);
            foreach (var file in files)
                TryParsePageFile(File.ReadAllText(file));
        }
    }

    private static void TryParsePageFile(string text)
    {
        // `page N "Name"` — plain pages
        foreach (Match m in RxPage.Matches(text))
        {
            if (!int.TryParse(m.Groups[1].Value, out int id)) continue;
            var name = m.Groups[2].Success ? m.Groups[2].Value : m.Groups[3].Value;
            _parsedPages[id] = new ParsedPage(id, name, IsExtension: false);
        }

        // `pageextension N "Name" extends "Base"` — pageextensions
        foreach (Match m in RxPageExtension.Matches(text))
        {
            if (!int.TryParse(m.Groups[1].Value, out int id)) continue;
            var name = m.Groups[2].Success ? m.Groups[2].Value : m.Groups[3].Value;
            _parsedPages[id] = new ParsedPage(id, name, IsExtension: true);
        }
    }
}

internal record ParsedPage(int Id, string Name, bool IsExtension);
