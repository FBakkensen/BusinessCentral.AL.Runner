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

var emitter = new BcCompiler();
var assembler = new BcAssembler();
var executor = new TestExecutor();
var results = new List<BucketResult>();

int i2 = 0;
foreach (var bundle in bundles)
{
    i2++;
    var bundleAbs = Path.GetFullPath(bundle);
    var rel = Path.GetRelativePath(Environment.CurrentDirectory, bundleAbs);

    var suites = EnumerateSuites(bundleAbs).ToList();
    if (suites.Count == 0) { Console.WriteLine($"[{i2}/{bundles.Count}] {rel} ... SKIP (no suites)"); continue; }
    Console.WriteLine($"[{i2}/{bundles.Count}] {rel} — {suites.Count} suites");

    // Pre-register every src dir for RecordPatches at the bundle level.
    foreach (var suite in suites)
    {
        var s = Path.Combine(suite, "src");
        if (Directory.Exists(s)) AlRunnerV2.Patches.RecordPatches.AddSourceDir(s);
    }

    var bundleEmit = TimeSpan.Zero;
    var bundleComp = TimeSpan.Zero;
    var bundleRun = TimeSpan.Zero;
    var bundleTests = new List<TestResult>();
    var bundleErrors = new List<string>();
    var bundleStage = BucketStage.Ran;
    int sP = 0, sF = 0, sE = 0;

    int si = 0;
    foreach (var suite in suites)
    {
        si++;
        var suiteName = Path.GetRelativePath(bundleAbs, suite);
        var suitePaths = CollectSuitePaths(suite);
        if (suitePaths.Count == 0) continue;

        var et = System.Diagnostics.Stopwatch.StartNew();
        IReadOnlyList<EmittedSource> sources;
        try { sources = emitter.Emit(suitePaths, $"V2_{Path.GetFileName(suite)}"); }
        catch (Exception ex)
        {
            et.Stop(); bundleEmit += et.Elapsed;
            bundleErrors.Add($"{suiteName}: EMIT-FAIL: {ex.Message.Split('\n')[0]}");
            continue;
        }
        et.Stop(); bundleEmit += et.Elapsed;

        var ct = System.Diagnostics.Stopwatch.StartNew();
        var compile = assembler.Compile($"V2_{Path.GetFileName(suite)}", sources);
        ct.Stop(); bundleComp += ct.Elapsed;
        if (!compile.Success)
        {
            bundleErrors.Add($"{suiteName}: COMPILE-FAIL ({compile.Errors.Count}): {compile.Errors.FirstOrDefault()?.Split('\n')[0]}");
            continue;
        }

        var rt = System.Diagnostics.Stopwatch.StartNew();
        IReadOnlyList<TestResult> tests;
        try
        {
            var asm = Assembly.Load(compile.AssemblyBytes!);
            BcRuntime.SetTestAssembly(asm);
            tests = executor.Run(asm);
        }
        catch (Exception ex)
        {
            rt.Stop(); bundleRun += rt.Elapsed;
            bundleErrors.Add($"{suiteName}: EXEC-FAIL: {ex.Message.Split('\n')[0]}");
            continue;
        }
        rt.Stop(); bundleRun += rt.Elapsed;
        bundleTests.AddRange(tests);
        sP += tests.Count(t => t.Outcome == TestOutcome.Pass);
        sF += tests.Count(t => t.Outcome == TestOutcome.Fail);
        sE += tests.Count(t => t.Outcome == TestOutcome.Error);
    }

    Console.WriteLine($"  → {sP}P/{sF}F/{sE}E across {bundleTests.Count} tests, {bundleErrors.Count} suite errors ({(bundleEmit + bundleComp + bundleRun).TotalSeconds:F1}s)");
    if (bundleTests.Count == 0 && bundleErrors.Count > 0) bundleStage = BucketStage.CompileFailed;
    results.Add(new BucketResult(bundleAbs, bundleStage,
        bundleErrors, null, bundleTests,
        bundleEmit, bundleComp, bundleRun));
}

Reporter.PrintSummary(results, Console.Out);
Reporter.WriteClassification(results, outPath);
Console.WriteLine($"Classification → {outPath}");
return 0;

// ── Helpers ──────────────────────────────────────────────────────────────────

// Collect this single suite's src/test/app* dirs for emit. Per-suite isolation
// avoids the cross-suite object-id collisions that silently zeroed-out bundled emit.
static List<string> CollectSuitePaths(string suite)
{
    var all = new List<string>();
    var s = Path.Combine(suite, "src");
    var t = Path.Combine(suite, "test");
    if (Directory.Exists(s)) all.Add(s);
    foreach (var app in Directory.EnumerateDirectories(suite, "app*"))
        all.Add(app);
    if (Directory.Exists(t)) all.Add(t);
    return all;
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
