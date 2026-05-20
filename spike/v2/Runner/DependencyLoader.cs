// DependencyLoader — turns a topo-sorted dep list into loaded Assemblies in
// the default ALC. Three-tier resolution per dep:
//
//   Tier 1: pre-compiled DLL at <bucketRoot>/.deps-bin/<Publisher>_<Name>_<Version>.dll
//   Tier 2: R2R `.app` (publishedartifacts/*.dll) — Microsoft-shipped binaries
//   Tier 3: source-only `.app` — extract src/*.al, run BcCompiler.Emit + BcAssembler.Compile
//
// All loads cache by AppId in a process-wide dictionary so cross-bucket sharing
// is free. A `Default.Resolving` handler is installed once at first use so the
// .NET runtime can re-resolve assemblies-by-name back to the byte[]-loaded
// instances (Assembly.Load(byte[]) puts the assembly in the default ALC, but
// reference resolution still goes by name).
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.Loader;

namespace AlRunnerV2;

public sealed class DependencyLoader
{
    private static readonly ConcurrentDictionary<Guid, Assembly> _cache = new();
    private static readonly ConcurrentDictionary<string, Assembly> _byName =
        new(StringComparer.OrdinalIgnoreCase);
    private static int _resolverInstalled;

    private readonly BcCompiler _compiler;
    private readonly BcAssembler _assembler;

    public DependencyLoader(BcCompiler compiler, BcAssembler assembler)
    {
        _compiler = compiler;
        _assembler = assembler;
        EnsureResolverInstalled();
    }

    public IReadOnlyList<Assembly> LoadAll(
        IReadOnlyList<(AppManifest Manifest, string AppPath)> ordered,
        string bucketRoot)
    {
        var list = new List<Assembly>();
        foreach (var (m, path) in ordered)
        {
            if (_cache.TryGetValue(m.AppId, out var existing))
            {
                list.Add(existing);
                continue;
            }
            var asm = LoadOne(m, path, bucketRoot);
            if (asm != null)
            {
                _cache[m.AppId] = asm;
                _byName[asm.GetName().Name ?? ""] = asm;
                list.Add(asm);
            }
        }
        return list;
    }

    private Assembly? LoadOne(AppManifest m, string appPath, string bucketRoot)
    {
        // Tier 1: precompiled DLL.
        var depsBin = Path.Combine(bucketRoot, ".deps-bin");
        var fileName = SanitizeFileName($"{m.Publisher}_{m.Name}_{m.Version}.dll");
        var precompiled = Path.Combine(depsBin, fileName);
        if (File.Exists(precompiled))
        {
            try
            {
                var bytes = File.ReadAllBytes(precompiled);
                return Assembly.Load(bytes);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[deps] tier-1 load failed for {m.Name}: {ex.Message}");
            }
        }

        // Tier 2: R2R extract. Microsoft ships large apps (notably Base
        // Application — 5 DLL chunks) as multiple `publishedartifacts/*.dll`
        // entries. Load every DLL; the chunk that defines the user-visible
        // app type (e.g. `Codeunit9015` for "Application System Constants")
        // is not necessarily the first one. We return the chunk whose
        // assembly name matches the manifest's app name when present, else
        // the first chunk; all chunks are registered in the by-name cache so
        // the Resolving handler can serve cross-chunk references.
        if (AppLoader.IsR2R(appPath))
        {
            var dlls = AppLoader.ExtractAllDlls(appPath);
            if (dlls.Count > 0)
            {
                Assembly? primary = null;
                int loaded = 0;
                foreach (var dll in dlls)
                {
                    try
                    {
                        var asm = Assembly.Load(dll);
                        var n = asm.GetName().Name ?? "";
                        _byName[n] = asm;
                        primary ??= asm;
                        loaded++;
                    }
                    catch (Exception ex)
                    {
                        Console.Error.WriteLine($"[deps] tier-2 R2R chunk load failed for {m.Name}: {ex.Message}");
                    }
                }
                if (loaded > 1)
                    Console.Error.WriteLine($"[deps] tier-2 R2R: {m.Name} loaded {loaded} DLL chunk(s)");
                return primary;
            }
        }

        // Tier 3: source-only compile-on-the-fly.
        var sw = Stopwatch.StartNew();
        var alSources = AppLoader.ExtractAl(appPath);
        if (alSources.Count == 0)
        {
            Console.Error.WriteLine(
                $"[deps] WARN: {m.Name} v{m.Version} contains neither R2R DLL nor src/*.al — skipping");
            return null;
        }

        var tempDir = Path.Combine(Path.GetTempPath(),
            "al-runner-deps", SanitizeFileName($"{m.Publisher}_{m.Name}_{m.Version}"));
        Directory.CreateDirectory(tempDir);
        // Clean previously emitted .al files so a stale one doesn't pollute the compile.
        foreach (var existing in Directory.EnumerateFiles(tempDir, "*.al"))
        {
            try { File.Delete(existing); } catch { }
        }
        foreach (var (name, src) in alSources)
        {
            var fileSafe = SanitizeFileName(name);
            File.WriteAllText(Path.Combine(tempDir, fileSafe), src);
        }

        IReadOnlyList<EmittedSource> emitted;
        try { emitted = _compiler.Emit(new[] { tempDir }, m.Name).Sources; }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[deps] compile-on-the-fly EMIT-FAIL: {m.Name}: {ex.Message.Split('\n')[0]}");
            return null;
        }
        if (emitted.Count == 0)
        {
            Console.Error.WriteLine(
                $"[deps] compile-on-the-fly EMIT-ZERO: {m.Name} v{m.Version} produced 0 sources " +
                $"(likely BC's silent zero-output sentinel — see HANDOFF.md §H)");
            return null;
        }

        var asmName = $"Dep_{SanitizeIdent(m.Publisher)}_{SanitizeIdent(m.Name)}_{m.Version.ToString().Replace('.', '_')}";
        var compile = _assembler.Compile(asmName, emitted);
        if (!compile.Success)
        {
            var first = compile.Errors.FirstOrDefault()?.Split('\n')[0];
            Console.Error.WriteLine($"[deps] compile-on-the-fly COMPILE-FAIL: {m.Name}: {first}");
            return null;
        }

        sw.Stop();
        Console.Error.WriteLine(
            $"[deps] compiled-on-the-fly: {m.Name} v{m.Version} ({sw.ElapsedMilliseconds}ms). " +
            $"For faster CI, run --precompile to snapshot.");
        try { return Assembly.Load(compile.AssemblyBytes!); }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[deps] tier-3 Assembly.Load failed for {m.Name}: {ex.Message}");
            return null;
        }
    }

    private static string SanitizeFileName(string s)
    {
        var bad = Path.GetInvalidFileNameChars().Concat(new[] { ' ', '/', '\\' }).ToArray();
        var sb = new System.Text.StringBuilder(s.Length);
        foreach (var ch in s)
            sb.Append(Array.IndexOf(bad, ch) >= 0 ? '_' : ch);
        return sb.ToString();
    }

    private static string SanitizeIdent(string s)
    {
        var sb = new System.Text.StringBuilder(s.Length);
        foreach (var ch in s)
            sb.Append(char.IsLetterOrDigit(ch) ? ch : '_');
        return sb.ToString();
    }

    /// <summary>
    /// Idempotent install of the default-ALC Resolving handler. Public so callers
    /// (e.g. Program.cs at startup) can install it before BcRuntime applies patches,
    /// in case a patch's reflection on a BC type triggers an assembly load for a
    /// transitively-referenced service-tier DLL that's not in the application bin.
    /// </summary>
    public static void EnsureResolverInstalled_Public() => EnsureResolverInstalled();

    private static void EnsureResolverInstalled()
    {
        if (Interlocked.Exchange(ref _resolverInstalled, 1) != 0) return;
        // BC service-tier artifact dir — same path BcRuntime/BcAssembler/Runner.csproj
        // resolve the 5 we project-reference (Types, Ncl, Common, Language, CodeAnalysis).
        // Microsoft.Dynamics.Nav.Ncl.dll transitively references ~24 BC DLLs, of which
        // we only project-reference 5; the rest sit in the artifact dir but aren't on
        // any probing path. When a generic instantiation or reflection call inside MS
        // R2R code reaches one (e.g. Microsoft.Dynamics.Nav.Core, .AL.Common, .Apps,
        // .TableProxyBuilder), it fails to load and the call NREs deep in MS code. The
        // probe below catches every Microsoft.Dynamics.Nav.* assembly request and serves
        // it from the artifact dir.
        var serviceTierPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".local/share/al-runner/artifacts/27.5.46862.48827");
        AssemblyLoadContext.Default.Resolving += (ctx, name) =>
        {
            if (name.Name == null) return null;
            if (_byName.TryGetValue(name.Name, out var asm))
                return asm;
            if (name.Name.StartsWith("Microsoft.Dynamics.Nav.", StringComparison.Ordinal))
            {
                var probe = Path.Combine(serviceTierPath, name.Name + ".dll");
                if (File.Exists(probe))
                    return ctx.LoadFromAssemblyPath(probe);
            }
            return null;
        };
    }

    /// <summary>
    /// Lookup helper for callers that want to access a loaded dep by name
    /// (e.g. when verifying that a compile-time symbol matches a runtime one).
    /// </summary>
    public static Assembly? TryGetByAppId(Guid appId)
        => _cache.TryGetValue(appId, out var asm) ? asm : null;
}
