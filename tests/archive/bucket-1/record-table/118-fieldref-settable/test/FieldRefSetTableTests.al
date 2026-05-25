// Renumbered from 50121 to avoid collision in new bucket layout (#1385).
codeunit 50427 "FieldRef SetTable Tests"
{
    Subtype = Test;

    var
        Assert: Codeunit Assert;


    local procedure Initialize()
    var
        Rec1: Record "FieldRef Test Table";
    begin
        Rec1.DeleteAll(false);
    end;

    [Test]
    procedure TestSetTableCopiesEntryNo()
    var
        Helper: Codeunit "FieldRef SetTable Helper";
        EntryNo: Integer;
        Desc: Text[100];
        _ResetFieldRefTestTable: Record "FieldRef Test Table";
    begin
        Initialize();
        _ResetFieldRefTestTable.DeleteAll();
        Helper.SetTableCopiesData(EntryNo, Desc);
        Assert.AreEqual(42, EntryNo, 'SetTable should copy Entry No. from RecRef');
    end;

    [Test]
    procedure TestSetTableCopiesDescription()
    var
        Helper: Codeunit "FieldRef SetTable Helper";
        EntryNo: Integer;
        Desc: Text[100];
    begin
        Initialize();
        Helper.SetTableCopiesData(EntryNo, Desc);
        Assert.AreEqual('SetTableTest', Desc, 'SetTable should copy Description from RecRef');
    end;
}
