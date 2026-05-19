/// Source table with an OnDelete trigger.
/// The trigger inserts a counter row (PK=Rec.PK, Hits=Rec.Val) so tests can
/// prove the trigger fired even though the source row no longer exists post-delete.
table 100016 "ODT Source"
{
    fields
    {
        field(1; PK; Integer) { }
        field(2; Val; Integer) { }
    }
    keys { key(PK; PK) { Clustered = true; } }

    trigger OnDelete()
    var
        Counter: Record "ODT Counter";
    begin
        // W-8a PR2: insert a counter row so the positive test can verify the
        // trigger ran after the source row is gone.  Hits = Rec.Val proves the
        // trigger could read field values from the about-to-be-deleted record.
        Counter.PK := Rec.PK;
        Counter.Hits := Rec.Val;
        Counter.Insert();
    end;
}

/// Counter table shared by the OnDelete trigger tests.
table 100017 "ODT Counter"
{
    fields
    {
        field(1; PK; Integer) { }
        field(2; Hits; Integer) { }
    }
    keys { key(PK; PK) { Clustered = true; } }
}
