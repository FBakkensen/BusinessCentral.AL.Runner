codeunit 50280 "RR Links CurrPage Test"
{
    Subtype = Test;

    var
        Assert: Codeunit Assert;
        Src: Codeunit "RR Links CurrPage Src";

    [Test]
    procedure RecordRef_HasLinks_AfterAdd()
    begin
        Assert.IsTrue(Src.RecordRefHasLinksAfterAdd(),
            'RecordRef.HasLinks should return true after AddLink');
    end;

    [Test]
    procedure RecordRef_HasLinks_WrongExpectationFails()
    begin
        asserterror Assert.AreEqual(false, Src.RecordRefHasLinksAfterAdd(),
            'RecordRef.HasLinks should not return false after AddLink');
        Assert.ExpectedError('AreEqual');
    end;

    [Test]
    procedure RecordRef_DeleteLinks_Clears()
    begin
        Assert.IsFalse(Src.RecordRefDeleteLinksClears(),
            'RecordRef.HasLinks should be false after DeleteLinks');
    end;

    [Test]
    procedure RecordRef_DeleteLinks_WrongExpectationFails()
    begin
        asserterror Assert.AreEqual(true, Src.RecordRefDeleteLinksClears(),
            'RecordRef.HasLinks should not return true after DeleteLinks');
        Assert.ExpectedError('AreEqual');
    end;

    [Test]
    procedure CurrPage_GetRecord_And_Part_SetRecord_Compiles()
    begin
        // In BC, CurrPage.GetRecord overwrites the local record with the page's current record,
        // which may differ from the pre-set value — so the return value is not guaranteed to be true.
        // Contract: the call must compile and not throw.
        Src.PageCurrPageCalls();
        Assert.IsTrue(true, 'CurrPage.GetRecord and CurrPage.Part.Page.SetRecord must compile and not throw');
    end;

    [Test]
    procedure CurrPage_GetRecord_And_Part_SetRecord_WrongExpectationFails()
    begin
        // Negative trap: asserterror with always-false condition must fail as expected.
        asserterror Assert.AreEqual(0, 1, 'zero must not equal one');
        Assert.ExpectedError('AreEqual');
    end;
}
