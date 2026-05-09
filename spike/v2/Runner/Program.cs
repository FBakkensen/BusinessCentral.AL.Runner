// Program — orchestrates the v2 pipeline:
//   1. Parse CLI (caches, bundles, --precompile subcommand).
//   2. If --precompile: dispatch single-app compile-to-DLL and exit.
//   3. Apply BC runtime patches once (BcRuntime).
//   4. For each top-level arg (a "bundle" — typically tests/bucket-N/<category>):
//        locate the bucket-root app.json (climb the path)
//        resolve declared deps via DependencyResolver
//        load deps via DependencyLoader (3-tier resolution)
//        SetResolvedDeps on BcCompiler so compile-time symbols mirror runtime
//        iterate suites: emit → compile → run → aggregate
//   5. Reporter writes JSON.
//
// Usage:
//   Runner [--out PATH] [--package-cache PATH ...] <bundle-dir>...
//   Runner --precompile <input.app> --out <output.dll>
using System.Reflection;
using AlRunnerV2;

if (args.Length == 0)
{
    Console.Error.WriteLine(
        "Usage: Runner [--out PATH] [--package-cache PATH ...] [--per-suite] <bundle-dir>...\n" +
        "       Runner --precompile <input.app> --out <output.dll>");
    return 2;
}

// ── --precompile subcommand ────────────────────────────────────────────────
if (args[0] == "--precompile")
{
    return RunPrecompile(args.Skip(1).ToArray());
}

string outPath = "v2-classification.json";
var bundles = new List<string>();
var packageCacheArgs = new List<string>();
// Bundled mode is the canonical fast path (5-7× faster, parity-verified across
// all 4 sub-buckets). `--per-suite` falls back to the legacy per-Compilation
// path; kept for one cycle for diagnostic comparisons. `--bundled` accepted as
// a no-op alias for backwards compatibility — will be removed.
bool bundledMode = true;
for (int i = 0; i < args.Length; i++)
{
    if (args[i] == "--out" && i + 1 < args.Length) { outPath = args[++i]; continue; }
    if (args[i] == "--package-cache" && i + 1 < args.Length) { packageCacheArgs.Add(args[++i]); continue; }
    if (args[i] == "--per-suite") { bundledMode = false; continue; }
    if (args[i] == "--bundled") { bundledMode = true; continue; }
    bundles.Add(args[i]);
}
Console.WriteLine($"AlRunner v2 — running {bundles.Count} bundle(s)");

var packageCacheDirs = packageCacheArgs.Count > 0
    ? packageCacheArgs.Where(Directory.Exists).ToList()
    : DefaultPackageCacheDirs().ToList();
Console.WriteLine($"  package caches: {packageCacheDirs.Count} dir(s)");

// One-time runtime setup. Must happen BEFORE any BC type is touched.
// Install the assembly Resolving handler FIRST so patch reflection or generic
// instantiation in BC code can resolve transitively-referenced service-tier DLLs
// (Microsoft.Dynamics.Nav.Core, .AL.Common, .Apps, .TableProxyBuilder, etc. — 19
// of the 24 BC DLLs Ncl.dll references aren't project-referenced).
DependencyLoader.EnsureResolverInstalled_Public();
var t0 = System.Diagnostics.Stopwatch.StartNew();
BcRuntime.EnsureApplied();
Console.WriteLine($"BC runtime patches applied ({t0.ElapsedMilliseconds}ms)");

var emitter = new BcCompiler();
var assembler = new BcAssembler();
var executor = new TestExecutor();
var depLoader = new DependencyLoader(emitter, assembler);
var results = new List<BucketResult>();

int i2 = 0;
foreach (var bundle in bundles)
{
    i2++;
    var bundleAbs = Path.GetFullPath(bundle);
    var rel = Path.GetRelativePath(Environment.CurrentDirectory, bundleAbs);

    // ── per-bucket dep resolution ──────────────────────────────────────────
    var bucketRoot = FindBucketRoot(bundleAbs);
    if (bucketRoot != null)
    {
        var appJsonPath = Path.Combine(bucketRoot, "app.json");
        if (File.Exists(appJsonPath))
        {
            try
            {
                var roots = ReadDependencies(appJsonPath);
                var resolver = new DependencyResolver(packageCacheDirs);
                var ordered = resolver.Resolve(roots);
                Console.WriteLine($"  [{rel}] resolved {ordered.Count} dep(s)");
                BcCompiler.SetResolvedDeps(ordered, packageCacheDirs);
                var loaded = depLoader.LoadAll(ordered, bucketRoot);
                Console.WriteLine($"  [{rel}] loaded {loaded.Count} dep assembl(ies)");
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"  [{rel}] DEP-RESOLVE-FAIL: {ex.Message}");
            }
        }
        else
        {
            Console.Error.WriteLine($"  [{rel}] WARN: no {appJsonPath} — skipping dep loading");
        }
    }

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

    if (bundledMode)
    {
        // ── Bundled mode (default): ONE Emit + ONE Compile + ONE Run across
        // all suites. 5-7× faster than per-suite, parity-verified on all 4
        // sub-buckets. Suites whose AL hits BC emit bugs or bundled-only
        // strictness checks are quarantined under tests/excluded/ with a
        // RUNNER-GAP-*.md note explaining the gap.
        var allPaths = new List<string>();
        foreach (var suite in suites)
            allPaths.AddRange(CollectSuitePaths(suite, bucketRoot));
        allPaths = allPaths.Distinct().ToList();

        var et = System.Diagnostics.Stopwatch.StartNew();
        IReadOnlyList<EmittedSource> sources;
        try { sources = emitter.Emit(allPaths, $"V2_{Path.GetFileName(bundleAbs)}"); }
        catch (Exception ex)
        {
            et.Stop(); bundleEmit += et.Elapsed;
            bundleErrors.Add($"<bundled>: EMIT-FAIL: {ex.Message.Split('\n')[0]}");
            sources = Array.Empty<EmittedSource>();
        }
        et.Stop(); bundleEmit += et.Elapsed;

        if (sources.Count > 0)
        {
            var ct = System.Diagnostics.Stopwatch.StartNew();
            var compile = assembler.Compile($"V2_{Path.GetFileName(bundleAbs)}", sources);
            ct.Stop(); bundleComp += ct.Elapsed;
            if (!compile.Success)
            {
                bundleErrors.Add($"<bundled>: COMPILE-FAIL ({compile.Errors.Count}): {compile.Errors.FirstOrDefault()?.Split('\n')[0]}");
            }
            else
            {
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
                    bundleErrors.Add($"<bundled>: EXEC-FAIL: {ex.Message.Split('\n')[0]}");
                    tests = Array.Empty<TestResult>();
                }
                rt.Stop(); bundleRun += rt.Elapsed;
                bundleTests.AddRange(tests);
                sP += tests.Count(t => t.Outcome == TestOutcome.Pass);
                sF += tests.Count(t => t.Outcome == TestOutcome.Fail);
                sE += tests.Count(t => t.Outcome == TestOutcome.Error);
            }
        }
    }
    else
    {
        int si = 0;
        foreach (var suite in suites)
        {
            si++;
            var suiteName = Path.GetRelativePath(bundleAbs, suite);
            var suitePaths = CollectSuitePaths(suite, bucketRoot);
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
// [HarmonySpike] Print per-call stats so we can see hit counts and overhead.
AlRunnerV2.Patches.HarmonySpike.PrintSummary();
return 0;

// ── Helpers ──────────────────────────────────────────────────────────────────

static int RunPrecompile(string[] subArgs)
{
    string? input = null;
    string? output = null;
    var caches = new List<string>();
    for (int i = 0; i < subArgs.Length; i++)
    {
        if (subArgs[i] == "--out" && i + 1 < subArgs.Length) { output = subArgs[++i]; continue; }
        if (subArgs[i] == "--package-cache" && i + 1 < subArgs.Length) { caches.Add(subArgs[++i]); continue; }
        if (input == null) { input = subArgs[i]; continue; }
    }
    if (input == null || output == null)
    {
        Console.Error.WriteLine("Usage: Runner --precompile <input.app> --out <output.dll> [--package-cache PATH ...]");
        return 2;
    }
    var manifest = AppLoader.ReadManifest(input);
    if (manifest == null) { Console.Error.WriteLine($"Failed to read manifest from {input}"); return 2; }

    var packageCacheDirs = caches.Count > 0 ? caches : DefaultPackageCacheDirs().ToList();

    // Apply BC patches before any BC type is touched (BcCompiler uses BC types).
    BcRuntime.EnsureApplied();

    // Resolve transitive deps of THIS app so its compile sees them as symbol refs.
    var resolver = new DependencyResolver(packageCacheDirs);
    var transitive = resolver.Resolve(manifest.Dependencies);
    BcCompiler.SetResolvedDeps(transitive, packageCacheDirs);

    var sw = System.Diagnostics.Stopwatch.StartNew();
    var compiler = new BcCompiler();
    var assembler = new BcAssembler();

    var alSources = AppLoader.ExtractAl(input);
    if (alSources.Count == 0)
    {
        Console.Error.WriteLine($"--precompile: {input} contains no src/*.al — nothing to compile");
        return 2;
    }
    var tempDir = Path.Combine(Path.GetTempPath(), "al-runner-precompile",
        Sanitize($"{manifest.Publisher}_{manifest.Name}_{manifest.Version}"));
    Directory.CreateDirectory(tempDir);
    foreach (var existing in Directory.EnumerateFiles(tempDir, "*.al"))
    {
        try { File.Delete(existing); } catch { }
    }
    foreach (var (name, src) in alSources)
        File.WriteAllText(Path.Combine(tempDir, Sanitize(name)), src);

    var emitted = compiler.Emit(new[] { tempDir }, manifest.Name);
    if (emitted.Count == 0)
    {
        Console.Error.WriteLine($"--precompile: 0 sources emitted from {manifest.Name} (BC silent zero-output sentinel?)");
        return 3;
    }
    var asmName = $"Dep_{Sanitize(manifest.Publisher)}_{Sanitize(manifest.Name)}_{manifest.Version.ToString().Replace('.', '_')}";
    var compile = assembler.Compile(asmName, emitted);
    if (!compile.Success)
    {
        Console.Error.WriteLine($"--precompile: COMPILE-FAIL: {compile.Errors.FirstOrDefault()?.Split('\n')[0]}");
        return 3;
    }
    Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output))!);
    File.WriteAllBytes(output, compile.AssemblyBytes!);
    sw.Stop();
    Console.WriteLine(
        $"precompiled {manifest.Name} v{manifest.Version} → {output} " +
        $"({compile.AssemblyBytes!.Length} bytes, {sw.ElapsedMilliseconds}ms)");
    return 0;

    static string Sanitize(string s)
    {
        var bad = Path.GetInvalidFileNameChars().Concat(new[] { ' ', '/', '\\' }).ToArray();
        var sb = new System.Text.StringBuilder(s.Length);
        foreach (var ch in s) sb.Append(Array.IndexOf(bad, ch) >= 0 ? '_' : ch);
        return sb.ToString();
    }
}

// Default cache: the latest BC artifact version under ~/.bcartifacts.cache/sandbox/
// + the curated symbol set under ~/.local/share/al-runner/symbols/.
static IEnumerable<string> DefaultPackageCacheDirs()
{
    var home = Environment.GetEnvironmentVariable("HOME");
    if (string.IsNullOrEmpty(home)) yield break;

    var bcRoot = Path.Combine(home, ".bcartifacts.cache", "sandbox");
    var bcLatest = LatestVersionDir(bcRoot);
    if (bcLatest != null)
    {
        var w1Ext = Path.Combine(bcLatest, "w1", "Extensions");
        if (Directory.Exists(w1Ext)) yield return w1Ext;
        var platApps = Path.Combine(bcLatest, "platform", "Applications");
        if (Directory.Exists(platApps)) yield return platApps;
    }

    var symRoot = Path.Combine(home, ".local", "share", "al-runner", "symbols");
    var symLatest = LatestVersionDir(symRoot);
    if (symLatest != null) yield return symLatest;
}

static string? LatestVersionDir(string root)
{
    if (!Directory.Exists(root)) return null;
    return Directory.EnumerateDirectories(root)
        .OrderByDescending(d => Path.GetFileName(d), StringComparer.Ordinal)
        .FirstOrDefault();
}

// Walks up from <bundlePath> until it finds a dir containing app.json.
// Returns null if none found before /tests/ or filesystem root.
static string? FindBucketRoot(string bundlePath)
{
    var cur = Directory.Exists(bundlePath) ? bundlePath : Path.GetDirectoryName(bundlePath);
    while (!string.IsNullOrEmpty(cur))
    {
        if (File.Exists(Path.Combine(cur, "app.json"))) return cur;
        var parent = Path.GetDirectoryName(cur);
        if (parent == cur) return null;
        cur = parent;
    }
    return null;
}

static IEnumerable<DependencyRef> ReadDependencies(string appJsonPath)
{
    using var doc = System.Text.Json.JsonDocument.Parse(File.ReadAllText(appJsonPath));
    if (!doc.RootElement.TryGetProperty("dependencies", out var deps)
        || deps.ValueKind != System.Text.Json.JsonValueKind.Array)
        yield break;
    foreach (var d in deps.EnumerateArray())
    {
        var idStr = d.TryGetProperty("id", out var pid) ? pid.GetString() : null;
        var name = d.TryGetProperty("name", out var pn) ? pn.GetString() ?? "" : "";
        var pub = d.TryGetProperty("publisher", out var pp) ? pp.GetString() ?? "" : "";
        var ver = d.TryGetProperty("version", out var pv) ? pv.GetString() ?? "0.0.0.0" : "0.0.0.0";
        Guid id = Guid.Empty;
        if (!string.IsNullOrEmpty(idStr)) Guid.TryParse(idStr, out id);
        if (!Version.TryParse(ver, out var v)) v = new Version(0, 0, 0, 0);
        yield return new DependencyRef(id, name, pub, v);
    }
}

// Collect this single suite's src/test/app* dirs for emit. Per-suite isolation
// avoids the cross-suite object-id collisions that silently zeroed-out bundled emit.
// When a bucket root is supplied, also include `<bucketRoot>/_shared/` so AL
// files at the bucket level (e.g. an Assert.Codeunit.al that satisfies a
// dependency without a runtime DLL) compile into every suite.
static List<string> CollectSuitePaths(string suite, string? bucketRoot = null)
{
    var all = new List<string>();
    var s = Path.Combine(suite, "src");
    var t = Path.Combine(suite, "test");
    if (Directory.Exists(s)) all.Add(s);
    foreach (var app in Directory.EnumerateDirectories(suite, "app*"))
        all.Add(app);
    if (Directory.Exists(t)) all.Add(t);
    if (bucketRoot != null)
    {
        var shared = Path.Combine(bucketRoot, "_shared");
        if (Directory.Exists(shared)) all.Add(shared);
    }
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
