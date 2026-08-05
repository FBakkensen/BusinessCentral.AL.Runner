// Renumbered from 56401 to avoid collision in new bucket layout (#1385).
codeunit 50543 "PRR Page Run Tests"
{
    Subtype = Test;

    var
        Assert: Codeunit Assert;

    [Test]
    procedure TestPageRunWithRecord_ThrowsOutOfScope()
    var
        Caller: Codeunit "PRR Caller";
        Item: Record "PRR Item";
    begin
        Item."No." := 'X1';
        Item.Insert();

        // [WHEN] Page.Run(PageId, Rec) inside a codeunit — non-modal UI (§3.11)
        // [THEN] OOS exception: NavForm.RunAsync
        asserterror Caller.ShowItem(Item);
        Assert.ExpectedError('out-of-scope: NavForm.RunAsync');
    end;

    [Test]
    procedure TestPageRunModalWithRecordCompiles()
    var
        Caller: Codeunit "PRR Caller";
        Item: Record "PRR Item";
        Result: Integer;
    begin
        Item."No." := 'X2';
        Item.Insert();

        Result := Caller.ShowItemCurrRec(Item);

        Assert.AreEqual(43, Result, 'Page.RunModal(PageId, Rec) must be a no-op and allow caller to return');
    end;
}
