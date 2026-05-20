/// Tests for Report instance variable RunRequestPage OOS — issue #1333.
codeunit 308001 "ReportInstanceRRP Test"
{
    Subtype = Test;

    var
        Assert: Codeunit Assert;

    [Test]
    procedure Report_Instance_RunRequestPage_1Arg_OutOfScope()
    var
        Src: Codeunit "ReportInstanceRRP Src";
    begin
        // [GIVEN] A report instance variable and non-empty request parameters
        // [WHEN]  Rep.RunRequestPage(requestParameters) 1-arg instance overload is called
        // [THEN]  Throws OOS — request-page UI rendering is out-of-scope
        asserterror Src.RunRequestPage1Arg('<RequestPage />');
        Assert.ExpectedError('out-of-scope: NavReport.RunRequestPage');
    end;

    [Test]
    procedure Report_Instance_RunRequestPage_1Arg_EmptyParams_OutOfScope()
    var
        Src: Codeunit "ReportInstanceRRP Src";
    begin
        // [GIVEN] A report instance variable and empty request parameters
        // [WHEN]  Rep.RunRequestPage('') 1-arg instance overload is called
        // [THEN]  Throws OOS
        asserterror Src.RunRequestPage1Arg('');
        Assert.ExpectedError('out-of-scope: NavReport.RunRequestPage');
    end;

    [Test]
    procedure Report_Instance_RunRequestPage_0Arg_OutOfScope()
    var
        Src: Codeunit "ReportInstanceRRP Src";
    begin
        // [GIVEN] A report instance variable
        // [WHEN]  Rep.RunRequestPage() 0-arg overload is called
        // [THEN]  Throws OOS
        asserterror Src.RunRequestPage0Arg();
        Assert.ExpectedError('out-of-scope: NavReport.RunRequestPage');
    end;
}
