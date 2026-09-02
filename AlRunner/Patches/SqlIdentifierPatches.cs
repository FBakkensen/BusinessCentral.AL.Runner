using System.Runtime.CompilerServices;
using System.Text;
using Microsoft.Dynamics.Nav.Runtime;

namespace AlRunner.Patches;

/// <summary>
/// Replacements for <c>NavSqlStatementHelper.ConvertToSqlIdentifier</c> (issue #2428).
///
/// BC's body reads <c>NavGlobal.AppDatabase.SqlDatabaseProperties.InvalidIdentifierChars</c>
/// — the SQL app database's own list of characters it cannot use in an identifier. The
/// runner has no SQL app database (<c>NavGlobal.AppDatabase</c> is <c>SystemTenant?.Database</c>;
/// the skeleton session carries a database object whose <c>SqlDatabaseProperties</c> is null),
/// so the original NREs. It is reached with no SQL backend
/// in sight: <c>NCLMetaTable.SqlTableName</c> (<c>ExternalName</c>, else
/// <c>ConvertToSqlIdentifier(TableName)</c>) is read while BC builds the FlowField sub-query
/// of a query that has a column over a FlowField
/// (<c>NCLMetaQuery.CreateDataItemsForFlowFields</c> → <c>SqlTableDataProviderHelper.CreateDataItemFromFlowField</c>),
/// and <c>Database.AlterKey</c>'s DDL tail hits the same frame (see BcRuntime.cs).
///
/// FAITHFULNESS: when an app database IS present the original logic runs unchanged. Without
/// one, BC's own <c>ConvertToSqlIdentifierWithDefaultInvalidIdentifierChars</c> — the same
/// method, using the default SQL Server set <c>."\/'%][</c> that
/// <c>SqlDatabaseProperties.InvalidIdentifierChars</c> carries on a real tier — answers. The
/// converted name is only ever used to compose SQL text the runner never executes, so the
/// AL-observable outcome (the query opens and its FlowField column is calculated) is the
/// same as on real BC.
/// </summary>
public static class SqlIdentifierPatches
{
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static string NavSqlStatementHelper_ConvertToSqlIdentifier(string identifier)
    {
        var chars = InvalidIdentifierCharsOrNull();
        if (chars == null)
            return NavSqlStatementHelper.ConvertToSqlIdentifierWithDefaultInvalidIdentifierChars(identifier);
        foreach (var oldChar in chars)
            identifier = identifier.Replace(oldChar, '_');
        return identifier;
    }

    /// <summary>
    /// The app database's invalid-identifier characters, or null when there is no app
    /// database or (the skeleton's case) a database object without SqlDatabaseProperties.
    /// </summary>
    private static string? InvalidIdentifierCharsOrNull()
    {
        try { return NavGlobal.AppDatabase?.SqlDatabaseProperties?.InvalidIdentifierChars; }
        catch { return null; }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static StringBuilder NavSqlStatementHelper_ConvertToSqlIdentifierSb(StringBuilder identifier)
    {
        var chars = InvalidIdentifierCharsOrNull() ?? ".\"\\/'%][";
        foreach (var oldChar in chars)
            identifier = identifier.Replace(oldChar, '_');
        return identifier;
    }
}
