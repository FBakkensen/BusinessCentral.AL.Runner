// RecordPatches.TransactionSnapshot — AL's write-transaction rollback.
//
// WHAT AL PROMISES
//   An AL error rolls the database back to the last COMMIT. The test framework establishes
//   a commit point at the start of every test method, and AL's Commit() establishes another.
//   BC implements the rollback half in its own code: NavMethodScope.AssertError catches the
//   error and calls session.Rollback().
//
//   That is observable, and the corpus pins it:
//     * TestTriggerRollback.OnModify_Throws_ValueNotModified — an explicit Commit() before
//       the asserterror, precisely so the Insert above it survives the rollback.
//     * TestTriggerRollback.OnDelete_Throws_RecordStillExists — Insert() (no error, no
//       Commit()) then `asserterror Delete()` whose OnDelete trigger throws: the Insert must
//       survive. The LATER statement here is ITSELF a write attempt against the SAME table
//       — see the "always re-baseline on the next write" fix below.
//     * TestTriggerRollback.OnInsert_Throws_RecordNotInserted — asserterror wraps the Insert
//       call itself, and OnInsert's own trigger throws; real BC (measured) keeps the row.
//       See ForceDurableFailedInserts below, and AlRunner#2142/#2167 for the open question
//       of WHY real BC keeps it — decompiling NavRecord.InsertAsync (Ncl.dll) shows OnInsert
//       runs BEFORE recordImplementation.InsertRecordAsync (the only call that physically
//       writes anything) with no surrounding try/catch, identically for RunTrigger=true and
//       false, in BOTH this runner (which reuses that method unmodified — see
//       RecordWritePatches.cs's own note that the trigger-bypass replacement is NOT
//       installed) and, presumably, real BC. That DISPROVES this file's earlier claim that
//       real BC runs OnInsert after the physical write — there is no ordering discrepancy to
//       fix, because the ordering was never the actual explanation. The mechanism that lets
//       BC's Count() see a row that was never handed to recordImplementation.InsertRecordAsync
//       remains unidentified; ForceDurableFailedInserts reproduces the OBSERVED outcome
//       without claiming to model how real BC gets there. Narrowly scoped to the exact
//       asserterror'd statement doing the inserting (see BeginAssertErrorScope), so it does
//       not reach into an unrelated, already-returned Insert() from an earlier statement —
//       but a genuinely different unwind path (an OnInsert failure that propagates past
//       asserterror, e.g. into Codeunit.Run()'s own trap) is NOT covered by this mechanism,
//       since ForceDurableFailedInserts is only ever called from the asserterror catch
//       handler. If BC's real mechanism turns out to apply on those paths too, this fix is
//       incomplete there — flagged in #2167 rather than silently assumed away.
//
//   AlRunner#2142 also originally cited TestScopeIsolationContracts.Test04 and
//   TestTransactionContracts.Error_After_Insert_Before_Commit_RecordPersists as examples of
//   the same bug — both assert the OPPOSITE of TestAssertErrorRollback.al (Codeunit 60943)
//   Record_Insert_UnrelatedAssertError_NoCommit_RowIsRolledBack for what looks like the
//   identical shape (an uncommitted, untriggered Insert() then a later, unrelated Error()).
//   Real BC passes all three (confirmed against CI run 33273501078, BC 27.5 and 28.3) — they
//   are NOT contradictory, so whatever distinguishes them is a real BC mechanism this runner
//   does not yet reproduce, not a corpus defect to invert. See #2167 for what's been ruled
//   in/out so far. SETTLED by measurement on a real BC 28.4 service tier (AlRunner#2402,
//   2026-09-02): the shape run ALONE — DeleteAll, Insert(1), `asserterror Error(...)`,
//   Get(1) — leaves the row ABSENT, i.e. the uncommitted Insert IS rolled back, no read
//   cache involved; the same shape run AFTER a test method that inserted row 1 and
//   Commit()ed it finds row 1 PRESENT with the COMMITTED values, not the values this
//   test's Insert wrote. So what keeps the row in Test04 and
//   Error_After_Insert_Before_Commit_RecordPersists is the earlier test method's committed
//   row: this test's own uncommitted DeleteAll removed it, the rollback puts it back. That
//   is plain commit-point rollback, and the lazy first-write baseline below reproduces it
//   (the every-write refresh of #2170 could not: the Insert's refresh had already captured
//   the post-DeleteAll, empty table). Both former known-gap entries are gone.
//
//   Without any of this the runner either never rolled anything back (silently wrong for a
//   test that checks the table afterwards) or rolled back to the wrong boundary.
//
//   BC's own APIs establish additional, NESTED commit points too — a real
//   Session.EndTransaction(commit: true) (or EndTransactionWorldAndTransaction) inside a BC
//   API is exactly as durable, from AL's point of view, as an explicit Commit() statement.
//   See NoteTransactionEnd below (AlRunner#1946).
//
// HOW IT IS DONE HERE
//   The runner's tables are BC TempTableDataProviders held in _dataAccessByTable, and
//   RecordPatches.InstallBaseline already knows how to copy rows out of them and put rows
//   back. A commit point is the same snapshot, kept separately; a rollback restores it.
//
//   The snapshot is taken on the FIRST write to a table since the last commit point (see
//   ALDatabasePatches.NoteRecordWrite / NoteRecordInsertWrite, prepended to every NavRecord
//   AL write entry) and then kept: that is the table's state at the commit point, which is
//   what a rollback must restore. #2170 instead refreshed it on EVERY write, which made the
//   baseline "the state before the most recent write" — a statement that wrote the same
//   table twice and then failed had only its LAST write undone (AlRunner#2402; real BC
//   undoes every write since the commit point). The one thing the every-write refresh was
//   buying is kept in a narrower form: the first write to a table INSIDE the statement
//   asserterror is currently wrapping re-baselines that table once (BeginAssertErrorScope /
//   _rebaselinedInScope). That is what keeps OnDelete_Throws_RecordStillExists green —
//   Insert() (outside the scope) establishes a baseline, `asserterror Delete(true)` whose
//   trigger throws before any physical delete re-baselines to include the Insert's row, so
//   the rollback is a no-op instead of erasing the earlier, uncommitted Insert. A second
//   write to the same table inside the same statement never moves the baseline again.
//   TestAssertErrorRollback's "unrelated Error() rolls back everything since commit" cases
//   are untouched: the wrapped statement writes nothing, so nothing is re-baselined.
//
//   Insert() gets one further, narrower exception: measured real BC keeps an Insert() row
//   durable even when THAT SAME Insert() statement's own OnInsert trigger throws
//   (TestTriggerRollback.OnInsert_Throws_RecordNotInserted). This is NOT because OnInsert
//   runs after the physical write on real BC — decompiling NavRecord.InsertAsync in Ncl.dll
//   shows OnInsert runs BEFORE recordImplementation.InsertRecordAsync (the only call that
//   physically writes anything) with no surrounding try/catch, and this runner reuses that
//   exact method unmodified (RecordWritePatches.cs's own comment confirms the bypass
//   replacement that would have skipped trigger dispatch is NOT installed). So in this
//   runner, exactly as in the decompiled real-BC code path, a throwing OnInsert means
//   InsertRecordAsync is never reached and the row is never written — RollbackToCommitPoint
//   has nothing to undo, because there was nothing to undo. The row still needs to end up in
//   the table to match real BC's measured outcome, and ForceDurableFailedInserts (below)
//   does that directly, reusing the record's own live field buffer. It acts ONLY on an
//   Insert() that never reached the physical write: NoteInsertLanded, prepended to
//   RecordImplementation.InsertRecordAsync, retires the attempt the moment the row is
//   handed to the provider, so a completed Insert() is an ordinary write the rollback
//   undoes (AlRunner#2402 — measured, two completed Insert()s then Error() inside one
//   asserterror statement leave zero rows on real BC). But WHY real BC's
//   Count() sees a row that its own InsertRecordAsync-equivalent was never called for is not
//   established here; see the WHAT AL PROMISES section and #2167. Scoped to Insert()
//   attempts made during the statement asserterror is CURRENTLY wrapping
//   (BeginAssertErrorScope/EndAssertErrorScope) — an Insert() from an EARLIER,
//   already-returned statement must stay fully subject to the general "unrelated error rolls
//   back everything since commit" rule above (that's the Codeunit 60943 case), so it must
//   NOT be in scope for a later, different asserterror's force-durable step. That scoping
//   also means an OnInsert failure that unwinds past asserterror entirely (never reaching
//   this catch handler) is NOT compensated for — if real BC's mechanism turns out to apply
//   there too, this fix does not cover it.
//
//   Restore is IN PLACE — the provider object is kept and its trees are rebuilt — because
//   unlike the codeunit-boundary install-baseline restore, a rollback happens mid-test with
//   AL record variables still holding references to the DataAccess they were opened on.
using System.Collections;
using System.Reflection;
using Microsoft.Dynamics.Nav.Runtime;

namespace AlRunner.Patches;

public static partial class RecordPatches
{
    // Per-table row images captured since the last commit point, keyed by the
    // (DataAccessSource, tableId) pair _dataAccessByTable itself is keyed by.
    private static readonly Dictionary<(object Source, int TableId), BaselineTable> _txCommitPoint = new();

    // Insert() attempts noted (ALDatabasePatches.NoteRecordInsertWrite) during the CURRENTLY
    // executing asserterror-wrapped statement — see ForceDurableFailedInserts. Scoped with a
    // [ThreadStatic] stack, pushed/cleared in BeginAssertErrorScope and restored in
    // EndAssertErrorScope, so an Insert() from an EARLIER, already-returned statement is
    // never mistaken for one made by the CURRENT statement (that distinction is exactly what
    // keeps this fix from reaching into TestAssertErrorRollback's "unrelated error" cases,
    // which must keep rolling back an uncommitted Insert normally).
    [ThreadStatic]
    private static List<object>? _pendingInsertsInScope;

    [ThreadStatic]
    private static Stack<List<object>>? _pendingInsertsScopeStack;

    // Tables whose commit-point snapshot has already been re-baselined ONCE inside the
    // CURRENTLY executing asserterror-wrapped statement — see NoteTransactionWrite. Scoped
    // exactly like _pendingInsertsInScope. Outside any asserterror scope this is null and a
    // table is snapshotted only on its first write since the last commit point.
    [ThreadStatic]
    private static HashSet<(object Source, int TableId)>? _rebaselinedInScope;

    [ThreadStatic]
    private static Stack<HashSet<(object Source, int TableId)>?>? _rebaselinedScopeStack;

    /// <summary>
    /// Establish a commit point: everything written up to now survives a later rollback.
    /// Called at each test-method boundary and from AL's <c>Commit()</c>.
    /// </summary>
    public static void MarkCommitPoint() => _txCommitPoint.Clear();

    /// <summary>
    /// Start tracking Insert() attempts for the statement asserterror is about to invoke,
    /// pushing aside whatever the OUTER scope (if any — nested asserterror) had accumulated.
    /// Called from MethodScopePatches.NavMethodScope_AssertError immediately before invoking
    /// the wrapped Action. Does NOT touch <see cref="_txCommitPoint"/> — the general
    /// roll-back-to-last-commit-point rule is unscoped by design (Codeunit 60943).
    /// </summary>
    public static void BeginAssertErrorScope()
    {
        (_pendingInsertsScopeStack ??= new()).Push(_pendingInsertsInScope ?? new List<object>());
        _pendingInsertsInScope = new List<object>();
        (_rebaselinedScopeStack ??= new()).Push(_rebaselinedInScope);
        _rebaselinedInScope = new HashSet<(object, int)>();
    }

    /// <summary>
    /// Restore the outer scope's pending-inserts list pushed aside by
    /// <see cref="BeginAssertErrorScope"/>. Called from
    /// MethodScopePatches.NavMethodScope_AssertError in a finally around the wrapped Action,
    /// after <see cref="ForceDurableFailedInserts"/> (if the statement threw) has already
    /// consumed this scope's own list.
    /// </summary>
    public static void EndAssertErrorScope()
    {
        var stack = _pendingInsertsScopeStack;
        _pendingInsertsInScope = (stack != null && stack.Count > 0) ? stack.Pop() : null;
        var rebaselined = _rebaselinedScopeStack;
        _rebaselinedInScope = (rebaselined != null && rebaselined.Count > 0) ? rebaselined.Pop() : null;
    }

    /// <summary>
    /// Note an Insert() attempted during the currently-executing asserterror-wrapped
    /// statement (or, outside any asserterror, harmlessly — nothing reads the list except
    /// <see cref="ForceDurableFailedInserts"/>, called only from the asserterror catch path).
    /// Called from ALDatabasePatches.NoteRecordInsertWrite.
    /// </summary>
    internal static void NoteInsertAttempt(object? record)
    {
        if (record == null) return;
        (_pendingInsertsInScope ??= new()).Add(record);
    }

    /// <summary>
    /// The physical write of an Insert() is about to happen (AlRunner#2402). Prepended by
    /// Cecil to <c>RecordImplementation.InsertRecordAsync</c> — the ONLY call in
    /// NavRecord.InsertAsync that hands the row to the data provider, reached only after
    /// OnInsert (and every other pre-write step) has returned normally. An Insert() that
    /// gets here is an ordinary, completed write: the commit-point rollback undoes it like
    /// any Modify/Delete, and it must NOT be re-inserted by
    /// <see cref="ForceDurableFailedInserts"/>, whose whole subject is the Insert() whose
    /// OWN trigger threw before this point (measured on real BC: two completed Insert()s
    /// followed by Error() inside one asserterror statement leave zero rows). So the pending
    /// entry for this record is retired here; only attempts that never reached the
    /// physical write stay pending. Matched by the NavRecord's own recordImplementation
    /// (the argument is that implementation, `this` of the prepended method), newest
    /// attempt first so an Insert() nested inside another's OnInsert retires its own entry.
    /// </summary>
    public static void NoteInsertLanded(object? recordImplementation)
    {
        var pending = _pendingInsertsInScope;
        if (recordImplementation == null || pending == null || pending.Count == 0) return;
        _fNavRecordRecordImplementation ??= typeof(NavRecord).GetField(
            "recordImplementation", BindingFlags.NonPublic | BindingFlags.Instance);
        if (_fNavRecordRecordImplementation == null) return;
        for (int i = pending.Count - 1; i >= 0; i--)
        {
            object? impl;
            try { impl = _fNavRecordRecordImplementation.GetValue(pending[i]); }
            catch { continue; }
            if (ReferenceEquals(impl, recordImplementation))
            {
                pending.RemoveAt(i);
                return;
            }
        }
    }

    /// <summary>
    /// Test-only observability hook for the current scope's pending-insert count — the
    /// BeginAssertErrorScope/EndAssertErrorScope stack has no other externally-visible
    /// effect until a real NavRecord reaches ForceDurableFailedInserts, which needs a full
    /// BC skeleton. Lets AlRunner.Tests pin the scoping (nested Begin/End isolates an inner
    /// statement's insert attempts from an outer one) with plain dummy objects instead.
    /// </summary>
    internal static int PendingInsertsCountForTests => _pendingInsertsInScope?.Count ?? 0;

    /// <summary>
    /// Prepended to SessionTransactionExtensions.EndTransaction(NavSession, bool commit) and
    /// .EndTransactionWorldAndTransaction(NavSession, bool commit) — see AlRunner#1946.
    ///
    /// BC's own APIs run their internal work inside an explicit nested transaction. The
    /// static overload of <c>NavXmlPort.Import</c> is one — decompiled, unmodified Ncl body:
    /// <c>Session.BeginTransaction(); ...; finally { Session.EndTransaction(commit); }</c> for
    /// <c>DataError.ThrowError</c>, or <c>Session.BeginTransactionWorldAndTransaction(); ...;
    /// finally { Session.EndTransactionWorldAndTransaction(commit); }</c> for
    /// <c>DataError.TrapError</c>. AL's compiler picks <c>TrapError</c> whenever the call's
    /// boolean result is captured into a variable — e.g. <c>Ok := XmlPort.Import(...)</c> —
    /// which is the common, idiomatic AL shape, so both extension methods need the hook, not
    /// just the more obviously-named one.
    ///
    /// A real <c>commit == true</c> there is exactly as durable, from AL's point of view, as
    /// an explicit <c>Commit()</c> statement: a later, unrelated <c>asserterror</c> in the
    /// CALLER must not roll back work an inner API already committed.
    ///
    /// Before this hook, only AL's own <c>Commit()</c> and the per-test isolation boundary
    /// called <see cref="MarkCommitPoint"/>, so <see cref="RollbackToCommitPoint"/> rolled
    /// all the way back to test-method start on ANY later trapped error — including rows a
    /// nested BC API (like XmlPort.Import) had already committed inside its own transaction.
    /// Observably: <c>XmlPort.Import(id, Stream, Rec)</c> inserts a row, a LATER, unrelated
    /// statement in the same test method throws (even caught by <c>asserterror</c>), and the
    /// earlier insert vanished — reproducible with no XmlPort involved at all, just a plain
    /// <c>Record.Insert()</c> followed by an unrelated failing <c>Record.Delete()</c>.
    ///
    /// This must NOT fire for a plain <c>Record.Insert/Modify/Delete/Rename</c> call — those
    /// never call <c>EndTransaction</c> themselves (see <see cref="ALDatabasePatches.NoteRecordWrite"/>);
    /// they just participate in whatever transaction is already open, ended by the test
    /// framework's own boundary or an explicit AL <c>Commit()</c>. So this only ever marks a
    /// commit point for a real nested-transaction completion, not for every write — the
    /// corpus's <c>OnModify_Throws_ValueNotModified</c> (an uncommitted plain
    /// <c>Insert()</c> IS rolled back by a later trapped error) still holds.
    /// </summary>
    public static void NoteTransactionEnd(object? session, bool commit)
    {
        if (commit) MarkCommitPoint();
    }

    /// <summary>
    /// Snapshot the record's table to its CURRENT live state before this write lands — but
    /// only on the FIRST write to that table since the last commit point, plus at most one
    /// re-baseline per asserterror-wrapped statement (see the file header's "re-baseline
    /// once per statement" note). Refreshing on EVERY write (the #2170 version of this
    /// method) collapsed the baseline to "the state before the most recent write": a
    /// statement that wrote the same table twice and then failed had only its LAST write
    /// rolled back (AlRunner#2402, measured against real BC: every write since the commit
    /// point is undone). Called from
    /// <see cref="ALDatabasePatches.NoteRecordWrite"/> / <see cref="ALDatabasePatches.NoteRecordInsertWrite"/>,
    /// which BC's own AL write entry points run before doing anything.
    /// </summary>
    internal static void NoteTransactionWrite(object? record)
    {
        if (record is not NavRecord rec) return;
        int tableId;
        try { tableId = rec.MetaTable.TableId; }
        catch { return; }

        foreach (var (source, perTable) in _dataAccessByTable)
        {
            if (!perTable.TryGetValue(tableId, out var dataAccess)) continue;
            var key = (source, tableId);

            if (_txCommitPoint.ContainsKey(key))
            {
                // A baseline already exists for this table. Keep it — unless this is the
                // first write to the table inside the CURRENT asserterror statement, in
                // which case re-baseline to the state at (effectively) the statement's
                // start: that is what keeps OnDelete_Throws_RecordStillExists' earlier,
                // uncommitted Insert alive across its trapped Delete. A SECOND write to the
                // same table inside the same statement must not move the baseline again.
                var rebaselined = _rebaselinedInScope;
                if (rebaselined == null || !rebaselined.Add(key)) continue;
            }
            else
            {
                _rebaselinedInScope?.Add(key);
            }

            var provider = GetDataProvider(dataAccess);
            if (provider == null || provider.GetType().Name != "TempTableDataProvider") continue;

            var providerType = provider.GetType();
            var metaTable = RequiredField(providerType, "table").GetValue(provider);
            if (metaTable == null) continue;

            // A null primaryTree simply means no row was ever inserted — the pre-write image
            // of this table is "empty", which is exactly what an empty row array restores to.
            var rows = new List<NavValue[]>();
            if (RequiredField(providerType, "primaryTree").GetValue(provider) is IEnumerable primaryTree)
                foreach (var row in primaryTree)
                    if (row is TempTableRecordBuffer buffer)
                        rows.Add(CloneValues(buffer.ToArray()));

            _txCommitPoint[key] = new BaselineTable(tableId, metaTable, rows.ToArray());
        }
    }

    /// <summary>
    /// Roll the row store back to the last commit point. Called from BC's own
    /// <c>SessionTransactionExtensions.Rollback</c> (rewritten to land here), which
    /// NavMethodScope.AssertError invokes after catching an AL error.
    ///
    /// Only tables written since the commit point were snapshotted, and only those are
    /// touched — a rollback must not disturb a table nothing wrote to.
    /// </summary>
    public static void RollbackToCommitPoint(object? session)
    {
        if (_txCommitPoint.Count == 0) return;
        foreach (var ((source, tableId), saved) in _txCommitPoint.ToList())
        {
            if (!_dataAccessByTable.TryGetValue(source, out var perTable)) continue;
            if (!perTable.TryGetValue(tableId, out var dataAccess)) continue;
            var provider = GetDataProvider(dataAccess);
            if (provider == null || provider.GetType().Name != "TempTableDataProvider") continue;

            ClearProviderInPlace(provider);
            InsertRows(provider, saved.MetaTable, saved.Rows);
        }
        // The rolled-back work is gone; the commit point itself still stands, so the next
        // write re-snapshots from the restored state.
        _txCommitPoint.Clear();
    }

    private static FieldInfo? _fNavRecordRecordImplementation;
    private static FieldInfo? _fRecordImplementationMutableRecordBuffer;

    /// <summary>
    /// Called from MethodScopePatches.NavMethodScope_AssertErrorCore's catch handler, AFTER
    /// <see cref="RollbackToCommitPoint"/> — order matters: a Modify/Delete on the SAME table
    /// tracked earlier in this same statement could restore the table to a state that
    /// pre-dates an Insert() also made during this statement, and inserting before that
    /// rollback runs would just get discarded again.
    ///
    /// For every Insert() attempted during THIS statement (BeginAssertErrorScope/
    /// EndAssertErrorScope-scoped — see their docs and the file header for why an Insert()
    /// from an earlier, different statement must NOT be forced durable here), forces the row
    /// durable if it isn't already there. Real BC's measured behaviour
    /// (TestTriggerRollback.OnInsert_Throws_RecordNotInserted) is that the row survives even
    /// OnInsert's own trigger throwing. This is NOT because OnInsert runs after the physical
    /// write on real BC — decompiled NavRecord.InsertAsync (Ncl.dll) runs OnInsert BEFORE
    /// recordImplementation.InsertRecordAsync with no surrounding try/catch, identically in
    /// this runner (which reuses that exact method unmodified) and, presumably, real BC — so
    /// a throwing OnInsert means the physical write genuinely never happens in EITHER. This
    /// method exists because real BC's row shows up anyway (see the file header and #2167
    /// for what's confirmed vs. still open about why), and RollbackToCommitPoint has nothing
    /// to roll back to reproduce that with. Reusing the record's own live
    /// <c>RecordImplementation.mutableRecordBuffer</c> (rather than re-deriving field values
    /// ourselves) means the values inserted are exactly what BC's own precompiled Insert()
    /// populated onto the record before OnInsert ran — faithful to the DATA even though the
    /// mechanism that makes real BC durable here is not modelled.
    ///
    /// A record that already made it into the table (OnInsert succeeded, or a previous
    /// force-insert already ran) throws a duplicate-key error from the provider's own Insert
    /// — swallowed here, since "already durable" is exactly the outcome wanted.
    /// </summary>
    public static void ForceDurableFailedInserts()
    {
        var pending = _pendingInsertsInScope;
        if (pending == null || pending.Count == 0) return;
        foreach (var record in pending) ForceDurableInsert(record);
        pending.Clear();
    }

    private static void ForceDurableInsert(object record)
    {
        if (record is not NavRecord rec) return;
        int tableId;
        try { tableId = rec.MetaTable.TableId; }
        catch { return; }

        _fNavRecordRecordImplementation ??= typeof(NavRecord).GetField(
            "recordImplementation", BindingFlags.NonPublic | BindingFlags.Instance);
        if (_fNavRecordRecordImplementation == null) return;
        object? recImpl;
        try { recImpl = _fNavRecordRecordImplementation.GetValue(rec); }
        catch { return; }
        if (recImpl == null) return;

        _fRecordImplementationMutableRecordBuffer ??= recImpl.GetType().GetField(
            "mutableRecordBuffer", BindingFlags.NonPublic | BindingFlags.Instance);
        if (_fRecordImplementationMutableRecordBuffer == null) return;
        object? buffer;
        try { buffer = _fRecordImplementationMutableRecordBuffer.GetValue(recImpl); }
        catch { return; }
        if (buffer == null) return;

        foreach (var (source, perTable) in _dataAccessByTable)
        {
            if (!perTable.TryGetValue(tableId, out var dataAccess)) continue;
            var provider = GetDataProvider(dataAccess);
            if (provider == null || provider.GetType().Name != "TempTableDataProvider") continue;

            try
            {
                var insert = provider.GetType().GetMethods(BindingFlags.Public | BindingFlags.Instance)
                    .First(m => m.Name == "Insert" && m.GetParameters().Length == 4
                             && m.GetParameters()[0].ParameterType == typeof(int));
                var insertOptions = Enum.ToObject(insert.GetParameters()[2].ParameterType, 0);
                insert.Invoke(provider, new object?[] { 0, buffer, insertOptions, null });
            }
            catch
            {
                // Already present (duplicate key from a successful Insert, or a previous
                // force-insert on a different DataAccessSource for the same table) — the
                // record being durable is exactly the outcome this method exists to reach.
            }
        }
    }

    /// <summary>
    /// Drop every row from a TempTableDataProvider without replacing the provider itself.
    /// Restoring in place matters: unlike the codeunit-boundary install-baseline restore, a
    /// rollback happens mid-test with AL record variables still holding the DataAccess they
    /// were opened on. The three collections are exactly what <c>Insert</c> re-creates
    /// through <c>EnsureTreeCreated()</c>, so nulling them is the provider's own
    /// "no rows yet" state.
    /// </summary>
    private static void ClearProviderInPlace(object provider)
    {
        var t = provider.GetType();
        foreach (var name in new[] { "trees", "primaryTree", "uniqueIndexes" })
            AlRunner.Infrastructure.FieldPoke.SetInstance(RequiredField(t, name), provider, null);
    }

    /// <summary>Put saved rows back, deep-copying so the snapshot stays reusable.</summary>
    private static void InsertRows(object provider, object metaTable, NavValue[][] rows)
    {
        if (rows.Length == 0) return;
        var insert = provider.GetType().GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .First(m => m.Name == "Insert" && m.GetParameters().Length == 4
                     && m.GetParameters()[0].ParameterType == typeof(int));
        var insertOptions = Enum.ToObject(insert.GetParameters()[2].ParameterType, 0);

        _ibMutableBufferCtor ??= typeof(ReadOnlyRecordBuffer).Assembly
            .GetType("Microsoft.Dynamics.Nav.Runtime.MutableRecordBuffer")
            ?.GetConstructor(
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance,
                binder: null, types: new[] { typeof(ReadOnlyRecordBuffer) }, modifiers: null)
            ?? throw new InvalidOperationException(
                "MutableRecordBuffer(ReadOnlyRecordBuffer) not found — BC metadata shape changed");

        foreach (var values in rows)
        {
            var readOnly = new ReadOnlyRecordBuffer((NCLMetaApplicationObject)metaTable, CloneValues(values));
            var mutable = _ibMutableBufferCtor.Invoke(new object[] { readOnly });
            insert.Invoke(provider, new object?[] { 0, mutable, insertOptions, null });
        }
    }
}
