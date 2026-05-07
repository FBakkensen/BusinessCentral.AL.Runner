// Program — orchestrates the v2 pipeline:
//   1. Apply BC runtime patches once (BcRuntime).
//   2. For each top-level arg (a "bundle" — typically tests/bucket-N):
//        collect every suite's src/test/app dirs underneath
//        AL→C# in ONE subprocess call (AlEmitter.Emit(IEnumerable<string>))
//        compile to ONE assembly (BcAssembler)
//        load + run all [Test] methods (TestExecutor)
//   3. Aggregate (Reporter), write JSON classification file.
//
// Usage:
//   dotnet run --project Runner.csproj -- [--out report.json] <bundle-dir>...
//
// A "bundle" is a directory under which we collect every suite's src/test/app*
// directory. The existing AlRunner accepts unnamed CLI args as folders and
// combines them into a single AL compilation; we drive it the same way so the
// AL-emit subprocess startup amortises across the whole bundle.
using System.Reflection;
using AlRunnerV2;

if (args.Length == 0)
{
    Console.Error.WriteLine("Usage: Runner <bundle-dir>... [--out PATH]");
    return 2;
}

string outPath = "v2-classification.json";
var bundles = new List<string>();
for (int i = 0; i < args.Length; i++)
{
    if (args[i] == "--out" && i + 1 < args.Length) { outPath = args[++i]; continue; }
    bundles.Add(args[i]);
}
Console.WriteLine($"AlRunner v2 — running {bundles.Count} bundle(s)");

// One-time runtime setup. Must happen BEFORE any BC type is touched.
var t0 = System.Diagnostics.Stopwatch.StartNew();
BcRuntime.EnsureApplied();
Console.WriteLine($"BC runtime patches applied ({t0.ElapsedMilliseconds}ms)");

var emitter = new AlEmitter();
var assembler = new BcAssembler();
var executor = new TestExecutor();
var results = new List<BucketResult>();

int i2 = 0;
foreach (var bundle in bundles)
{
    i2++;
    var bundleAbs = Path.GetFullPath(bundle);
    var rel = Path.GetRelativePath(Environment.CurrentDirectory, bundleAbs);
    Console.Write($"[{i2}/{bundles.Count}] {rel} ... ");

    // Collect every src/test/app* dir under the bundle.
    var (paths, srcDirs) = CollectBundlePaths(bundleAbs);
    if (paths.Count == 0) { Console.WriteLine("SKIP (no src/test/app dirs)"); continue; }

    // Register every src dir so RecordPatches can build NCLMetaTable instances
    // for any AL table referenced anywhere in the bundle.
    foreach (var s in srcDirs) AlRunnerV2.Patches.RecordPatches.AddSourceDir(s);

    var emitTimer = System.Diagnostics.Stopwatch.StartNew();
    IReadOnlyList<EmittedSource> sources;
    try { sources = emitter.Emit(paths); }
    catch (Exception ex)
    {
        Console.WriteLine($"EMIT-FAIL: {ex.Message.Split('\n')[0]}");
        results.Add(new BucketResult(bundleAbs, BucketStage.ExecuteFailed,
            Array.Empty<string>(), ex.Message, Array.Empty<TestResult>(),
            emitTimer.Elapsed, TimeSpan.Zero, TimeSpan.Zero));
        continue;
    }
    emitTimer.Stop();

    var compTimer = System.Diagnostics.Stopwatch.StartNew();
    var compile = assembler.Compile($"V2_{Path.GetFileName(bundleAbs)}", sources);
    compTimer.Stop();
    if (!compile.Success)
    {
        Console.WriteLine($"COMPILE-FAIL ({compile.Errors.Count} errors): {compile.Errors.FirstOrDefault()?.Split('\n')[0]}");
        results.Add(new BucketResult(bundleAbs, BucketStage.CompileFailed,
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
        results.Add(new BucketResult(bundleAbs, BucketStage.ExecuteFailed,
            Array.Empty<string>(), ex.ToString(), Array.Empty<TestResult>(),
            emitTimer.Elapsed, compTimer.Elapsed, runTimer.Elapsed));
        continue;
    }
    runTimer.Stop();
    var p = tests.Count(t => t.Outcome == TestOutcome.Pass);
    var f = tests.Count(t => t.Outcome == TestOutcome.Fail);
    var e = tests.Count(t => t.Outcome == TestOutcome.Error);
    Console.WriteLine($"{p}P/{f}F/{e}E ({(emitTimer.Elapsed + compTimer.Elapsed + runTimer.Elapsed).TotalSeconds:F1}s)");
    results.Add(new BucketResult(bundleAbs, BucketStage.Ran,
        Array.Empty<string>(), null, tests,
        emitTimer.Elapsed, compTimer.Elapsed, runTimer.Elapsed));
}

Reporter.PrintSummary(results, Console.Out);
Reporter.WriteClassification(results, outPath);
Console.WriteLine($"Classification → {outPath}");
return 0;

// ── Helpers ──────────────────────────────────────────────────────────────────

// Collect every (src, test, app*) dir under the bundle root, in a stable order
// (parent suite first, then children — matches the existing CI loop's iteration).
// Also returns the set of "src" dirs separately so RecordPatches can register them
// for AL table parsing.
static (List<string> all, List<string> srcOnly) CollectBundlePaths(string bundleRoot)
{
    var all = new List<string>();
    var src = new List<string>();
    if (!Directory.Exists(bundleRoot)) return (all, src);

    // A "suite" is any directory that has either a `test/` subdir or a `src/` subdir.
    // We walk the bundle root recursively and collect every suite's contributing dirs.
    foreach (var suite in EnumerateSuites(bundleRoot))
    {
        var s = Path.Combine(suite, "src");
        var t = Path.Combine(suite, "test");
        if (Directory.Exists(s)) { all.Add(s); src.Add(s); }
        // Any sibling directory matching app* (app1, app2, …) also counts — these are
        // extension app dependencies declared inside a suite.
        foreach (var app in Directory.EnumerateDirectories(suite, "app*"))
            all.Add(app);
        if (Directory.Exists(t)) all.Add(t);
    }
    return (all, src);
}

static IEnumerable<string> EnumerateSuites(string root)
{
    if (LooksLikeSuite(root)) { yield return Path.GetFullPath(root); yield break; }
    foreach (var d in Directory.EnumerateDirectories(root, "*", SearchOption.AllDirectories))
        if (LooksLikeSuite(d))
            yield return Path.GetFullPath(d);
}

static bool LooksLikeSuite(string dir)
    => Directory.Exists(Path.Combine(dir, "test"))
    || Directory.Exists(Path.Combine(dir, "src"));
