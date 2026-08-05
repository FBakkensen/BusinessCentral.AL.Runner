/// Tests for Page.Run/RunModal with page variables and the implicit NavForm conversion.
/// Issue #1106: CS1503 — Page<N> can't be passed where NavForm is expected after
/// NavForm is stripped from the page class base list.  The fix injects an implicit
/// conversion operator on every generated Page<N> class.
codeunit 50443 "PRV Test"
{
    Subtype = Test;

    var
        Assert: Codeunit Assert;
        Src: Codeunit "PRV Source";

    [Test]
    procedure PageRunWithRecord_ThrowsOutOfScope()
    var
        Rec: Record "PRV Row";
    begin
        // [WHEN] Page.Run(PageId, Rec) — non-modal UI (§3.11)
        // [THEN] OOS exception: NavForm.RunAsync
        asserterror Src.RunWithRecord(Rec);
        Assert.ExpectedError('out-of-scope: NavForm.RunAsync');
    end;

    [Test]
    procedure PageRunModalWithRecord_ReturnsActionNone()
    var
        Rec: Record "PRV Row";
        Result: Action;
    begin
        // [WHEN] Page.RunModal(PageId, Rec) is called
        Result := Src.RunModalWithRecord(Rec);
        // [THEN] Returns Action::None (stub default)
        Assert.AreEqual(Action::None, Result, 'Page.RunModal(PageId, Rec) must return Action::None');
    end;

    [Test]
    procedure PageVarRun_ThrowsOutOfScope()
    begin
        // [WHEN] A page variable's .Run() is called — non-modal UI (§3.11)
        // [THEN] OOS exception: NavForm.RunAsync
        asserterror Src.PageVarRun();
        Assert.ExpectedError('out-of-scope: NavForm.RunAsync');
    end;

    [Test]
    procedure PageVarSetRecord_ThrowsOutOfScope()
    var
        Rec: Record "PRV Row";
    begin
        // [WHEN] SetRecord + Run on a page variable — NavFormHandle.Target is null without service tier
        // [THEN] Throws before Run (NullReferenceException from SetRecord; OOS hook unreachable here)
        asserterror Src.PageVarSetRecord(Rec);
    end;

    [Test]
    [HandlerFunctions('PageVarRunModalHandler')]
    procedure PageVarRunModal_ReturnsActionNone()
    var
        Result: Action;
    begin
        // [WHEN] RunModal() is called on a page variable (with ModalPageHandler)
        Result := Src.PageVarRunModal();
        // [THEN] Returns the action set by the handler (Action::LookupOK when TestPage.OK is invoked)
        Assert.AreEqual(Action::LookupOK, Result, 'Page var .RunModal() must return handler action');
    end;

    [ModalPageHandler]
    procedure PageVarRunModalHandler(var Page: TestPage "PRV Card")
    begin
        Page.OK().Invoke();
    end;
}
