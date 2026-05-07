// RecordWritePatches — replacements for the NavRecord write/find path.
//
// NavRecord.InsertAsync, RecordImplementation.InternalFindRecordWithoutCheckingValuesAsync —
// the original bodies dispatch through trigger/event/extension and permission-event
// telemetry that NREs on the skeleton session. We bypass those and call the underlying
// dataAccess directly. The TempTableDataProvider hooked up by RecordPatches.cs handles
// actual storage.
using System.Reflection;
using System.Runtime.CompilerServices;

namespace AlRunnerV2;

public static partial class BcRuntime
{
    /// <summary>
    /// Replacement for RecordImplementation.InternalFindRecordWithoutCheckingValuesAsync —
    /// thin passthrough that hits dataAccess.TryGetByPrimaryKeyAsync and bypasses the original
    /// body's permission-event/diagnostic args evaluation, which NREs through
    /// Session.CurrentMethodScope.ApplicationObject (null on the skeleton root scope) when the
    /// requested record is not found.
    /// </summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static System.Threading.Tasks.ValueTask<bool> RecordImpl_InternalFindRecordWithoutCheckingValuesAsync(
        object self,
        Microsoft.Dynamics.Nav.Types.DataError errorLevel,
        object request,
        bool useRecord,
        bool calcAutoCalcFields)
    {
        try
        {
            var dataAccess = _fRecordImplementationDataAccess?.GetValue(self);
            if (dataAccess == null || _mDataAccessTryGetByPrimaryKeyAsync == null)
                return new System.Threading.Tasks.ValueTask<bool>(false);
            var taskObj = _mDataAccessTryGetByPrimaryKeyAsync.Invoke(dataAccess, new[] { request });
            if (taskObj == null) return new System.Threading.Tasks.ValueTask<bool>(false);

            // taskObj is ValueTask<MutableRecordBufferResult<bool>> — block via .AsTask().Result.
            var asTaskMi = taskObj.GetType().GetMethod("AsTask");
            var asTask = asTaskMi?.Invoke(taskObj, null) as System.Threading.Tasks.Task;
            asTask?.Wait();
            var resultObj = asTask?.GetType().GetProperty("Result")?.GetValue(asTask);
            if (resultObj == null) return new System.Threading.Tasks.ValueTask<bool>(false);

            bool found = (bool)(_pMrbResultResult!.GetValue(resultObj) ?? false);
            if (found && useRecord)
            {
                var recBuffer = _pMrbResultRecordBuffer?.GetValue(resultObj);
                _fRecordImplementationMutableRecordBuffer?.SetValue(self, recBuffer);
            }
            if (found) return new System.Threading.Tasks.ValueTask<bool>(true);
            // Not-found path: TrapError → false; ThrowError → throw.
            if ((int)errorLevel == 1) return new System.Threading.Tasks.ValueTask<bool>(false);
            throw new InvalidOperationException("Record not found (skeleton find).");
        }
        catch (TargetInvocationException tie) when (tie.InnerException != null)
        {
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw(tie.InnerException);
            return default;
        }
    }

    /// <summary>
    /// Replacement for NavRecord.InsertAsync(DataError, bool, bool, bool).
    /// Bypasses all the trigger/event/extension dispatch that NREs on a skeleton session
    /// (NavCurrentThread.ResolveAppGroup, DataModificationListener, etc.) and delegates straight
    /// to recordImplementation.InsertRecordAsync, which goes through our hooked
    /// TempTableDataProvider DataAccessSource. W-8 will layer trigger dispatch back on top of
    /// this once permanent-table semantics are wired up.
    /// </summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static System.Threading.Tasks.ValueTask<bool> NavRecord_InsertAsync(
        Microsoft.Dynamics.Nav.Runtime.NavRecord self,
        Microsoft.Dynamics.Nav.Types.DataError errorLevel,
        bool runApplicationTrigger,
        bool runGlobalTrigger,
        bool insertWithSystemId)
    {
        try
        {
            var recImpl = _fNavRecordRecordImplementation?.GetValue(self);
            if (recImpl == null || _mRecordImplementationInsertRecordAsync == null)
                return new System.Threading.Tasks.ValueTask<bool>(false);
            var result = _mRecordImplementationInsertRecordAsync.Invoke(recImpl, new object?[] { errorLevel });
            if (result is System.Threading.Tasks.ValueTask<bool> vt) return vt;
            return new System.Threading.Tasks.ValueTask<bool>(true);
        }
        catch (TargetInvocationException tie) when (tie.InnerException != null)
        {
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw(tie.InnerException);
            return default; // unreachable
        }
    }
}
