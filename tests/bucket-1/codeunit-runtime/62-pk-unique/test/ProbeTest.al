codeunit 50335 "PK Probe Tests"
{
    Subtype = Test;
    var
        Assert: Codeunit Assert;

    [Test]
    procedure DuplicateInsertFails()
    var
        R: Record "PK Probe Row";
        Second: Record "PK Probe Row";
    begin
        R.DeleteAll();
        R.Id := 1; R.Name := 'a'; R.Insert();

        Second.Id := 1;
        Second.Name := 'b';
        asserterror Second.Insert();

        // Only one row should remain
        Assert.AreEqual(1, R.Count(), 'Duplicate Insert should not have created a second row');
    end;

    [Test]
    procedure DistinctInsertSucceeds()
    var
        R: Record "PK Probe Row";
    begin
        R.Id := 1; R.Insert();
        R.Init();
        R.Id := 2; R.Insert();
        Assert.AreEqual(2, R.Count(), 'Two distinct inserts should land');
    end;
}
