codeunit 79802 "RR4 Tests"
{
    Subtype = Test;

    var
        Assert: Codeunit Assert;
        Src: Codeunit "RR4 Source";

    // ------------------------------------------------------------------
    // Report.Run(ReportId, RequestPage, SystemPrinter, Record) — 4-arg
    // ------------------------------------------------------------------

    [Test]
    procedure ReportRun_FourArgs_OutOfScope()
    var
        Rec: Record "RR4 Table";
    begin
        // [GIVEN] A record variable (empty, no filter)
        // [WHEN]  Report.Run(id, false, false, Rec) is called
        // [THEN]  Throws OOS — report execution is out-of-scope in standalone mode
        asserterror Src.CallRunFourArgs(29800, Rec);
        Assert.ExpectedError('out-of-scope: NavReport.Run');
    end;

    [Test]
    procedure ReportRun_FourArgs_FilterSurvivesCall_OutOfScope()
    begin
        // [GIVEN] A record with a filter applied
        // [WHEN]  Report.Run(id, false, false, Rec) is called
        // [THEN]  Throws OOS — report execution is out-of-scope; filter check is moot
        asserterror Src.SetupTableAndRun(29800);
        Assert.ExpectedError('out-of-scope: NavReport.Run');
    end;
}
