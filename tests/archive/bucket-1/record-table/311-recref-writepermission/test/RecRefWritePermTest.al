codeunit 50533 "RecRef WritePerm Test"
{
    Subtype = Test;

    var
        Assert: Codeunit Assert;

    [Test]
    procedure WritePermission_OpenRef_ReturnsTrue()
    // Proves WritePermission returns true (not a default false stub) when RecordRef is open
    var
        Helper: Codeunit "RecRef WritePerm Helper";
    begin
        Assert.IsTrue(Helper.TestWritePermission(), 'WritePermission should return true in standalone mode');
    end;

    [Test]
    procedure WritePermission_ClosedRef_Throws()
    // BC 16.1: WritePermission on an uninitialized (never-opened) RecordRef raises
    // "The record is not open." — permission check requires an open reference.
    var
        RecRef: RecordRef;
        Dummy: Boolean;
    begin
        asserterror Dummy := RecRef.WritePermission;
        Assert.ExpectedError('not open');
    end;

    [Test]
    procedure WritePermission_AfterClose_Throws()
    // BC 16.1: WritePermission after Close() raises "The record is not open."
    var
        RecRef: RecordRef;
        Dummy: Boolean;
    begin
        RecRef.Open(Database::"RecRef WritePerm Table");
        RecRef.Close();
        asserterror Dummy := RecRef.WritePermission;
        Assert.ExpectedError('not open');
    end;
}
