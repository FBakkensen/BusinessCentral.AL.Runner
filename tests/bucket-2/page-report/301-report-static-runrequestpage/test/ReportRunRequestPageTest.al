/// Tests for Report.RunRequestPage OOS — issue #1329.
codeunit 307401 "RRP Test"
{
    Subtype = Test;

    var
        Assert: Codeunit Assert;

    [Test]
    procedure Report_RunRequestPage_2Arg_OutOfScope()
    var
        Src: Codeunit "RRP Src";
    begin
        // [GIVEN] A report id and non-empty request parameters
        // [WHEN]  Report.RunRequestPage(reportId, requestParameters) is called
        // [THEN]  Throws OOS — request-page UI rendering is out-of-scope
        asserterror Src.RunRequestPage2Arg(99999, '<ReqParams />');
        Assert.ExpectedError('out-of-scope: NavReport.RunRequestPage');
    end;

    [Test]
    procedure Report_RunRequestPage_2Arg_EmptyParams_OutOfScope()
    var
        Src: Codeunit "RRP Src";
    begin
        // [GIVEN] A report id and an empty request parameters string
        // [WHEN]  Report.RunRequestPage(reportId, '') is called
        // [THEN]  Throws OOS
        asserterror Src.RunRequestPage2Arg(99999, '');
        Assert.ExpectedError('out-of-scope: NavReport.RunRequestPage');
    end;

    [Test]
    procedure Report_RunRequestPage_1Arg_OutOfScope()
    var
        Src: Codeunit "RRP Src";
    begin
        // [GIVEN] A valid (dummy) report id
        // [WHEN]  Report.RunRequestPage(reportId) 1-arg overload is called
        // [THEN]  Throws OOS
        asserterror Src.RunRequestPage1Arg(99999);
        Assert.ExpectedError('out-of-scope: NavReport.RunRequestPage');
    end;
}
