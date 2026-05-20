/// Tests that Report.SaveAs (with and without RecordRef) is out-of-scope:
/// NavReport.SaveAsAsync is Cecil-rewritten to throw OOS.
codeunit 50407 "Report SaveAs RecordRef Test"
{
    Subtype = Test;

    var
        Assert: Codeunit Assert;

    [Test]
    procedure Report_SaveAs_WithRecordRef_OutOfScope()
    var
        Src: Codeunit "Report SaveAs RecordRef Src";
    begin
        // [GIVEN] A report id, request data, and a RecordRef
        // [WHEN]  NavReport.SaveAsAsync is Cecil-rewritten to throw OOS
        // [THEN]  Throws out-of-scope error — report-rendering is not supported
        asserterror Src.SaveAsWithRecordRef(99999, '');
        Assert.ExpectedError('out-of-scope: NavReport.SaveAs');
    end;

    [Test]
    procedure Report_SaveAs_WithoutRecordRef_OutOfScope()
    var
        Src: Codeunit "Report SaveAs RecordRef Src";
    begin
        // [GIVEN] A report id and request data
        // [WHEN]  NavReport.SaveAsAsync is Cecil-rewritten to throw OOS
        // [THEN]  Throws out-of-scope error — report-rendering is not supported
        asserterror Src.SaveAsWithoutRecordRef(99999, '');
        Assert.ExpectedError('out-of-scope: NavReport.SaveAs');
    end;
}
