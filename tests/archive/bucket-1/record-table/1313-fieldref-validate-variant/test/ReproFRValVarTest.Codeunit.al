codeunit 50446 "Repro FRValVar Test"
{
    Subtype = Test;

    var
        Assert: Codeunit Assert;


    local procedure Initialize()
    var
        Rec1: Record "Repro FRValVar Tab";
    begin
        Rec1.DeleteAll(false);
    end;

    [Test]
    procedure FieldRef_Validate_Variant_PositiveValue_Persists()
    var
        ReproRec: Record "Repro FRValVar Tab";
        Rr: RecordRef;
        Fr: FieldRef;
        V: Variant;
    begin
        Initialize();
        ReproRec.Init();
        ReproRec."No." := 1;
        ReproRec.Insert();
        Rr.GetTable(ReproRec);
        Fr := Rr.Field(2);

        V := 12.5;
        Fr.Validate(V);
        Rr.Modify();
        Rr.Close();

        ReproRec.Get(1);
        Assert.AreEqual(12.5, ReproRec.Amount, 'FieldRef.Validate(Variant) must write the value');
    end;

    [Test]
    procedure FieldRef_Validate_Variant_NegativeValue_FiresOnValidateError()
    var
        ReproRec: Record "Repro FRValVar Tab";
        Rr: RecordRef;
        Fr: FieldRef;
        V: Variant;
    begin
        Initialize();
        ReproRec.Init();
        ReproRec."No." := 2;
        ReproRec.Insert();
        Rr.GetTable(ReproRec);
        Fr := Rr.Field(2);

        V := -1.0;
        asserterror Fr.Validate(V);
        Assert.ExpectedError('Amount must be non-negative');
    end;
}
