codeunit 308202 "RR3 Tests"
{
    Subtype = Test;

    var
        Assert: Codeunit Assert;
        Src: Codeunit "RR3 Source";

    // ------------------------------------------------------------------
    // Report.Run(ReportId, RequestPage, SystemPrinter) — 3-arg overload
    // Issue #1336: CS7036 'systemPrinter' missing argument
    // ------------------------------------------------------------------

    [Test]
    procedure ReportRun_ThreeArgs_OutOfScope()
    begin
        // [GIVEN] A report ID
        // [WHEN]  Report.Run(id, false, false) is called — 3-arg overload
        // [THEN]  Throws OOS — report execution is out-of-scope in standalone mode
        asserterror Src.CallRunThreeArgs(308200);
        Assert.ExpectedError('out-of-scope: NavReport.Run');
    end;

    [Test]
    procedure ReportRun_ThreeArgs_RequestPageTrue_OutOfScope()
    begin
        // [GIVEN] A report ID
        // [WHEN]  Report.Run(id, true, false) — requestPage=true
        // [THEN]  Throws OOS — report execution is out-of-scope in standalone mode
        asserterror Src.CallRunThreeArgsRequestPage(308200);
        Assert.ExpectedError('out-of-scope: NavReport.Run');
    end;
}
