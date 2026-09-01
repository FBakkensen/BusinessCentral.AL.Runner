// BcAppSymbolCacheQueryDataItemQualifierTests — issue #2295.
//
// A SymbolReference.json object reference is `#<appIdNoHyphens>#<Name>` whenever it crosses
// a module boundary. Report data items already had that qualifier stripped (CacheVersion
// v6); Query data items did not, so a source-defined Query over a dependency table (Base
// Application `Item`) kept `RelatedTable = "#437dbf0e84ff417a965ded2bb9650972#Item"`,
// RecordPatches.ResolveTableIdByName could not match it, BuildMetaQueryDesign gave up, and
// the Query was constructed with NCLMetaQuery=NULL — SetRange()/Open() then NRE'd.
//
// Two claims, same shape as BcAppSymbolCacheQueryMethodVersionTests:
//   1. the parse strips the qualifier on root AND nested data items, and leaves a plain
//      (same-module) name untouched;
//   2. the CacheVersion bump (v14 -> v15) means a stale entry written by the old parse —
//      still carrying the qualified name — is never served as a HIT.
using System.IO.Compression;
using System.Text;
using AlRunner.Patches;
using Xunit;

namespace AlRunner.Tests;

[Collection(CacheRootsSerialCollection.Name)]
public sealed class BcAppSymbolCacheQueryDataItemQualifierTests
{
    private const string BaseAppQualifier = "#437dbf0e84ff417a965ded2bb9650972#";

    private static string NewTempDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), "bc-symbol-cache-query-qualifier-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static string WriteApp(string dir, string fileName, string queryName)
    {
        var appPath = Path.Combine(dir, fileName);
        using (var zip = new FileStream(appPath, FileMode.Create))
        using (var za = new ZipArchive(zip, ZipArchiveMode.Create))
        {
            var entry = za.CreateEntry("SymbolReference.json");
            using var w = new StreamWriter(entry.Open(), Encoding.UTF8);
            w.Write($$"""
                {
                  "RuntimeVersion": "17.0",
                  "Queries": [
                    {
                      "Id": 64585,
                      "Name": "{{queryName}}",
                      "Properties": [ { "Name": "QueryType", "Value": "Normal" } ],
                      "Elements": [
                        {
                          "Id": 1,
                          "Name": "Item",
                          "RelatedTable": "{{BaseAppQualifier}}Item",
                          "Properties": [],
                          "Columns": [ { "Id": 2, "Name": "No", "SourceColumn": "No.", "Properties": [] } ],
                          "Filters": [],
                          "DataItems": [
                            {
                              "Id": 3,
                              "Name": "ItemLedgerEntry",
                              "RelatedTable": "{{BaseAppQualifier}}Item Ledger Entry",
                              "Properties": [ { "Name": "DataItemLink", "Value": "Item No.=Item.No" } ],
                              "Columns": [ { "Id": 4, "Name": "Quantity", "SourceColumn": "Quantity", "Properties": [] } ],
                              "Filters": [],
                              "DataItems": [
                                {
                                  "Id": 5,
                                  "Name": "LocalRows",
                                  "RelatedTable": "Qdt Local",
                                  "Properties": [],
                                  "Columns": [],
                                  "Filters": []
                                }
                              ]
                            }
                          ]
                        }
                      ]
                    }
                  ]
                }
                """);
        }
        return appPath;
    }

    [Fact]
    public void Get_StripsModuleQualifier_OnRootAndNestedQueryDataItems_AndLeavesPlainNamesAlone()
    {
        var dir = NewTempDir();
        try
        {
            var queryName = "IQ Item Rows " + Guid.NewGuid().ToString("N");
            var appPath = WriteApp(dir, "qualifier-" + Guid.NewGuid().ToString("N") + ".app", queryName);
            BcAppSymbolCache.ResetProcessCacheForTests();

            var symbols = BcAppSymbolCache.Get(appPath);

            var query = Assert.Single(symbols.Queries, q => q.Name == queryName);
            var root = Assert.Single(query.DataItems);
            Assert.Equal("Item", root.RelatedTable);

            var nested = Assert.Single(root.DataItems);
            Assert.Equal("Item Ledger Entry", nested.RelatedTable);

            var plain = Assert.Single(nested.DataItems);
            Assert.Equal("Qdt Local", plain.RelatedTable);
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public void Get_StaleV14EntryWithQualifiedRelatedTable_IsIgnored_AndTheAppIsReparsed()
    {
        var dir = NewTempDir();
        try
        {
            var queryName = "IQ Item Rows Stale " + Guid.NewGuid().ToString("N");
            var appPath = WriteApp(dir, "qualifier-stale-" + Guid.NewGuid().ToString("N") + ".app", queryName);

            BcAppSymbolCache.ResetProcessCacheForTests();
            var contentHash = BcAppSymbolCache.ComputeAppContentHash(appPath);

            // The exact path a CacheVersion=14 build wrote for this SAME .app content, with
            // the payload the OLD parse produced: the qualifier still on RelatedTable.
            var staleCachePath = BcAppSymbolCache.CachePathForVersionForTests(appPath, contentHash, cacheVersion: 14);
            Directory.CreateDirectory(Path.GetDirectoryName(staleCachePath)!);
            File.WriteAllText(staleCachePath, $$"""
                {
                  "ContentHash": "{{contentHash}}",
                  "Tables": [], "Enums": [],
                  "Queries": [
                    {
                      "Id": 64585, "Name": "{{queryName}}", "QueryType": "Normal", "Caption": null, "OrderBy": null,
                      "TopNumberOfRowsToReturn": 0,
                      "DataItems": [
                        {
                          "Id": 1, "Name": "Item", "RelatedTable": "{{BaseAppQualifier}}Item", "SqlJoinType": null, "DataItemLink": null,
                          "Columns": [ { "Id": 2, "Name": "No", "SourceColumn": "No.", "Caption": null, "Method": null } ],
                          "Filters": [], "DataItems": []
                        }
                      ]
                    }
                  ],
                  "Objects": null, "Reports": null, "Pages": null
                }
                """);

            Assert.Equal(0, BcAppSymbolCache.ParseInvocationCountForTests(appPath));

            var symbols = BcAppSymbolCache.Get(appPath);

            var query = Assert.Single(symbols.Queries, q => q.Name == queryName);
            Assert.Equal("Item", Assert.Single(query.DataItems).RelatedTable);
            Assert.Equal(1, BcAppSymbolCache.ParseInvocationCountForTests(appPath));

            var currentCachePath = BcAppSymbolCache.CachePathForVersionForTests(
                appPath, contentHash, BcAppSymbolCache.CacheVersionForTests);
            Assert.NotEqual(staleCachePath, currentCachePath);
            Assert.True(File.Exists(currentCachePath),
                $"Expected a fresh cache entry at the current-version path {currentCachePath}");
        }
        finally { Directory.Delete(dir, recursive: true); }
    }
}
