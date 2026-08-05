codeunit 50623 "GBS GetBySystemId Tests"
{
    Subtype = Test;

    var
        Assert: Codeunit Assert;


    local procedure Initialize()
    var
        Rec1: Record "GBS Test Record";
    begin
        Rec1.DeleteAll(false);
    end;

    [Test]
    procedure GetBySystemIdFindsInsertedRecord()
    var
        Rec: Record "GBS Test Record";
        LookupRec: Record "GBS Test Record";
        RecordSystemId: Guid;
    begin
        Initialize();
        // [GIVEN] A record inserted into the table
        Rec.Id := 1;
        Rec.Name := 'Alpha';
        Rec.Insert(true);
        RecordSystemId := Rec.SystemId;

        // [WHEN] GetBySystemId is called with the inserted record's SystemId
        LookupRec.GetBySystemId(RecordSystemId);

        // [THEN] The inserted record should be found
        Assert.AreEqual(Rec.Id, LookupRec.Id, 'GetBySystemId should return the inserted record');
        Assert.AreEqual(Rec.Name, LookupRec.Name, 'GetBySystemId should return the inserted record values');
    end;

    [Test]
    procedure GetBySystemIdNotFoundReturnsFalse()
    var
        Rec: Record "GBS Test Record";
        FakeId: Guid;
    begin
        Initialize();
        // [GIVEN] An empty table and a random GUID
        FakeId := CreateGuid();

        // [WHEN] GetBySystemId is called with a non-existent ID
        // [THEN] It should error because there is no matching record
        asserterror Rec.GetBySystemId(FakeId);
        Assert.ExpectedError('does not exist');
    end;
}
