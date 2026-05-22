codeunit 50556 "Test Count SetFilter"
{
    Subtype = Test;

    var
        Assert: Codeunit Assert;


    local procedure Initialize()
    var
        Rec1: Record "CountSF Probe";
    begin
        Rec1.DeleteAll(false);
    end;

    [Test]
    procedure CountWithGreaterThanFilter()
    var
        Rec: Record "CountSF Probe";
    begin
        Initialize();
        Rec.DeleteAll();
        // Positive: '>' comparator filter.
        Seed();
        Rec.SetFilter(Status, '>1');
        Assert.AreEqual(3, Rec.Count,
            'Status>1 matches 2,2,3 — expect 3');
    end;

    [Test]
    procedure CountWithLessThanFilter()
    var
        Rec: Record "CountSF Probe";
    begin
        Initialize();
        Rec.DeleteAll();
        // Positive: '<' comparator filter.
        Seed();
        Rec.SetFilter(Status, '<2');
        Assert.AreEqual(2, Rec.Count,
            'Status<2 matches two 1s — expect 2');
    end;

    [Test]
    procedure CountWithNotEqualFilter()
    var
        Rec: Record "CountSF Probe";
    begin
        Initialize();
        Rec.DeleteAll();
        // Positive: '<>' filter.
        Seed();
        Rec.SetFilter(Status, '<>2');
        Assert.AreEqual(3, Rec.Count,
            'Status<>2 matches 1,1,3 — expect 3');
    end;

    [Test]
    procedure CountWithOrFilter()
    var
        Rec: Record "CountSF Probe";
    begin
        Initialize();
        Rec.DeleteAll();
        // Positive: 'a|b' OR-list filter.
        Seed();
        Rec.SetFilter(Status, '1|3');
        Assert.AreEqual(3, Rec.Count,
            'Status in {1,3} matches 1,1,3 — expect 3');
    end;

    [Test]
    procedure CountWithRangeExpressionFilter()
    var
        Rec: Record "CountSF Probe";
    begin
        Initialize();
        Rec.DeleteAll();
        // Positive: 'a..b' range expression.
        Seed();
        Rec.SetFilter(Status, '2..3');
        Assert.AreEqual(3, Rec.Count,
            'Status in 2..3 matches 2,2,3 — expect 3');
    end;

    [Test]
    procedure CountWithNoMatchingFilter()
    var
        Rec: Record "CountSF Probe";
    begin
        Initialize();
        Rec.DeleteAll();
        // Negative: filter that matches nothing.
        Seed();
        Rec.SetFilter(Status, '>99');
        Assert.AreEqual(0, Rec.Count,
            'Filter matching nothing must produce Count 0');
    end;

    [Test]
    procedure CountRestoredAfterReset()
    var
        Rec: Record "CountSF Probe";
    begin
        Initialize();
        // Positive/reset: Reset returns to the full table count.
        Seed();
        Rec.SetFilter(Status, '>1');
        Assert.AreEqual(3, Rec.Count, 'Precondition: filtered count is 3');

        Rec.Reset();
        Assert.AreEqual(5, Rec.Count,
            'After Reset, Count must be the full 5');
    end;

    local procedure Seed()
    var
        Rec: Record "CountSF Probe";
    begin
        Insert2('A', 1);
        Insert2('B', 2);
        Insert2('C', 1);
        Insert2('D', 2);
        Insert2('E', 3);
    end;

    local procedure Insert2("No.": Code[20]; Status: Integer)
    var
        Rec: Record "CountSF Probe";
    begin
        Rec.Init();
        Rec."No." := "No.";
        Rec.Status := Status;
        Rec.Insert(true);
    end;
}
