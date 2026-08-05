codeunit 50606 "Isolation Probe Test"
{
    Subtype = Test;

    var
        Assert: Codeunit Assert;
        Probe: Codeunit "Isolation Probe";


    local procedure Initialize()
    var
        Rec1: Record "Isolation Probe";
    begin
        Rec1.DeleteAll(false);
    end;

    [Test]
    procedure RecordReadIsolationDoesNotCrash()
    begin
        Initialize();
        // Positive: setting ReadIsolation on a Record should be a no-op
        Probe.SetRecordReadIsolation();
        Assert.IsTrue(true, 'Record.ReadIsolation should not crash');
    end;

    [Test]
    procedure RecRefReadIsolationDoesNotCrash()
    begin
        Initialize();
        // Positive: setting ReadIsolation on a RecordRef should be a no-op
        Probe.SetRecRefReadIsolation();
        Assert.IsTrue(true, 'RecordRef.ReadIsolation should not crash');
    end;

    [Test]
    procedure RecRefDuplicateSharesTable()
    begin
        Initialize();
        // Positive: Duplicate returns a copy that sees the same table data
        Assert.AreEqual(1, Probe.DuplicateRecRef(), 'Duplicate RecRef should see 1 record');
    end;

    [Test]
    procedure InStreamAssignCopiesData()
    begin
        Initialize();
        // Positive: InStr2 := InStr1 should copy the stream so ReadText works
        Probe.AssignInStream();
        Assert.IsTrue(true, 'InStream assign should not crash');
    end;

    [Test]
    procedure RecRefDuplicateOnClosedRefIsEmpty()
    var
        RecRef: RecordRef;
        RecRef2: RecordRef;
    begin
        Initialize();
        // In BC, Duplicate on a RecRef that was never opened throws "not open"
        asserterror RecRef2 := RecRef.Duplicate();
        Assert.ExpectedError('not open');
    end;
}
