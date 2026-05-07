// BcAssembler — Roslyn-compiles emitted C# (unmodified) against real BC DLLs.
// Replaces the AL Runner's RoslynRewriter step entirely.
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace AlRunnerV2;

public sealed record CompileResult(byte[]? AssemblyBytes, IReadOnlyList<string> Errors)
{
    public bool Success => AssemblyBytes != null;
}

public sealed class BcAssembler
{
    public string ServiceTierDir { get; init; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".local/share/al-runner/artifacts/27.5.46862.48827");

    public CompileResult Compile(string assemblyName, IEnumerable<EmittedSource> sources)
    {
        var trees = sources.Select(s => CSharpSyntaxTree.ParseText(
                                            ApplyPolyfillRedirects(s.Code), path: s.Name + ".cs"))
                           .ToList();
        // Inject the missing helpers the AL compiler 17.0.34 emits but the BC 27.5
        // service tier doesn't expose. Source patches above redirect callers.
        trees.Add(CSharpSyntaxTree.ParseText(PolyfillSource, path: "_polyfill.cs"));

        var refs = ReferencePaths().Select(p => MetadataReference.CreateFromFile(p)).ToList();

        var compilation = CSharpCompilation.Create(
            assemblyName,
            trees,
            refs,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary,
                allowUnsafe: true,
                concurrentBuild: true,
                optimizationLevel: OptimizationLevel.Release));

        using var ms = new MemoryStream();
        var emit = compilation.Emit(ms);
        if (!emit.Success)
        {
            var errs = emit.Diagnostics
                .Where(d => d.Severity == DiagnosticSeverity.Error)
                .Select(d => d.ToString())
                .ToList();
            return new CompileResult(null, errs);
        }
        return new CompileResult(ms.ToArray(), Array.Empty<string>());
    }

    private IEnumerable<string> ReferencePaths()
    {
        // Real BC service-tier DLLs
        foreach (var n in new[] { "Microsoft.Dynamics.Nav.Types", "Microsoft.Dynamics.Nav.Ncl",
                                  "Microsoft.Dynamics.Nav.Common", "Microsoft.Dynamics.Nav.Language",
                                  "Microsoft.Dynamics.Nav.Types.Report", "Microsoft.Dynamics.Nav.Types.Report.Base",
                                  "Microsoft.Dynamics.Nav.Types.Report.Runtime", "Microsoft.Dynamics.Nav.Core" })
        {
            var p = Path.Combine(ServiceTierDir, n + ".dll");
            if (File.Exists(p)) yield return p;
        }
        // .NET shared framework — System.Runtime, mscorlib equivalents
        var tpa = (string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!;
        foreach (var p in tpa.Split(Path.PathSeparator))
        {
            var name = Path.GetFileNameWithoutExtension(p);
            if (name.StartsWith("System.") || name == "mscorlib" || name == "netstandard")
                yield return p;
        }
    }

    // Source patches applied to emitted C# before parsing. Each entry redirects a
    // missing-in-runtime symbol to our polyfill. Pure string replace for now —
    // upgrade to a Roslyn rewriter only if false-positive matches show up.
    private static readonly (string from, string to)[] _polyfillRedirects = new[]
    {
        ("NavRuntimeHelpers.ThrowIfWrongArgumentCount",
         "global::AlRunnerV2Shim.NavRuntimeHelpersShim.ThrowIfWrongArgumentCount"),
    };

    private static string ApplyPolyfillRedirects(string code)
    {
        foreach (var (from, to) in _polyfillRedirects)
            code = code.Replace(from, to);
        return code;
    }

    private const string PolyfillSource = @"
namespace AlRunnerV2Shim
{
    public static class NavRuntimeHelpersShim
    {
        public static void ThrowIfWrongArgumentCount(int expected, object[] args, string memberName)
        {
            if (args is null || args.Length != expected)
                throw new System.ArgumentException(
                    $""Expected {expected} argument(s) for '{memberName}', got {(args?.Length ?? 0)}"");
        }
    }
}
";
}
