codeunit 163002 "RRM4 Tests"
{
    Subtype = Test;

    var
        Assert: Codeunit Assert;
        Src: Codeunit "RRM4 Src";

    // ------------------------------------------------------------------
    // Report.RunModal — all static overloads are no-ops in standalone mode
    // ------------------------------------------------------------------

    [Test]
    procedure Report_RunModal_1Arg_OutOfScope()
    begin
        // [GIVEN] A non-existent report id
        // [WHEN]  Report.RunModal(id) 1-arg is called
        // [THEN]  Throws OOS — report execution is out-of-scope in standalone mode
        asserterror Src.CallRunModal1Arg(99999);
        Assert.ExpectedError('out-of-scope: NavReport.RunModal');
    end;

    [Test]
    procedure Report_RunModal_2Arg_OutOfScope()
    begin
        // [GIVEN] A non-existent report id
        // [WHEN]  Report.RunModal(id, false) 2-arg is called
        // [THEN]  Throws OOS — report execution is out-of-scope in standalone mode
        asserterror Src.CallRunModal2Arg(99999, false);
        Assert.ExpectedError('out-of-scope: NavReport.RunModal');
    end;

    [Test]
    procedure Report_RunModal_3Arg_OutOfScope()
    begin
        // [GIVEN] A non-existent report id
        // [WHEN]  Report.RunModal(id, false, false) 3-arg is called
        // [THEN]  Throws OOS — report execution is out-of-scope in standalone mode
        asserterror Src.CallRunModal3Arg(99999, false, false);
        Assert.ExpectedError('out-of-scope: NavReport.RunModal');
    end;

    [Test]
    procedure Report_RunModal_4Arg_OutOfScope()
    var
        DummyRec: Record "RRM4 Dummy";
    begin
        // [GIVEN] A non-existent report id and a record variable
        // [WHEN]  Report.RunModal(id, false, false, Rec) 4-arg is called
        // [THEN]  Throws OOS — report execution is out-of-scope in standalone mode
        asserterror Src.CallRunModal4Arg(99999, false, false, DummyRec);
        Assert.ExpectedError('out-of-scope: NavReport.RunModal');
    end;
}
