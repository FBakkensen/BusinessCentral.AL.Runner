codeunit 50397 "RecRef Factory Test"
{
    Subtype = Test;

    [Test]
    procedure RecRefArray_FactoryCompiles()
    var
        Src: Codeunit "RecRef Factory Src";
        Result: Integer;
    begin
        // Positive: array of RecordRef compiles and works correctly
        Result := Src.GetRecRefFromArray();
        Assert.AreEqual(50001, Result, 'RecRefs[2].Number should be 50001 after Open(50001)');
    end;

    [Test]
    procedure RecRefArray_ElementsAreIndependent()
    var
        RecRefs: array[2] of RecordRef;
    begin
        // Positive: each element in a RecordRef array is independent
        RecRefs[1].Open(50000);
        RecRefs[2].Open(50001);
        Assert.AreEqual(50000, RecRefs[1].Number, 'RecRefs[1] should have table 50000');
        Assert.AreEqual(50001, RecRefs[2].Number, 'RecRefs[2] should have table 50001');
    end;

    var
        Assert: Codeunit "Library Assert";
}
