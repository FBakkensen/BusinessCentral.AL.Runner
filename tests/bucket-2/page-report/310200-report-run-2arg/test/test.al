codeunit 310202 "RR2 Tests"
{
    Subtype = Test;

    var
        Assert: Codeunit Assert;
        Src: Codeunit "RR2 Source";

    // ------------------------------------------------------------------
    // Report.Run(ReportId, RequestPage) — 2-arg overload
    // Issue #1427: CS1501 'StaticRun' (2 args) no overload
    // ------------------------------------------------------------------

    [Test]
    procedure ReportRun_TwoArgs_RequestPageFalse_OutOfScope()
    begin
        // [GIVEN] A report ID
        // [WHEN]  Report.Run(id, false) is called — 2-arg overload
        // [THEN]  Throws OOS — report execution is out-of-scope in standalone mode
        asserterror Src.CallRunTwoArgs(310200);
        Assert.ExpectedError('out-of-scope: NavReport.Run');
    end;

    [Test]
    procedure ReportRun_TwoArgs_RequestPageTrue_OutOfScope()
    begin
        // [GIVEN] A report ID
        // [WHEN]  Report.Run(id, true) — requestPage=true
        // [THEN]  Throws OOS — report execution is out-of-scope in standalone mode
        asserterror Src.CallRunTwoArgsRequestPageTrue(310200);
        Assert.ExpectedError('out-of-scope: NavReport.Run');
    end;
}
