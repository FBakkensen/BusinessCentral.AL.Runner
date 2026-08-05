codeunit 50544 "RPH Request Page Tests"
{
    Subtype = Test;

    var
        Assert: Codeunit Assert;

    [Test]
    procedure RunRequestPageInvokesHandler_OutOfScope()
    var
        Caller: Codeunit "RPH Report Caller";
    begin
        // [GIVEN] RunRequestPage is called
        // [THEN] Throws OOS — request-page UI rendering is out-of-scope
        asserterror Caller.CallRunRequestPage();
        Assert.ExpectedError('out-of-scope: NavReport.RunRequestPage');
    end;

    [Test]
    procedure RunRequestPageWithoutHandlerThrows()
    var
        Caller: Codeunit "RPH Report Caller";
    begin
        // [GIVEN] No handler registered
        // [WHEN] RunRequestPage is called without a handler
        // [THEN] Throws OOS — request-page UI rendering is out-of-scope
        asserterror Caller.CallRunRequestPage();
        Assert.ExpectedError('out-of-scope: NavReport.RunRequestPage');
    end;
}
