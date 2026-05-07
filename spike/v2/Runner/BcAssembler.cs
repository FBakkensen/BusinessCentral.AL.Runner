// BcAssembler — Roslyn-compiles emitted C# against real BC DLLs.
//
// Two transformations are applied to the AL-emitter output before compile:
//   1. ApplyPolyfillRedirects — string substitutions that route AL-compiler-emitted
//      symbol references for APIs that don't exist on the real BC service-tier DLLs
//      to small in-process polyfill shims (defined inline below as PolyfillSource).
//   2. ByRefWrapRewriter — the ONE mechanical syntax transformation BC's own service
//      tier applies at extension install time but `--dump-csharp` doesn't include:
//      every parameter marked `[NavByReferenceAttribute] T` gets its type wrapped to
//      `ByRef<T>`, and the matching backing field's declared type is wrapped too.
//      Microsoft's pre-compiled DLLs prove this convention (8K+ ByRef<>, 0 NavByRef).
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using AlRunnerV2.Rewriters;

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
        var sourceList = sources.ToList();
        if (Environment.GetEnvironmentVariable("DUMP_CS") == "1")
            foreach (var s in sourceList) File.WriteAllText($"/tmp/gen_{s.Name}.cs", s.Code);
        var trees = sourceList
            .Select(s => CSharpSyntaxTree.ParseText(ApplyPolyfillRedirects(s.Code), path: s.Name + ".cs"))
            .Select(ByRefWrapRewriter.Rewrite)   // wrap [NavByReferenceAttribute] params in ByRef<T>
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
        // AL compiler 17.0.34 emits a 2-arg ConvertToDotNetFormatString(session, format) but
        // BC 27.5 only ships the 1-arg overload. Redirect to our shim that drops the session.
        ("ALCompiler.ConvertToDotNetFormatString(",
         "global::AlRunnerV2Shim.NavRuntimeHelpersShim.ConvertToDotNetFormatString("),
        // NCLEnumMetadata.Create(int) chains through NavGlobal.MetadataProvider which NREs on the
        // skeleton session.  After JIT tiering the JMP-hook on that method is bypassed, so we
        // redirect at source level.  Our shim returns NCLOptionMetadata.Default which preserves
        // ordinal arithmetic for any enum value that callers create with NavOption.Create.
        ("NCLEnumMetadata.Create(",
         "global::AlRunnerV2Shim.NavRuntimeHelpersShim.NCLEnumMetadataCreate("),
        // ALDebugger methods all throw NavObsoleteMethodException and have value-type params
        // (DataError enum) — redirect at source level to avoid JMP-hook ABI issues.
        ("ALDebugger.ALActivate(",     "global::AlRunnerV2Shim.NavRuntimeHelpersShim.ALDebugger_ALActivate("),
        ("ALDebugger.ALDeactivate(",   "global::AlRunnerV2Shim.NavRuntimeHelpersShim.ALDebugger_ALDeactivate("),
        ("ALDebugger.ALIsActive(",     "global::AlRunnerV2Shim.NavRuntimeHelpersShim.ALDebugger_ALIsActive("),
        ("ALDebugger.ALIsAttached(",   "global::AlRunnerV2Shim.NavRuntimeHelpersShim.ALDebugger_ALIsAttached("),
        ("ALDebugger.CheckPermissionToDebug(", "global::AlRunnerV2Shim.NavRuntimeHelpersShim.ALDebugger_CheckPermissionToDebug("),
        // ALSession.ALStopSession sync wrappers NRE via session.Diagnostics; return false.
        ("ALSession.ALStopSession(",   "global::AlRunnerV2Shim.NavRuntimeHelpersShim.ALSession_StopSession("),
        // ALSession.ALGetExecutionContext / ALGetModuleExecutionContext NRE via session properties.
        // Return ExecutionContext.Normal (0) which is the expected value in a headless runner.
        ("ALSession.ALGetExecutionContext(",         "global::AlRunnerV2Shim.NavRuntimeHelpersShim.ALGetExecutionContext("),
        ("ALSession.ALGetModuleExecutionContext(",   "global::AlRunnerV2Shim.NavRuntimeHelpersShim.ALGetModuleExecutionContext("),
        // ALSession.ALSendTraceTag NREs via session.Diagnostics; telemetry is a no-op here.
        ("ALSession.ALSendTraceTag(",  "global::AlRunnerV2Shim.NavRuntimeHelpersShim.ALSession_SendTraceTag("),
        // ALSessionInformation static properties NRE via session.SqlDebuggingStatisticsCheckPoint.
        // Return 0 — SQL counters are 0 in a skeleton/non-database run.
        ("ALSessionInformation.ALSqlRowsRead",         "global::AlRunnerV2Shim.NavRuntimeHelpersShim.ALSqlRowsRead"),
        ("ALSessionInformation.ALSqlStatementsExecuted", "global::AlRunnerV2Shim.NavRuntimeHelpersShim.ALSqlStatementsExecuted"),
        // ALSystemErrorHandling.ALGetLastErrorCallStack NREs via NavCurrentThread.Session; return "".
        ("ALSystemErrorHandling.ALGetLastErrorCallStack", "global::AlRunnerV2Shim.NavRuntimeHelpersShim.ALGetLastErrorCallStack"),
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

        // AL compiler 17.0.34 emits ConvertToDotNetFormatString(session, format) but BC 27.5 only
        // ships the 1-arg overload. The 2-arg shim drops the session (not used by the 1-arg impl).
        public static Microsoft.Dynamics.Nav.Runtime.NavOemText ConvertToDotNetFormatString(
            object session, string format)
            => Microsoft.Dynamics.Nav.Runtime.ALCompiler.ConvertToDotNetFormatString(format);

        // Forward 1-arg calls that went through the redirect unchanged.
        public static Microsoft.Dynamics.Nav.Runtime.NavOemText ConvertToDotNetFormatString(
            string format)
            => Microsoft.Dynamics.Nav.Runtime.ALCompiler.ConvertToDotNetFormatString(format);

        // NCLEnumMetadata.Create(int) chains through NavGlobal.MetadataProvider which NREs on the
        // skeleton session.  Return NCLOptionMetadata.Default instead — ordinal arithmetic and
        // NavOption.Create both work correctly with the default stub metadata.
        public static Microsoft.Dynamics.Nav.Runtime.NCLOptionMetadata NCLEnumMetadataCreate(int id)
            => Microsoft.Dynamics.Nav.Runtime.NCLOptionMetadata.Default;

        // ALDebugger — all classic-debugger methods are obsolete stubs that throw.
        // Shims return false / no-op so Debugger.IsActive, .Activate, .Deactivate work in tests.
        public static bool ALDebugger_ALActivate(Microsoft.Dynamics.Nav.Types.DataError e) => false;
        public static bool ALDebugger_ALActivate() => false;
        public static bool ALDebugger_ALDeactivate(Microsoft.Dynamics.Nav.Types.DataError e) => false;
        public static bool ALDebugger_ALDeactivate() => false;
        public static bool ALDebugger_ALIsActive() => false;
        public static bool ALDebugger_ALIsAttached() => false;
        public static void ALDebugger_CheckPermissionToDebug() { }

        // ALSession.ALStopSession — sync wrappers call ALStopSessionAsync which NREs.
        public static bool ALSession_StopSession(Microsoft.Dynamics.Nav.Types.DataError e, int sessionId) => false;
        public static bool ALSession_StopSession(Microsoft.Dynamics.Nav.Types.DataError e, int sessionId, string comment) => false;

        // ALSession.ALGetExecutionContext / ALGetModuleExecutionContext.
        // Return Normal (0) — headless runner has no install/upgrade execution context.
        public static Microsoft.Dynamics.Nav.Types.ExecutionContext ALGetExecutionContext(object session)
            => Microsoft.Dynamics.Nav.Types.ExecutionContext.Normal;
        public static Microsoft.Dynamics.Nav.Types.ExecutionContext ALGetModuleExecutionContext(object session)
            => Microsoft.Dynamics.Nav.Types.ExecutionContext.Normal;
        public static Microsoft.Dynamics.Nav.Types.ExecutionContext ALGetModuleExecutionContext(object session, int id)
            => Microsoft.Dynamics.Nav.Types.ExecutionContext.Normal;
        public static Microsoft.Dynamics.Nav.Types.ExecutionContext ALGetModuleExecutionContext(object session, System.Guid id)
            => Microsoft.Dynamics.Nav.Types.ExecutionContext.Normal;

        // ALSession.ALSendTraceTag — telemetry no-op; accepts all parameter overloads.
        public static void ALSession_SendTraceTag(object session, string tag, string category, object verbosity, string message) { }
        public static void ALSession_SendTraceTag(object session, string tag, string category, object verbosity, string message, object dataClass) { }

        // ALSessionInformation — SQL counters are 0 in a headless/skeleton run.
        public static long ALSqlRowsRead => 0L;
        public static long ALSqlStatementsExecuted => 0L;

        // ALSystemErrorHandling — GetLastErrorCallStack uses NavCurrentThread.Session which is
        // null in our skeleton run.  Return empty string (no error occurred, no callstack).
        public static string ALGetLastErrorCallStack => string.Empty;
    }
}
";
}
