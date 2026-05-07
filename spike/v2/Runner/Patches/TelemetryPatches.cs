// TelemetryPatches — replacements for telemetry / diagnostic / dialog APIs that NRE
// because the underlying EventSource singletons and DiagnosticsResolver instances are
// uninitialised in headless mode.
//
// We either return null/no-op (telemetry is not needed) or convert AL errors into
// throwable exceptions so asserterror still traps them.
using System.Reflection;
using System.Runtime.CompilerServices;

namespace AlRunnerV2;

public static partial class BcRuntime
{
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static object? NavServerEventSource_get_Log() => _skeletonNavServerEventSource;

    /// <summary>
    /// No-op for NavServerEventSource.WritePermissionUncheckedEvent — instance method with 10 args
    /// (4 strings + 6 ints). The replacement is static so first arg is the receiver.
    /// </summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static void NavServerEventSource_WritePermissionUncheckedEvent(
        object self,
        string serverInstanceName, string navTenantId, string environmentName, string environmentType,
        int sessionId, int objectType, int objectId, int permissions, int callingObjectType, int callingObjectId)
    { }

    /// <summary>
    /// Replacement for CallStackElement.TryGetSourceInfo(out ObjectSourceInfo sourceInfo).
    /// The real implementation chains through NavGlobal.NCLMetadata which NREs on a skeleton session.
    /// Returns false (no source info) and zeros the out-param so callers see a null/default sourceInfo.
    /// The out-param is passed as an IntPtr to the raw managed pointer location.
    /// </summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static unsafe bool CallStackElement_TryGetSourceInfo(object? self, IntPtr sourceInfoOutPtr)
    {
        if (sourceInfoOutPtr != IntPtr.Zero)
            *(IntPtr*)sourceInfoOutPtr.ToPointer() = IntPtr.Zero;
        return false;
    }

    /// <summary>
    /// Replacement for NavDialog.ALError(Guid automationId, NavALErrorInfo errorInfo).
    /// On Linux x86-64, Guid (16 bytes) occupies two register slots, so the actual
    /// parameters received are: a = Guid-lo64, b = Guid-hi64, errorInfo = NavALErrorInfo.
    /// The real body NREs through diagnostics infrastructure; we construct NavNCLDialogException
    /// directly from the error message so asserterror traps it correctly.
    /// Note: the 3-arg overload ALError(NavSession, Guid, NavALErrorInfo) is hooked to NoOp4
    /// (session + two Guid halves + errorInfo) since it is only called from ALLogInternalError
    /// which we already suppress.
    /// </summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static void NavDialogALError_NavALErrorInfo(object? a, object? b, object? errorInfo)
    {
        string msg = string.Empty;
        if (errorInfo != null)
        {
            try
            {
                var ei = (Microsoft.Dynamics.Nav.Runtime.NavALErrorInfo)errorInfo;
                if (ei.ALErrorType == Microsoft.Dynamics.Nav.Types.ALErrorType.Internal)
                    return;
                msg = ei.ALMessage ?? string.Empty;
            }
            catch
            {
                try
                {
                    var msgProp = errorInfo.GetType().GetProperty("ALMessage", BindingFlags.Public | BindingFlags.Instance);
                    msg = msgProp?.GetValue(errorInfo) as string ?? string.Empty;
                }
                catch { }
            }
        }
        if (_navNCLDialogExceptionType != null)
        {
            var exc = Activator.CreateInstance(_navNCLDialogExceptionType, msg) as Exception
                ?? new Exception(msg);
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw(exc);
        }
        throw new Exception(msg.Length > 0 ? msg : "AL error");
    }
}
