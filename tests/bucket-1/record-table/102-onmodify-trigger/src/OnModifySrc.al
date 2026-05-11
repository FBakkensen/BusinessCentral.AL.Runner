/// Source table with an OnModify trigger.
/// The trigger inserts a counter row (PK=Rec.PK, Hits=Rec.Val) so tests can
/// prove the trigger fired and read the updated field values from Rec.
table 100013 "OMT Source"
{
    fields
    {
        field(1; PK; Integer) { }
        field(2; Val; Integer) { }
    }
    keys { key(PK; PK) { Clustered = true; } }

    trigger OnModify()
    var
        Counter: Record "OMT Counter";
    begin
        // W-8a PR3: insert a counter row so the positive test can verify the
        // trigger ran. Hits = Rec.Val proves the trigger reads the post-modify
        // field values (Rec holds the new state inside OnModify).
        Counter.PK := Rec.PK;
        Counter.Hits := Rec.Val;
        Counter.Insert();
    end;
}

/// Counter table shared by the OnModify trigger tests.
table 100014 "OMT Counter"
{
    fields
    {
        field(1; PK; Integer) { }
        field(2; Hits; Integer) { }
    }
    keys { key(PK; PK) { Clustered = true; } }
}
