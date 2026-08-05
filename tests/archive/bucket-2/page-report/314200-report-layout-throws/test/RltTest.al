codeunit 50601 "RLT Test"
{
    Subtype = Test;

    var
        Assert: Codeunit "Library Assert";

    /// A report without ProcessingOnly = true (i.e. AL default false) must
    /// throw an AL-observable out-of-scope error at .Run() — the runner cannot
    /// render layouts without a service tier. The error originates from
    /// NavReport.RDLCLayout → GetLayoutCore (rendering entry point), not from
    /// a guard at the top of Run.
    [Test]
    procedure Run_OnLayoutReport_ThrowsOOS()
    var
        Drv: Codeunit "RLT Driver";
    begin
        asserterror Drv.RunLayoutReport();
        Assert.ExpectedError('out-of-scope: NavReport');
    end;

    /// A ProcessingOnly report skips rendering and runs lifecycle only — Run
    /// must NOT throw.
    [Test]
    procedure Run_OnProcessingOnlyReport_Succeeds()
    var
        Drv: Codeunit "RLT Driver";
    begin
        Drv.RunProcessingReport();
        Assert.IsTrue(true, 'Processing-only report ran without throwing');
    end;
}
