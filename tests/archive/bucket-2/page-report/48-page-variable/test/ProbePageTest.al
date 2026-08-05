codeunit 50479 "PV Page Var Tests"
{
    Subtype = Test;

    var
        Assert: Codeunit Assert;

    [Test]
    procedure PageVariableRun_ThrowsOutOfScope()
    var
        P: Page "PV Probe Page";
    begin
        // [WHEN] Calling .Run() on a page variable — non-modal UI (§3.11)
        // [THEN] OOS exception: NavForm.RunAsync
        asserterror P.Run();
        Assert.ExpectedError('out-of-scope: NavForm.RunAsync');
    end;

    [Test]
    procedure PageRunStaticForm_ThrowsOutOfScope()
    var
        R: Record "PV Row";
    begin
        // [WHEN] Static Page.Run(pageId, Rec) — non-modal UI (§3.11)
        // [THEN] OOS exception: NavForm.RunAsync
        asserterror Page.Run(Page::"PV Probe Page", R);
        Assert.ExpectedError('out-of-scope: NavForm.RunAsync');
    end;
}
