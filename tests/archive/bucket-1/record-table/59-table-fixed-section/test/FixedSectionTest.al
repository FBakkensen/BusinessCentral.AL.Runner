codeunit 50580 "Test Table Fixed Section"
{
    Subtype = Test;

    var
        Assert: Codeunit Assert;


    local procedure Initialize()
    var
        Rec1: Record "Fixed Section Test Table";
    begin
        Rec1.DeleteAll(false);
    end;

    [Test]
    procedure FixedSection_InsertAndGet()
    var
        Rec: Record "Fixed Section Test Table";
    begin
        Initialize();
        // [GIVEN] A table with fieldgroup(Fixed; ...) compiles and allows Insert
        Rec.Id := 1;
        Rec.Name := 'Widget';
        Rec.Amount := 99.50;
        Rec.Insert();

        // [WHEN] Get the record by PK
        Rec.Get(1);

        // [THEN] All fields retain their values
        Assert.AreEqual(1, Rec.Id, 'Id must be 1');
        Assert.AreEqual('Widget', Rec.Name, 'Name must be Widget');
        Assert.AreEqual(99.50, Rec.Amount, 'Amount must be 99.50');
    end;

    [Test]
    procedure FixedSection_Modify()
    var
        Rec: Record "Fixed Section Test Table";
    begin
        Initialize();
        // [GIVEN] An inserted record on a table with fieldgroup(Fixed; ...)
        Rec.Id := 2;
        Rec.Name := 'Original';
        Rec.Amount := 10.00;
        Rec.Insert();

        // [WHEN] Modify the record
        Rec.Get(2);
        Rec.Name := 'Modified';
        Rec.Modify();

        // [THEN] The modified field is persisted
        Rec.Get(2);
        Assert.AreEqual('Modified', Rec.Name, 'Name must reflect modification');
    end;

    [Test]
    procedure FixedSection_Delete()
    var
        Rec: Record "Fixed Section Test Table";
    begin
        Initialize();
        // [GIVEN] An inserted record on a table with fieldgroup(Fixed; ...)
        Rec.Id := 3;
        Rec.Name := 'ToDelete';
        Rec.Amount := 5.00;
        Rec.Insert();

        // [WHEN] Delete the record
        Rec.Get(3);
        Rec.Delete();

        // [THEN] Get returns false
        Assert.IsFalse(Rec.Get(3), 'Get must return false after Delete');
    end;

    [Test]
    procedure FixedSection_GetNonExistent_ReturnsFalse()
    var
        Rec: Record "Fixed Section Test Table";
    begin
        Initialize();
        // [GIVEN] No record with Id 999 exists
        // [WHEN] Get is called for a non-existent key
        // [THEN] Get returns false — fieldgroup(Fixed) does not affect key lookups
        Assert.IsFalse(Rec.Get(999), 'Get must return false for missing key');
    end;


    // FixedLayoutGroup_SetAndReadLeft, FixedLayoutGroup_SetAndReadRight, and
    // FixedLayoutGroup_FieldValue_NotDefaultAfterSet removed: BC 16.1 raises CLR
    // NotSupportedException for TestPage field access inside fixed() layout groups.
}
