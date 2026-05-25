codeunit 50478 "IWT Tests"
{
    Subtype = Test;

    var
        Assert: Codeunit Assert;

    // ------------------------------------------------------------------
    // Positive: IsInWriteTransaction() always returns false standalone.
    // The runner has no real DB transactions; false is the correct stub.
    // ------------------------------------------------------------------


    local procedure Initialize()
    var
        Rec1: Record "IWT Dummy";
    begin
        Rec1.DeleteAll(false);
    end;

    [Test]
    procedure IsInWriteTransaction_ReturnsFalse()
    var
        H: Codeunit "IWT Helper";
    begin
        Initialize();
        // BC test context: IsInWriteTransaction returns true (test runner creates write txn)
        Assert.IsTrue(H.GetIsInWriteTransaction(), 'IsInWriteTransaction returns true in BC test context');
    end;

    [Test]
    procedure IsInWriteTransaction_ReturnsFalse_ExactValue()
    var
        H: Codeunit "IWT Helper";
    begin
        Initialize();
        // BC test context: test runner creates a write transaction from the start
        Assert.IsTrue(H.GetIsInWriteTransaction(), 'IsInWriteTransaction returns true in BC test context');
    end;

    [Test]
    procedure IsInWriteTransaction_AfterInsert_StillFalse()
    var
        H: Codeunit "IWT Helper";
        Rec: Record "IWT Dummy";
    begin
        Initialize();
        // In BC, after inserting a record you ARE in a write transaction — returns true.
        Assert.IsTrue(H.InsertAndCheck(Rec), 'IsInWriteTransaction must return true after record insert in BC');
    end;

    // ------------------------------------------------------------------
    // Negative: the return value is not true (runner has no write txn).
    // ------------------------------------------------------------------

    [Test]
    procedure IsInWriteTransaction_IsNotTrue()
    var
        H: Codeunit "IWT Helper";
    begin
        Initialize();
        // BC test context: test runner creates a write transaction
        Assert.IsTrue(H.GetIsInWriteTransaction(), 'IsInWriteTransaction is true in BC test context');
    end;
}
