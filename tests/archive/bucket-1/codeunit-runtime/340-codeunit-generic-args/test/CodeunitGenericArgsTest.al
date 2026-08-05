codeunit 50303 "CGA Test"
{
    Subtype = Test;

    var
        Assert: Codeunit Assert;
        Manager: Codeunit "CGA Manager";

    [Test]
    procedure NestedGenericListDictionary_ReturnsWorkerValue()
    var
        result: Integer;
    begin
        result := Manager.GetFirstValue();
        Assert.AreEqual(42, result, 'Nested List/Dictionary of Codeunit should preserve the worker value');
    end;

    [Test]
    procedure DictionaryAdd_DuplicateKey_Throws()
    begin
        asserterror Manager.AddDuplicateKey();
        Assert.ExpectedError('same key');
    end;
}
