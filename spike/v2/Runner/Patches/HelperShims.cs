// BcRuntime.Helpers — generic NoOp / ReturnX shims that JMP-hooks redirect to.
//
// Each helper matches the receiver+args slot count of the call sites it replaces:
//   * For an instance method, the static replacement takes one extra leading object
//     parameter for the receiver (`this`). e.g. an instance void method with two
//     reference args needs a NoOp3.
//   * For value-returning helpers (Return*), the return type's CLR slot must match
//     the original — bool→bool, ValueTask→ValueTask, etc.
//
// All helpers are `[MethodImpl(NoInlining)]` because the JIT must produce a real
// callable function pointer for JmpHook to patch.
using System.Reflection;
using System.Runtime.CompilerServices;

namespace AlRunnerV2;

public static partial class BcRuntime
{
    [MethodImpl(MethodImplOptions.NoInlining)] public static void NoOp_0Args() { }
    [MethodImpl(MethodImplOptions.NoInlining)] public static void NoOp_OneArg(object? a) { }
    [MethodImpl(MethodImplOptions.NoInlining)] public static void NoOp2(object? a, object? b) { }
    [MethodImpl(MethodImplOptions.NoInlining)] public static void NoOp3(object? a, object? b, object? c) { }
    [MethodImpl(MethodImplOptions.NoInlining)] public static void NoOp4(object? a, object? b, object? c, object? d) { }
    [MethodImpl(MethodImplOptions.NoInlining)] public static void NoOp5(object? a, object? b, object? c, object? d, object? e) { }

    [MethodImpl(MethodImplOptions.NoInlining)] public static bool ReturnFalse_0Args() => false;
    [MethodImpl(MethodImplOptions.NoInlining)] public static bool ReturnFalse_1Arg(object? a) => false;
    [MethodImpl(MethodImplOptions.NoInlining)] public static bool ReturnFalse_2Args(object? a, object? b) => false;

    [MethodImpl(MethodImplOptions.NoInlining)] public static System.Threading.Tasks.ValueTask ReturnValueTask2(object? a, object? b) => default;
    [MethodImpl(MethodImplOptions.NoInlining)] public static System.Threading.Tasks.ValueTask ReturnValueTask3(object? a, object? b, object? c) => default;
    [MethodImpl(MethodImplOptions.NoInlining)] public static System.Threading.Tasks.ValueTask ReturnValueTask4(object? a, object? b, object? c, object? d) => default;
    [MethodImpl(MethodImplOptions.NoInlining)] public static System.Threading.Tasks.ValueTask ReturnValueTask5(object? a, object? b, object? c, object? d, object? e) => default;

    [MethodImpl(MethodImplOptions.NoInlining)] public static object? ReturnNull_OneArg(object a) => null;
    [MethodImpl(MethodImplOptions.NoInlining)] public static int ReturnZero_OneArg(object? a) => 0;

    /// <summary>
    /// Replacement for <c>NavNotification.ALSend(DataError)</c> and
    /// <c>NavNotification.ALRecall(DataError)</c>. The real body NREs at
    /// <c>session.Diagnostics.SendTraceTag(...)</c> (Diagnostics is null on the
    /// skeleton session) and even past that calls <c>SendLocalNotification</c> /
    /// <c>SendGlobalNotification</c> which require the notification dispatch layer
    /// (handlers, AddIn host) that the runner does not have.
    ///
    /// Faithful behavior preserved: <c>NotificationInfo.Id</c> is populated with
    /// a fresh Guid if empty (mirrors the real ALSend body). Returns true to
    /// mean "operation completed". Tests that observe a <c>[SendNotificationHandler]</c>
    /// being invoked will still fail (visibly, at the AL assertion) — that
    /// dispatch layer is out of scope and tracked separately.
    /// </summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static bool NavNotification_ALSendOrRecall(object self, object errorLevel)
    {
        try
        {
            var notifInfoProp = self.GetType().GetProperty("NotificationInfo",
                BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            var info = notifInfoProp?.GetValue(self);
            if (info != null)
            {
                var idProp = info.GetType().GetProperty("Id");
                if (idProp != null && idProp.CanRead && idProp.CanWrite)
                {
                    var current = (System.Guid)(idProp.GetValue(info) ?? System.Guid.Empty);
                    if (current == System.Guid.Empty)
                        idProp.SetValue(info, System.Guid.NewGuid());
                }
            }
        }
        catch { /* best-effort Id population */ }
        return true;
    }

    /// <summary>
    /// Replacement for <c>ALSystemOperatingSystem.GetUrlCore</c>. The real body reaches
    /// into <c>ALSession.ALCurrentClientType</c>, <c>NavEnvironment.Instance.Tenants</c>,
    /// and <c>NavCurrentThread.Session.Tenant.Id</c> — all of which NRE on the skeleton
    /// session. Returns a stub URL so AL tests that only verify non-empty result pass.
    /// Real URL generation is out of scope (requires a service-tier endpoint manager).
    /// NavClientType / NavObjectType are int-backed enums; declared here as int to match
    /// the native ABI slot layout JmpHook patches.
    /// </summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static string ALSystemOperatingSystem_GetUrlCore(
        int clientType, string company, int objectType, int objectId,
        object record, bool useFilter, string layout)
        => "https://stub.example.com/";
    [MethodImpl(MethodImplOptions.NoInlining)] public static object? GetSkeletonCompanyReplacement(object self) => _skeletonCompany;

    /// <summary>
    /// Replacement for the static NCL <c>ALCompanyProperty.ALDisplayName()</c>. The real body
    /// reads from a NavRecord on table 2000000006 which the skeleton runtime can't serve
    /// (system-table DataAccess gap). Returns the stub display name "My Company" — observably
    /// equivalent to BC running with a Company row whose Display Name field is empty (the
    /// `GetCompanyDisplayNameDefaulted` fallback path). Faithful per docs/scope.md §2.
    /// </summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static string ALCompanyProperty_ALDisplayName() => "My Company";

    /// <summary>
    /// Replacement for <c>NavSystemCodeunitGlobalTriggers.GetTriggersOnTable(int tableId)</c>.
    /// Returns <c>Triggers.None</c> (= 0) for every table. The real body invokes a system
    /// codeunit via NavSystemCodeunit.Invoke, whose internal scope-machinery state is
    /// uninitialized on our skeleton factory and NREs. Headless runner has no AL global
    /// triggers (no codeunit subscribes to [EventSubscriber(GlobalTriggers,…)]), so
    /// Triggers.None is observably equivalent. Faithful per docs/scope.md §2.
    /// Return type is the NCL <c>Triggers</c> enum (int-backed, Flags). The JIT-emitted
    /// callers receive an int in RAX which they treat as the enum value.
    /// </summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static int NavSystemCodeunitGlobalTriggers_GetTriggersOnTable(object self, int tableId) => 0;

    /// <summary>
    /// Replacement for <c>NavSession.get_GlobalLanguage()</c>. Real body reads
    /// <c>cultureSettings.LCID</c>, but that struct field is zero-initialized on the
    /// GetUninitializedObject-built skeleton session. With IsOpen now seeded true, callers
    /// such as <c>RuntimeLanguage</c> read GlobalLanguage and pass it to
    /// <c>CultureInfo.GetCultureInfo(0)</c> which throws. Return 1033 (en-US) — the same
    /// value <c>NavEnvironment.DefaultLanguage</c> returns and what the pre-IsOpen-seeded code
    /// path was already using as fallback.
    /// </summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static int NavSession_GlobalLanguage_1033(object self) => 1033;

    [MethodImpl(MethodImplOptions.NoInlining)] public static bool ReturnFalse_3Args(object? a, object? b, object? c) => false;
    [MethodImpl(MethodImplOptions.NoInlining)] public static bool ReturnFalse2(object? a, object? b) => false;

    /// <summary>
    /// Diagnostic helper used for the RecordImplementation.IsOpen hook — logs the call
    /// site so we can trace which patched receiver is being asked. Always returns true
    /// (record is open) because by the time the test harness asks, we want the read path
    /// to proceed against TempTableDataProvider rather than throw NotOpened.
    /// </summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static bool ReturnTrue(object? a)
    {
        Console.Error.WriteLine($"[ReturnTrue] IsOpen hook fired for {a?.GetType().Name}");
        return true;
    }
}
