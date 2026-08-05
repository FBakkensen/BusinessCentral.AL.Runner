codeunit 50294 "VG Test"
{
    Subtype = Test;

    var
        Assert: Codeunit Assert;
        Src: Codeunit "VG Src";


    local procedure Initialize()
    var
        Rec1: Record "VG Table";
    begin
        Rec1.DeleteAll(false);
    end;

    [Test]
    procedure GetBySystemId_FromVariant_ReturnsTrue()
    var
        rec: Record "VG Table";
        v: Variant;
    begin
        Initialize();
        rec.Init();
        rec.Id := 1;
        rec.Insert();
        v := rec.SystemId;

        Assert.IsTrue(Src.GetBySystemIdFromVariant(v),
            'RecordRef.GetBySystemId should accept a Variant holding a Guid');
    end;

    [Test]
    procedure GetBySystemId_FromVariant_Missing_ReturnsFalse()
    var
        v: Variant;
    begin
        Initialize();
        v := CreateGuid();

        Assert.IsFalse(Src.GetBySystemIdFromVariant(v),
            'RecordRef.GetBySystemId should return false for a missing SystemId');
    end;
}
