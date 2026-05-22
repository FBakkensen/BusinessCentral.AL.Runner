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
        "Usage: Runner [--out PATH] [--package-cache PATH ...] [--per-suite]\n" +
        "              [--verbose] [--show-pass] <bundle-dir>...\n" +
        "       Runner --precompile <input.app> --out <output.dll>\n" +
        "\n" +
        "  --verbose      Show internal [Component] diagnostic logs.\n" +
        "                 Equivalent to env AL_RUNNER_VERBOSE=1.\n" +
        "  --show-pass    Include PASS lines in per-test output\n" +
        "                 (default: only FAIL/ERROR are printed per test).");
    return 2;
}

// Output filters must be installed BEFORE any other code prints to Console.
// Reads AL_RUNNER_VERBOSE env var by default; --verbose flag overrides below.
AlRunnerV2.Log.Install();
bool showPass = Environment.GetEnvironmentVariable("AL_RUNNER_SHOW_PASS") == "1";

// AL_RUNNER_TRACE_NRE=1 — log every first-chance NullReferenceException with its
// full stack trace before it gets swallowed by AL `asserterror` / test machinery.
if (Environment.GetEnvironmentVariable("AL_RUNNER_TRACE_NRE") == "1")
{
    AppDomain.CurrentDomain.FirstChanceException += (s, e) =>
    {
        if (e.Exception is NullReferenceException)
        {
            Console.Error.WriteLine($"[FCE-NRE] {e.Exception}");
        }
    };
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
// Spike B keystone: AL-output cache. When set, the bundled-mode pipeline writes
// its emitted DLL to <cacheDir>/<key>.dll and on a subsequent invocation
// short-circuits Emit+Compile by loading that DLL directly. The key is a hash
// of (all .al source files contributing to the bundle, the resolved-deps list,
// the runner assembly mtime). See `precompiled-dll-respect.md` —
// "Our AL output is meant to be cacheable".
string? alCacheDir = null;
// Test isolation mode — default matches BC's "Test Runner - Isol. Codeunit" (130450).
var isolation = AlRunnerV2.TestIsolation.Codeunit;
for (int i = 0; i < args.Length; i++)
{
    if (args[i] == "--out" && i + 1 < args.Length) { outPath = args[++i]; continue; }
    if (args[i] == "--package-cache" && i + 1 < args.Length) { packageCacheArgs.Add(args[++i]); continue; }
    if (args[i] == "--per-suite") { bundledMode = false; continue; }
    if (args[i] == "--bundled") { bundledMode = true; continue; }
    if (args[i] == "--cache" && i + 1 < args.Length) { alCacheDir = args[++i]; continue; }
    if (args[i] == "--verbose") { AlRunnerV2.Log.Verbose = true; continue; }
    if (args[i] == "--show-pass") { showPass = true; continue; }
    if (args[i] == "--isolation" && i + 1 < args.Length)
    {
        var mode = args[++i].ToLowerInvariant();
        isolation = mode switch
        {
            "codeunit" => AlRunnerV2.TestIsolation.Codeunit,
            "test"     => AlRunnerV2.TestIsolation.Test,
            "disabled" => AlRunnerV2.TestIsolation.Disabled,
            _ => throw new ArgumentException($"--isolation: unknown mode '{mode}' (codeunit|test|disabled)")
        };
        continue;
    }
    bundles.Add(args[i]);
}
if (alCacheDir != null) Directory.CreateDirectory(alCacheDir);
Console.WriteLine($"AlRunner v2 — running {bundles.Count} bundle(s)");

// Cecil-rewrite Ncl.dll IN-PLACE on the bin path BEFORE CoreCLR's TPA probe
// resolves it. Must run BEFORE any reference to BcRuntime (whose field metadata
// triggers Ncl load on class init). Allowed surface per
// .claude/rules/precompiled-dll-respect.md — Ncl is runtime engine, not BaseApp.
{
    var srcDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        ".local/share/al-runner/artifacts/27.5.46862.48827");
    var binNcl = Path.Combine(AppContext.BaseDirectory, "Microsoft.Dynamics.Nav.Ncl.dll");
    AlRunnerV2.Infrastructure.NclCecilRewrite.RewriteInPlace(srcDir, binNcl);
}

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
var executor = new TestExecutor { Isolation = isolation };
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
                // Register dep .app paths with RecordPatches so the NCLMetaTable
                // populator can fall back to the AL source shipped inside the .app
                // (NAVX zip) for tables defined in compiled BC dependencies — the
                // case spike-a-baseapp's Currency-init scenario depends on.
                foreach (var (_, appPath) in ordered)
                    AlRunnerV2.Patches.RecordPatches.AddBcAppPath(appPath);
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

        var moduleName = $"V2_{Path.GetFileName(bundleAbs)}";

        // ── AL-output cache check (Spike B keystone) ───────────────────────
        // Sidecar `<key>.enum-registry.json` carries the AlEnumMetadataRegistry
        // entries that emit would have populated as a side effect — see
        // BcCompiler.CaptureOutputter.AddApplicationObject. On HIT we must
        // replay them BEFORE Assembly.Load so any test executing
        // `Enum::"X".Names()` / `.Ordinals()` finds the registry populated.
        // Cache HIT requires BOTH files to exist; missing sidecar → MISS.
        byte[]? cachedBytes = null;
        string? cacheKey = null;
        string? cachePath = null;
        string? sidecarPath = null;
        if (alCacheDir != null)
        {
            cacheKey = ComputeAlCacheKey(allPaths, moduleName, ordered: GetOrderedDepIds(bucketRoot, packageCacheDirs));
            cachePath = Path.Combine(alCacheDir, cacheKey + ".dll");
            sidecarPath = Path.Combine(alCacheDir, cacheKey + ".enum-registry.json");
            if (File.Exists(cachePath) && File.Exists(sidecarPath))
            {
                try { cachedBytes = File.ReadAllBytes(cachePath); }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"  [cache] read failed for {cachePath}: {ex.Message}");
                    cachedBytes = null;
                }
            }
            else if (File.Exists(cachePath))
            {
                Console.Error.WriteLine($"  [cache] DLL present but sidecar missing — treating as MISS ({sidecarPath})");
            }
        }

        byte[]? assemblyBytes = null;
        if (cachedBytes != null)
        {
            // Replay the enum-registry sidecar BEFORE Assembly.Load. Test
            // execution is what reads the registry (via the
            // NCLEnumMetadata_CreateByIdAlAware hook), so as long as replay
            // completes before executor.Run that's sufficient — but doing it
            // pre-Load is cheap insurance against any module-cctor that
            // touches enum metadata.
            int replayed = 0;
            try { replayed = LoadEnumRegistrySidecar(sidecarPath!); }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"  [cache] sidecar replay failed for {sidecarPath}: {ex.Message} — falling through to MISS");
                cachedBytes = null;
            }
            if (cachedBytes != null)
            {
                Console.Error.WriteLine($"  [cache] HIT  key={cacheKey} path={cachePath} ({cachedBytes.Length} bytes, {replayed} enum entries replayed) — skipping Emit+Compile");
                assemblyBytes = cachedBytes;
            }
        }
        if (assemblyBytes == null)
        {
            if (alCacheDir != null) Console.Error.WriteLine($"  [cache] MISS key={cacheKey} — running Emit+Compile");
            var et = System.Diagnostics.Stopwatch.StartNew();
            IReadOnlyList<EmittedSource> sources = Array.Empty<EmittedSource>();
            IReadOnlyList<string> alDiagnostics = Array.Empty<string>();
            // Emit-phase timeout: default 120 s, override via AL_RUNNER_EMIT_TIMEOUT_SEC.
            // Note: Task.Run thread continues in background after timeout — acceptable for a CLI tool.
            int emitTimeoutSec = int.TryParse(
                Environment.GetEnvironmentVariable("AL_RUNNER_EMIT_TIMEOUT_SEC"), out var ts) ? ts : 120;
            var emitTask = Task.Run(() => emitter.Emit(allPaths, moduleName));
            try
            {
                if (!emitTask.Wait(TimeSpan.FromSeconds(emitTimeoutSec)))
                {
                    Console.Error.WriteLine(
                        $"<bundled>: EMIT-TIMEOUT after {emitTimeoutSec}s on {allPaths.Count} AL paths");
                    Console.Error.WriteLine(
                        "Hint: increase AL_RUNNER_EMIT_TIMEOUT_SEC or quarantine the offending suite under tests/excluded/.");
                    bundleErrors.Add($"<bundled>: EMIT-TIMEOUT after {emitTimeoutSec}s");
                }
                else
                {
                    var emitOutput = emitTask.Result;
                    sources = emitOutput.Sources;
                    alDiagnostics = emitOutput.Diagnostics;
                }
            }
            catch (AggregateException aggEx) when (emitTask.IsFaulted)
            {
                var flat = aggEx.Flatten();
                var rootEx = flat.InnerExceptions[0];
                Console.Error.WriteLine($"<bundled>: EMIT-FAIL — {rootEx.GetType().Name}: {rootEx.Message}");
                if (rootEx.StackTrace is { } st) Console.Error.WriteLine(st);
                if (flat.InnerExceptions.Count > 1)
                    foreach (var inner in flat.InnerExceptions.Skip(1))
                        Console.Error.WriteLine($"  → {inner.GetType().Name}: {inner.Message}");
                bundleErrors.Add($"<bundled>: EMIT-FAIL: {rootEx.Message.Split('\n')[0]}");
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"<bundled>: EMIT-FAIL — {ex.GetType().Name}: {ex.Message}");
                if (ex.StackTrace is { } st) Console.Error.WriteLine(st);
                bundleErrors.Add($"<bundled>: EMIT-FAIL: {ex.Message.Split('\n')[0]}");
            }
            finally
            {
                et.Stop();
                bundleEmit += et.Elapsed;
            }

            if (sources.Count == 0 && alDiagnostics.Count > 0)
            {
                // Emit produced zero sources — BC's compiler swallowed exceptions internally.
                // Surface AL diagnostics (parse/declaration errors) so the failure is visible.
                Console.Error.WriteLine($"<bundled>: EMIT-ZERO — 0 sources emitted, {alDiagnostics.Count} AL error(s):");
                foreach (var d in alDiagnostics)
                    Console.Error.WriteLine($"  {d}");
                bundleErrors.Add($"<bundled>: EMIT-ZERO ({alDiagnostics.Count} AL error(s))");
            }
            if (sources.Count > 0)
            {
                var ct = System.Diagnostics.Stopwatch.StartNew();
                var compile = assembler.Compile(moduleName, sources);
                ct.Stop(); bundleComp += ct.Elapsed;
                if (!compile.Success)
                {
                    Console.Error.WriteLine($"<bundled>: COMPILE-FAIL — {compile.Errors.Count} error(s):");
                    foreach (var err in compile.Errors)
                        Console.Error.WriteLine($"  {err}");
                    if (alDiagnostics.Count > 0)
                    {
                        Console.Error.WriteLine($"<bundled>: AL diagnostics from emit ({alDiagnostics.Count}):");
                        foreach (var d in alDiagnostics)
                            Console.Error.WriteLine($"  {d}");
                    }
                    bundleErrors.Add($"<bundled>: COMPILE-FAIL ({compile.Errors.Count}): {compile.Errors.FirstOrDefault()?.Split('\n')[0]}");
                }
                else
                {
                    assemblyBytes = compile.AssemblyBytes;
                    if (cachePath != null && assemblyBytes != null)
                    {
                        try
                        {
                            File.WriteAllBytes(cachePath, assemblyBytes);
                            // Sidecar: persist the AlEnumMetadataRegistry side-effect that
                            // emit just populated. Without this, cache HIT replays the DLL
                            // but leaves the registry empty → enum tests fail.
                            int written = SaveEnumRegistrySidecar(sidecarPath!);
                            Console.Error.WriteLine($"  [cache] WROTE key={cacheKey} path={cachePath} ({assemblyBytes.Length} bytes, {written} enum entries → sidecar)");
                        }
                        catch (Exception ex)
                        {
                            Console.Error.WriteLine($"  [cache] write failed for {cachePath}: {ex.Message}");
                        }
                    }
                }
            }
        }

        if (assemblyBytes != null)
        {
            var rt = System.Diagnostics.Stopwatch.StartNew();
            IReadOnlyList<TestResult> tests;
            try
            {
                var asm = Assembly.Load(assemblyBytes);
                BcRuntime.SetTestAssembly(asm);
                BcRuntime.OosHooksActive = true;
                tests = executor.Run(asm);
            }
            catch (Exception ex)
            {
                rt.Stop(); bundleRun += rt.Elapsed;
                bundleErrors.Add($"<bundled>: EXEC-FAIL: {ex.Message.Split('\n')[0]}");
                tests = Array.Empty<TestResult>();
            }
            finally
            {
                BcRuntime.OosHooksActive = false;
            }
            rt.Stop(); bundleRun += rt.Elapsed;
            bundleTests.AddRange(tests);
            sP += tests.Count(t => t.Outcome == TestOutcome.Pass);
            sF += tests.Count(t => t.Outcome == TestOutcome.Fail);
            sE += tests.Count(t => t.Outcome == TestOutcome.Error);
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
            IReadOnlyList<string> suiteAlDiagnostics = Array.Empty<string>();
            try
            {
                var emitOutput = emitter.Emit(suitePaths, $"V2_{Path.GetFileName(suite)}");
                sources = emitOutput.Sources;
                suiteAlDiagnostics = emitOutput.Diagnostics;
            }
            catch (Exception ex)
            {
                et.Stop(); bundleEmit += et.Elapsed;
                Console.Error.WriteLine($"{suiteName}: EMIT-FAIL — {ex.GetType().Name}: {ex.Message}");
                if (ex.StackTrace is { } st) Console.Error.WriteLine(st);
                bundleErrors.Add($"{suiteName}: EMIT-FAIL: {ex.Message.Split('\n')[0]}");
                continue;
            }
            et.Stop(); bundleEmit += et.Elapsed;

            var ct = System.Diagnostics.Stopwatch.StartNew();
            var compile = assembler.Compile($"V2_{Path.GetFileName(suite)}", sources);
            ct.Stop(); bundleComp += ct.Elapsed;
            if (!compile.Success)
            {
                Console.Error.WriteLine($"{suiteName}: COMPILE-FAIL — {compile.Errors.Count} error(s):");
                foreach (var err in compile.Errors)
                    Console.Error.WriteLine($"  {err}");
                if (suiteAlDiagnostics.Count > 0)
                {
                    Console.Error.WriteLine($"{suiteName}: AL diagnostics ({suiteAlDiagnostics.Count}):");
                    foreach (var d in suiteAlDiagnostics)
                        Console.Error.WriteLine($"  {d}");
                }
                bundleErrors.Add($"{suiteName}: COMPILE-FAIL ({compile.Errors.Count}): {compile.Errors.FirstOrDefault()?.Split('\n')[0]}");
                continue;
            }

            var rt = System.Diagnostics.Stopwatch.StartNew();
            IReadOnlyList<TestResult> tests;
            try
            {
                var asm = Assembly.Load(compile.AssemblyBytes!);
                BcRuntime.SetTestAssembly(asm);
                BcRuntime.OosHooksActive = true;
                tests = executor.Run(asm);
            }
            catch (Exception ex)
            {
                rt.Stop(); bundleRun += rt.Elapsed;
                bundleErrors.Add($"{suiteName}: EXEC-FAIL: {ex.Message.Split('\n')[0]}");
                continue;
            }
            finally
            {
                BcRuntime.OosHooksActive = false;
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

Reporter.PrintPerTest(results, Console.Out, showPass);
Reporter.PrintFailureClassification(results, Console.Out);
Reporter.PrintSummary(results, Console.Out);
Reporter.WriteClassification(results, outPath);
Console.WriteLine($"Classification → {outPath}");
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

    var emitted = compiler.Emit(new[] { tempDir }, manifest.Name).Sources;
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

// Deterministic cache key for the bundled-mode emit:
//   sha256( runner-asm-mtime-ticks
//         | moduleName
//         | each (ordered dep id+version)
//         | each (.al file relpath + sha256-of-contents) sorted )
// Hashed in a single pass with line-separated framing so two different file
// layouts can't collide. The key is hex-encoded sha256 (64 chars).
static string ComputeAlCacheKey(
    IReadOnlyList<string> alFolders,
    string moduleName,
    IReadOnlyList<string> ordered)
{
    using var sha = System.Security.Cryptography.SHA256.Create();
    using var ms = new MemoryStream();
    void WriteLine(string s)
    {
        var bytes = System.Text.Encoding.UTF8.GetBytes(s + "\n");
        ms.Write(bytes, 0, bytes.Length);
    }

    // 0. Cache schema version — bumped whenever the on-disk cache layout
    //    (sidecar set, sidecar shape, or hash framing) changes. Old DLLs
    //    written before the bump simply hash to a different key and become
    //    unreachable garbage in <cacheDir>; the new key MISSes and rebuilds.
    //    v2: added <key>.enum-registry.json sidecar so cache HIT replays the
    //    AlEnumMetadataRegistry side-effects that emit would have set up.
    WriteLine("schema:v2");

    // 1. Runner assembly fingerprint — any rewriter / polyfill / patch change
    //    in the runner forces a cache miss.
    var runnerLoc = typeof(AlRunnerV2.BcAssembler).Assembly.Location;
    if (!string.IsNullOrEmpty(runnerLoc) && File.Exists(runnerLoc))
        WriteLine($"runner:{File.GetLastWriteTimeUtc(runnerLoc).Ticks}:{new FileInfo(runnerLoc).Length}");
    else
        WriteLine("runner:unknown");

    WriteLine($"module:{moduleName}");

    foreach (var d in ordered) WriteLine($"dep:{d}");

    // Enumerate every .al file in stable order, hash each.
    var alFiles = alFolders
        .Where(Directory.Exists)
        .SelectMany(d => Directory.EnumerateFiles(d, "*.al", SearchOption.AllDirectories))
        .Distinct()
        .OrderBy(p => p, StringComparer.Ordinal)
        .ToList();
    foreach (var f in alFiles)
    {
        byte[] hash;
        using (var fs = File.OpenRead(f))
            hash = sha.ComputeHash(fs);
        WriteLine($"al:{f}:{Convert.ToHexString(hash)}");
    }

    ms.Position = 0;
    var keyBytes = sha.ComputeHash(ms);
    return Convert.ToHexString(keyBytes).ToLowerInvariant();
}

// Sidecar: serialize AlEnumMetadataRegistry to <key>.enum-registry.json so
// cache HIT can replay the side-effect that emit would have populated.
// Schema (v2): { "enums": [ { "id": int, "name": string, "options": [string], "indexes": [int] }, ... ] }
static int SaveEnumRegistrySidecar(string path)
{
    var entries = AlEnumMetadataRegistry.Snapshot();
    var dto = new
    {
        enums = entries.Select(e => new
        {
            id = e.Id,
            name = e.Name,
            options = e.Options,
            indexes = e.Indexes,
        }).ToArray()
    };
    var json = System.Text.Json.JsonSerializer.Serialize(dto, new System.Text.Json.JsonSerializerOptions
    {
        WriteIndented = false,
    });
    File.WriteAllText(path, json);
    return entries.Count;
}

// Replay AlEnumMetadataRegistry from <key>.enum-registry.json. Throws on
// corrupt JSON; the caller treats any exception as cache MISS and rebuilds.
static int LoadEnumRegistrySidecar(string path)
{
    var json = File.ReadAllText(path);
    using var doc = System.Text.Json.JsonDocument.Parse(json);
    if (!doc.RootElement.TryGetProperty("enums", out var arr)
        || arr.ValueKind != System.Text.Json.JsonValueKind.Array)
        throw new InvalidDataException("enum-registry.json: missing 'enums' array");
    int count = 0;
    foreach (var e in arr.EnumerateArray())
    {
        int id = e.GetProperty("id").GetInt32();
        string name = e.GetProperty("name").GetString() ?? string.Empty;
        var optsEl = e.GetProperty("options");
        var idxEl = e.GetProperty("indexes");
        var opts = new string[optsEl.GetArrayLength()];
        int oi = 0;
        foreach (var o in optsEl.EnumerateArray()) opts[oi++] = o.GetString() ?? string.Empty;
        var idxs = new int[idxEl.GetArrayLength()];
        int ii = 0;
        foreach (var x in idxEl.EnumerateArray()) idxs[ii++] = x.GetInt32();
        AlEnumMetadataRegistry.Register(id, name, opts, idxs);
        count++;
    }
    return count;
}

// Read app.json deps and feed them through DependencyResolver so the cache key
// reflects the exact resolved set (id+version), not just declared roots. This
// matches what BcCompiler.SetResolvedDeps fed into the compile.
static IReadOnlyList<string> GetOrderedDepIds(string? bucketRoot, IReadOnlyList<string> packageCacheDirs)
{
    if (bucketRoot == null) return Array.Empty<string>();
    var appJsonPath = Path.Combine(bucketRoot, "app.json");
    if (!File.Exists(appJsonPath)) return Array.Empty<string>();
    try
    {
        var roots = ReadDependencies(appJsonPath).ToList();
        var resolver = new AlRunnerV2.DependencyResolver(packageCacheDirs);
        var ordered = resolver.Resolve(roots);
        return ordered
            .Select(d => $"{d.Manifest.AppId:N}:{d.Manifest.Version}")
            .OrderBy(s => s, StringComparer.Ordinal)
            .ToList();
    }
    catch
    {
        return Array.Empty<string>();
    }
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
