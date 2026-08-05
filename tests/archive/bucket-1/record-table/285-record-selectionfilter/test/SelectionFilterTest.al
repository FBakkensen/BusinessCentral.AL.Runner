codeunit 50526 "SSF Test"
{
    Subtype = Test;
    var
        Assert: Codeunit Assert;
        Src: Codeunit "SSF Source";
        Rec: Record "SSF Table";

    [Test]
    procedure SetSelectionFilter_FilterCopied_CountMatches()
    begin
        Rec.DeleteAll();
        // [GIVEN] Two records; SetRange on Id='A001'; SetSelectionFilter copies filter
        // [THEN] Filtered.Count() = 1
        Src.InsertRecord('A001', 10);
        Src.InsertRecord('A002', 20);
        Assert.AreEqual(1, Src.FilteredCountByRange('A001'), 'Filter-based selection must return 1 record');
    end;

    [Test]
    procedure SetSelectionFilter_NoMatchingRecords_ReturnsZero()
    begin
        Rec.DeleteAll();
        // [GIVEN] Records exist; filter matches nothing
        // [THEN] Filtered.Count() = 0
        Src.InsertRecord('B001', 10);
        Src.InsertRecord('B002', 20);
        Assert.AreEqual(0, Src.FilteredCountEmpty(), 'Filter with no match must return 0');
    end;

    [Test]
    procedure SetSelectionFilter_NoFilter_AllRecordsVisible()
    begin
        Rec.DeleteAll();
        // [GIVEN] Three records; no filter on source
        // [THEN] Filtered.Count() = total record count (all records visible)
        Src.InsertRecord('C001', 10);
        Src.InsertRecord('C002', 20);
        Src.InsertRecord('C003', 30);
        Assert.AreEqual(3, Src.FilteredCountNoFilter(), 'No filter: all records must be visible');
    end;

    [Test]
    procedure SetSelectionFilter_SourceRetainsOwnFilter()
    begin
        Rec.DeleteAll();
        // [GIVEN] Source has a range filter; SetSelectionFilter is called
        // [THEN] Source's own filter is unchanged (Count = 1)
        Src.InsertRecord('D001', 10);
        Src.InsertRecord('D002', 20);
        Assert.AreEqual(1, Src.SourceCountUnchangedAfterSetSelectionFilter('D001'),
            'SetSelectionFilter must not change the source record filter');
    end;
}
