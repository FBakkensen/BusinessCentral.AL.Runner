codeunit 50335 "PK Probe Tests"
{
    Subtype = Test;
    var
        Assert: Codeunit Assert;


    local procedure Initialize()
    var
        Rec1: Record "PK Probe Row";
    begin
        Rec1.DeleteAll(false);
    end;

    [Test]
    procedure DuplicateInsertFails()
    var
        R: Record "PK Probe Row";
        Second: Record "PK Probe Row";
        InsertOk: Boolean;
    begin
        Initialize();
        R.DeleteAll();
        R.Id := 1; R.Name := 'a'; R.Insert();

        Second.Id := 1;
        Second.Name := 'b';
        InsertOk := Second.Insert();

        // Duplicate insert must return false and not create a second row
        Assert.IsFalse(InsertOk, 'Duplicate Insert must return false');
        Assert.AreEqual(1, R.Count(), 'Duplicate Insert should not have created a second row');
    end;

    [Test]
    procedure DistinctInsertSucceeds()
    var
        R: Record "PK Probe Row";
    begin
        Initialize();
        R.Id := 1; R.Insert();
        R.Init();
        R.Id := 2; R.Insert();
        Assert.AreEqual(2, R.Count(), 'Two distinct inserts should land');
    end;
}
