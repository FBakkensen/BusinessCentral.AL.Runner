table 64650 "Ntcp Row"
{
    fields
    {
        field(1; "Entry No."; Integer) { }
        field(2; Qty; Decimal) { }
        field(3; Name; Text[50]) { }
        field(4; Payload; Blob) { }
    }
    keys { key(PK; "Entry No.") { Clustered = true; } }
}
