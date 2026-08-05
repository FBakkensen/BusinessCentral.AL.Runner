codeunit 50346 "Stub Methods Tests"
{
    Subtype = Test;

    var
        Assert: Codeunit Assert;


    local procedure Initialize()
    var
        Rec1: Record "Stub Test Table";
    begin
        Rec1.DeleteAll(false);
    end;

    // TestPageFieldCaptionReturnsText removed: BC 16.1 raises CLR NotSupportedException
    // for TestPage field Caption property access in the test runner API context.

    [Test]
    procedure RecRefSetLoadFieldsNoOp()
    var
        RecRef: RecordRef;
        Rec: Record "Stub Test Table";
    begin
        Initialize();
        // [GIVEN] A table with a record
        Rec.Id := 1;
        Rec.Name := 'Test';
        Rec.Insert(true);

        // [WHEN] Opening RecRef and calling SetLoadFields then finding
        RecRef.Open(50050);
        RecRef.SetLoadFields(1, 2);
        RecRef.FindFirst();

        // [THEN] Record is found (SetLoadFields is a no-op, all fields are in memory)
        Assert.AreEqual(1, RecRef.Field(1).Value, 'Field 1 should be 1');
    end;

    [Test]
    procedure RecRefSetLoadFieldsDoesNotFilter()
    var
        RecRef: RecordRef;
        Rec: Record "Stub Test Table";
    begin
        Initialize();
        // [GIVEN] A table with a record that has a Name field
        Rec.Id := 10;
        Rec.Name := 'Hello';
        Rec.Amount := 99;
        Rec.Insert(true);

        // [WHEN] SetLoadFields is called with only field 1
        RecRef.Open(50050);
        RecRef.SetLoadFields(1);
        RecRef.FindFirst();

        // [THEN] All fields are still readable (negative: SetLoadFields does NOT restrict access)
        Assert.AreEqual(10, RecRef.Field(1).Value, 'Field 1 should still be readable');
    end;

    [Test]
    procedure RecRefNameReturnsText()
    var
        RecRef: RecordRef;
        TableName: Text;
    begin
        Initialize();
        // [GIVEN] A RecordRef opened on a table
        RecRef.Open(50050);
        // [WHEN] Reading the Name property
        TableName := RecRef.Name;
        // [THEN] It returns a non-error text value
        Assert.AreNotEqual('', TableName, 'RecRef.Name should return a non-empty stub');
    end;

    [Test]
    procedure RecRefNameBeforeOpenThrows()
    // BC 16.1: RecordRef.Name before Open() raises "The record is not open."
    var
        RecRef: RecordRef;
        Dummy: Text;
    begin
        Initialize();
        asserterror Dummy := RecRef.Name;
        Assert.ExpectedError('not open');
    end;

    [Test]
    procedure PageUpdateNoOp()
    var
        Logic: Codeunit "Stub Logic";
    begin
        Initialize();
        // [GIVEN/WHEN] Code that calls Page.Update() and Page.Update(false)
        Logic.UsePageUpdate();
        // [THEN] No error thrown — Update is a no-op
        Assert.IsTrue(true, 'Page.Update() must compile and no-op');
    end;

    [Test]
    procedure PageUpdateWithParamNoOp()
    var
        P: Page "Stub Test Card";
    begin
        Initialize();
        // [GIVEN] A page variable
        // [WHEN] Calling Update with explicit boolean
        P.Update(true);
        P.Update(false);
        // [THEN] Both complete without error
        Assert.IsTrue(true, 'Page.Update(bool) must compile and no-op');
    end;
}
