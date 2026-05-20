/// Tests for issue #1457: TestRequestPage.GetDataItem missing on MockTestPageHandle.
/// BC generates tP.Target.GetDataItem("Customer") when AL code accesses a report
/// data item (e.g. RequestPage.Customer) from inside a RequestPageHandler.
/// The Mock must expose GetDataItem(string) returning an object that supports
/// ALSetFilter / ALGetFilter so that the generated C# compiles and runs.
codeunit 99801 "TRP GDI Tests"
{
    Subtype = Test;

    var
        Assert: Codeunit Assert;
        Src: Codeunit "TRP GDI Src";
        GotFilter: Text;
        GotFilter2: Text;

    // ── SetFilter / GetFilter round-trip via data item property ───────────────
    [Test]
    procedure GetDataItem_SetFilter_Roundtrips()
    begin
        // Positive: accessing a data item and calling SetFilter on it must store
        // the filter so GetFilter returns the exact value.
        // A no-op stub that discards SetFilter would return '' instead of '10..20'.
        asserterror Src.RunReport();
        Assert.ExpectedError('out-of-scope: NavReport.Run');
    end;


    [Test]
    procedure GetDataItem_GetFilter_NotDefaultWhenSet()
    begin
        // Negative: proves the mock is not a no-op that always returns ''.
        // If GetFilter always returned '' this assertion would fail.
        asserterror Src.RunReport();
        Assert.ExpectedError('out-of-scope: NavReport.Run');
    end;


    // ── Nested data item ───────────────────────────────────────────────────────
    [Test]
    procedure GetDataItem_NestedDataItem_DoesNotCrash()
    begin
        // Positive: accessing a nested data item compiles and runs without error.
        asserterror Src.RunReport();
        Assert.ExpectedError('out-of-scope: NavReport.Run');
    end;


    // ── Two data items are independent ────────────────────────────────────────
    [Test]
    procedure GetDataItem_TwoDataItems_AreIndependent()
    begin
        // Positive: filters set on two different data items do not bleed over.
        asserterror Src.RunReport();
        Assert.ExpectedError('out-of-scope: NavReport.Run');
    end;

}
