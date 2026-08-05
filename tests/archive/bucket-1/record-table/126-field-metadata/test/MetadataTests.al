codeunit 50435 "Metadata Tests"
{
    Subtype = Test;
    var
        Assert: Codeunit Assert;
        Probe: Codeunit "Metadata Probe";

    [Test]
    procedure FieldCaptionReturnsExplicitCaption()
    begin
        // field 1 has Caption = 'Entry Number'
        Assert.AreEqual('Entry Number', Probe.GetFieldCaption(50097, 1), 'Should return explicit field caption');
    end;

    [Test]
    procedure FieldCaptionFallsBackToFieldName()
    begin
        // field 3 (Amount) has no Caption property — should return field name
        Assert.AreEqual('Amount', Probe.GetFieldCaption(50097, 3), 'Should fall back to field name when no caption');
    end;

    [Test]
    procedure FieldNameReturnsCorrectName()
    begin
        Assert.AreEqual('Entry No.', Probe.GetFieldName(50097, 1), 'Should return field name');
    end;

    [Test]
    procedure FieldNameReturnsQuotedFieldName()
    begin
        Assert.AreEqual('Item Code', Probe.GetFieldName(50097, 4), 'Should return quoted field name');
    end;

    [Test]
    procedure TableNameReturnsRealName()
    begin
        Assert.AreEqual('Metadata Test Item', Probe.GetTableName(50097), 'Should return real table name');
    end;

    [Test]
    procedure TableCaptionReturnsExplicitCaption()
    begin
        Assert.AreEqual('Test Item', Probe.GetRecordTableCaption(), 'Should return explicit table caption');
    end;

    [Test]
    procedure RecordTableNameReturnsRealName()
    begin
        Assert.AreEqual('Metadata Test Item', Probe.GetRecordTableName(), 'Should return real table name via Record');
    end;

    [Test]
    procedure FieldCaptionFromRecordReturnsCaption()
    var
        Item: Record "Metadata Test Item";
    begin
        // FieldCaption("Entry No.") should return 'Entry Number'
        Assert.AreEqual('Entry Number', Item.FieldCaption("Entry No."), 'Record.FieldCaption should return caption');
    end;

    [Test]
    procedure FieldCaptionNoExplicitReturnsFallback()
    var
        Item: Record "Metadata Test Item";
    begin
        // Amount has no Caption property, so should return field name 'Amount'
        Assert.AreEqual('Amount', Item.FieldCaption(Amount), 'Record.FieldCaption should fall back to field name');
    end;

    [Test]
    procedure TextFieldLengthReturnsCorrectValue()
    begin
        // field 2 (Description) is Text[100] — length should be 100
        Assert.AreEqual(100, Probe.GetFieldLength(50097, 2), 'Text[100] should return length 100');
    end;

    [Test]
    procedure CodeFieldLengthReturnsCorrectValue()
    begin
        // field 4 (Item Code) is Code[20] — length should be 20
        Assert.AreEqual(20, Probe.GetFieldLength(50097, 4), 'Code[20] should return length 20');
    end;

    [Test]
    procedure RecRefFieldCountReturnsSchemaCount()
    begin
        // Table 50097 has 6 declared fields
        Assert.AreEqual(6, Probe.GetRecRefFieldCount(50097), 'FieldCount should return number of declared fields');
    end;

    [Test]
    procedure CaptionWithEmbeddedApostropheIsUnescaped()
    begin
        // field 6 has Caption = 'Vendor''s Name' — the doubled apostrophe should be unescaped
        Assert.AreEqual('Vendor''s Name', Probe.GetFieldCaption(50097, 6), 'Embedded apostrophe should be unescaped');
    end;

    [Test]
    procedure UnknownField_Throws()
    // BC 16.1: RecRef.Field(999) on a table that has no field 999 throws
    begin
        asserterror Probe.GetFieldCaption(50097, 999);
        Assert.ExpectedError('999');
    end;

    [Test]
    procedure UnknownTable_LookupThrows()
    begin
        // Table 99999 is not in the bundle — NCLMetadata throws on lookup.
        // In real BC, RecRef.Name would return 'Table99999'; the runner faithfully
        // throws instead (no phantom metadata is synthesised for unknown IDs).
        asserterror Probe.GetTableName(99999);
        Assert.ExpectedError('99999');
    end;

    [Test]
    procedure IntegerFieldTypeIsInteger()
    begin
        // field 1 (Entry No.) is Integer
        Assert.AreEqual('Integer', Probe.GetFieldType(50097, 1), 'Integer field should report type Integer');
    end;

    [Test]
    procedure DecimalFieldTypeIsDecimal()
    begin
        // field 3 (Amount) is Decimal
        Assert.AreEqual('Decimal', Probe.GetFieldType(50097, 3), 'Decimal field should report type Decimal');
    end;

    [Test]
    procedure BooleanFieldTypeIsBoolean()
    begin
        // field 5 (Active) is Boolean
        Assert.AreEqual('Boolean', Probe.GetFieldType(50097, 5), 'Boolean field should report type Boolean');
    end;

    [Test]
    procedure TextFieldTypeIsText()
    begin
        // field 2 (Description) is Text[100]
        Assert.AreEqual('Text', Probe.GetFieldType(50097, 2), 'Text field should report type Text');
    end;

    [Test]
    procedure CodeFieldTypeIsCode()
    begin
        // field 4 (Item Code) is Code[20]
        Assert.AreEqual('Code', Probe.GetFieldType(50097, 4), 'Code field should report type Code');
    end;
}
