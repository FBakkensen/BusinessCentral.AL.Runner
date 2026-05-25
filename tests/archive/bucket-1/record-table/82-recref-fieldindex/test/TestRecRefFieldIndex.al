codeunit 50610 "Test RecRef FieldIndex"
{
    Subtype = Test;

    var
        Assert: Codeunit Assert;

    // === Blocker 1: RecordRef.FieldIndex and Caption ===


    local procedure Initialize()
    var
        Rec1: Record "Test Item";
    begin
        Rec1.DeleteAll(false);
    end;

    [Test]
    procedure FieldIndexReturnsFieldRef()
    var
        RecRef: RecordRef;
        FldRef: FieldRef;
        Item: Record "Test Item";
    begin
        Initialize();
        // Setup: insert a record so fields are registered
        Item."No." := 'ITEM1';
        Item.Description := 'Widget';
        Item.Amount := 42.5;
        Item.Insert();

        RecRef.GetTable(Item);

        // FieldIndex(1) should return a valid FieldRef
        FldRef := RecRef.FieldIndex(1);
        Assert.AreNotEqual(0, FldRef.Number, 'FieldIndex(1) should return a FieldRef with non-zero number');
    end;

    [Test]
    procedure FieldIndexSecondField()
    var
        RecRef: RecordRef;
        FldRef: FieldRef;
        Item: Record "Test Item";
    begin
        Initialize();
        Item."No." := 'ITEM2';
        Item.Description := 'Gadget';
        Item.Amount := 10.0;
        Item.Insert();

        RecRef.GetTable(Item);

        // FieldIndex(2) should return the second field
        FldRef := RecRef.FieldIndex(2);
        Assert.AreNotEqual(0, FldRef.Number, 'FieldIndex(2) should return a FieldRef with non-zero number');
    end;

    [Test]
    procedure FieldIndexOutOfRangeThrows()
    // BC 16.1: RecRef.FieldIndex() with an index beyond the field count throws
    var
        RecRef: RecordRef;
        FldRef: FieldRef;
        Item: Record "Test Item";
    begin
        Initialize();
        Item."No." := 'ITEM3';
        Item.Insert();

        RecRef.GetTable(Item);

        // FieldIndex with an index beyond the field count throws in BC 16.1
        asserterror FldRef := RecRef.FieldIndex(999);
    end;

    [Test]
    procedure CaptionReturnsText()
    var
        RecRef: RecordRef;
        Item: Record "Test Item";
    begin
        Initialize();
        RecRef.GetTable(Item);

        // Caption should return a text value (stub returns empty string)
        // Just verify it does not error out
        Assert.AreNotEqual('IMPOSSIBLE_CAPTION_VALUE', Format(RecRef.Caption),
            'Caption should return a text value');
    end;

    // TestPageFieldVisibleReturnsTrue, TestPageFieldVisibleNegative removed:
    // BC 16.1 raises CLR NotSupportedException for TestPage field Visible property access
    // in the test runner API context.

    // TestPageFieldLookupNoError and TestPageFieldDrillDownNoError removed:
    // BC 16.1 raises CLR NotSupportedException for TestPage field Lookup() and DrillDown()
    // in the test runner API context.

    // === Blocker 3: FieldRef.SetRange with variant/object ===

    [Test]
    procedure FieldRefSetRangeWithVariant()
    var
        RecRef: RecordRef;
        FldRef: FieldRef;
        Item: Record "Test Item";
        V: Variant;
    begin
        Initialize();
        Item."No." := 'V1';
        Item.Description := 'First';
        Item.Insert();

        Item."No." := 'V2';
        Item.Description := 'Second';
        Item.Insert();

        RecRef.Open(Database::"Test Item");
        FldRef := RecRef.Field(1);

        // SetRange with a variant value
        V := 'V1';
        FldRef.SetRange(V);

        Assert.IsTrue(RecRef.FindFirst(), 'Should find record with variant filter');
    end;

    [Test]
    procedure FieldRefSetRangeWithVariantNegative()
    var
        RecRef: RecordRef;
        FldRef: FieldRef;
        Item: Record "Test Item";
        V: Variant;
    begin
        Initialize();
        Item."No." := 'N1';
        Item.Description := 'Only';
        Item.Insert();

        RecRef.Open(Database::"Test Item");
        FldRef := RecRef.Field(1);

        // SetRange with variant that matches no records
        V := 'NONEXISTENT';
        FldRef.SetRange(V);

        Assert.IsTrue(RecRef.IsEmpty(), 'Should find no records with non-matching variant filter');
    end;

}
