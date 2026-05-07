// Program — orchestrates the v2 pipeline:
//   1. Apply BC runtime patches once (BcRuntime).
//   2. For each bucket: AL→C# (AlEmitter) → compile (BcAssembler) → load → run (TestExecutor).
//   3. Aggregate (Reporter), write JSON classification file.
//
// Usage:
//   dotnet run --project Runner.csproj -- [--out report.json] <test-bucket-or-glob>...
//
// A "bucket" is any directory containing al-runner.json with sourcePath/testPath
// pointing at AL source dirs.
using System.Reflection;
using AlRunnerV2;

if (args.Length == 0)
{
    Console.Error.WriteLine("Usage: Runner <bucket-dir>... [--out PATH]");
    return 2;
}

string outPath = "v2-classification.json";
var buckets = new List<string>();
for (int i = 0; i < args.Length; i++)
{
    if (args[i] == "--out" && i + 1 < args.Length) { outPath = args[++i]; continue; }
    buckets.Add(args[i]);
}
var bucketDirs = buckets.SelectMany(ExpandBucket).Distinct().ToList();
Console.WriteLine($"AlRunner v2 — running {bucketDirs.Count} bucket(s)");

// One-time runtime setup. Must happen BEFORE any BC type is touched.
var t0 = System.Diagnostics.Stopwatch.StartNew();
BcRuntime.EnsureApplied();
Console.WriteLine($"BC runtime patches applied ({t0.ElapsedMilliseconds}ms)");

var emitter = new AlEmitter();
var assembler = new BcAssembler();
var executor = new TestExecutor();
var results = new List<BucketResult>();

int i2 = 0;
foreach (var bucket in bucketDirs)
{
    i2++;
    Console.Write($"[{i2}/{bucketDirs.Count}] {Path.GetRelativePath(Environment.CurrentDirectory, bucket)} ... ");
    var (_, srcDir, testDir) = ReadBucketConfig(bucket);
    if (srcDir == null && testDir == null) { Console.WriteLine("SKIP (no src/test dirs)"); continue; }

    // Register AL table sources so RecordPatches can build NCLMetaTable instances.
    if (srcDir != null) AlRunnerV2.Patches.RecordPatches.AddSourceDir(srcDir);

    var emitTimer = System.Diagnostics.Stopwatch.StartNew();
    IReadOnlyList<EmittedSource> sources;
    try { sources = emitter.Emit(srcDir!, testDir!); }
    catch (Exception ex)
    {
        Console.WriteLine($"EMIT-FAIL: {ex.Message.Split('\n')[0]}");
        results.Add(new BucketResult(bucket, BucketStage.ExecuteFailed,
            Array.Empty<string>(), ex.Message, Array.Empty<TestResult>(),
            emitTimer.Elapsed, TimeSpan.Zero, TimeSpan.Zero));
        continue;
    }
    emitTimer.Stop();

    var compTimer = System.Diagnostics.Stopwatch.StartNew();
    var compile = assembler.Compile($"V2_{Path.GetFileName(bucket)}", sources);
    compTimer.Stop();
    if (!compile.Success)
    {
        Console.WriteLine($"COMPILE-FAIL ({compile.Errors.Count} errors): {compile.Errors.FirstOrDefault()?.Split('\n')[0]}");
        results.Add(new BucketResult(bucket, BucketStage.CompileFailed,
            compile.Errors, null, Array.Empty<TestResult>(),
            emitTimer.Elapsed, compTimer.Elapsed, TimeSpan.Zero));
        continue;
    }
    var runTimer = System.Diagnostics.Stopwatch.StartNew();
    IReadOnlyList<TestResult> tests;
    try
    {
        var asm = Assembly.Load(compile.AssemblyBytes!);
        BcRuntime.SetTestAssembly(asm);
        tests = executor.Run(asm);
    }
    catch (Exception ex)
    {
        Console.WriteLine($"EXEC-FAIL: {ex.Message.Split('\n')[0]}");
        results.Add(new BucketResult(bucket, BucketStage.ExecuteFailed,
            Array.Empty<string>(), ex.ToString(), Array.Empty<TestResult>(),
            emitTimer.Elapsed, compTimer.Elapsed, runTimer.Elapsed));
        continue;
    }
    runTimer.Stop();
    var p = tests.Count(t => t.Outcome == TestOutcome.Pass);
    var f = tests.Count(t => t.Outcome == TestOutcome.Fail);
    var e = tests.Count(t => t.Outcome == TestOutcome.Error);
    Console.WriteLine($"{p}P/{f}F/{e}E ({(emitTimer.Elapsed + compTimer.Elapsed + runTimer.Elapsed).TotalSeconds:F1}s)");
    results.Add(new BucketResult(bucket, BucketStage.Ran,
        Array.Empty<string>(), null, tests,
        emitTimer.Elapsed, compTimer.Elapsed, runTimer.Elapsed));
}

Reporter.PrintSummary(results, Console.Out);
Reporter.WriteClassification(results, outPath);
Console.WriteLine($"Classification → {outPath}");
return 0;

static (object? config, string? src, string? test) ReadBucketConfig(string dir)
{
    // al-runner.json is optional. When present, sourcePath/testPath override the defaults.
    // When absent, fall back to ./src and ./test (matches the existing CI-loop convention
    // in .github/workflows/test-matrix.yml which iterates by src/test directory presence).
    var cfg = Path.Combine(dir, "al-runner.json");
    string src = "src";
    string test = "test";
    object? json = null;
    if (File.Exists(cfg))
    {
        var doc = System.Text.Json.JsonDocument.Parse(File.ReadAllText(cfg));
        var root = doc.RootElement;
        if (root.TryGetProperty("sourcePath", out var s) && s.GetString() is { } sv) src = sv;
        if (root.TryGetProperty("testPath",   out var t) && t.GetString() is { } tv) test = tv;
        json = doc;
    }
    var srcAbs = Path.GetFullPath(Path.Combine(dir, src));
    var testAbs = Path.GetFullPath(Path.Combine(dir, test));
    if (!Directory.Exists(srcAbs) && !Directory.Exists(testAbs)) return (null, null, null);
    return (json, srcAbs, testAbs);
}

// Discover suites under `arg` by either:
//   • the dir itself looks like a suite (has src/ or test/), or
//   • any descendant dir contains src/+test/ (suite leaf).
// al-runner.json is no longer required — its presence only customises sourcePath/testPath.
static IEnumerable<string> ExpandBucket(string arg)
{
    if (!Directory.Exists(arg)) yield break;
    string full = Path.GetFullPath(arg);
    if (LooksLikeSuite(full)) { yield return full; yield break; }
    foreach (var d in Directory.EnumerateDirectories(full, "*", SearchOption.AllDirectories))
        if (LooksLikeSuite(d))
            yield return Path.GetFullPath(d);
}

static bool LooksLikeSuite(string dir)
{
    if (Directory.Exists(Path.Combine(dir, "test"))) return true;
    if (Directory.Exists(Path.Combine(dir, "src")) && File.Exists(Path.Combine(dir, "al-runner.json")))
        return true;
    return false;
}
