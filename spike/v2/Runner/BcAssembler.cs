// BcAssembler — Roslyn-compiles emitted C# against real BC DLLs.
//
// Pre-compile passes:
//   1. ApplyPolyfillRedirects — string substitutions routing AL-compiler-emitted
//      references for APIs that don't exist on the real service-tier DLLs to
//      small in-process polyfill shims (defined inline as PolyfillSource).
//   2. CallSiteArgWrap — fixes the residual call-site ByRef gap BC's emitter
//      doesn't cover (e.g. `dict.ALGet(K, fieldOfHandleT)` → wraps the field arg
//      as `new ByRef<T>(() => expr, v => expr = v)`). BC's emitter handles
//      parameter-declaration ByRef wraps natively at codeanalysis.cs:342854 —
//      no syntax rewriter needed for those.
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
            .ToList();
        // Inject helpers for runtime-API mismatches between alc-emit and the
        // service-tier DLLs. PolyfillRedirects above route callers here.
        trees.Add(CSharpSyntaxTree.ParseText(PolyfillSource, path: "_polyfill.cs"));

        var refs = ReferencePaths().Select(p => MetadataReference.CreateFromFile(p)).ToList();

        // Fill BC's call-site ByRef gap. Runs a throwaway compile to find CS1503
        // 'cannot convert T to ByRef<T>' errors and rewrites only those args.
        trees = CallSiteArgWrap.Apply(trees, refs).ToList();

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
        // The runner's own assembly — polyfill shims call back into AlRunnerV2.BcRuntime
        // helpers (e.g. NCLEnumMetadata_CreateByIdAlAware) so AL emit-time captured
        // metadata is reachable from compiled-AL call sites.
        var runnerDll = typeof(BcAssembler).Assembly.Location;
        if (!string.IsNullOrEmpty(runnerDll) && File.Exists(runnerDll))
            yield return runnerDll;
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
        // NavSession.Sleep — real body NREs via session state on the skeleton runtime.
        // In-scope (§3.9): inline-execution model, no parallel sessions — Sleep is a no-op delay.
        // The shim sleeps the current thread by `duration` ms (clamped to >=0).
        ("NavSession.Sleep(", "global::AlRunnerV2Shim.NavRuntimeHelpersShim.NavSession_Sleep("),
        // ALSession.ALIsSessionActive — real body chases session state that doesn't exist.
        // Faithful in-scope answer (§3.9): the runner runs sessions inline + synchronously,
        // so any session id is "no longer active" by the time the caller asks. Return false.
        ("ALSession.ALIsSessionActive(", "global::AlRunnerV2Shim.NavRuntimeHelpersShim.ALSession_ALIsSessionActive("),
        // ALSession.ALStartSession — real body schedules an async session via NavCurrentThread/
        // Diagnostics which both NRE on the skeleton. Faithful in-scope replacement (§3.9):
        // dispatch the target codeunit synchronously in-process, assign a fresh non-zero
        // session id, and return true. Missing codeunit → return false (DataError.TrapError
        // pathway). See BcRuntime.AlRunnerStartSession for the dispatch logic.
        ("ALSession.ALStartSession(", "global::AlRunnerV2Shim.NavRuntimeHelpersShim.ALSession_ALStartSession("),
        // NavForm.Run (static, non-modal) — OOS §3.11. BC emits the [Obsolete] sync wrapper
        // NavForm.Run(...) (not RunAsync) for Page.Run calls. The real body calls
        // RunAsync().AsTask().GetAwaiter().GetResult() which NREs deep in NavForm/NCLMetaForm
        // because the skeleton has no live session.  JmpHook.Apply() cannot intercept this
        // because the JIT resolves the call from freshly compiled AL code to a different
        // address than what the hook patches (R2R vs JIT code layout mismatch on .NET 8).
        // Source-level redirect is the reliable alternative: "NavForm.Run(" cannot be a
        // substring of "NavForm.RunModal(" so there is no false-positive risk.
        ("NavForm.Run(", "global::AlRunnerV2Shim.NavRuntimeHelpersShim.NavForm_Run("),
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
        // skeleton session.  Forward to AlRunnerV2.BcRuntime.NCLEnumMetadata_CreateByIdAlAware
        // which returns a real NCLOptionMetadata subclass populated with the AL enum's
        // (names[], ordinals[]) so GetNames()/GetOrdinals() work; falls back to
        // NCLOptionMetadata.Default for system / dependency enums whose metadata isn't
        // captured at AL emit time.
        public static Microsoft.Dynamics.Nav.Runtime.NCLOptionMetadata NCLEnumMetadataCreate(int id)
            => global::AlRunnerV2.BcRuntime.NCLEnumMetadata_CreateByIdAlAware(id);

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

        // ───────────────────────────────────────────────────────────────────────
        // NavSession.Sleep — in-scope (§3.9). Inline execution model: a Sleep
        // simply pauses the current thread by `duration` ms (clamped to >= 0).
        // The real body chases skeleton-null session state and NREs.
        public static void NavSession_Sleep(int duration)
        {
            if (duration <= 0) return;
            try { System.Threading.Thread.Sleep(duration); } catch { /* ignore */ }
        }

        // ───────────────────────────────────────────────────────────────────────
        // ALSession.ALIsSessionActive — in-scope (§3.9). Inline-synchronous
        // dispatch means any session id is already completed by the time the
        // caller observes it. Faithful answer for both overloads: false.
        public static bool ALSession_ALIsSessionActive(int sessionId) => false;
        public static bool ALSession_ALIsSessionActive(
            Microsoft.Dynamics.Nav.Runtime.NavSession session, int sessionId) => false;

        // ───────────────────────────────────────────────────────────────────────
        // ALSession.ALStartSession — in-scope (§3.9). Dispatch the target
        // codeunit synchronously, assign a fresh positive session id, return true.
        // Missing codeunit (or any execution error under DataError.TrapError) → false.
        // All overloads route through the central BcRuntime helper.
        public static bool ALSession_ALStartSession(
            Microsoft.Dynamics.Nav.Types.DataError errorLevel,
            Microsoft.Dynamics.Nav.Runtime.ByRef<int> sessionId,
            int objectId)
            => global::AlRunnerV2.BcRuntime.AlRunnerStartSession(
                errorLevel, sessionId, objectId, null, null);

        public static bool ALSession_ALStartSession(
            Microsoft.Dynamics.Nav.Types.DataError errorLevel,
            Microsoft.Dynamics.Nav.Runtime.ByRef<int> sessionId,
            int objectId,
            string companyName)
            => global::AlRunnerV2.BcRuntime.AlRunnerStartSession(
                errorLevel, sessionId, objectId, companyName, null);

        public static bool ALSession_ALStartSession(
            Microsoft.Dynamics.Nav.Types.DataError errorLevel,
            Microsoft.Dynamics.Nav.Runtime.ByRef<int> sessionId,
            int objectId,
            Microsoft.Dynamics.Nav.Runtime.NavDuration timeout)
            => global::AlRunnerV2.BcRuntime.AlRunnerStartSession(
                errorLevel, sessionId, objectId, null, null);

        public static bool ALSession_ALStartSession(
            Microsoft.Dynamics.Nav.Types.DataError errorLevel,
            Microsoft.Dynamics.Nav.Runtime.ByRef<int> sessionId,
            int objectId,
            Microsoft.Dynamics.Nav.Runtime.NavDuration timeout,
            string companyName)
            => global::AlRunnerV2.BcRuntime.AlRunnerStartSession(
                errorLevel, sessionId, objectId, companyName, null);

        public static bool ALSession_ALStartSession(
            Microsoft.Dynamics.Nav.Types.DataError errorLevel,
            Microsoft.Dynamics.Nav.Runtime.ByRef<int> sessionId,
            int objectId,
            Microsoft.Dynamics.Nav.Runtime.NavDuration timeout,
            string companyName,
            Microsoft.Dynamics.Nav.Runtime.NavRecord record)
            => global::AlRunnerV2.BcRuntime.AlRunnerStartSession(
                errorLevel, sessionId, objectId, companyName, record);

        public static bool ALSession_ALStartSession(
            Microsoft.Dynamics.Nav.Types.DataError errorLevel,
            Microsoft.Dynamics.Nav.Runtime.ByRef<int> sessionId,
            int objectId,
            string companyName,
            Microsoft.Dynamics.Nav.Runtime.NavRecord record)
            => global::AlRunnerV2.BcRuntime.AlRunnerStartSession(
                errorLevel, sessionId, objectId, companyName, record);

        public static bool ALSession_ALStartSession(
            Microsoft.Dynamics.Nav.Types.DataError errorLevel,
            Microsoft.Dynamics.Nav.Runtime.ByRef<int> sessionId,
            int objectId,
            string companyName,
            Microsoft.Dynamics.Nav.Runtime.NavRecord record,
            Microsoft.Dynamics.Nav.Runtime.NavDuration timeout)
            => global::AlRunnerV2.BcRuntime.AlRunnerStartSession(
                errorLevel, sessionId, objectId, companyName, record);

        // ───────────────────────────────────────────────────────────────────────
        // NavForm.Run (static, non-modal) — OOS §3.11 #ui.
        // BC emits NavForm.Run(...) (the [Obsolete] sync wrapper around RunAsync)
        // for all Page.Run call sites. JmpHook.Apply cannot reliably intercept
        // these on .NET 8 R2R (code-layout mismatch); source-level redirect is safe.
        // All overloads throw RunnerOutOfScopeException when OosHooksActive (i.e.
        // inside a test run); calls during BC SA init pass through harmlessly.
        public static void NavForm_Run(int formId)
        {
            if (global::AlRunnerV2.BcRuntime.OosHooksActive)
                global::AlRunnerV2.Infrastructure.RunnerScope.ThrowOutOfScope(""NavForm.RunAsync"", ""non-modal-ui"", ""ui"");
        }
        public static void NavForm_Run(int formId, Microsoft.Dynamics.Nav.Runtime.NavRecord record)
        {
            if (global::AlRunnerV2.BcRuntime.OosHooksActive)
                global::AlRunnerV2.Infrastructure.RunnerScope.ThrowOutOfScope(""NavForm.RunAsync"", ""non-modal-ui"", ""ui"");
        }
        public static void NavForm_Run(int formId, Microsoft.Dynamics.Nav.Runtime.NavRecord record, int fieldNo)
        {
            if (global::AlRunnerV2.BcRuntime.OosHooksActive)
                global::AlRunnerV2.Infrastructure.RunnerScope.ThrowOutOfScope(""NavForm.RunAsync"", ""non-modal-ui"", ""ui"");
        }
        public static void NavForm_Run(string fullName, Microsoft.Dynamics.Nav.Runtime.NavRecord record)
        {
            if (global::AlRunnerV2.BcRuntime.OosHooksActive)
                global::AlRunnerV2.Infrastructure.RunnerScope.ThrowOutOfScope(""NavForm.RunAsync"", ""non-modal-ui"", ""ui"");
        }
        public static void NavForm_Run(string fullName, Microsoft.Dynamics.Nav.Runtime.NavRecord record, int fieldNo)
        {
            if (global::AlRunnerV2.BcRuntime.OosHooksActive)
                global::AlRunnerV2.Infrastructure.RunnerScope.ThrowOutOfScope(""NavForm.RunAsync"", ""non-modal-ui"", ""ui"");
        }
    }
}
";
}
