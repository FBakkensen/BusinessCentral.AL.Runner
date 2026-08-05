codeunit 50155 "SI Test"
{
    Subtype = Test;

    var
        Assert: Codeunit Assert;
        Src: Codeunit "SI Src";

    [Test]
    procedure SqlRowsRead_ReturnsNonNegative()
    begin
        // Env-agnostic: runner returns 0, real BC returns actual SQL row count.
        // Contract: must not throw and must return a non-negative integer.
        Assert.IsTrue(Src.GetSqlRowsRead() >= 0,
            'SessionInformation.SqlRowsRead must return a non-negative integer');
    end;

    [Test]
    procedure SqlStatementsExecuted_ReturnsNonNegative()
    begin
        // Env-agnostic: runner returns 0, real BC returns actual SQL statement count.
        Assert.IsTrue(Src.GetSqlStatementsExecuted() >= 0,
            'SessionInformation.SqlStatementsExecuted must return a non-negative integer');
    end;

    [Test]
    procedure Callstack_DoesNotThrow()
    var
        cs: Text;
    begin
        // Callstack may return empty or a stack trace; just verify it doesn't crash.
        cs := Src.GetCallstack();
        Assert.IsTrue(true, 'SessionInformation.Callstack must not throw');
    end;
}
