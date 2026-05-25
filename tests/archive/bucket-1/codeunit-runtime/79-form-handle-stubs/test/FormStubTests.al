// Renumbered from 57901 to avoid collision in new bucket layout (#1385).
codeunit 50352 "Form Stub Tests"
{
    Subtype = Test;

    var
        Assert: Codeunit Assert;

    [Test]
    procedure TestSetTableViewNoOp()
    var
        Logic: Codeunit "Form Stub Logic";
    begin
        // [GIVEN] A codeunit that calls Page.SetTableView(Rec)
        // [WHEN] We call it
        Logic.ExercisePageStubs();
        // [THEN] No crash — SetTableView is a no-op
        Assert.IsTrue(true, 'SetTableView should not crash');
    end;

    [Test]
    procedure TestLookupModeDefaultFalse()
    var
        Logic: Codeunit "Form Stub Logic";
    begin
        // [GIVEN] A new page variable
        // [WHEN] We read LookupMode
        // [THEN] Default is false
        Assert.AreEqual(false, Logic.GetLookupMode(), 'LookupMode should default to false');
    end;

    [Test]
    procedure TestEditableDefaultTrue()
    var
        Logic: Codeunit "Form Stub Logic";
    begin
        // [GIVEN] A new page variable
        // [WHEN] We read Editable
        // [THEN] Default is true
        Assert.AreEqual(true, Logic.GetEditable(), 'Editable should default to true');
    end;

    [Test]
    procedure TestPageCaptionDefaultIsPageName()
    var
        Logic: Codeunit "Form Stub Logic";
        Cap: Text;
    begin
        // [GIVEN] A new page variable
        // [WHEN] We read Caption
        // [THEN] BC returns the page name as the default caption
        Cap := Logic.GetCaption();
        Assert.AreEqual('Form Stub Page', Cap, 'Caption should default to the page name in BC');
    end;

    [Test]
    procedure TestGetRecordNoOp()
    var
        Logic: Codeunit "Form Stub Logic";
    begin
        // [GIVEN] A codeunit that calls Page.GetRecord(Rec)
        // [WHEN] We call it
        Logic.ExercisePageStubs();
        // [THEN] No crash
        Assert.IsTrue(true, 'GetRecord(Rec) should not crash');
    end;

    [Test]
    procedure TestClearNoOp()
    var
        Logic: Codeunit "Form Stub Logic";
    begin
        // [GIVEN] A codeunit that calls Clear(Page)
        // [WHEN] We call it
        Logic.ExercisePageStubs();
        // [THEN] No crash
        Assert.IsTrue(true, 'Clear(Page) should not crash');
    end;

    // TestCustomActionInvoke removed: BC 16.1 raises CLR NotSupportedException
    // for TestPage action Invoke() in the test runner API context.

    [Test]
    procedure TestExerciseAllStubsTogether()
    var
        Logic: Codeunit "Form Stub Logic";
    begin
        // [GIVEN] A codeunit that exercises ALL page stubs
        // [WHEN] We call it
        Logic.ExercisePageStubs();
        // [THEN] All stubs work without crashing
        Assert.AreEqual(false, Logic.GetLookupMode(), 'LookupMode still false after exercise');
        Assert.AreEqual(true, Logic.GetEditable(), 'Editable still true after exercise');
        Assert.AreEqual('Form Stub Page', Logic.GetCaption(), 'Caption defaults to page name after exercise');
    end;
}
