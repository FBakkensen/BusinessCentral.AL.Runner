codeunit 50571 "FF Tests"
{
    Subtype = Test;

    var
        Assert: Codeunit Assert;


    local procedure Initialize()
    var
        Rec1: Record "FF Child";
        Rec2: Record "FF Master";
    begin
        Rec1.DeleteAll(false);
        Rec2.DeleteAll(false);
    end;

    [Test]
    procedure CalcFieldsExistsFalseWhenNoChild()
    var
        M: Record "FF Master";
    begin
        Initialize();
        M.Id := 1;
        M.Insert();

        M.CalcFields("Has Child");
        Assert.IsFalse(M."Has Child", 'No children inserted — exist FlowField must be false');
    end;

    [Test]
    procedure CalcFieldsExistsTrueAfterChildInsert()
    var
        M: Record "FF Master";
        C: Record "FF Child";
    begin
        Initialize();
        M.Id := 2;
        M.Insert();

        C.Id := 1;
        C.ParentId := 2;
        C.Insert();

        M.CalcFields("Has Child");
        Assert.IsTrue(M."Has Child", 'Child with ParentId=2 exists — exist FlowField must be true');
    end;
}
