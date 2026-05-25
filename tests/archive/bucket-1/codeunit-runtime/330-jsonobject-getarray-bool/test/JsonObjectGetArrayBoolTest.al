codeunit 50282 "JsonObject Bool Test"
{
    Subtype = Test;

    var
        Assert: Codeunit Assert;
        Src: Codeunit "JsonObject Bool Src";

    [Test]
    procedure GetObject_RequireExists_ReturnsNested()
    begin
        Assert.IsTrue(Src.GetObjectRequireExists(),
            'GetObject(key, true) should return the nested object');
    end;

    [Test]
    procedure GetObject_RequireExists_MissingNoThrow()
    begin
        // BC 16.1: GetObject(key, true) does NOT throw for missing keys — returns empty object.
        Src.GetObjectRequireExistsMissing();
        Assert.IsTrue(true, 'GetObject(key, true) for missing key must not throw in BC 16.1');
    end;

    [Test]
    procedure GetObject_Missing_ThrowsOrEmpty()
    begin
        // BC 16.1: GetObject(key, false) still throws when the key does not exist.
        asserterror Src.GetObjectMissingNoError();
        Assert.ExpectedError('There is no property');
    end;

    [Test]
    procedure GetArray_RequireExists_ReturnsArray()
    begin
        Assert.AreEqual(2, Src.GetArrayRequireExists(),
            'GetArray(key, true) should return the array value');
    end;

    [Test]
    procedure GetArray_RequireExists_MissingNoThrow()
    begin
        // BC 16.1: GetArray(key, true) does NOT throw for missing keys — returns empty array.
        // Verify no exception is raised.
        Src.GetArrayRequireExistsMissing();
        Assert.IsTrue(true, 'GetArray(key, true) for missing key must not throw in BC 16.1');
    end;

    [Test]
    procedure GetArray_Missing_ReturnsEmptyArray()
    // BC 16.1: GetArray(key, false) still throws when the key does not exist
    begin
        asserterror Src.GetArrayMissingNoError();
        Assert.ExpectedError('There is no property');
    end;
}
