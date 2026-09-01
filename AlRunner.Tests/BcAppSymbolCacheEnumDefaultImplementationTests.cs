// BcAppSymbolCacheEnumDefaultImplementationTests — issue #2302.
//
// The AL compiler resolves an enum's enum-level `DefaultImplementation = "IFoo" = "Codeunit"`
// to a symbol property carrying one codeunit id per implemented interface, the same
// comma-separated shape a value's `Implementation` carries. TryParseEnumSymbol read only the
// value-level property, so every value without its own Implementation resolved to no
// codeunit and `ToInterface` threw. Same two-claim shape as
// BcAppSymbolCacheQueryDataItemQualifierTests: the parse captures it, and the CacheVersion
// bump (v15 -> v16) keeps a stale entry written by the old parse from being served.
using System.IO.Compression;
using System.Text;
using AlRunner.Patches;
using Xunit;

namespace AlRunner.Tests;

[Collection(CacheRootsSerialCollection.Name)]
public sealed class BcAppSymbolCacheEnumDefaultImplementationTests
{
    private static string NewTempDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), "bc-symbol-cache-enum-default-impl-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static string WriteApp(string dir, string fileName, string enumName)
    {
        var appPath = Path.Combine(dir, fileName);
        using (var zip = new FileStream(appPath, FileMode.Create))
        using (var za = new ZipArchive(zip, ZipArchiveMode.Create))
        {
            var entry = za.CreateEntry("SymbolReference.json");
            using var w = new StreamWriter(entry.Open(), Encoding.UTF8);
            // The exact shape the AL compiler emitted for tests/runner-extras/enum-default-implementation.
            w.Write($$"""
                {
                  "RuntimeVersion": "15.0",
                  "EnumTypes": [
                    {
                      "Values": [
                        { "Name": "Default" },
                        { "Ordinal": 1, "Name": "Quiet" },
                        { "Ordinal": 2, "Properties": [ { "Name": "Implementation", "Value": "64631" } ], "Name": "Loud" }
                      ],
                      "ImplementedInterfaces": [ "\"Edi Greeter\"" ],
                      "Properties": [
                        { "Name": "Extensible", "Value": "1" },
                        { "Name": "DefaultImplementation", "Value": "64630" }
                      ],
                      "Id": 64632,
                      "Name": "{{enumName}}"
                    }
                  ]
                }
                """);
        }
        return appPath;
    }

    [Fact]
    public void Get_CapturesEnumLevelDefaultImplementation_AndKeepsValueLevelImplementation()
    {
        var dir = NewTempDir();
        try
        {
            var enumName = "Edi Greeting " + Guid.NewGuid().ToString("N");
            var appPath = WriteApp(dir, "enum-default-" + Guid.NewGuid().ToString("N") + ".app", enumName);
            BcAppSymbolCache.ResetProcessCacheForTests();

            var symbols = BcAppSymbolCache.Get(appPath);

            var e = Assert.Single(symbols.Enums, x => x.Name == enumName);
            Assert.Equal(new[] { "Default", "Quiet", "Loud" }, e.Options);
            Assert.Equal(new[] { 64630 }, e.DefaultImplementations);
            Assert.Empty(e.Implementations[0]);
            Assert.Empty(e.Implementations[1]);
            Assert.Equal(new[] { 64631 }, e.Implementations[2]);
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public void Get_StaleV15EntryWithoutDefaultImplementations_IsIgnored_AndTheAppIsReparsed()
    {
        var dir = NewTempDir();
        try
        {
            var enumName = "Edi Greeting Stale " + Guid.NewGuid().ToString("N");
            var appPath = WriteApp(dir, "enum-default-stale-" + Guid.NewGuid().ToString("N") + ".app", enumName);

            BcAppSymbolCache.ResetProcessCacheForTests();
            var contentHash = BcAppSymbolCache.ComputeAppContentHash(appPath);

            // The payload a CacheVersion=15 build wrote for this SAME .app: no
            // DefaultImplementations property at all.
            var staleCachePath = BcAppSymbolCache.CachePathForVersionForTests(appPath, contentHash, cacheVersion: 15);
            Directory.CreateDirectory(Path.GetDirectoryName(staleCachePath)!);
            File.WriteAllText(staleCachePath, $$"""
                {
                  "ContentHash": "{{contentHash}}",
                  "Tables": [],
                  "Enums": [
                    {
                      "Id": 64632, "Name": "{{enumName}}",
                      "Options": [ "Default", "Quiet", "Loud" ], "Indexes": [ 0, 1, 2 ],
                      "Implementations": [ [], [], [ 64631 ] ], "Captions": [ null, null, null ]
                    }
                  ],
                  "Queries": [], "Objects": null, "Reports": null, "Pages": null
                }
                """);

            Assert.Equal(0, BcAppSymbolCache.ParseInvocationCountForTests(appPath));

            var symbols = BcAppSymbolCache.Get(appPath);

            var e = Assert.Single(symbols.Enums, x => x.Name == enumName);
            Assert.Equal(new[] { 64630 }, e.DefaultImplementations);
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
