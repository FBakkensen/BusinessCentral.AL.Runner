/// Source table with an OnRename trigger.
/// After Rename, Rec holds the new PK values and xRec holds the old ones.
/// The trigger inserts a counter row keyed by the new PK (Rec.PK) with
/// Hits = Rec.Val, so tests can verify the trigger fired and saw correct state.
table 50088 "ORT Source"
{
    fields
    {
        field(1; PK; Integer) { }
        field(2; TriggerRan; Boolean) { }
        field(3; Val; Integer) { }
    }
    keys { key(PK; PK) { Clustered = true; } }

    trigger OnRename()
    var
        Counter: Record "ORT Counter";
    begin
        // W-8a PR2: Rec.PK is the new (post-rename) PK inside OnRename.
        // Insert a counter row keyed by the new PK so tests can confirm:
        //   (a) the trigger fired at all, and
        //   (b) the trigger saw the updated Rec.PK (new value) and Rec.Val.
        Counter.PK := Rec.PK;
        Counter.Hits := Rec.Val;
        Counter.Insert();
        Rec.TriggerRan := true;
    end;
}

/// Counter table shared by the OnRename trigger tests.
table 50089 "ORT Counter"
{
    fields
    {
        field(1; PK; Integer) { }
        field(2; Hits; Integer) { }
    }
    keys { key(PK; PK) { Clustered = true; } }
}
