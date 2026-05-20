codeunit 91001 "RS Tests"
{
    Subtype = Test;

    var
        Assert: Codeunit Assert;
        Src: Codeunit "RS Src";

    // ------------------------------------------------------------------
    // No-op methods: Run, RunModal
    // ------------------------------------------------------------------

    [Test]
    procedure Report_Run_OutOfScope()
    begin
        // [GIVEN] A report id
        // [WHEN]  We call Report.Run(id) in standalone mode
        // [THEN]  Throws OOS — report execution is out-of-scope
        asserterror Src.CallRun(99999);
        Assert.ExpectedError('out-of-scope: NavReport.Run');
    end;

    [Test]
    procedure Report_RunModal_OutOfScope()
    begin
        // [GIVEN] A report id
        // [WHEN]  We call Report.RunModal(id) in standalone mode
        // [THEN]  Throws OOS — report execution is out-of-scope
        asserterror Src.CallRunModal(99999);
        Assert.ExpectedError('out-of-scope: NavReport.RunModal');
    end;

    // ------------------------------------------------------------------
    // SaveAs* methods: report-rendering is out-of-scope (OOS)
    // NavReport.SaveAsAsync is Cecil-rewritten to throw OOS.
    // ------------------------------------------------------------------

    [Test]
    procedure Report_SaveAsPdf_OutOfScope()
    begin
        asserterror Src.CallSaveAsPdf(99999, 'report.pdf');
        Assert.ExpectedError('out-of-scope: NavReport.SaveAs');
    end;

    [Test]
    procedure Report_SaveAsWord_OutOfScope()
    begin
        asserterror Src.CallSaveAsWord(99999, 'report.docx');
        Assert.ExpectedError('out-of-scope: NavReport.SaveAs');
    end;

    [Test]
    procedure Report_SaveAsExcel_OutOfScope()
    begin
        asserterror Src.CallSaveAsExcel(99999, 'report.xlsx');
        Assert.ExpectedError('out-of-scope: NavReport.SaveAs');
    end;

    [Test]
    procedure Report_SaveAsXml_OutOfScope()
    begin
        asserterror Src.CallSaveAsXml(99999, 'report.xml');
        Assert.ExpectedError('out-of-scope: NavReport.SaveAs');
    end;

    // ------------------------------------------------------------------
    // Value-returning methods
    // ------------------------------------------------------------------

    [Test]
    procedure Report_GetSubstituteReportId_ReturnsSameId()
    begin
        // [GIVEN] A report id
        // [WHEN]  Report.GetSubstituteReportId(id) is called
        // [THEN]  Returns the same id (no substitution in standalone mode)
        Assert.AreEqual(50100, Src.GetSubstituteId(50100), 'GetSubstituteReportId should return input id');
    end;

    [Test]
    procedure Report_RunRequestPage_OutOfScope()
    begin
        // [GIVEN] A report id
        // [WHEN]  Report.RunRequestPage(id) is called
        // [THEN]  Throws OOS — request-page UI rendering is out-of-scope
        asserterror Src.GetRunRequestPage(99999);
        Assert.ExpectedError('out-of-scope: NavReport.RunRequestPage');
    end;
}
