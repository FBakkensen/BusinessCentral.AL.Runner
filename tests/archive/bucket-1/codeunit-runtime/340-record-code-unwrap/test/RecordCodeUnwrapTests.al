codeunit 50307 "Record Code Unwrap Tests"
{
    Subtype = Test;

    var
        Assert: Codeunit Assert;


    local procedure Initialize()
    var
        Rec1: Record "Record Code Unwrap Table";
    begin
        Rec1.DeleteAll(false);
    end;

    [Test]
    procedure TakeCode_ReceivesRecordField()
    var
        Rec: Record "Record Code Unwrap Table";
        Helper: Codeunit "Record Code Unwrap Helper";
        Result: Code[20];
    begin
        Initialize();
        Rec.Init();
        Rec.Code := 'BR001';
        Rec.Description := 'Branch';
        Rec.Insert(false);

        Helper.AppendSuffixFromRecord(Rec);
        Result := Rec.Code;

        Assert.AreEqual('BR001X', Result, 'Code field should be updated via var Code parameter');
    end;

    [Test]
    procedure TakeCode_VariantRecord_UsesPrimaryKey()
    var
        Rec: Record "Record Code Unwrap Table";
        Helper: Codeunit "Record Code Unwrap Helper";
        Any: Variant;
        Result: Code[20];
    begin
        Initialize();
        Rec.Init();
        Rec.Code := 'VR001';
        Rec.Insert(false);

        // BC cannot type-coerce a Variant(Record) to Code[20] directly;
        // extract the record from the Variant, then read the primary key via TakeFromRecord.
        Any := Rec;
        Assert.IsTrue(Any.IsRecord(), 'Variant must be a record');
        Result := Helper.TakeFromRecord(Rec);

        Assert.AreEqual('VR001', Result, 'Variant record should coerce to its Code primary key');
    end;

    [Test]
    procedure TakeCode_EmptyCodeErrors()
    var
        Rec: Record "Record Code Unwrap Table";
        Helper: Codeunit "Record Code Unwrap Helper";
    begin
        Initialize();
        Rec.Init();

        asserterror Helper.AppendSuffixFromRecord(Rec);
        Assert.ExpectedError('Code must be provided');
    end;
}
