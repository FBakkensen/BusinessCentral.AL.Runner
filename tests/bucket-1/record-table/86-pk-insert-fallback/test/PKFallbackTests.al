codeunit 50615 "PK Fallback Tests"
{
    Subtype = Test;
    var
        Assert: Codeunit Assert;

    // Positive: two records with distinct PKs must both land

    local procedure Initialize()
    var
        Rec1: Record "No Key Table";
    begin
        Rec1.DeleteAll(false);
    end;

    [Test]
    procedure DistinctInsertsSucceed()
    var
        Rec: Record "No Key Table";
    begin
        Initialize();
        Rec."Entry No." := 1;
        Rec.Description := 'First';
        Rec.Insert();

        Rec.Init();
        Rec."Entry No." := 2;
        Rec.Description := 'Second';
        Rec.Insert();

        Assert.AreEqual(2, Rec.Count(), 'Two distinct inserts should produce two rows');
    end;

    // Negative: inserting a record whose field-1 value already exists must fail
    [Test]
    procedure DuplicateInsertErrors()
    var
        Rec: Record "No Key Table";
        InsertOk: Boolean;
    begin
        Initialize();
        Rec.DeleteAll();
        Rec."Entry No." := 42;
        Rec.Description := 'Original';
        Rec.Insert();

        Rec.Init();
        Rec."Entry No." := 42;
        Rec.Description := 'Duplicate';
        InsertOk := Rec.Insert();

        // Duplicate insert must return false and not create a second row
        Assert.IsFalse(InsertOk, 'Duplicate Insert must return false');
        Assert.AreEqual(1, Rec.Count(), 'Duplicate Insert must not create a second row');
    end;
}
