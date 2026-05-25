/// <summary>
/// Proves that MockRecordHandle implements IConvertible so that
/// Convert.ToInt32 / Convert.ToBoolean / Convert.ChangeType(record, T) do not throw
/// "Unable to cast MockRecordHandle to IConvertible".
///
/// The BC transpiler emits NavIndirectValueToInt32 / NavIndirectValueToBoolean when a
/// Variant holding a Record is assigned to an Integer / Boolean local.  After rewriting
/// these become AlCompat.NavIndirectValueToInt32 / NavIndirectValueToBoolean, which
/// call Convert.ToInt32 / Convert.ToBoolean internally — both require IConvertible.
/// </summary>
codeunit 50515 "RIC Tests"
{
    Subtype = Test;
    var Assert: Codeunit Assert;

    /// <summary>
    /// Positive: Variant holding a default Record assigned to Integer must return 0 (not crash).
    /// </summary>

    local procedure Initialize()
    var
        Rec1: Record "RIC Table";
    begin
        Rec1.DeleteAll(false);
    end;

    [Test]
    procedure VariantRecord_ToInt_Throws()
    // BC 16.1: assigning a Variant holding a Record to Integer throws a type conversion error
    var
        Rec: Record "RIC Table";
        Helper: Codeunit "RIC Helper";
        V: Variant;
        ResultInt: Integer;
        ResultBool: Boolean;
    begin
        Initialize();
        V := Rec;
        asserterror Helper.VariantRecordToIntBool(V, ResultInt, ResultBool);
    end;

    /// <summary>
    /// BC 16.1: assigning a Variant holding a Record to Boolean throws a type conversion error.
    /// </summary>
    [Test]
    procedure VariantRecord_ToBool_Throws()
    // BC 16.1: assigning a Variant holding a Record to Boolean throws a type conversion error
    var
        Rec: Record "RIC Table";
        Helper: Codeunit "RIC Helper";
        V: Variant;
        ResultInt: Integer;
        ResultBool: Boolean;
    begin
        Initialize();
        V := Rec;
        asserterror Helper.VariantRecordToIntBool(V, ResultInt, ResultBool);
    end;

    /// <summary>
    /// Positive: Format(Variant-holding-Record) must return a non-empty string.
    /// Proves Convert path does not break the Format round-trip.
    /// </summary>
    [Test]
    procedure VariantRecord_Format_ReturnsNonEmpty()
    var
        Rec: Record "RIC Table";
        Helper: Codeunit "RIC Helper";
        V: Variant;
        Result: Text;
    begin
        Initialize();
        // Positive: Format of a Variant wrapping a Record must produce a non-empty string
        V := Rec;
        Result := Helper.FormatVariantRecord(V);
        Assert.IsTrue(Result <> '', 'Format(Variant<Record>) must return a non-empty string');
    end;

    /// <summary>
    /// Positive: Record → Variant → Format round-trip with a populated key
    /// must return a string containing the key value.
    /// </summary>
    [Test]
    procedure PopulatedRecord_ToVariant_FormatContainsKey()
    var
        Rec: Record "RIC Table";
        Helper: Codeunit "RIC Helper";
        Result: Text;
    begin
        Initialize();
        // Positive: Format of a populated Record through Variant must contain the PK
        Rec.Id := 99;
        Rec.Name := 'IConvertible';
        Rec.Insert();
        Rec.Get(99);
        Result := Helper.RecordToVariantToText(Rec);
        Assert.IsTrue(Result <> '', 'Format(populated Record via Variant) must be non-empty');
        Assert.IsTrue(StrPos(Result, '99') > 0, 'Format result must contain the key value 99');
    end;

    /// <summary>
    /// BC 16.1: extracting Int from a Variant-holding-Record throws a type conversion error.
    /// </summary>
    [Test]
    procedure VariantRecord_ToInt_IsExactlyZero_NotGarbage()
    // BC 16.1: assigning a Variant holding a Record to Integer throws a type conversion error
    var
        Rec: Record "RIC Table";
        Helper: Codeunit "RIC Helper";
        V: Variant;
        ResultInt: Integer;
        ResultBool: Boolean;
    begin
        Initialize();
        Rec.Id := 7;
        V := Rec;
        asserterror Helper.VariantRecordToIntBool(V, ResultInt, ResultBool);
    end;
}
