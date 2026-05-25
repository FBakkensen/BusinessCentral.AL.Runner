/// Helper codeunit that wraps IsInWriteTransaction() so the test codeunit
/// can call it and verify the standalone runner always returns false.
codeunit 50477 "IWT Helper"
{
    procedure GetIsInWriteTransaction(): Boolean
    begin
        exit(IsInWriteTransaction());
    end;

    procedure InsertAndCheck(var Rec: Record "IWT Dummy"): Boolean
    begin
        Rec.Insert();
        exit(IsInWriteTransaction());
    end;
}

/// Minimal table used to test IsInWriteTransaction after a record insert.
table 50110 "IWT Dummy"
{
    fields
    {
        field(1; ID; Integer) { }
    }
}
