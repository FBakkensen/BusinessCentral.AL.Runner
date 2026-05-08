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

        // Tier 2: R2R extract.
        if (AppLoader.IsR2R(appPath))
        {
            var dll = AppLoader.ExtractDll(appPath);
            if (dll != null)
            {
                try { return Assembly.Load(dll); }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"[deps] tier-2 R2R load failed for {m.Name}: {ex.Message}");
                    return null;
                }
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
        try { emitted = _compiler.Emit(new[] { tempDir }, m.Name); }
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

    private static void EnsureResolverInstalled()
    {
        if (Interlocked.Exchange(ref _resolverInstalled, 1) != 0) return;
        AssemblyLoadContext.Default.Resolving += (ctx, name) =>
        {
            if (name.Name != null && _byName.TryGetValue(name.Name, out var asm))
                return asm;
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
