table 64600 "Amwr Row"
{
    fields
    {
        field(1; "Entry No."; Integer) { }
        field(2; Qty; Decimal) { }
        field(3; "Fail OnInsert"; Boolean) { }
    }
    keys { key(PK; "Entry No.") { Clustered = true; } }

    trigger OnInsert()
    begin
        if "Fail OnInsert" then
            Error('Amwr OnInsert refused');
    end;
}
