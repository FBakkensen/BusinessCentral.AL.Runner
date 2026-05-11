// ALDatabasePatches — loud out-of-scope throws for the ALDatabase static NRE cluster.
//
// Root cause (diagnosed 2026-05-11, worker 5faf7fb3):
//   NavCurrentThread.Session returns the skeleton NavSession built via
//   GetUninitializedObject. Three fields on that session are null:
//     • <Authenticator>k__BackingField   → ALSid / ALChangeUserPassword NREs
//     • <TableConnectionManager>k__BackingField → ALGetDefaultTableConnection /
//                                          ALRegisterTableConnection /
//                                          ALUnregisterTableConnection NREs
//     • tenant (private readonly)         → ALLastUsedRowVersion /
//                                          ALMinimumActiveRowVersion /
//                                          ALImportData NREs
//
// All 8 methods are permanently out of scope per docs/scope.md:
//   ALSid / ALChangeUserPassword      — auth-identity §3.8 (licensing)
//   ALGetDefaultTableConnection /
//   ALRegisterTableConnection /
//   ALUnregisterTableConnection       — external-table-connections §3.2
//   ALLastUsedRowVersion /
//   ALMinimumActiveRowVersion         — SQL-transactions §3.10 (transactions)
//   ALImportData                      — file-storage §3.4
//
// We replace them with RunnerScope.ThrowOutOfScope calls so test output names
// the exact BC API + reason instead of surfacing a raw NullReferenceException.
// Silent stubs (return "S-1-0-0", return 0, etc.) are forbidden by the
// loud-failures rule (.claude/rules/loud-failures.md).
//
// Note on NavBigInteger return type: ALLastUsedRowVersion / ALMinimumActiveRowVersion
// return NavBigInteger (a BC value type). The replacements declare `long` as the
// return type which is ABI-safe because we throw before the return is ever reached.
// Using `long` avoids the calling-convention mismatch that caused a segfault in the
// prior silent-stub attempt (stash@{0}) which tried to materialise NavBigInteger.Create(0L).
using System.Reflection;
using System.Runtime.CompilerServices;
using AlRunnerV2.Infrastructure;

namespace AlRunnerV2;

public static partial class BcRuntime
{
    // ── Replacements ────────────────────────────────────────────────────────────

    /// <summary>ALDatabase.ALSid(string userName) — permanently out of scope (auth-identity/licensing).</summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static string ALDatabase_ALSid(string userName)
    {
        RunnerScope.ThrowOutOfScope(
            "ALDatabase.ALSid",
            "user-identity resolution requires a live BC auth stack — permanently out of scope",
            "licensing");
        return default!; // unreachable; satisfies compiler
    }

    /// <summary>ALDatabase.ALChangeUserPassword(string, string) — permanently out of scope (auth-identity/licensing).</summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static void ALDatabase_ALChangeUserPassword(string userName, string newPassword)
    {
        RunnerScope.ThrowOutOfScope(
            "ALDatabase.ALChangeUserPassword",
            "password management requires a live BC auth stack — permanently out of scope",
            "licensing");
    }

    /// <summary>ALDatabase.ALGetDefaultTableConnection(TableConnectionType) — permanently out of scope (external-table-connections).</summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static string ALDatabase_ALGetDefaultTableConnection(object connectionType)
    {
        RunnerScope.ThrowOutOfScope(
            "ALDatabase.ALGetDefaultTableConnection",
            "external table connections require a live BC session manager — permanently out of scope",
            "external-http");
        return default!; // unreachable
    }

    /// <summary>ALDatabase.ALRegisterTableConnection(TableConnectionType, string, string) — permanently out of scope.</summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static void ALDatabase_ALRegisterTableConnection3(object connectionType, string connectionName, string connectionString)
    {
        RunnerScope.ThrowOutOfScope(
            "ALDatabase.ALRegisterTableConnection",
            "external table connections require a live BC session manager — permanently out of scope",
            "external-http");
    }

    /// <summary>ALDatabase.ALRegisterTableConnection(CompilationTarget, TableConnectionType, string, string) — permanently out of scope.</summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static void ALDatabase_ALRegisterTableConnection4(object compilationTarget, object connectionType, string connectionName, string connectionString)
    {
        RunnerScope.ThrowOutOfScope(
            "ALDatabase.ALRegisterTableConnection",
            "external table connections require a live BC session manager — permanently out of scope",
            "external-http");
    }

    /// <summary>ALDatabase.ALUnregisterTableConnection(TableConnectionType, string) — permanently out of scope.</summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static void ALDatabase_ALUnregisterTableConnection(object connectionType, string connectionName)
    {
        RunnerScope.ThrowOutOfScope(
            "ALDatabase.ALUnregisterTableConnection",
            "external table connections require a live BC session manager — permanently out of scope",
            "external-http");
    }

    /// <summary>
    /// ALDatabase.ALImportData(DataError, bool, ByRef&lt;NavText&gt;, bool, bool, NavRecord, bool) — permanently out of scope (file-storage).
    /// </summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static bool ALDatabase_ALImportData(
        int dataError, bool b, object? navText, bool d, bool e, object? record, bool g)
    {
        RunnerScope.ThrowOutOfScope(
            "ALDatabase.ALImportData",
            "AL data import requires service-tier SQL + blob storage — permanently out of scope",
            "file-storage");
        return default; // unreachable
    }

    /// <summary>
    /// ALDatabase.ALLastUsedRowVersion() — permanently out of scope (SQL-transactions).
    /// Return type declared as long (not NavBigInteger) to avoid calling-convention mismatch;
    /// we throw before the return value is ever materialised.
    /// </summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static long ALDatabase_ALLastUsedRowVersion()
    {
        RunnerScope.ThrowOutOfScope(
            "ALDatabase.ALLastUsedRowVersion",
            "row-version tracking requires a live BC SQL session — permanently out of scope",
            "transactions");
        return default; // unreachable
    }

    /// <summary>
    /// ALDatabase.ALMinimumActiveRowVersion() — permanently out of scope (SQL-transactions).
    /// Same ABI note as ALLastUsedRowVersion.
    /// </summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static long ALDatabase_ALMinimumActiveRowVersion()
    {
        RunnerScope.ThrowOutOfScope(
            "ALDatabase.ALMinimumActiveRowVersion",
            "row-version tracking requires a live BC SQL session — permanently out of scope",
            "transactions");
        return default; // unreachable
    }

    // ── Registration ─────────────────────────────────────────────────────────────

    internal static void RegisterALDatabasePatches(Assembly navNcl)
    {
        var alDbType = navNcl.GetType("Microsoft.Dynamics.Nav.Runtime.ALDatabase");
        if (alDbType == null)
        {
            Console.Error.WriteLine("[ALDatabase] Type not found in navNcl — skipping patches");
            return;
        }

        var pub = BindingFlags.Public | BindingFlags.Static;

        // ALSid(String) — returns string
        var alSid = alDbType.GetMethods(pub)
            .FirstOrDefault(m => m.Name == "ALSid"
                && m.GetParameters() is { Length: 1 } p0
                && p0[0].ParameterType == typeof(string));
        if (alSid != null)
            Hook(alSid,
                typeof(BcRuntime).GetMethod(nameof(ALDatabase_ALSid), pub)!,
                "ALDatabase.ALSid(String)");

        // ALChangeUserPassword(String, String) — void
        var changePwd = alDbType.GetMethods(pub)
            .FirstOrDefault(m => m.Name == "ALChangeUserPassword"
                && m.ReturnType == typeof(void)
                && m.GetParameters() is { Length: 2 } p1
                && p1[0].ParameterType == typeof(string));
        if (changePwd != null)
            Hook(changePwd,
                typeof(BcRuntime).GetMethod(nameof(ALDatabase_ALChangeUserPassword), pub)!,
                "ALDatabase.ALChangeUserPassword(String,String)");

        // ALGetDefaultTableConnection(TableConnectionType) — returns string
        var getDefault = alDbType.GetMethods(pub)
            .FirstOrDefault(m => m.Name == "ALGetDefaultTableConnection"
                && m.GetParameters().Length == 1
                && m.ReturnType == typeof(string));
        if (getDefault != null)
            Hook(getDefault,
                typeof(BcRuntime).GetMethod(nameof(ALDatabase_ALGetDefaultTableConnection), pub)!,
                "ALDatabase.ALGetDefaultTableConnection(TableConnectionType)");

        // ALRegisterTableConnection(TableConnectionType, String, String) — void, 3 args
        var register3 = alDbType.GetMethods(pub)
            .FirstOrDefault(m => m.Name == "ALRegisterTableConnection"
                && m.GetParameters().Length == 3
                && m.ReturnType == typeof(void));
        if (register3 != null)
            Hook(register3,
                typeof(BcRuntime).GetMethod(nameof(ALDatabase_ALRegisterTableConnection3), pub)!,
                "ALDatabase.ALRegisterTableConnection(TableConnectionType,String,String)");

        // ALRegisterTableConnection(CompilationTarget, TableConnectionType, String, String) — void, 4 args
        var register4 = alDbType.GetMethods(pub)
            .FirstOrDefault(m => m.Name == "ALRegisterTableConnection"
                && m.GetParameters().Length == 4
                && m.ReturnType == typeof(void));
        if (register4 != null)
            Hook(register4,
                typeof(BcRuntime).GetMethod(nameof(ALDatabase_ALRegisterTableConnection4), pub)!,
                "ALDatabase.ALRegisterTableConnection(CompilationTarget,TableConnectionType,String,String)");

        // ALUnregisterTableConnection(TableConnectionType, String) — void, 2 args
        var unregister = alDbType.GetMethods(pub)
            .FirstOrDefault(m => m.Name == "ALUnregisterTableConnection"
                && m.GetParameters().Length == 2
                && m.ReturnType == typeof(void));
        if (unregister != null)
            Hook(unregister,
                typeof(BcRuntime).GetMethod(nameof(ALDatabase_ALUnregisterTableConnection), pub)!,
                "ALDatabase.ALUnregisterTableConnection(TableConnectionType,String)");

        // ALImportData(DataError, bool, ByRef<NavText>, bool, bool, NavRecord, bool) — 7 args, returns bool
        var importData = alDbType.GetMethods(pub)
            .FirstOrDefault(m => m.Name == "ALImportData"
                && m.GetParameters().Length == 7
                && m.ReturnType == typeof(bool));
        if (importData != null)
            Hook(importData,
                typeof(BcRuntime).GetMethod(nameof(ALDatabase_ALImportData), pub)!,
                "ALDatabase.ALImportData(DataError,bool,ByRef<NavText>,bool,bool,NavRecord,bool)");

        // ALLastUsedRowVersion() — 0 args, returns NavBigInteger (hooked as long; throws before return)
        var lastUsed = alDbType.GetMethod("ALLastUsedRowVersion", pub, null, Type.EmptyTypes, null);
        if (lastUsed != null)
            Hook(lastUsed,
                typeof(BcRuntime).GetMethod(nameof(ALDatabase_ALLastUsedRowVersion), pub)!,
                "ALDatabase.ALLastUsedRowVersion()");

        // ALMinimumActiveRowVersion() — 0 args, returns NavBigInteger (hooked as long; throws before return)
        var minActive = alDbType.GetMethod("ALMinimumActiveRowVersion", pub, null, Type.EmptyTypes, null);
        if (minActive != null)
            Hook(minActive,
                typeof(BcRuntime).GetMethod(nameof(ALDatabase_ALMinimumActiveRowVersion), pub)!,
                "ALDatabase.ALMinimumActiveRowVersion()");
    }
}
