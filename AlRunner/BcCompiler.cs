// BcCompiler — in-process AL→C# compile via BC's own Compilation.Emit.
//
// Replaces the old AlEmitter (which shelled out to AlRunner --dump-csharp).
// The output bytes from this stage are ALREADY post-rewrite C# — BC's emitter
// applies the [NavByReferenceAttribute] T → ByRef<T> wrap natively at parameter
// declaration sites (see codeanalysis.cs:342854 EmitParameterType,
// codeanalysis.cs:342867 EmitMethodScopeFieldType, predicate at 340864
// ShouldBePassedByRef = IsVar && !IsArray && !IsUserType). v1's
// `--dump-csharp` is just `Console.WriteLine` of the same byte[] payload —
// the "before rewriting" label refers to v1's downstream RoslynRewriter, not
// to BC's compiler. So v2 no longer needs ByRefWrapRewriter.
//
// Wins over the subprocess path:
//   • ~88 % wall-time saving (no `dotnet AlRunner.dll` cold-start per bundle).
//   • No custom rewriter — BC's compiler already does the only mechanical
//     transformation that was happening in v2's syntax-rewrite pass.
//   • One in-memory Compilation per top-level arg, exactly mirroring v1's
//     `AlTranspiler.TranspileMulti` (AlRunner/Program.cs:1480) — single
//     compilation across all suite folders inside the bundle, just like the
//     existing AL emitter subprocess used to do.
//
// What still happens downstream (BcAssembler): parse the captured C# strings
// into Roslyn SyntaxTrees and CSharpCompilation.Emit() to produce IL. BC's
// service tier itself does the same two-stage AL→C#→IL handoff
// (Microsoft.Dynamics.Nav.Ncl.dll → NavAppPackageCompiler.RecompileFullPackage
//  → CSharpCompiler.Instance.CompileCSharpFilesAsync); the CSharpCompiler
// internal type is unreachable from out-of-process code (depends on
// NavEnvironment.Instance + live tenant context), so we own that step.
using System.Collections.Immutable;
using NavCA = Microsoft.Dynamics.Nav.CodeAnalysis;
using NavSyntax = Microsoft.Dynamics.Nav.CodeAnalysis.Syntax;
using NavEmit = Microsoft.Dynamics.Nav.CodeAnalysis.Emit;
using NavDiag = Microsoft.Dynamics.Nav.CodeAnalysis.Diagnostics;
using NavSymRef = Microsoft.Dynamics.Nav.CodeAnalysis.SymbolReference;
using NavDotNet = Microsoft.Dynamics.Nav.CodeAnalysis.DotNet;

namespace AlRunnerV2;

public sealed record EmittedSource(string Name, string Code);

/// <summary>
/// Output of <see cref="BcCompiler.Emit"/>: emitted C# sources plus any AL-level
/// diagnostics (parse errors, declaration errors, emit-result errors) formatted
/// alc-style: <c>path(line,col): error ALXXXX: message</c>.
/// </summary>
public sealed record BcEmitOutput(IReadOnlyList<EmittedSource> Sources, IReadOnlyList<string> Diagnostics);

public sealed class BcCompiler
{
    /// <summary>
    /// Compile every .al file under <paramref name="alFolders"/> into a single
    /// in-memory Compilation; capture per-AL-object C# from the emit stage.
    /// </summary>
    /// <remarks>
    /// Mirrors v1's AlTranspiler.TranspileMulti shape (AlRunner/Program.cs:1480):
    /// one ParseOptions, one Compilation, parallel SyntaxTree.ParseObjectText.
    /// Exceptions during emit (the BC compiler throws AggregateException for
    /// individual method-body emit failures) are caught so partial output is
    /// still returned — same policy as v1 (Program.cs:1996).
    /// </remarks>
    // Lifted to static so the IReferenceLoader + SymbolReferenceSpecification[] are
    // built once per process. v1's pattern was "compile against a symbol reference
    // one app at a time"; per-suite emit + a shared loader is the in-process
    // equivalent. Bundling all suites into one Compilation ran into cross-suite
    // object-id collisions and silently produced 0 sources.
    private static NavCA.ISymbolReferenceLoader? _refLoader;
    private static NavCA.SymbolReferenceSpecification[]? _refSpecs;
    private static readonly object _refSync = new();
    // Set by Program.cs once after dep resolution. The compile-time symbol set
    // mirrors the runtime-loaded dep set by construction — no allow-list drift.
    private static IReadOnlyList<(AppManifest Manifest, string AppPath)>? _resolvedDeps;
    private static IReadOnlyList<string>? _packageCacheDirs;

    // Cached DotNet resolver factory — constructed once from the service-tier
    // artifacts dir so AL `DotNet` variable types resolve to real .NET types.
    // Without this, NavTypeKind stays None and Compilation.Emit throws
    // UnexpectedValue(NavTypeKind.None) for any AL object with DotNet interop.
    private static NavDotNet.IDotNetResolverFactory? _dotNetResolverFactory;
    private static readonly object _dotNetSync = new();

    // When true (set by --precompile), the symbol-reference fallback enumerates
    // all discoverable .app files in the package cache dirs. This is needed for
    // apps whose NavxManifest.xml <Dependencies/> is empty but whose AL source
    // uses `using` statements that require BaseApp/System Application symbols.
    // Left false for corpus runs (where SetResolvedDeps provides the dep list).
    private static bool _usePackageCacheFallback;
    private static Guid _packageCacheFallbackExcludeId;

    /// <summary>
    /// Called from the --precompile path to enable the all-packages fallback for
    /// apps that declare no manifest deps. <paramref name="excludeAppId"/> is the
    /// AppId of the app being compiled — excluded to avoid AL0275 self-reference errors.
    /// </summary>
    public static void SetPackageCacheFallback(Guid excludeAppId)
    {
        lock (_refSync)
        {
            _usePackageCacheFallback = true;
            _packageCacheFallbackExcludeId = excludeAppId;
            _refLoader = null;
            _refSpecs = null;
        }
    }

    // The service-tier artifacts dir mirrors BcAssembler.ServiceTierDir.
    // It contains the DLLs (XmlTextReader etc.) that BC DotNet interop resolves against.
    internal static readonly string DefaultServiceTierDir =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".local/share/al-runner/artifacts/27.5.46862.48827");

    private static NavDotNet.IDotNetResolverFactory GetOrCreateDotNetFactory()
    {
        lock (_dotNetSync)
        {
            if (_dotNetResolverFactory != null)
                return _dotNetResolverFactory;

            // Probing paths: service-tier artifacts dir (BC's own .NET deps
            // such as Aspose, Azure SDK, BouncyCastle etc. shipped alongside Ncl.dll)
            // plus the runtime's own base-class library location.
            var probingPaths = new List<string>();
            if (Directory.Exists(DefaultServiceTierDir))
                probingPaths.Add(DefaultServiceTierDir);
            // BCL: where mscorlib / System.* lives (net10.0 shared framework).
            var runtimeDir = System.Runtime.InteropServices.RuntimeEnvironment.GetRuntimeDirectory();
            if (Directory.Exists(runtimeDir))
                probingPaths.Add(runtimeDir);

            var locator = new NavDotNet.AssemblyLocator(probingPaths);
            _dotNetResolverFactory = new NavDotNet.DotNetResolverFactory(locator);
            return _dotNetResolverFactory;
        }
    }

    /// <summary>
    /// Set by Program.cs after DependencyResolver runs. The set of .app paths
    /// passed here is exactly what DependencyLoader will load at runtime, so
    /// compile-time symbols == runtime types by construction.
    /// </summary>
    public static void SetResolvedDeps(
        IReadOnlyList<(AppManifest Manifest, string AppPath)> deps,
        IReadOnlyList<string> packageCacheDirs)
    {
        lock (_refSync)
        {
            _resolvedDeps = deps;
            _packageCacheDirs = packageCacheDirs;
            _refLoader = null;
            _refSpecs = null;
        }
    }

    private static (NavCA.ISymbolReferenceLoader? Loader, NavCA.SymbolReferenceSpecification[] Specs)
        GetSharedReferences(IEnumerable<string> bundleAlpackagesDirs)
    {
        lock (_refSync)
        {
            if (_refLoader != null && _refSpecs != null)
                return (_refLoader, _refSpecs);

            // Reference loader scans whole package cache dirs (so it can resolve
            // anything BC's emitter walks), but Specs is the explicit list of
            // resolved deps — no allow-list, no drift between compile and runtime.
            var packageDirs = bundleAlpackagesDirs
                .Where(Directory.Exists)
                .Distinct()
                .ToList();
            if (_packageCacheDirs != null)
                packageDirs.AddRange(_packageCacheDirs.Where(Directory.Exists));
            else
                packageDirs.AddRange(ResolveSymbolDirs());
            packageDirs = packageDirs.Distinct().ToList();
            if (packageDirs.Count == 0) return (null, Array.Empty<NavCA.SymbolReferenceSpecification>());

            _refLoader = NavSymRef.ReferenceLoaderFactory.CreateReferenceLoader(packageDirs);

            if (_resolvedDeps != null && _resolvedDeps.Count > 0)
            {
                _refSpecs = _resolvedDeps
                    .Select(d => new NavCA.SymbolReferenceSpecification(
                        publisher: d.Manifest.Publisher,
                        name: d.Manifest.Name,
                        version: d.Manifest.Version,
                        exact: false,
                        appId: d.Manifest.AppId,
                        isPropagated: false,
                        alternateIds: ImmutableArray<Guid>.Empty))
                    .ToArray();
            }
            else if (_usePackageCacheFallback)
            {
                // --precompile path only: no explicit dep list (e.g. Customizations.app with
                // empty <Dependencies/>). Fall back to adding every discoverable .app in the
                // package cache dirs as a symbol reference — exactly what `alc --packagecachepath`
                // does implicitly. Covers apps that declare no manifest deps but still compile
                // against BaseApp/System Application via namespace-qualified `using` statements.
                // _packageCacheFallbackExcludeId: skip the app being compiled (avoids AL0275).
                var byId = new Dictionary<Guid, NavCA.SymbolReferenceSpecification>();
                foreach (var dir in packageDirs)
                {
                    if (!Directory.Exists(dir)) continue;
                    foreach (var appFile in Directory.EnumerateFiles(dir, "*.app", SearchOption.AllDirectories))
                    {
                        var m = AppLoader.ReadManifest(appFile);
                        if (m == null || byId.ContainsKey(m.AppId)) continue;
                        if (_packageCacheFallbackExcludeId != default
                            && m.AppId == _packageCacheFallbackExcludeId) continue;
                        byId[m.AppId] = new NavCA.SymbolReferenceSpecification(
                            publisher: m.Publisher,
                            name: m.Name,
                            version: m.Version,
                            exact: false,
                            appId: m.AppId,
                            isPropagated: false,
                            alternateIds: ImmutableArray<Guid>.Empty);
                    }
                }
                _refSpecs = byId.Values.ToArray();
            }
            else
            {
                _refSpecs = Array.Empty<NavCA.SymbolReferenceSpecification>();
            }
            return (_refLoader, _refSpecs);
        }
    }

    public BcEmitOutput Emit(IEnumerable<string> alFolders, string moduleName)
    {
        var dirs = alFolders.Where(Directory.Exists).Distinct().ToList();
        if (dirs.Count == 0)
            throw new InvalidOperationException("BcCompiler.Emit: no source folders");

        var alFiles = dirs
            .SelectMany(d => Directory.EnumerateFiles(d, "*.al", SearchOption.AllDirectories))
            .Distinct()
            .ToList();
        if (alFiles.Count == 0)
            throw new InvalidOperationException(
                $"BcCompiler.Emit: no .al files under {string.Join(", ", dirs)}");

        // Preprocessor symbols: CLEANSCHEMA1..25. v1 computes per-source max from
        // any #pragma the AL files set (Program.cs:1454-1462); we use the static
        // 1..25 set v2 was already shipping — sufficient for the tests/ corpus.
        var parseOpts = new NavCA.ParseOptions(
            runtimeVersion: null!,
            preprocessorSymbols: Enumerable.Range(1, 25).Select(n => $"CLEANSCHEMA{n}"),
            documentationMode: NavCA.DocumentationMode.None);

        var trees = new NavSyntax.SyntaxTree[alFiles.Count];
        Parallel.For(0, alFiles.Count, i =>
        {
            var src = File.ReadAllText(alFiles[i]);
            trees[i] = NavSyntax.SyntaxTree.ParseObjectText(
                src, path: alFiles[i], encoding: null!, parseOpts, default);
        });

        // CompilationOptions: identical to v1 (Program.cs:1548-1555).
        var compOpts = new NavCA.CompilationOptions(
            continueBuildOnError: true,
            target: NavCA.CompilationTarget.OnPrem,
            generateOptions:
                NavCA.CompilationGenerationOptions.Code |
                NavCA.CompilationGenerationOptions.Navigation);

        var appId = DeterministicGuid(moduleName);
        var compilation = NavCA.Compilation.Create(
            moduleName: moduleName,
            publisher: "AlRunnerV2",
            version: new Version(1, 0, 0, 0),
            appId: appId,
            syntaxTrees: trees,
            options: compOpts);

        // Suite-local .alpackages (rare in v2's corpus today, but cheap to honour).
        var bundleAlpackages = dirs
            .SelectMany(d => Directory.EnumerateDirectories(d, ".alpackages", SearchOption.AllDirectories))
            .Distinct();
        var (refLoader, specs) = GetSharedReferences(bundleAlpackages);
        if (refLoader != null)
        {
            compilation = compilation.WithReferenceLoader(refLoader);
            if (specs.Length > 0)
                compilation = compilation.AddReferences(specs);
        }

        // Attach a local DotNet resolver so AL `DotNet` variables resolve to real
        // .NET types. Without this the default NullDotNetResolverFactory leaves
        // NavTypeKind = None, causing Compilation.Emit to throw
        // UnexpectedValue(NavTypeKind.None) for every DotNet-using method.
        compilation = compilation.WithDotNetResolverFactory(GetOrCreateDotNetFactory());

        var outputter = new CaptureOutputter();
        Exception? caught = null;
        Microsoft.Dynamics.Nav.CodeAnalysis.Emit.EmitResult? emitResult = null;
        try
        {
            // Compilation.Emit returns an EmitResult with Success + Diagnostics. The
            // silent-zero failure mode (captured=0, no thrown exception) is when
            // EmitResult.Success=false because the internal Compile step caught
            // diagnostics rather than throwing. Capture the result so the diag
            // block can surface them — otherwise we have no signal at all.
            emitResult = compilation.Emit(NavCA.EmitOptions.Default, outputter);
        }
        catch (Exception ex) { caught = ex; }

        if (Environment.GetEnvironmentVariable("BCCOMPILER_DIAG") == "1")
        {
            Console.Error.WriteLine($"[BcCompiler-diag] module={moduleName} alFiles={alFiles.Count} addCalls={outputter.AddCalls} captured={outputter.Captured.Count} lastAdded={outputter.LastAddedName ?? "<none>"} caught={caught?.GetType().Name ?? "<none>"} emitSuccess={emitResult?.Success}");
            if (emitResult != null && !emitResult.Success)
            {
                var emitErrs = emitResult.Diagnostics
                    .Where(d => d.Severity == NavDiag.DiagnosticSeverity.Error)
                    .ToList();
                Console.Error.WriteLine($"  EmitResult.Diagnostics: {emitErrs.Count} error(s)");
                foreach (var d in emitErrs.Take(20))
                    Console.Error.WriteLine($"    emit[{d.Id}] @ {d.Location}: {d.GetMessage().Split('\n', 2)[0]}");
                if (emitErrs.Count > 20)
                    Console.Error.WriteLine($"    ... and {emitErrs.Count - 20} more");
            }
            if (caught != null)
            {
                Console.Error.WriteLine($"  msg: {caught.Message.Split('\n', 2)[0]}");
                if (caught is AggregateException agg)
                {
                    var inners = agg.Flatten().InnerExceptions.ToList();
                    Console.Error.WriteLine($"  inner exceptions: {inners.Count}");
                    int verbose = Environment.GetEnvironmentVariable("BCCOMPILER_DIAG_VERBOSE") == "1" ? 50 : 5;
                    foreach (var inner in inners.Take(verbose))
                    {
                        // Group object+method extracted from the AggregateException.Message
                        // (each AL emit failure includes "Object:'X' Method:'Y'" in the
                        // AggregateException line for that inner — but the inner itself
                        // only carries the BC-internal NRE/InvalidOpEx). Print full inner
                        // message + stack to surface the actual BC emit code path.
                        Console.Error.WriteLine($"  inner[{inner.GetType().Name}]: {inner.Message}");
                        if (inner.StackTrace != null)
                        {
                            // Show the top BC-emitter frames so the failing CodeGenerator
                            // method is visible (Microsoft.Dynamics.Nav.CodeAnalysis.* path).
                            var topFrames = inner.StackTrace
                                .Split('\n')
                                .Where(l => l.Contains("Microsoft.Dynamics.Nav.CodeAnalysis"))
                                .Take(8);
                            foreach (var frame in topFrames)
                                Console.Error.WriteLine($"    {frame.Trim()}");
                        }
                        if (inner.InnerException != null)
                            Console.Error.WriteLine($"    causedby[{inner.InnerException.GetType().Name}]: {inner.InnerException.Message.Split('\n', 2)[0]}");
                    }
                    // The outer AggregateException.Message has "Object:'X' Method:'Y'"
                    // for each inner. Extract and print as a clean per-method list.
                    Console.Error.WriteLine("  failing methods (extracted from AggregateException msg):");
                    var rx = new System.Text.RegularExpressions.Regex(
                        @"Object:'([^']+)' Method:'([^']+)' \(([^)]+)\)");
                    foreach (System.Text.RegularExpressions.Match m in rx.Matches(caught.Message))
                        Console.Error.WriteLine($"    {m.Groups[1].Value} :: {m.Groups[2].Value}  [{m.Groups[3].Value}]");
                }
                else if (Environment.GetEnvironmentVariable("BCCOMPILER_DIAG_VERBOSE") == "1")
                {
                    Console.Error.WriteLine($"  full: {caught}");
                }
            }
            var declErrs = compilation.GetDeclarationDiagnostics()
                .Where(d => d.Severity == NavDiag.DiagnosticSeverity.Error).ToList();
            var parseErrs = trees.SelectMany(t => t.GetDiagnostics())
                .Where(d => d.Severity == NavDiag.DiagnosticSeverity.Error).ToList();
            Console.Error.WriteLine($"  declErrors={declErrs.Count} parseErrors={parseErrs.Count}");
            foreach (var d in parseErrs.Take(5))
                Console.Error.WriteLine($"    parse[{d.Id}] @ {d.Location}: {d.GetMessage().Split('\n', 2)[0]}");
            // AL0275 = ambiguous reference (the cross-suite conflict signal we care about).
            foreach (var d in declErrs.Where(d => d.Id == "AL0275"))
                Console.Error.WriteLine($"    AL0275 @ {d.Location}: {d.GetMessage().Split('\n', 2)[0]}");
            foreach (var d in declErrs.Where(d => d.Id != "AL0275").Take(10))
                Console.Error.WriteLine($"    {d.Id} @ {d.Location}: {d.GetMessage().Split('\n', 2)[0]}");
        }

        // Collect AL-level diagnostics for Program.cs to surface at the compile
        // boundary — formatted alc-style so they read like `alc.exe` output.
        var alDiags = new List<string>();
        var allParseErrs = trees
            .SelectMany(t => t.GetDiagnostics())
            .Where(d => d.Severity == NavDiag.DiagnosticSeverity.Error)
            .ToList();
        var allDeclErrs = compilation.GetDeclarationDiagnostics()
            .Where(d => d.Severity == NavDiag.DiagnosticSeverity.Error)
            .ToList();
        foreach (var d in allParseErrs)
            alDiags.Add($"{d.Location}: error {d.Id}: {d.GetMessage().Split('\n', 2)[0]}");
        foreach (var d in allDeclErrs)
            alDiags.Add($"{d.Location}: error {d.Id}: {d.GetMessage().Split('\n', 2)[0]}");
        if (emitResult != null && !emitResult.Success)
        {
            foreach (var d in emitResult.Diagnostics
                .Where(d => d.Severity == NavDiag.DiagnosticSeverity.Error))
                alDiags.Add($"{d.Location}: error {d.Id}: {d.GetMessage().Split('\n', 2)[0]}");
        }
        // When Compilation.Emit throws (BC's emitter crashed on a per-object bound
        // tree — e.g. an unresolved type reaching codegen), the AggregateException
        // message carries one "Object:'X' Method:'Y' (reason)" entry per failing
        // object. Surface them in the returned diagnostics so callers fail LOUDLY
        // with the failing object names by default — not only under BCCOMPILER_DIAG.
        // See loud-failures.md / runner issue #1620.
        if (caught != null)
        {
            var rx = new System.Text.RegularExpressions.Regex(
                @"Object:'([^']+)' Method:'([^']+)' \(([^)]+)\)");
            var matches = rx.Matches(caught.Message);
            foreach (System.Text.RegularExpressions.Match m in matches)
                alDiags.Add($"emit-crash: {m.Groups[1].Value} :: {m.Groups[2].Value} — {m.Groups[3].Value}");
            if (matches.Count == 0)
                // No per-object breakdown in the message — surface the raw emit failure.
                alDiags.Add($"emit-crash: {caught.GetType().Name}: {caught.Message.Split('\n', 2)[0]}");
        }

        return new BcEmitOutput(outputter.Captured, alDiags);
    }

    /// <summary>
    /// Resolve symbol-package search dirs. Scans (in order):
    ///   1. `~/.local/share/al-runner/symbols/<bc-ver>/` — the v2-curated set
    ///      (Application + Base + System Application).
    ///   2. `~/.bcartifacts.cache/sandbox/<bc-ver>/w1/Extensions/` — full set
    ///      from the BC W1 artifact (Business Foundation, Library Assert,
    ///      Test Runner, Library Variable Storage, etc.).
    ///   3. `~/.bcartifacts.cache/sandbox/<bc-ver>/platform/Applications/` —
    ///      platform Test Library apps.
    /// Picks the highest BC version found in each pool.
    /// </summary>
    private static IEnumerable<string> ResolveSymbolDirs()
    {
        var home = Environment.GetEnvironmentVariable("HOME");
        if (string.IsNullOrEmpty(home)) yield break;

        foreach (var rel in new[] { ".local/share/al-runner/symbols", ".bcartifacts.cache/sandbox" })
        {
            var root = Path.Combine(home, rel);
            if (!Directory.Exists(root)) continue;
            var bestVer = Directory.EnumerateDirectories(root)
                .Select(d => (Dir: d, Ver: System.Version.TryParse(Path.GetFileName(d), out var v) ? v : null))
                .Where(t => t.Ver != null)
                .OrderByDescending(t => t.Ver)
                .Select(t => t.Dir)
                .FirstOrDefault();
            if (bestVer == null) continue;

            if (rel.StartsWith(".local"))
            {
                yield return bestVer;
            }
            else
            {
                // bcartifacts.cache/sandbox/<ver>/{w1/Extensions, platform/Applications}
                var w1Ext = Path.Combine(bestVer, "w1", "Extensions");
                if (Directory.Exists(w1Ext)) yield return w1Ext;
                var platApps = Path.Combine(bestVer, "platform", "Applications");
                if (Directory.Exists(platApps)) yield return platApps;
            }
        }
    }

    private static Guid DeterministicGuid(string seed)
    {
        // Hash the seed and reuse the first 16 bytes as a GUID. Stable, no crypto.
        var bytes = System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(seed));
        var guidBytes = new byte[16];
        Array.Copy(bytes, guidBytes, 16);
        return new Guid(guidBytes);
    }

    /// <summary>
    /// CodeModuleOutputter override that accumulates UTF-8 C# bytes per AL object.
    /// Mirrors v1's CSharpCaptureOutputter (AlRunner/Program.cs:4516).
    /// </summary>
    private sealed class CaptureOutputter : NavEmit.CodeModuleOutputter
    {
        public List<EmittedSource> Captured { get; } = new();
        public string? LastAddedName { get; private set; }
        public int AddCalls { get; private set; }

        public CaptureOutputter() : base(NavCA.EmitOptions.Default) { }

        public override void InitializeModule(NavCA.IModuleSymbol moduleSymbol) { }

        public override void AddApplicationObject(
            NavCA.IApplicationObjectTypeSymbol symbol,
            byte[] code, string metadata, string debugCode)
        {
            AddCalls++;
            LastAddedName = symbol.Name;
            var src = System.Text.Encoding.UTF8.GetString(code);
            Captured.Add(new EmittedSource(symbol.Name, src));

            // Capture (id, name, options[], indexes[]) for AL enum types so the
            // runtime NCLEnumMetadata.Create(int) hook can return real
            // GetNames()/GetOrdinals() data instead of NCLOptionMetadata.Default
            // (which throws NavNCLNotSupportedOperationException). Enum
            // extensions also flow through here as IEnumExtensionTypeSymbol;
            // both expose Values via the IEnumBaseTypeSymbol interface.
            if (symbol is NavCA.IEnumBaseTypeSymbol enumSym)
            {
                var values = enumSym.Values;
                var options = new string[values.Length];
                var indexes = new int[values.Length];
                for (int i = 0; i < values.Length; i++)
                {
                    options[i] = values[i].Name ?? string.Empty;
                    indexes[i] = values[i].Ordinal;
                }
                AlEnumMetadataRegistry.Register(enumSym.Id, enumSym.Name, options, indexes);
            }
            if (Environment.GetEnvironmentVariable("BCCOMPILER_TRACE") == "1")
                Console.Error.WriteLine($"  emit[{AddCalls}]: {symbol.Name}");
            if (Environment.GetEnvironmentVariable("BCCOMPILER_DUMP_CS") == "1")
            {
                var dir = Path.Combine(Path.GetTempPath(), "bccompiler-dump");
                Directory.CreateDirectory(dir);
                var fname = string.Concat(symbol.Name.Select(c => char.IsLetterOrDigit(c) ? c : '_')) + ".cs";
                File.WriteAllText(Path.Combine(dir, fname), src);
            }
        }

        public override void AddProfileObject(
            NavCA.ISymbol symbol, byte[] code, string metadata, string debugCode) { }
        public override void AddNavigationObject(string content) { }
        public override void AddExternalBusinessEvent(string content) { }
        public override void AddMovedObjects(string content) { }
        public override void FinalizeModule() { }
        public override ImmutableArray<NavDiag.Diagnostic> GetDiagnostics()
            => ImmutableArray<NavDiag.Diagnostic>.Empty;
    }
}
